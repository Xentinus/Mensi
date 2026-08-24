using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Import;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Core.Services;

/// <summary>
/// Period Tracker PDF-riport betöltése: cikluskezdetek, napi jelek a grafikon-oldalak
/// vektoros jelölőiből (folyás-intenzitás, BBT, nyák, görcs, pecsételés, hangulat,
/// együttlét, ovulációs teszt) és a szöveges ovulációs teszt-tábla. Nem destruktív:
/// meglévő értéket sosem ír felül — csak üres mezőt tölt, a kihagyásokat számolja.
/// Dry-run módban csak az előnézetet adja vissza.
/// </summary>
public sealed class PcReportImporter(
    MensiDbContext db,
    TodayProvider todayProvider,
    CurrentUser user,
    AuditWriter audit,
    CycleRecomputeService recompute,
    TimeProvider clock)
{
    private sealed class PlannedDay
    {
        public bool PeriodStart;
        public FlowIntensity? Flow;
        public LhTest? Lh;
        public decimal? Bbt;
        public CervicalMucus? Mucus;
        public bool Cramps;
        public List<Mood> Moods = [];
        public int UnprotectedSex;
        public int ProtectedSex;
    }

    public async Task<ImportResultDto> ImportAsync(
        byte[] pdf, bool dryRun, DateOnly? fromDate = null, CancellationToken ct = default)
    {
        var data = PcReportParser.Parse(PdfTextLines.Extract(pdf));
        var warnings = new List<string>(data.Warnings);
        var today = todayProvider.Today;

        var (chartDaily, chartWarnings) = PcChartExtractor.Extract(pdf, data.Cycles);
        warnings.AddRange(chartWarnings);

        // Kezdődátum-szűrés: csak az ettől kezdődő ciklusok és napi jelek töltődnek be.
        var cycles = fromDate is DateOnly from
            ? data.Cycles.Where(c => c.StartDate >= from).ToList()
            : data.Cycles.ToList();
        var lhTests = fromDate is DateOnly fromLh
            ? data.LhTests.Where(t => t.Date >= fromLh).ToList()
            : data.LhTests.ToList();
        var daily = fromDate is DateOnly fromDaily
            ? chartDaily.Where(d => d.Date >= fromDaily).ToList()
            : chartDaily.ToList();

        // Terv: naponként mely mezők jönnének a riportból.
        var plan = new Dictionary<DateOnly, PlannedDay>();
        var futureSkipped = 0;

        PlannedDay Plan(DateOnly date) =>
            plan.TryGetValue(date, out var p) ? p : plan[date] = new PlannedDay();

        // 1) Ciklustörténet: period_start + szintetikus folyásnapok (a grafikon felülírja, ha van).
        foreach (var cycle in cycles)
        {
            for (var i = 0; i < cycle.PeriodDays; i++)
            {
                var date = cycle.StartDate.AddDays(i);
                if (date > today) { futureSkipped++; continue; }
                var p = Plan(date);
                if (i == 0) p.PeriodStart = true;
                p.Flow ??= i == 0 ? FlowIntensity.Medium : FlowIntensity.Light;
            }
        }

        // 2) Grafikon napi jelei — a tényleges folyás-intenzitás erősebb a szintetikusnál.
        foreach (var d in daily)
        {
            if (d.Date > today) { futureSkipped++; continue; }
            var p = Plan(d.Date);
            if (d.Flow is FlowIntensity f) p.Flow = f;
            else if (d.Spotting) p.Flow ??= FlowIntensity.Spotting;
            if (d.Bbt is decimal t) p.Bbt = t;
            if (d.Mucus is CervicalMucus mu) p.Mucus = mu;
            if (d.Cramps) p.Cramps = true;
            if (d.Moods.Count > 0) p.Moods = d.Moods.ToList();
            if (d.Lh is LhTest lh) p.Lh = lh;
            p.UnprotectedSex = d.UnprotectedSex;
            p.ProtectedSex = d.ProtectedSex;
        }

        // 3) Szöveges ovulációs teszt-tábla (a grafikon-jelet nem írja felül).
        foreach (var test in lhTests)
        {
            if (test.Date > today) { futureSkipped++; continue; }
            Plan(test.Date).Lh ??= test.Result;
        }

        var dates = plan.Keys.ToList();
        var logs = await db.DailyLogs.Include(l => l.Intercourse)
            .Where(l => dates.Contains(l.Date)).ToDictionaryAsync(l => l.Date, ct);

        var daysWritten = 0;
        var fieldsSkipped = 0;
        var counts = new Dictionary<string, int>
        {
            ["bbt"] = 0, ["mucus"] = 0, ["cramp"] = 0, ["mood"] = 0, ["sex"] = 0, ["lh"] = 0,
        };
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

            void Set(bool has, bool existsAlready, Action apply, string? countKey = null)
            {
                if (!has) return;
                if (existsAlready) { fieldsSkipped++; return; }
                Ensure();
                if (!dryRun) apply();
                wrote = true;
                if (countKey is not null) counts[countKey]++;
            }

            Set(planned.PeriodStart, log?.PeriodStart == true, () => log!.PeriodStart = true);
            Set(planned.Flow is not null, log?.FlowIntensity is not null,
                () => log!.FlowIntensity = planned.Flow);
            Set(planned.Lh is not null, log?.LhTest is not null,
                () => log!.LhTest = planned.Lh, "lh");
            Set(planned.Bbt is not null, log?.BbtCelsius is not null,
                () => log!.BbtCelsius = planned.Bbt, "bbt");
            Set(planned.Mucus is not null, log?.CervicalMucus is not null,
                () => log!.CervicalMucus = planned.Mucus, "mucus");
            Set(planned.Cramps, log?.CrampSeverity is not null,
                () => { log!.CrampSeverity = 1; log.CrampType = null; }, "cramp");
            Set(planned.Moods.Count > 0, log is { Moods.Count: > 0 },
                () => log!.Moods = planned.Moods.ToList(), "mood");
            Set(planned.UnprotectedSex + planned.ProtectedSex > 0, log is { Intercourse.Count: > 0 },
                () =>
                {
                    for (var i = 0; i < planned.UnprotectedSex; i++)
                        log!.Intercourse.Add(new IntercourseEvent { Date = date, Protected = false, CreatedAt = now });
                    for (var i = 0; i < planned.ProtectedSex; i++)
                        log!.Intercourse.Add(new IntercourseEvent { Date = date, Protected = true, CreatedAt = now });
                }, "sex");

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
                    ["lhTests"] = (null, counts["lh"]),
                    ["fromDate"] = (null, fromDate?.ToString("yyyy-MM-dd")),
                    ["daysWritten"] = (null, daysWritten),
                    ["fieldsSkipped"] = (null, fieldsSkipped),
                    ["bbt"] = (null, counts["bbt"]),
                    ["intercourseDays"] = (null, counts["sex"]),
                });
            await db.SaveChangesAsync(ct);
            await recompute.RecomputeAsync(ct);
        }

        return new ImportResultDto(
            applied,
            cycles.Count,
            cycles.Count > 0 ? cycles[0].StartDate : null,
            cycles.Count > 0 ? cycles[^1].StartDate : null,
            plan.Values.Count(p => p.Lh is not null),
            daysWritten,
            fieldsSkipped,
            counts["bbt"],
            counts["sex"],
            counts["mucus"],
            counts["cramp"] + counts["mood"],
            cycles.Select(c => new ImportCycleDto(c.StartDate, c.PeriodDays)).ToList(),
            warnings);
    }
}
