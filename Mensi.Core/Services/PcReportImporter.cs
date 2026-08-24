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

    public async Task<ImportResultDto> ImportAsync(
        byte[] pdf, bool dryRun, DateOnly? fromDate = null, CancellationToken ct = default)
    {
        var data = PcReportParser.Parse(PdfTextLines.Extract(pdf));
        var warnings = new List<string>(data.Warnings);
        var today = todayProvider.Today;

        // Kezdődátum-szűrés: csak az ettől kezdődő ciklusok és tesztek kerülnek betöltésre.
        var cycles = fromDate is DateOnly from
            ? data.Cycles.Where(c => c.StartDate >= from).ToList()
            : data.Cycles.ToList();
        var lhTests = fromDate is DateOnly fromLh
            ? data.LhTests.Where(t => t.Date >= fromLh).ToList()
            : data.LhTests.ToList();

        // Terv: naponként mely mezők jönnének a riportból.
        var plan = new Dictionary<DateOnly, PlannedDay>();
        var futureSkipped = 0;
        foreach (var cycle in cycles)
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
        foreach (var test in lhTests)
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
            audit.Add(user.Email, "import.pcReport", cycles.Count > 0 ? cycles[0].StartDate : today,
                new Dictionary<string, (object?, object?)>
                {
                    ["cyclesFound"] = (null, cycles.Count),
                    ["lhTests"] = (null, lhTests.Count),
                    ["fromDate"] = (null, fromDate?.ToString("yyyy-MM-dd")),
                    ["daysWritten"] = (null, daysWritten),
                    ["fieldsSkipped"] = (null, fieldsSkipped),
                });
            await db.SaveChangesAsync(ct);
            await recompute.RecomputeAsync(ct);
        }

        return new ImportResultDto(
            applied,
            cycles.Count,
            cycles.Count > 0 ? cycles[0].StartDate : null,
            cycles.Count > 0 ? cycles[^1].StartDate : null,
            lhTests.Count,
            daysWritten,
            fieldsSkipped,
            cycles.Select(c => new ImportCycleDto(c.StartDate, c.PeriodDays)).ToList(),
            warnings);
    }
}
