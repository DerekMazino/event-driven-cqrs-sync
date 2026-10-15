using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ReadModelProjector;

[BsonIgnoreExtraElements]
public class LoanReadDocument
{
    [BsonId]
    public long LoanId { get; set; }

    [BsonElement("customerId")]
    public long CustomerId { get; set; }

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("interestRate")]
    public decimal InterestRate { get; set; }

    [BsonElement("termMonths")]
    public int TermMonths { get; set; }

    [BsonElement("monthlyInstallment")]
    public decimal MonthlyInstallment { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("lastSyncedAt")]
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}