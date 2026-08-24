using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Mensi.Core.Import;

/// <summary>Vékony PdfPig-adapter: PDF bájtokból oldalankénti szövegsorok. A soron belüli
/// térközöket egyetlen szóközre normalizálja, mert a kinyert szöveg térközei a PDF
/// belső elrendezésétől függenek.</summary>
public static class PdfTextLines
{
    public static IReadOnlyList<string> Extract(byte[] pdf)
    {
        var lines = new List<string>();
        using var document = PdfDocument.Open(pdf);
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            lines.AddRange(text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => Regex.Replace(line, @"\s+", " ")));
        }
        return lines;
    }
}
