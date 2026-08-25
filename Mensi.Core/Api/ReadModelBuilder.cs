using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Core.Api;

public sealed record ModelInput(
    IReadOnlyList<DailyLog> Logs,
    IReadOnlyList<Cycle> Cycles,
    DateOnly Today);

public static class ReadModelBuilder
{
    /// <summary>A naptár-navigáció visszamenőleges horizontja években (historikus feltöltéshez).</summary>
    public const int BackfillYears = 5;

    /// <summary>Ennyi hónapra előre navigálható a naptár az előrevetített ciklusok mentén.</summary>
    public const int ForecastMonths = 12;

    public const string ConfidenceNote =
        "A becslés a Wilcox-féle napi valószínűségeken és az ovuláció-posterioron alapul; "
        + "a sáv a lezárt ciklusok számával szűkül.";

    // ---- közös segédek -------------------------------------------------

    private static DailyLogSnapshot Snapshot(DailyLog l) => new(
        l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
        l.FlowIntensity, l.PeriodStart, l.Intercourse.Count,
        l.Intercourse.Count(i => i.Protected != true), l.LhValue);

    private static bool HasAnyEntry(DailyLog l) =>
        l.BbtCelsius is not null || l.CervicalMucus is not null || l.LhTest is not null
        || l.CrampSeverity is not null || l.FlowIntensity is not null || l.PeriodStart
        || l.Moods.Count > 0 || l.Intercourse.Count > 0;

    private static Cycle? CurrentCycle(ModelInput input) =>
        input.Cycles.LastOrDefault(c => c.StartDate <= input.Today);

    private static CyclePrediction? Predict(ModelInput input)
    {
        var current = CurrentCycle(input);
        if (current is null) return null;
        var closed = input.Cycles.Where(c => c.LengthDays is not null)
            .Select(c => new ClosedCycleStat(c.StartDate, c.LengthDays!.Value,
                c.LutealPhaseLength, c.Anovulatory, c.PredictedLengthDays))
            .ToList();
        var logs = input.Logs
            .Where(l => l.Date >= current.StartDate && l.Date <= input.Today)
            .Select(Snapshot).ToList();
        return PredictionEngine.Evaluate(
            new EngineInput(closed, current.StartDate, logs, input.Today));
    }

    /// <summary>Ciklusonkénti BBT-elemzés a kiugró-flageléshez: nap → outlier.</summary>
    private static Dictionary<DateOnly, bool> OutlierMap(ModelInput input)
    {
        var map = new Dictionary<DateOnly, bool>();
        foreach (var cycle in input.Cycles)
        {
            var end = cycle.LengthDays is int len
                ? cycle.StartDate.AddDays(len - 1)
                : input.Today;
            var cycleLogs = input.Logs
                .Where(l => l.Date >= cycle.StartDate && l.Date <= end).ToList();
            if (cycleLogs.Count == 0) continue;
            var lastDay = end.DayNumber - cycle.StartDate.DayNumber + 1;
            var byDay = cycleLogs.ToDictionary(
                l => l.Date.DayNumber - cycle.StartDate.DayNumber + 1);
            var analysis = BbtAnalyzer.Analyze(Enumerable.Range(1, lastDay)
                .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.BbtCelsius : null))
                .ToList());
            foreach (var l in cycleLogs)
                map[l.Date] = analysis.OutlierDays.Contains(
                    l.Date.DayNumber - cycle.StartDate.DayNumber + 1);
        }
        return map;
    }

    private static DailyLogDto Map(DailyLog l, bool outlier) => new(
        l.Date, l.BbtCelsius, outlier, l.CervicalMucus, l.LhTest,
        LhScale.Resolve(l.LhValue, l.LhTest), l.CrampType,
        l.CrampSeverity, l.FlowIntensity, l.PeriodStart, l.Moods,
        l.Intercourse.OrderBy(i => i.Id).Select(i => new IntercourseDto(i.Id, i.Protected)).ToList(),
        l.UpdatedAt == default ? null : l.UpdatedAt, l.UpdatedBy == "" ? null : l.UpdatedBy);

    private static DailyLogDto Empty(DateOnly date) =>
        new(date, null, false, null, null, null, null, null, null, false, [], [], null, null);

    public static IReadOnlyList<DailyLogDto> MapRange(ModelInput input, DateOnly from, DateOnly to)
    {
        var outliers = OutlierMap(input);
        return input.Logs.Where(l => l.Date >= from && l.Date <= to)
            .Select(l => Map(l, outliers.GetValueOrDefault(l.Date))).ToList();
    }

    public static DailyLogDto MapOne(ModelInput input, DateOnly date)
    {
        var log = input.Logs.FirstOrDefault(l => l.Date == date);
        return log is null ? Empty(date) : Map(log, OutlierMap(input).GetValueOrDefault(date));
    }

    private static DayCategory Categorize(ModelInput input, CyclePrediction? prediction, DateOnly date)
    {
        var firstLog = input.Logs.Count > 0 ? input.Logs[0].Date : (DateOnly?)null;
        if (firstLog is null || date < firstLog) return DayCategory.PreCycle;

        var cycle = input.Cycles.LastOrDefault(c => c.StartDate <= date);
        if (cycle is null) return DayCategory.PreCycle;

        if (cycle.LengthDays is int len)
        {
            var flowDays = input.Logs
                .Where(l => l.FlowIntensity >= FlowIntensity.Light
                            && l.Date >= cycle.StartDate && l.Date < cycle.StartDate.AddDays(len))
                .Select(l => l.Date).ToHashSet();
            return DayCategorizer.CategorizeClosed(date, cycle.StartDate, len,
                cycle.OvulationDayConfirmed ?? cycle.OvulationDayEstimated, flowDays);
        }
        return prediction?.Categorize(date) ?? DayCategory.Unknown;
    }

    private static int? CycleDayOf(ModelInput input, CyclePrediction? prediction, DateOnly date)
    {
        var cycle = input.Cycles.LastOrDefault(c => c.StartDate <= date);
        if (cycle is null) return null;
        var day = date.DayNumber - cycle.StartDate.DayNumber + 1;
        if (cycle.LengthDays is int len) return day > len ? null : day;
        // Nyitott ciklus: a becsült menstruáció után már az előrevetített ciklus számoz,
        // különben a jövőbeli napok a nyitott ciklus 60., 90. napjaként jelennének meg.
        return prediction?.ProjectedCycleDay(date) ?? day;
    }

    private static double Percent(double chance) => Math.Round(chance * 100, 1);

    // ---- overview -------------------------------------------------------

    public static OverviewDto BuildOverview(ModelInput input)
    {
        var outliers = OutlierMap(input);
        DailyLogDto MapDay(DateOnly d)
        {
            var log = input.Logs.FirstOrDefault(l => l.Date == d);
            return log is null ? Empty(d) : Map(log, outliers.GetValueOrDefault(d));
        }
        var todayLog = MapDay(input.Today);
        var yesterdayLog = MapDay(input.Today.AddDays(-1));

        var prediction = Predict(input);
        if (prediction is null)
            return new OverviewDto(input.Today, true, null, null, null, null, null, null,
                null, null, null, null, todayLog, yesterdayLog);

        var monday = input.Today.AddDays(-(((int)input.Today.DayOfWeek + 6) % 7));
        var stripFrom = monday.AddDays(-14);
        var stripDays = Enumerable.Range(0, 35).Select(i =>
        {
            var date = stripFrom.AddDays(i);
            return new StripDayDto(date, CycleDayOf(input, prediction, date),
                Categorize(input, prediction, date), date == input.Today);
        }).ToList();

        var countByDate = input.Logs.ToDictionary(l => l.Date, l => l.Intercourse.Count);
        var windowDays = new List<TimingDayDto>();
        for (var date = prediction.FertileFrom; date <= prediction.FertileTo; date = date.AddDays(1))
            windowDays.Add(new TimingDayDto(date,
                date.DayNumber - prediction.CycleStart.DayNumber + 1,
                countByDate.GetValueOrDefault(date),
                date >= prediction.OvulationFrom, date > input.Today));

        var timing = new TimingDto(prediction.Timing, Percent(prediction.Chance),
            Math.Max(prediction.FertileTo.DayNumber - input.Today.DayNumber, 0),
            windowDays.Sum(d => d.IntercourseCount), windowDays);

        return new OverviewDto(input.Today, false,
            new CycleInfoDto(prediction.CycleDay, prediction.CycleStart),
            new PhaseDto(prediction.Phase.Key, prediction.Phase.Label,
                prediction.Phase.TotalDays, prediction.Phase.ElapsedDays, prediction.Phase.RemainingDays),
            prediction.Headline,
            new WindowDto(prediction.OvulationFrom, prediction.OvulationTo),
            new WindowDto(prediction.PeriodFrom, prediction.PeriodTo),
            prediction.Confidence, prediction.PregnancyHint, prediction.MeasurementHint,
            new StripDto(stripFrom, stripFrom.AddDays(34), stripDays),
            timing, todayLog, yesterdayLog);
    }

    // ---- trends ---------------------------------------------------------

    private static TimingSummaryDto ClosedTiming(ModelInput input, Cycle cycle)
    {
        var len = cycle.LengthDays!.Value;
        var days = input.Logs
            .Where(l => l.Date >= cycle.StartDate && l.Date < cycle.StartDate.AddDays(len)
                        && l.Intercourse.Any(i => i.Protected != true))
            .Select(l => l.Date.DayNumber - cycle.StartDate.DayNumber + 1).ToList();
        var ovu = cycle.OvulationDayConfirmed ?? cycle.OvulationDayEstimated ?? Math.Max(len - 14, 1);
        var chance = WilcoxKernel.RetroChance(ovu, days);
        return new TimingSummaryDto(WilcoxKernel.Label(chance), Percent(chance));
    }

    public static TrendsDto BuildTrends(ModelInput input)
    {
        var closed = input.Cycles.Where(c => c.LengthDays is not null).ToList();
        TrendsStatsDto? stats = null;
        if (closed.Count > 0)
        {
            var s = CycleStats.Compute(closed.Select(c => new ClosedCycleStat(
                c.StartDate, c.LengthDays!.Value, c.LutealPhaseLength, c.Anovulatory,
                c.PredictedLengthDays)).ToList())!;
            var window = closed.TakeLast(6).ToList();
            var totalDays = window.Sum(c => c.LengthDays!.Value);
            var loggedDays = window.Sum(c => input.Logs.Count(l =>
                l.Date >= c.StartDate && l.Date < c.StartDate.AddDays(c.LengthDays!.Value)
                && HasAnyEntry(l)));
            stats = new TrendsStatsDto(
                Math.Round(s.MeanLength, 1), s.MinLength, s.MaxLength,
                Math.Round(s.StdDevLength, 1),
                s.MeanLuteal is null ? null : Math.Round(s.MeanLuteal.Value, 1),
                totalDays == 0 ? 0 : (int)Math.Round(100.0 * loggedDays / totalDays));
        }

        var cycles = closed
            .OrderByDescending(c => c.StartDate)
            .Select(c => new TrendCycleDto(c.StartDate, c.LengthDays!.Value,
                stats is null ? 0 : (int)Math.Round(c.LengthDays.Value - stats.AverageLength),
                c.LutealPhaseLength, c.Anovulatory, ClosedTiming(input, c)))
            .ToList();

        TrendsBbtDto? bbt = null;
        var prediction = Predict(input);
        var current = CurrentCycle(input);
        if (prediction is not null && current is not null)
        {
            var a = prediction.Bbt;
            var byDay = input.Logs
                .Where(l => l.Date >= current.StartDate && l.Date <= input.Today)
                .ToDictionary(l => l.Date.DayNumber - current.StartDate.DayNumber + 1);
            var rows = Enumerable.Range(1, prediction.CycleDay).Select(d =>
            {
                byDay.TryGetValue(d, out var log);
                var value = log?.BbtCelsius;
                return new BbtRowDto(current.StartDate.AddDays(d - 1), d, value,
                    value is not null && a.Coverline is not null ? value - a.Coverline : null,
                    a.OutlierDays.Contains(d), a.AboveCoverlineDays.Contains(d),
                    new BbtMarksDto(log?.CervicalMucus, log?.LhTest,
                        LhScale.Resolve(log?.LhValue, log?.LhTest)));
            }).ToList();
            bbt = new TrendsBbtDto(a.Coverline, a.ConfirmedOvulationDay is not null,
                a.ConfirmedOvulationDay is int o ? current.StartDate.AddDays(o - 1) : null,
                a.OutlierDays.Count, a.MissingDays.Count, rows);
        }

        return new TrendsDto(stats, cycles, bbt);
    }

    // ---- calendar ---------------------------------------------------------

    public static CalendarDto BuildCalendar(ModelInput input, int year, int month)
    {
        var prediction = Predict(input);
        var first = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var logByDate = input.Logs.ToDictionary(l => l.Date);

        var days = Enumerable.Range(0, daysInMonth).Select(i =>
        {
            var date = first.AddDays(i);
            logByDate.TryGetValue(date, out var log);
            return new CalendarDayDto(date, CycleDayOf(input, prediction, date),
                Categorize(input, prediction, date),
                log?.BbtCelsius is not null, log?.Intercourse.Count ?? 0,
                log is not null && HasAnyEntry(log), date == input.Today,
                prediction?.ProjectedFor(date) is not null);
        }).ToList();

        // A naptár visszamenőleg is bejárható: az alsó határ a legkorábbi bejegyzés vagy
        // 5 év vissza — amelyik korábbi. Az első feltöltéskor (üres DB) e nélkül nem lehetne
        // múltbeli hónapra navigálni és historikus adatot rögzíteni.
        var backfillHorizon = input.Today.AddYears(-BackfillYears);
        var firstLog = input.Logs.Count > 0 ? input.Logs[0].Date : input.Today;
        var firstMonth = firstLog < backfillHorizon ? firstLog : backfillHorizon;
        // Előre annyi hónap járható be, ameddig az előrevetített ciklusok érnek — a naptár
        // a jövőbeli menstruációt és ovulációt is megmutatja.
        var lastMonth = input.Today.AddMonths(ForecastMonths);
        return new CalendarDto(
            $"{year:D4}-{month:D2}",
            new MonthRangeDto($"{firstMonth.Year:D4}-{firstMonth.Month:D2}",
                $"{lastMonth.Year:D4}-{lastMonth.Month:D2}"),
            input.Today.Year == year && input.Today.Month == month
                ? CycleDayOf(input, prediction, input.Today) : null,
            days.Any(d => d.HasAnyEntry), days);
    }

    // ---- chance -----------------------------------------------------------

    public static ChanceDto BuildChance(ModelInput input)
    {
        var prediction = Predict(input);
        if (prediction is null)
            return new ChanceDto(true, null, null, null, null, null, null);

        var fertileLogs = input.Logs
            .Where(l => l.Date >= prediction.FertileFrom && l.Date <= prediction.FertileTo)
            .ToList();
        var unprotectedDays = fertileLogs
            .Where(l => l.Intercourse.Any(i => i.Protected != true))
            .Select(l => l.Date).OrderBy(d => d).ToList();
        var eventCount = fertileLogs.Sum(l => l.Intercourse.Count(i => i.Protected != true));

        string explanation;
        if (eventCount == 0)
        {
            explanation = "Ebben a ciklusban még nincs együttlét a termékeny ablakban.";
        }
        else
        {
            var rels = unprotectedDays
                .Select(d => prediction.OvulationP50.DayNumber - d.DayNumber)
                .Distinct().OrderByDescending(r => r).ToList();
            explanation = rels.All(r => r > 0)
                ? $"{eventCount} együttlét esik a termékeny ablakba, {unprotectedDays.Count} külön napon "
                  + $"— a becsült ovuláció előtt {string.Join(" és ", rels)} nappal."
                : $"{eventCount} együttlét esik a termékeny ablakba, {unprotectedDays.Count} külön napon.";
        }

        var countByDate = input.Logs.ToDictionary(l => l.Date, l => l.Intercourse.Count);
        var days = new List<FertileDayDto>();
        for (var date = prediction.FertileFrom; date <= prediction.FertileTo; date = date.AddDays(1))
            days.Add(new FertileDayDto(date,
                date.DayNumber - prediction.CycleStart.DayNumber + 1,
                countByDate.GetValueOrDefault(date), date > input.Today, date == input.Today));

        var ovuTotal = prediction.OvulationTo.DayNumber - prediction.OvulationFrom.DayNumber + 1;
        var ovuElapsed = Math.Clamp(
            input.Today.DayNumber - prediction.OvulationFrom.DayNumber + 1, 0, ovuTotal);

        var closed = input.Cycles.Where(c => c.LengthDays is not null)
            .OrderByDescending(c => c.StartDate)
            .Select(c => new ChanceHistoryCycleDto(c.StartDate, ClosedTiming(input, c)))
            .ToList();

        return new ChanceDto(false,
            new TimingSummaryDto(prediction.Timing, Percent(prediction.Chance)),
            explanation, ConfidenceNote,
            new FertileWindowDto(
                Math.Max(prediction.FertileTo.DayNumber - input.Today.DayNumber, 0),
                ovuTotal, ovuElapsed, days),
            prediction.WhatIfHint,
            new ChanceHistoryDto(closed.Count(c => c.Timing.Label == TimingLabel.Good),
                closed.Count, closed));
    }
}
