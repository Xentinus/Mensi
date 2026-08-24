namespace Mensi.Core.Options;

public class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>A "ma" e szerint az időzóna szerint értendő (a dátumok naptári napok).</summary>
    public string TimeZone { get; set; } = "Europe/Budapest";
}
