using Mensi.Core.Api;
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests;

public class ReadModelBuilderTests
{
    private static readonly DateOnly CurStart = new(2026, 8, 10);
    private static readonly DateOnly Today = new(2026, 8, 23); // ciklus 14. napja

    /// <summary>2 lezárt ciklus (28 nap, ovuláció ~14. nap, BBT-vel megerősítve) + nyitott
    /// ciklus 14 nappal: LH-csúcs a 13., nyúlós nyák a 11–14., együttlét a 12. napon.</summary>
    private static ModelInput Fixture()
    {
        var logs = new List<DailyLog>();
        void AddCycle(DateOnly start, int? length)
        {
            var days = length ?? (Today.DayNumber - start.DayNumber + 1);
            for (var d = 1; d <= days; d++)
            {
                var date = start.AddDays(d - 1);
                var log = new DailyLog
                {
                    Date = date,
                    PeriodStart = d == 1,
                    FlowIntensity = d <= 5 ? FlowIntensity.Medium : null,
                    BbtCelsius = length is null
                        ? 36.30m + (d % 3) * 0.03m                      // nyitott: még alacsony
                        : d <= 14 ? 36.30m + (d % 3) * 0.03m : 36.70m,  // lezárt: shift a 15. naptól
                    CervicalMucus = length is null && d >= 11 ? CervicalMucus.EggWhite : null,
                    LhTest = length is null && d == 13 ? LhTest.Peak : null,
                    UpdatedBy = "a@b.hu",
                };
                if (length is null && d == 12)
                    log.Intercourse.Add(new IntercourseEvent { Date = date, Protected = false });
                logs.Add(log);
            }
        }
        AddCycle(CurStart.AddDays(-56), 28);
        AddCycle(CurStart.AddDays(-28), 28);
        AddCycle(CurStart, null);

        // A cycle tábla tartalmát a deriver adja — a builder-teszt a teljes láncot fedi.
        var snapshots = logs.Select(l => new DailyLogSnapshot(
            l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
            l.FlowIntensity, l.PeriodStart, l.Intercourse.Count,
            l.Intercourse.Count(i => i.Protected != true))).ToList();
        var cycles = CycleDeriver.Derive(snapshots).Select(d => new Cycle
        {
            StartDate = d.Start, LengthDays = d.LengthDays,
            OvulationDayConfirmed = d.OvulationConfirmed, OvulationDayEstimated = d.OvulationEstimated,
            LutealPhaseLength = d.LutealLength, Anovulatory = d.Anovulatory,
        }).ToList();

        return new ModelInput(logs, cycles, Today);
    }

    [Fact]
    public void Overview_has_all_view_state()
    {
        var o = ReadModelBuilder.BuildOverview(Fixture());
        Assert.False(o.IsEmpty);
        Assert.Equal(14, o.Cycle!.Day);
        Assert.NotNull(o.Headline);
        Assert.True(o.OvulationWindow!.From <= o.OvulationWindow.To);
        Assert.True(o.NextPeriodWindow!.From > o.OvulationWindow.To);
        Assert.Equal(35, o.Strip!.Days.Count);
        Assert.Equal(DayOfWeek.Monday, o.Strip.From.DayOfWeek);
        Assert.Contains(o.Strip.Days, d => d.IsToday);
        Assert.Contains(o.Timing!.WindowDays, d => d.IntercourseCount == 1);
        Assert.True(o.Timing.ChancePercent > 0);
        Assert.Equal(Today, o.TodayLog!.Date);
        Assert.Equal(Today.AddDays(-1), o.YesterdayLog!.Date);
    }

    [Fact]
    public void Overview_without_closed_cycles_is_empty_state()
    {
        var logs = new List<DailyLog>
        {
            new() { Date = Today.AddDays(-2), PeriodStart = true, FlowIntensity = FlowIntensity.Heavy },
        };
        var input = new ModelInput(logs,
            [new Cycle { StartDate = Today.AddDays(-2) }], Today);
        var o = ReadModelBuilder.BuildOverview(input);
        Assert.True(o.IsEmpty);
        Assert.Null(o.OvulationWindow);
        Assert.NotNull(o.TodayLog); // a sheet előtöltéséhez üresen is jár
    }

    [Fact]
    public void Trends_stats_history_and_bbt_rows()
    {
        var t = ReadModelBuilder.BuildTrends(Fixture());
        Assert.Equal(28, t.Stats!.AverageLength, 3);
        Assert.Equal(2, t.Cycles.Count);
        Assert.True(t.Cycles[0].StartDate > t.Cycles[1].StartDate); // legújabb elöl
        Assert.All(t.Cycles, c => Assert.Equal(14, c.LutealLength));
        Assert.InRange(t.Stats.LoggedPercent, 1, 100);
        Assert.Equal(14, t.Bbt!.Rows.Count);              // nyitott ciklus 14 napja
        Assert.False(t.Bbt.OvulationConfirmed);           // a nyitott ciklusban még nincs shift
        Assert.All(t.Bbt.Rows, r => Assert.Equal(r.Date.DayNumber - CurStart.DayNumber + 1, r.CycleDay));
    }

    [Fact]
    public void Calendar_categorizes_past_and_future()
    {
        var c = ReadModelBuilder.BuildCalendar(Fixture(), 2026, 8);
        Assert.Equal("2026-08", c.Month);
        Assert.True(c.HasData);
        Assert.Equal(14, c.CycleDayOfToday);
        Assert.Equal(31, c.Days.Count);
        Assert.Equal(DayCategory.Menstruation, c.Days.Single(d => d.Date == CurStart).Category);
        Assert.Contains(c.Days, d => d.Category is DayCategory.Ovulation or DayCategory.Fertile);
        // A backfill-horizont (5 év) korábbi, mint az első bejegyzés → az nyer.
        Assert.Equal("2021-08", c.Range.FirstMonth);
        Assert.Equal("2026-09", c.Range.LastMonth);
    }

    [Fact]
    public void Chance_explains_and_lists_history()
    {
        var ch = ReadModelBuilder.BuildChance(Fixture());
        Assert.False(ch.IsEmpty);
        Assert.NotNull(ch.Timing);
        Assert.Contains("együttlét", ch.Explanation!);
        Assert.Equal(2, ch.History!.TotalCount);
        Assert.True(ch.FertileWindow!.Days.Count >= 6);
        Assert.Contains(ch.FertileWindow.Days, d => d.IntercourseCount == 1);
    }

    [Fact]
    public void Map_one_returns_empty_dto_for_missing_day()
    {
        var dto = ReadModelBuilder.MapOne(Fixture(), Today.AddDays(-100));
        Assert.Null(dto.BbtCelsius);
        Assert.Empty(dto.Intercourse);
        Assert.False(dto.PeriodStart);
    }
}
