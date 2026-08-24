namespace Mensi.Core.Prediction;

public static class PopulationPriors
{
    public const double CycleMean = 28, CycleVar = 16;   // Normal(28, 4²)
    public const double LutealMean = 14, LutealVar = 4;  // Normal(14, 2²)
}

/// <summary>Normál-normál konjugált frissítés (spec 4.2): kevés saját ciklusnál a populációs
/// prior dominál, sok adatnál a személyes átlag. A Variance prediktív: posterior + s²_within.</summary>
public static class Shrinkage
{
    public static (double Mean, double Variance) Apply(
        double popMean, double popVar, int n, double sampleMean, double sampleVar)
    {
        if (n <= 0) return (popMean, popVar);
        var s2 = n < 2 || sampleVar <= 0 ? popVar : sampleVar;
        var precision = n / s2 + 1 / popVar;
        var mean = (n * sampleMean / s2 + popMean / popVar) / precision;
        return (mean, 1 / precision + s2);
    }
}
