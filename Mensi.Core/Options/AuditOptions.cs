namespace Mensi.Core.Options;

public class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Napokban; 0 = örökre. Az audit sor emailt tartalmaz, ezért szabály is van rá,
    /// nem csak kézi törlés.</summary>
    public int RetentionDays { get; set; } = 365;
}
