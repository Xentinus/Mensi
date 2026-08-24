namespace Mensi.Core.Domain;

public class Cycle
{
    public long Id { get; set; }
    public DateOnly StartDate { get; set; }
    public int? LengthDays { get; set; }
    public int? OvulationDayEstimated { get; set; }
    public int? OvulationDayConfirmed { get; set; }
    public int? LutealPhaseLength { get; set; }
    public bool Anovulatory { get; set; }
    /// <summary>A modell predikciója a ciklus indulásakor — egyszer íródik, nem számolódik újra.</summary>
    public int? PredictedLengthDays { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
