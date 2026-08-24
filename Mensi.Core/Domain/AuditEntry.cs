namespace Mensi.Core.Domain;

public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Email { get; set; } = "";
    public string Action { get; set; } = "";
    public DateOnly EntryDate { get; set; }
    /// <summary>jsonb: mező → { "old": …, "new": … }</summary>
    public string ChangesJson { get; set; } = "{}";
}
