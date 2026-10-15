using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ReadModelProjector;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _config;
    private readonly IMongoCollection<LoanReadDocument> _collection;
    private IConnection? _rabbitConn;
    private IModel? _rabbitChannel;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;

        var mongoClient = new MongoClient(_config["MongoDB:ConnectionString"]);
        var database = mongoClient.GetDatabase(_config["MongoDB:DatabaseName"]);
        _collection = database.GetCollection<LoanReadDocument>(_config["MongoDB:CollectionName"]);
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

        var exchange = _config["RabbitMQ:ExchangeName"];
        var queue = _config["RabbitMQ:QueueName"];
        var routingKey = _config["RabbitMQ:RoutingKey"];

        _rabbitChannel.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true);
        _rabbitChannel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
        _rabbitChannel.QueueBind(queue, exchange, routingKey);

        _rabbitChannel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);

        _logger.LogInformation("Consumidor suscrito a la cola: {Queue}", queue);
        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_rabbitChannel);

        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var doc = JsonSerializer.Deserialize<LoanReadDocument>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (doc != null)
                {
                    doc.LastSyncedAt = DateTime.UtcNow;

                    // Upsert Idempotente en MongoDB
                    await _collection.ReplaceOneAsync(
                        filter: d => d.LoanId == doc.LoanId,
                        replacement: doc,
                        options: new ReplaceOptions { IsUpsert = true },
                        cancellationToken: stoppingToken
                    );

                    _logger.LogInformation("Proyectado con éxito LoanId: {LoanId} en MongoDB.", doc.LoanId);
                }

                _rabbitChannel!.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al proyectar evento hacia el Read Model.");
                _rabbitChannel!.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _rabbitChannel.BasicConsume(queue: _config["RabbitMQ:QueueName"], autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _rabbitChannel?.Close();
        _rabbitConn?.Close();
        base.Dispose();
    }
}