using System.Data;
using System.Text;
using Oracle.ManagedDataAccess.Client;
using RabbitMQ.Client;

namespace OutboxPublisher;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly string _oracleConnString;
    private IConnection? _rabbitConn;
    private IModel? _rabbitChannel;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _oracleConnString = _config.GetConnectionString("OracleOltp")!;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:HostName"],
            Port = int.Parse(_config["RabbitMQ:Port"]!),
            UserName = _config["RabbitMQ:UserName"],
            Password = _config["RabbitMQ:Password"]
        };

        _rabbitConn = factory.CreateConnection();
        _rabbitChannel = _rabbitConn.CreateModel();

        // Declarar exchange tipo Topic durable
        _rabbitChannel.ExchangeDeclare(
            exchange: _config["RabbitMQ:ExchangeName"],
            type: ExchangeType.Topic,
            durable: true
        );

        _logger.LogInformation("Conexión establecida con RabbitMQ.");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando lote del Outbox.");
            }

            await Task.Delay(1000, stoppingToken); // Intervalo de sondeo
        }
    }

    private async Task ProcessOutboxBatchAsync(CancellationToken ct)
    {
        using var conn = new OracleConnection(_oracleConnString);
        await conn.OpenAsync(ct);

        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);

        // FOR UPDATE SKIP LOCKED: Clave para concurrencia limpia
        const string selectSql = @"
            SELECT outbox_id, aggregate_id, event_type, payload
            FROM transactional_outbox
            WHERE status = 'PENDING'
            FETCH FIRST 50 ROWS ONLY
            FOR UPDATE SKIP LOCKED";

        var eventsToDispatch = new List<(long OutboxId, string AggId, string EventType, string Payload)>();

        using (var cmd = new OracleCommand(selectSql, conn))
        {
            cmd.Transaction = tx;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                eventsToDispatch.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)
                ));
            }
        }

        if (eventsToDispatch.Count == 0)
        {
            tx.Rollback();
            return;
        }

        _logger.LogInformation("Encontrados {Count} eventos en Outbox para despachar.", eventsToDispatch.Count);

        foreach (var evt in eventsToDispatch)
        {
            var body = Encoding.UTF8.GetBytes(evt.Payload);
            var props = _rabbitChannel!.CreateBasicProperties();
            props.Persistent = true;
            props.Type = evt.EventType;
            props.MessageId = evt.AggId;

            // Publicar en RabbitMQ
            _rabbitChannel.BasicPublish(
                exchange: _config["RabbitMQ:ExchangeName"],
                routingKey: _config["RabbitMQ:RoutingKey"],
                basicProperties: props,
                body: body
            );

            // Actualizar estado en Oracle
            const string updateSql = @"
                UPDATE transactional_outbox
                SET status = 'DISPATCHED',
                    dispatched_at = SYSTIMESTAMP
                WHERE outbox_id = :p_id";

            using var updateCmd = new OracleCommand(updateSql, conn);
            updateCmd.Transaction = tx;
            updateCmd.Parameters.Add("p_id", OracleDbType.Int64).Value = evt.OutboxId;
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        tx.Commit();
        _logger.LogInformation("Lote de {Count} eventos despachado y confirmado en Oracle.", eventsToDispatch.Count);
    }

    public override void Dispose()
    {
        _rabbitChannel?.Close();
        _rabbitConn?.Close();
        base.Dispose();
    }
}