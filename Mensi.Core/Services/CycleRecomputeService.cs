using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Prediction;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Core.Services;

/// <summary>Minden írás után a cycle tábla determinisztikusan újraépül a napi logokból.
/// Egyetlen kivétel a PredictedLengthDays: azt a sor születésekor írjuk (az akkori modell
/// becslése), és soha nem számoljuk újra — ebből lesz a "késés" statisztika.</summary>
public sealed class CycleRecomputeService(MensiDbContext db, TimeProvider clock)
{
    public async Task RecomputeAsync(CancellationToken ct = default)
    {
        var logs = await db.DailyLogs.AsNoTracking().OrderBy(l => l.Date).ToListAsync(ct);
        var snapshots = logs.Select(l => new DailyLogSnapshot(
            l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
            l.FlowIntensity, l.PeriodStart, 0, 0)).ToList();

        var derived = CycleDeriver.Derive(snapshots);
        var existing = await db.Cycles.ToDictionaryAsync(c => c.StartDate, ct);
        var now = clock.GetUtcNow();

        foreach (var stale in existing.Values.Where(c => derived.All(d => d.Start != c.StartDate)))
            db.Cycles.Remove(stale);

        for (var i = 0; i < derived.Count; i++)
        {
            var d = derived[i];
            if (!existing.TryGetValue(d.Start, out var row))
            {
                // Új ciklus: az addig lezárt ciklusokból számolt predikció egyszer íródik be.
                var prior = derived.Take(i)
                    .Where(p => p.LengthDays is not null)
                    .Select(p => new ClosedCycleStat(
                        p.Start, p.LengthDays!.Value, p.LutealLength, p.Anovulatory, null))
                    .ToList();
                row = new Cycle { StartDate = d.Start, PredictedLengthDays = LengthPredictor.Predict(prior) };
                db.Cycles.Add(row);
            }
            row.LengthDays = d.LengthDays;
            row.OvulationDayConfirmed = d.OvulationConfirmed;
            row.OvulationDayEstimated = d.OvulationEstimated;
            row.LutealPhaseLength = d.LutealLength;
            row.Anovulatory = d.Anovulatory;
            row.ComputedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
