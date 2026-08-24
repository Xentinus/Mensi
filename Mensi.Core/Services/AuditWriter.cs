using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Data;
using Mensi.Core.Domain;

namespace Mensi.Core.Services;

public sealed class AuditWriter(MensiDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public void Add(string email, string action, DateOnly entryDate,
        IReadOnlyDictionary<string, (object? Old, object? New)> changes) =>
        db.AuditEntries.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            Email = email,
            Action = action,
            EntryDate = entryDate,
            ChangesJson = BuildChangesJson(changes),
        });

    public static string BuildChangesJson(
        IReadOnlyDictionary<string, (object? Old, object? New)> changes)
    {
        var shaped = changes.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, object?> { ["old"] = kv.Value.Old, ["new"] = kv.Value.New });
        return JsonSerializer.Serialize(shaped, Json);
    }
}
