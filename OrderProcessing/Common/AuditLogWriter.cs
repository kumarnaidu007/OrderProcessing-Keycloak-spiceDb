using System.Text.Json;
using OrderProcessing.Models;

namespace OrderProcessing.Common;

public static class AuditLogWriter
{
    public static void Add(
        OrderProcessingContext db,
        string entityType,
        string entityId,
        string actionCode,
        long? userId,
        object? metadata = null)
    {
        var json = metadata is null
            ? "{}"
            : JsonSerializer.Serialize(metadata, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (json.Length > 4000)
            json = json[..4000];

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            ActionCode = actionCode,
            UserId = userId,
            MetadataJson = json,
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
