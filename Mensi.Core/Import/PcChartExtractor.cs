using System.Text.RegularExpressions;
using Mensi.Core.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Mensi.Core.Import;

/// <summary>Egy nap kinyert jelei a Period Tracker grafikon-oldalairól.</summary>
public sealed record PcDailyData(
    DateOnly Date,
    FlowIntensity? Flow,
    decimal? Bbt,
    CervicalMucus? Mucus,
    bool Cramps,
    bool Spotting,
    IReadOnlyList<Mood> Moods,
    LhTest? Lh,
    int UnprotectedSex,
    int ProtectedSex);

/// <summary>
/// A Period Tracker riport grafikon-oldalainak vektoros jelölőit (pöttyeit) olvassa ki:
/// a rács szabályos — a nap-oszlopok x-e a fejléc-számokból, a kategória-sorok y-a a
/// bal oldali címkékből adódik, a hőmérséklet a tengelyfeliratokból lineárisan
/// interpolálható. Csak a „Temperature" elrendezésű oldalakat dolgozza fel (a
/// Weight/Sleep oldalak Mensi-mező nélküliek).
/// </summary>
public static class PcChartExtractor
{
    private static readonly Regex HeaderRange = new(
        @"([A-Z][a-z]{2} \d{1,2}(?:,\d{4})?) - (?:Today|[A-Z][a-z]{2} \d{1,2}(?:,\d{4})?) \(\d+ Days?\)",
        RegexOptions.Compiled);

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private const double ColumnTolerance = 12;
    private const double RowTolerance = 9;
    private const double MinDot = 4, MaxDot = 16;

    public static (IReadOnlyList<PcDailyData> Daily, IReadOnlyList<string> Warnings) Extract(
        byte[] pdf, IReadOnlyList<PcCycle> cycles)
    {
        var warnings = new List<string>();
        // (dátum, sor) → érték; a naponkénti több pötty (két oldalon átlógó rács) összeolvad.
        var flow = new Dictionary<DateOnly, FlowIntensity>();
        var bbt = new Dictionary<DateOnly, decimal>();
        var mucus = new Dictionary<DateOnly, CervicalMucus>();
        var cramps = new HashSet<DateOnly>();
        var spotting = new HashSet<DateOnly>();
        var moods = new Dictionary<DateOnly, HashSet<Mood>>();
        var lh = new Dictionary<DateOnly, LhTest>();
        var sexUnprotected = new HashSet<DateOnly>();
        var sexProtected = new HashSet<DateOnly>();

        using var document = PdfDocument.Open(pdf);
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (!text.Contains("Temperature")) continue;

            var headerMatch = HeaderRange.Match(text);
            if (!headerMatch.Success) continue;
            var cycleStart = ResolveCycleStart(headerMatch.Groups[1].Value, cycles);
            if (cycleStart is not DateOnly start)
            {
                warnings.Add($"A(z) „{headerMatch.Groups[1].Value}” kezdetű grafikon-oldal nem illeszthető ciklushoz — kihagyva.");
                continue;
            }

            var words = page.GetWords().ToList();
            var columns = FindDayColumns(words);
            if (columns.Count == 0) continue;
            var rows = FindRows(words);
            var axis = FindTemperatureAxis(words);

            foreach (var (x, y) in FindDots(page))
            {
                var column = columns.MinBy(c => Math.Abs(c.X - x));
                if (Math.Abs(column.X - x) > ColumnTolerance) continue;
                var date = start.AddDays(column.Day - 1);

                if (axis is { } a && y <= a.Top + 8 && y >= a.Bottom - 8)
                {
                    // y-up: a 37.5 felirat FELÜL van (nagyobb y), a 35.5 alul. A visszamért
                    // érték pontossága a szöveg-boundingbox miatt ~±0,01–0,02 °C.
                    var celsius = 37.5 - (a.Top - y) / ((a.Top - a.Bottom) / 2.0);
                    var value = Math.Clamp(Math.Round((decimal)celsius, 2), 35.00m, 38.99m);
                    bbt[date] = value;
                    continue;
                }

                var row = rows.MinBy(r => Math.Abs(r.Y - y));
                if (row is null || Math.Abs(row.Y - y) > RowTolerance) continue;

                switch (row.Kind)
                {
                    case RowKind.Flow:
                        var intensity = (FlowIntensity)row.Value;
                        if (!flow.TryGetValue(date, out var existing) || intensity > existing)
                            flow[date] = intensity;
                        break;
                    case RowKind.Sex:
                        if (row.Value == 1) sexUnprotected.Add(date);
                        else sexProtected.Add(date);
                        break;
                    case RowKind.Mucus:
                        var m = (CervicalMucus)row.Value;
                        if (!mucus.TryGetValue(date, out var existingMucus) || m > existingMucus)
                            mucus[date] = m;
                        break;
                    case RowKind.Cramps:
                        cramps.Add(date);
                        break;
                    case RowKind.Spotting:
                        spotting.Add(date);
                        break;
                    case RowKind.Mood:
                        (moods.TryGetValue(date, out var set) ? set : moods[date] = []).Add((Mood)row.Value);
                        break;
                    case RowKind.Lh:
                        var result = (LhTest)row.Value;
                        if (!lh.TryGetValue(date, out var existingLh) || result > existingLh)
                            lh[date] = result;
                        break;
                }
            }
        }

        var dates = flow.Keys
            .Concat(bbt.Keys).Concat(mucus.Keys).Concat(cramps).Concat(spotting)
            .Concat(moods.Keys).Concat(lh.Keys).Concat(sexUnprotected).Concat(sexProtected)
            .Distinct().OrderBy(d => d);

        var daily = dates.Select(d => new PcDailyData(
            d,
            flow.TryGetValue(d, out var f) ? f : null,
            bbt.TryGetValue(d, out var t) ? t : null,
            mucus.TryGetValue(d, out var mu) ? mu : null,
            cramps.Contains(d),
            spotting.Contains(d),
            moods.TryGetValue(d, out var mo) ? mo.OrderBy(x => x).ToList() : [],
            lh.TryGetValue(d, out var l) ? l : null,
            sexUnprotected.Contains(d) ? 1 : 0,
            sexProtected.Contains(d) ? 1 : 0)).ToList();

        return (daily, warnings);
    }

    private enum RowKind { Flow, Sex, Mucus, Cramps, Spotting, Mood, Lh }

    private sealed record Row(RowKind Kind, int Value, double Y);

    private sealed record Column(int Day, double X);

    private static DateOnly? ResolveCycleStart(string token, IReadOnlyList<PcCycle> cycles)
    {
        var parts = token.Split(',');
        var monthDay = parts[0].Split(' ');
        var month = Array.IndexOf(Months, monthDay[0]) + 1;
        var day = int.Parse(monthDay[1]);
        int? year = parts.Length > 1 ? int.Parse(parts[1]) : null;

        var matches = cycles
            .Where(c => c.StartDate.Month == month && c.StartDate.Day == day
                        && (year is null || c.StartDate.Year == year))
            .ToList();
        return matches.Count == 1 ? matches[0].StartDate : null;
    }

    /// <summary>Nap-oszlopok: a „Cycle Details" felirat sorában álló számok (1..60).</summary>
    private static List<Column> FindDayColumns(List<Word> words)
    {
        var anchor = words.FirstOrDefault(w => w.Text == "Details");
        if (anchor is null) return [];
        var y = Center(anchor).Y;
        return words
            .Where(w => int.TryParse(w.Text, out var n) && n is >= 1 and <= 60
                        && Math.Abs(Center(w).Y - y) < 10)
            .Select(w => new Column(int.Parse(w.Text), Center(w).X))
            .ToList();
    }

    /// <summary>Kategória-sorok a bal oldali címkékből. Az azonos szövegű címkék (Dry)
    /// y szerint csökkenő sorrendben (felülről lefelé) kapnak jelentést: az első a nyák,
    /// a második a tünet-sor.</summary>
    private static List<Row> FindRows(List<Word> words)
    {
        var labels = words.Where(w => Center(w).X < 250).ToList();

        List<double> YsOf(string text) => labels.Where(w => w.Text == text)
            .Select(w => Center(w).Y).OrderByDescending(y => y).ToList();

        var rows = new List<Row>();
        void Add(RowKind kind, int value, string label, int occurrence = 0)
        {
            var ys = YsOf(label);
            if (ys.Count > occurrence) rows.Add(new Row(kind, value, ys[occurrence]));
        }

        Add(RowKind.Flow, (int)FlowIntensity.Light, "Light");
        Add(RowKind.Flow, (int)FlowIntensity.Medium, "Medium");
        Add(RowKind.Flow, (int)FlowIntensity.Heavy, "Heavy");
        Add(RowKind.Flow, (int)FlowIntensity.Heavy, "Disaster"); // Mensi-ben az erős a maximum
        Add(RowKind.Sex, 0, "Protected");
        Add(RowKind.Sex, 1, "Unprotected");
        Add(RowKind.Mucus, (int)CervicalMucus.Dry, "Dry");            // 1. Dry = nyák
        Add(RowKind.Mucus, (int)CervicalMucus.Sticky, "Sticky");
        Add(RowKind.Mucus, (int)CervicalMucus.Creamy, "Creamy");
        Add(RowKind.Mucus, (int)CervicalMucus.Creamy, "Watery");      // Watery → nedves
        Add(RowKind.Mucus, (int)CervicalMucus.EggWhite, "Egg");       // "Egg White" első szava
        Add(RowKind.Cramps, 0, "Cramps");
        Add(RowKind.Spotting, 0, "Spotting/bleeding");
        Add(RowKind.Mood, (int)Mood.Sad, "Sad");
        Add(RowKind.Mood, (int)Mood.Longing, "Horny");
        Add(RowKind.Mood, (int)Mood.Tired, "Exhausted");
        Add(RowKind.Mood, (int)Mood.Cheerful, "Happy");
        Add(RowKind.Lh, (int)LhTest.Negative, "Low");
        Add(RowKind.Lh, (int)LhTest.Positive, "High");
        Add(RowKind.Lh, (int)LhTest.Peak, "Peak");
        return rows;
    }

    private sealed record TempAxis(double Top, double Bottom); // 37.5 és 35.5 y-a (y-up)

    private static TempAxis? FindTemperatureAxis(List<Word> words)
    {
        double? top = null, bottom = null;
        foreach (var w in words)
        {
            if (w.Text == "37.5") top = Center(w).Y;
            if (w.Text == "35.5") bottom = Center(w).Y;
        }
        return top is double t && bottom is double b && t > b ? new TempAxis(t, b) : null;
    }

    /// <summary>Adat-pöttyök: kis méretű, kitöltött vektor-útvonalak.</summary>
    private static IEnumerable<(double X, double Y)> FindDots(Page page)
    {
        foreach (var path in page.Paths)
        {
            if (!path.IsFilled) continue;
            var box = path.GetBoundingRectangle();
            if (box is null) continue;
            var b = box.Value;
            if (b.Width is < MinDot or > MaxDot || b.Height is < MinDot or > MaxDot) continue;
            yield return ((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2);
        }
    }

    private static (double X, double Y) Center(Word w) =>
        ((w.BoundingBox.Left + w.BoundingBox.Right) / 2,
         (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2);
}
