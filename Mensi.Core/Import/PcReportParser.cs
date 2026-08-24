using System.Text.RegularExpressions;
using Mensi.Core.Domain;

namespace Mensi.Core.Import;

public sealed record PcCycle(DateOnly StartDate, int PeriodDays);

public sealed record PcLhTest(DateOnly Date, LhTest Result);

public sealed record PcReportData(
    IReadOnlyList<PcCycle> Cycles,
    IReadOnlyList<PcLhTest> LhTests,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Period Tracker / Period Calendar PDF-riport szöveges sorainak értelmezése.
/// Csak a szövegként jelen lévő adatot dolgozza fel: a ciklustörténetet (kezdet +
/// menstruáció-hossz) és az ovulációs teszteket — a grafikonok raszterképek, azokból
/// napi adat nem nyerhető ki megbízhatóan.
/// </summary>
public static class PcReportParser
{
    // "Jun 12 - Jul 22 5 Days 41 Days" | "Jul 23 - Today 6 Days" | "Dec 25,2025 - Jan 28 4 Days 35 Days"
    private static readonly Regex CycleRow = new(
        @"^(?<start>[A-Z][a-z]{2} \d{1,2}(?:,\d{4})?) - (?:Today|[A-Z][a-z]{2} \d{1,2}(?:,\d{4})?) (?<period>\d+) Days?(?: \d+ Days?)?$",
        RegexOptions.Compiled);

    // "CD 12 Apr 15 Negative" (opcionális idő-tokennel a dátum után)
    private static readonly Regex LhRow = new(
        @"^CD (?<cd>\d+) (?<date>[A-Z][a-z]{2} \d{1,2}(?:,\d{4})?)(?: \d{1,2}:\d{2})? (?<result>Negative|Low|High|Peak|Positive)$",
        RegexOptions.Compiled);

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static PcReportData Parse(IReadOnlyList<string> lines)
    {
        var warnings = new List<string>();
        var rawCycles = new List<(int Month, int Day, int? Year, int PeriodDays)>();
        var rawLh = new List<(int Cd, int Month, int Day, int? Year, LhTest Result)>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var cycleMatch = CycleRow.Match(line);
            if (cycleMatch.Success)
            {
                var (m, d, y) = ParseDateToken(cycleMatch.Groups["start"].Value);
                var period = int.Parse(cycleMatch.Groups["period"].Value);
                if (period is < 1 or > 14)
                {
                    warnings.Add($"Gyanús menstruáció-hossz ({period} nap) a(z) {cycleMatch.Groups["start"].Value} kezdetű sorban — kihagyva.");
                    continue;
                }
                rawCycles.Add((m, d, y, period));
                continue;
            }

            var lhMatch = LhRow.Match(line);
            if (lhMatch.Success)
            {
                var (m, d, y) = ParseDateToken(lhMatch.Groups["date"].Value);
                rawLh.Add((int.Parse(lhMatch.Groups["cd"].Value), m, d, y, MapLh(lhMatch.Groups["result"].Value)));
            }
        }

        // A riport a ciklusokat legújabbtól a legrégebbiig listázza — kronologikus sorrendbe
        // fordítva az évek az explicit (",2025" alakú) horgonyokból és a folytonosságból adódnak.
        rawCycles.Reverse();
        var cycles = new List<PcCycle>();
        DateOnly? previous = null;
        foreach (var (month, day, year, period) in rawCycles)
        {
            DateOnly start;
            if (year is int explicitYear)
            {
                start = new DateOnly(explicitYear, month, day);
                if (previous is not null && start <= previous)
                    warnings.Add($"Az explicit évszámú {start:yyyy-MM-dd} kezdet nem követi az előző ciklust — az explicit érték maradt.");
            }
            else if (previous is DateOnly prev)
            {
                start = new DateOnly(prev.Year, month, day);
                if (start <= prev) start = start.AddYears(1);
            }
            else
            {
                warnings.Add("A legrégebbi cikluskezdeten nincs évszám — a ciklustörténet nem dolgozható fel.");
                break;
            }

            if (cycles.All(c => c.StartDate != start)) cycles.Add(new PcCycle(start, period));
            previous = start;
        }

        // LH-teszt éve: a CD (ciklusnap) + valamelyik cikluskezdet egyezéséből.
        var lhTests = new List<PcLhTest>();
        foreach (var (cd, month, day, year, result) in rawLh)
        {
            var candidates = cycles
                .Select(c => c.StartDate.AddDays(cd - 1))
                .Where(e => e.Month == month && e.Day == day && (year is null || e.Year == year))
                .Distinct()
                .ToList();
            if (candidates.Count == 1)
            {
                if (lhTests.All(t => t.Date != candidates[0]))
                    lhTests.Add(new PcLhTest(candidates[0], result));
            }
            else
            {
                warnings.Add($"A CD {cd} / {Months[month - 1]} {day} ovulációs teszt nem illeszthető egyértelműen ciklushoz — kihagyva.");
            }
        }

        return new PcReportData(cycles, lhTests, warnings);
    }

    private static (int Month, int Day, int? Year) ParseDateToken(string token)
    {
        // "Jul 8,2025" vagy "Jul 8"
        var parts = token.Split(',');
        var monthDay = parts[0].Split(' ');
        var month = Array.IndexOf(Months, monthDay[0]) + 1;
        return (month, int.Parse(monthDay[1]), parts.Length > 1 ? int.Parse(parts[1]) : null);
    }

    private static LhTest MapLh(string result) => result switch
    {
        "Peak" => LhTest.Peak,
        "High" or "Positive" => LhTest.Positive,
        _ => LhTest.Negative, // Negative, Low
    };
}
