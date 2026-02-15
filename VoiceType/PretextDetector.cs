using System.Text;

namespace VoiceType;

public static class PretextDetector
{
    public static string StripFlowDirectives(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        const string openTag = "<flow>";
        const string closeTag = "</flow>";

        var firstOpen = text.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (firstOpen < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var openIndex = text.IndexOf(openTag, index, StringComparison.OrdinalIgnoreCase);
            if (openIndex < 0)
            {
                sb.Append(text, index, text.Length - index);
                break;
            }

            sb.Append(text, index, openIndex - index);

            var closeIndex = text.IndexOf(closeTag, openIndex + openTag.Length, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                // If we see an opening directive with no closing tag, do not risk injecting the remainder.
                break;
            }

            var nextIndex = closeIndex + closeTag.Length;

            // Avoid creating "  " when the transcription already had whitespace on both sides of the directive.
            if (sb.Length > 0 && IsSpaceOrTab(sb[^1]))
            {
                while (nextIndex < text.Length && IsSpaceOrTab(text[nextIndex]))
                    nextIndex++;
            }

            if (sb.Length > 0 && nextIndex < text.Length)
            {
                var prevChar = sb[^1];
                var nextChar = text[nextIndex];

                if (!char.IsWhiteSpace(prevChar) &&
                    !char.IsWhiteSpace(nextChar) &&
                    !IsNoSpaceAfter(prevChar) &&
                    !IsNoSpaceBefore(nextChar))
                {
                    sb.Append(' ');
                }
            }

            index = nextIndex;
        }

        return sb.ToString().Trim();
    }

    private static bool IsSpaceOrTab(char ch) => ch is ' ' or '\t';

    private static bool IsNoSpaceAfter(char ch)
    {
        // Common "opening" punctuation where a space is typically not desired.
        return ch is '(' or '[' or '{' or '"' or '\'';
    }

    private static bool IsNoSpaceBefore(char ch)
    {
        // Common punctuation that typically should not be preceded by a space.
        return ch is '.' or ',' or '!' or '?' or ':' or ';' or ')' or ']' or '}' or '"' or '\'';
    }
}

