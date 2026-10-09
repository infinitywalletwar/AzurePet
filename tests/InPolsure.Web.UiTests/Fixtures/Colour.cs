using System.Globalization;
using System.Text.RegularExpressions;

namespace InPolsure.Web.UiTests.Fixtures;

/// <summary>sRGB colour from a computed CSS value, with the WCAG 2.1 contrast ratio.</summary>
public readonly partial record struct Colour(byte R, byte G, byte B)
{
    /// <summary>Parses the <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c> form that <c>getComputedStyle</c> returns.</summary>
    public static Colour Parse(string computed)
    {
        var match = RgbPattern().Match(computed);
        if (!match.Success)
        {
            throw new FormatException($"Not a computed rgb() colour: '{computed}'.");
        }

        return new Colour(Channel(match, 1), Channel(match, 2), Channel(match, 3));
    }

    /// <summary>WCAG 2.1 contrast ratio, 1 to 21.</summary>
    public static double Contrast(Colour a, Colour b)
    {
        var (lighter, darker) = (Math.Max(a.Luminance, b.Luminance), Math.Min(a.Luminance, b.Luminance));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    private static double Linear(byte channel)
    {
        var c = channel / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Channel(Match match, int group) =>
        byte.Parse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^rgba?\((\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})(?:,\s*[\d.]+)?\)$", RegexOptions.CultureInvariant)]
    private static partial Regex RgbPattern();
}
