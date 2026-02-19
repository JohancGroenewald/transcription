using System.Globalization;
using System.Windows.Forms;
using System.Drawing;

namespace VoiceType;

internal sealed class NerdFontIconService
{
    private static readonly Dictionary<string, string> NerdFontIconClassToGlyph = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nf-md-close_box", "\U000F0157" },
        { "md-close_box", "\U000F0157" },
        { "nf-md-record_rec", "\U000F044B" },
        { "md-record_rec", "\U000F044B" },
        { "nf-fa-stop", "\U0000F04D" },
        { "fa-stop", "\U0000F04D" }
    };

    private readonly HashSet<string> _loggedNerdFontIconResolution = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedNerdFontIconRenderFailure = new(StringComparer.Ordinal);

    public string? ResolveIconGlyph(string? iconClass)
    {
        if (string.IsNullOrWhiteSpace(iconClass))
            return null;

        var normalized = iconClass.Trim();
        if (normalized.Length == 2 && char.IsSurrogatePair(normalized, 0))
            return normalized;
        if (normalized.Length == 1)
            return normalized;

        if (normalized.StartsWith(@"\u", StringComparison.OrdinalIgnoreCase) && normalized.Length == 6)
        {
            normalized = normalized.AsSpan(2).ToString();
        }
        else if (normalized.StartsWith(@"\U", StringComparison.OrdinalIgnoreCase) && normalized.Length == 10)
        {
            normalized = normalized.AsSpan(2).ToString();
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.AsSpan(2).ToString();

        if (normalized.Length is >= 4 and <= 6)
        {
            var looksHex = true;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (!IsHexDigit(normalized[i]))
                {
                    looksHex = false;
                    break;
                }
            }

            if (looksHex && int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                return char.ConvertFromUtf32(codePoint);
        }

        return NerdFontIconClassToGlyph.TryGetValue(normalized, out var glyph)
            ? glyph
            : null;
    }

    public bool DrawIcon(
        Graphics graphics,
        Rectangle iconBounds,
        Font iconFont,
        Color iconColor,
        string? iconGlyph)
    {
        var originalIconValue = string.IsNullOrWhiteSpace(iconGlyph) ? "<null>" : iconGlyph.Trim();
        var resolvedIcon = ResolveIconGlyph(iconGlyph);
        if (string.IsNullOrWhiteSpace(resolvedIcon))
        {
            if (_loggedNerdFontIconResolution.Add($"missing:{originalIconValue}"))
                Log.Info($"Nerd icon not resolved, overlay fallback applied. icon={originalIconValue}.");
            return false;
        }

        var resolvedGlyph = DescribeNerdGlyph(resolvedIcon);
        if (_loggedNerdFontIconResolution.Add($"resolved:{originalIconValue}:{resolvedGlyph}"))
            Log.Info($"Nerd icon resolved. icon={originalIconValue}, glyph={resolvedGlyph}.");

        try
        {
            var textSize = TextRenderer.MeasureText(
                graphics,
                resolvedIcon,
                iconFont,
                new Size(int.MaxValue / 4, int.MaxValue / 4),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var glyphBounds = new Rectangle(
                iconBounds.Left + Math.Max(0, (iconBounds.Width - textSize.Width) / 2),
                iconBounds.Top + Math.Max(0, (iconBounds.Height - textSize.Height) / 2),
                Math.Max(1, Math.Min(textSize.Width, iconBounds.Width)),
                Math.Max(1, Math.Min(textSize.Height, iconBounds.Height)));

            TextRenderer.DrawText(
                graphics,
                resolvedIcon,
                iconFont,
                glyphBounds,
                iconColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        catch (Exception ex)
        {
            if (_loggedNerdFontIconRenderFailure.Add($"{originalIconValue}:{resolvedGlyph}"))
                Log.Error($"Failed Nerd icon render path. icon={originalIconValue}, glyph={resolvedGlyph}, bounds={iconBounds}, color={iconColor}, font={iconFont.Name}, Message={ex.Message}");

            return false;
        }

        return true;
    }

    private static string DescribeNerdGlyph(string? iconGlyph)
    {
        if (string.IsNullOrEmpty(iconGlyph))
            return "<empty>";

        try
        {
            var codePoint = char.ConvertToUtf32(iconGlyph, 0);
            return $"U+{codePoint:X4}";
        }
        catch
        {
            return $"<invalid:{iconGlyph}>";
        }
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'A' && c <= 'F') ||
               (c >= 'a' && c <= 'f');
    }
}
