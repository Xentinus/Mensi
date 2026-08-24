using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public static class PeriodDistribution
{
    public const int LutealMin = 9, LutealMax = 18;

    public static (int P15, int P50, int P85) NextPeriod(
        Posterior ovulation, double lutealMean, double lutealVariance)
    {
        // Diszkretizált, [9,18]-ra vágott luteális eloszlás.
        var v = Math.Max(lutealVariance, 0.0001);
        var luteal = new double[LutealMax - LutealMin + 1];
        for (var i = 0; i < luteal.Length; i++)
        {
            var x = LutealMin + i - lutealMean;
            luteal[i] = Math.Exp(-x * x / (2 * v));
        }
        var lutealSum = luteal.Sum();

        var minDay = Posterior.GridMin + LutealMin;
        var maxDay = Posterior.GridMax + LutealMax;
        var period = new double[maxDay - minDay + 1];
        for (var o = Posterior.GridMin; o <= Posterior.GridMax; o++)
        {
            var po = ovulation[o];
            if (po <= 0) continue;
            for (var i = 0; i < luteal.Length; i++)
                period[o + LutealMin + i - minDay] += po * luteal[i] / lutealSum;
        }

        return (Quantile(0.15), Quantile(0.50), Quantile(0.85));

        int Quantile(double q)
        {
            var total = period.Sum();
            double cum = 0;
            for (var i = 0; i < period.Length; i++)
            {
                cum += period[i];
                if (cum >= q * total) return minDay + i;
            }
            return maxDay;
        }
    }
}

public static class ConfidenceRule
{
    public static ConfidenceLevel From(int widthDays, int closedCycleCount)
    {
        var level = widthDays <= 4 ? ConfidenceLevel.High
            : widthDays <= 7 ? ConfidenceLevel.Medium
            : ConfidenceLevel.Low;
        if (closedCycleCount < 3 && level == ConfidenceLevel.High) level = ConfidenceLevel.Medium;
        return level;
    }
}
