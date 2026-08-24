namespace Mensi.Core.Domain;

public class DailyLog
{
    public DateOnly Date { get; set; }
    public decimal? BbtCelsius { get; set; }
    public CervicalMucus? CervicalMucus { get; set; }
    public LhTest? LhTest { get; set; }
    public CrampType? CrampType { get; set; }
    public short? CrampSeverity { get; set; }
    public FlowIntensity? FlowIntensity { get; set; }
    public bool PeriodStart { get; set; }
    public List<Mood> Moods { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public List<IntercourseEvent> Intercourse { get; set; } = [];
}
