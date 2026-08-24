using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class BbtAnalyzerTests
{
    private static IReadOnlyList<BbtDay> Series(params decimal?[] values) =>
        values.Select((v, i) => new BbtDay(i + 1, v)).ToList();

    [Fact]
    public void Three_consecutive_highs_confirm_ovulation()
    {
        // 1..9 alacsony (utolsó 6 maximuma 36.42), 10..12 magas: mind > 36.62
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m, 36.70m));
        Assert.Equal(36.42m, a.Coverline);
        Assert.Equal(9, a.ConfirmedOvulationDay);
        Assert.Equal([10, 11, 12], a.AboveCoverlineDays.Order());
    }

    [Fact]
    public void Two_highs_do_not_confirm_and_coverline_is_provisional()
    {
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m));
        Assert.Null(a.ConfirmedOvulationDay);
        Assert.NotNull(a.Coverline); // provizórikus: az utolsó 6 érvényes érték maximuma
    }

    [Fact]
    public void Lone_spike_is_excluded_as_outlier()
    {
        // az 5. nap 36.85 kiugrás a 36.3x-os környezetben, a szomszédok nem trendtársak
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.85m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m, 36.70m));
        Assert.Contains(5, a.OutlierDays);
        Assert.Equal(36.42m, a.Coverline);   // a kiugrás nem emeli meg a coverline-t
        Assert.Equal(9, a.ConfirmedOvulationDay);
    }

    [Fact]
    public void Rising_trend_is_not_an_outlier()
    {
        // a 10. nap magas, de a 11–12. is: trend része, nem magányos kiugrás
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.70m, 36.68m, 36.72m));
        Assert.DoesNotContain(10, a.OutlierDays);
    }

    [Fact]
    public void Missing_days_are_listed_and_do_not_break_confirmation()
    {
        // a 11. nap hiányzik; a 10., 12., 13. mérés így is 3 egymást követő ÉRVÉNYES érték
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, null, 36.66m, 36.70m));
        Assert.Equal([11], a.MissingDays.Order());
        Assert.Equal(9, a.ConfirmedOvulationDay);
    }

    [Fact]
    public void Too_few_values_yield_no_coverline()
    {
        var a = BbtAnalyzer.Analyze(Series(36.30m, 36.35m, null, 36.40m));
        Assert.Null(a.Coverline);
        Assert.Null(a.ConfirmedOvulationDay);
        Assert.Equal(3, a.ValidCount);
    }
}
