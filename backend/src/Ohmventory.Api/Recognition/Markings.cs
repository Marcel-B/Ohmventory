using System.Text.RegularExpressions;

namespace Ohmventory.Api.Recognition;

/// <summary>
/// Sortiert gelesene Aufdrucke: Was taugt als Suchbegriff (z. B. „1.30" IIC V2.2", „GY-521"), und was ist nur
/// Bestückungsdruck (C1, R5) oder Pin-Beschriftung (VDD, GND, SDA)?
/// </summary>
public static partial class Markings
{
    private static readonly HashSet<string> PinLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "VCC", "VDD", "VIN", "VOUT", "VBAT", "V", "GND", "G", "SCL", "SCK", "SDA", "SDI", "SDO", "MOSI", "MISO",
        "CS", "SS", "CE", "RST", "RES", "RESET", "DC", "D/C", "BLK", "BL", "LED", "INT", "IRQ", "TX", "RX", "TXD", "RXD",
        "3V3", "3.3V", "5V", "OUT", "IN", "EN", "ADDR", "AD0", "A0", "SIG", "S", "DATA", "CLK", "DIN", "DOUT", "NC",
    };

    /// <summary>Bestückungsdruck wie R1, C12, D1, U3, Q2, SW1, LED1.</summary>
    public static bool IsDesignator(string text) => Designator().IsMatch(text.Trim());

    /// <summary>
    /// Aufdrucke, die etwas über das Modul sagen. Raus fallen Bestückungsdrucke, reine Zahlen und reine Pin-Beschriftungen
    /// (auch Formen wie „1(VDD)").
    /// </summary>
    public static List<string> SearchTerms(IEnumerable<string> markings) =>
        markings
            .Select(m => m.Trim())
            .Where(m => m.Length >= 2 && !IsDesignator(m) && !IsPinLabelOnly(m) && Letter().IsMatch(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Ob es mindestens einen Aufdruck gibt, mit dem sich eine Websuche lohnt (zwei Buchstaben, die kein Pin-Name sind).</summary>
    public static bool HasDistinctive(IEnumerable<string> markings) =>
        SearchTerms(markings).Any(m => Letter().Matches(StripPinWords(m)).Count >= 2);

    /// <summary>
    /// Suchanfrage aus den Aufdrucken: Zoll-Zeichen werden zu „inch", weil Suchmaschinen Anführungszeichen als Phrase lesen.
    /// </summary>
    public static string? BuildQuery(IEnumerable<string> markings, int maxTerms = 4)
    {
        if (!HasDistinctive(markings))
            return null;

        var terms = SearchTerms(markings)
            .OrderByDescending(m => Letter().Matches(StripPinWords(m)).Count >= 2)
            .Take(maxTerms)
            .Select(m => Inch().Replace(m, "$1 inch "))
            .Select(m => Quotes().Replace(m, " "))
            .Select(m => Whitespace().Replace(m, " ").Trim());

        return string.Join(' ', terms) + " module";
    }

    private static bool IsPinLabelOnly(string text) => Letter().Matches(StripPinWords(text)).Count == 0;

    private static string StripPinWords(string text) =>
        Word().Replace(text, w => PinLabels.Contains(w.Value) ? "" : w.Value);

    [GeneratedRegex(@"^(R|C|D|U|Q|L|J|P|SW|LED|IC|Y|F|K|T)\d{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex Designator();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex Letter();

    [GeneratedRegex(@"[\p{L}\d./]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:""|″|''|”|“)")]
    private static partial Regex Inch();

    [GeneratedRegex(@"[""″“”„']")]
    private static partial Regex Quotes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
