using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Import;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Core.Services;

/// <summary>
/// Period Tracker PDF-riport betöltése: cikluskezdetek (period_start + folyásnapok) és
/// ovulációs tesztek. Nem destruktív: meglévő értéket sosem ír felül — csak üres mezőt
/// tölt, a kihagyásokat számolja. Dry-run módban csak az előnézetet adja vissza.
/// </summary>
public sealed class PcReportImporter(
    MensiDbContext db,
    TodayProvider todayProvider,
    CurrentUser user,
    AuditWriter audit,
    CycleRecomputeService recompute,
    TimeProvider clock)
{
    private sealed record PlannedDay(bool? PeriodStart, FlowIntensity? Flow, LhTest? Lh);

    public async Task<ImportResultDto> ImportAsync(byte[] pdf, bool dryRun, CancellationToken ct = default)
    {
        var data = PcReportParser.Parse(PdfTextLines.Extract(pdf));
        var warnings = new List<string>(data.Warnings);
        var today = todayProvider.Today;

        // Terv: naponként mely mezők jönnének a riportból.
        var plan = new Dictionary<DateOnly, PlannedDay>();
        var futureSkipped = 0;
        foreach (var cycle in data.Cycles)
        {
            for (var i = 0; i < cycle.PeriodDays; i++)
            {
                var date = cycle.StartDate.AddDays(i);
                if (date > today) { futureSkipped++; continue; }
                // A riport intenzitást nem közöl: 1. nap közepes, a többi enyhe.
                var flow = i == 0 ? FlowIntensity.Medium : FlowIntensity.Light;
                plan[date] = new PlannedDay(i == 0 ? true : null, flow, plan.GetValueOrDefault(date)?.Lh);
            }
        }
        foreach (var test in data.LhTests)
        {
            if (test.Date > today) { futureSkipped++; continue; }
            var existing = plan.GetValueOrDefault(test.Date);
            plan[test.Date] = new PlannedDay(existing?.PeriodStart, existing?.Flow, test.Result);
        }

        var dates = plan.Keys.ToList();
        var logs = await db.DailyLogs.Where(l => dates.Contains(l.Date)).ToDictionaryAsync(l => l.Date, ct);

        var daysWritten = 0;
        var fieldsSkipped = 0;
        var now = clock.GetUtcNow();

        foreach (var (date, planned) in plan.OrderBy(p => p.Key))
        {
            logs.TryGetValue(date, out var log);
            var wrote = false;

            void Ensure()
            {
                if (log is not null) return;
                log = new DailyLog { Date = date, CreatedAt = now };
                if (!dryRun) db.DailyLogs.Add(log);
                logs[date] = log;
            }

            if (planned.PeriodStart == true)
            {
                if (log?.PeriodStart == true) fieldsSkipped++;
                else { Ensure(); if (!dryRun) log!.PeriodStart = true; wrote = true; }
            }
            if (planned.Flow is FlowIntensity flow)
            {
                if (log?.FlowIntensity is not null) fieldsSkipped++;
                else { Ensure(); if (!dryRun) log!.FlowIntensity = flow; wrote = true; }
            }
            if (planned.Lh is LhTest lh)
            {
                if (log?.LhTest is not null) fieldsSkipped++;
                else { Ensure(); if (!dryRun) log!.LhTest = lh; wrote = true; }
            }

            if (wrote)
            {
                daysWritten++;
                if (!dryRun)
                {
                    log!.UpdatedAt = now;
                    log.UpdatedBy = user.Email;
                }
            }
        }

        if (futureSkipped > 0)
            warnings.Add($"{futureSkipped} jövőbeli nap kihagyva.");

        var applied = !dryRun && daysWritten > 0;
        if (applied)
        {
            audit.Add(user.Email, "import.pcReport", data.Cycles.Count > 0 ? data.Cycles[0].StartDate : today,
                new Dictionary<string, (object?, object?)>
                {
                    ["cyclesFound"] = (null, data.Cycles.Count),
                    ["lhTests"] = (null, data.LhTests.Count),
                    ["daysWritten"] = (null, daysWritten),
                    ["fieldsSkipped"] = (null, fieldsSkipped),
                });
            await db.SaveChangesAsync(ct);
            await recompute.RecomputeAsync(ct);
        }

        return new ImportResultDto(
            applied,
            data.Cycles.Count,
            data.Cycles.Count > 0 ? data.Cycles[0].StartDate : null,
            data.Cycles.Count > 0 ? data.Cycles[^1].StartDate : null,
            data.LhTests.Count,
            daysWritten,
            fieldsSkipped,
            warnings);
    }
}
