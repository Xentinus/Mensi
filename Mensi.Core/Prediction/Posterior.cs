namespace Mensi.Core.Prediction;

/// <summary>Diszkrét eloszlás az ovulációs nap fölött a [GridMin, GridMax] ciklusnap-rácson.</summary>
public sealed class Posterior
{
    public const int GridMin = 6, GridMax = 40;
    private readonly double[] _p; // index 0 = GridMin

    private Posterior(double[] p) => _p = p;

    public double this[int day] =>
        day < GridMin || day > GridMax ? 0 : _p[day - GridMin];

    public double Sum => _p.Sum();

    public int Quantile(double q)
    {
        double cum = 0;
        for (var i = 0; i < _p.Length; i++)
        {
            cum += _p[i];
            if (cum >= q) return GridMin + i;
        }
        return GridMax;
    }

    public static Posterior FromNormal(double mean, double variance)
    {
        var v = Math.Max(variance, 0.25); // degenerált prior ellen
        var p = new double[GridMax - GridMin + 1];
        for (var i = 0; i < p.Length; i++)
        {
            var x = GridMin + i - mean;
            p[i] = Math.Exp(-x * x / (2 * v));
        }
        return new Posterior(Normalize(p));
    }

    public static Posterior FromPointMass(int day)
    {
        var p = new double[GridMax - GridMin + 1];
        p[Math.Clamp(day, GridMin, GridMax) - GridMin] = 1;
        return new Posterior(p);
    }

    public Posterior Reweighted(Func<int, double> factor)
    {
        var p = new double[_p.Length];
        for (var i = 0; i < p.Length; i++) p[i] = _p[i] * factor(GridMin + i);
        // Ha minden jel kioltja egymást, a súlyozatlan eloszlás marad — a modell nem "hal meg".
        return p.Sum() < 1e-12 ? this : new Posterior(Normalize(p));
    }

    private static double[] Normalize(double[] p)
    {
        var sum = p.Sum();
        if (sum <= 0) return p;
        for (var i = 0; i < p.Length; i++) p[i] /= sum;
        return p;
    }
}
