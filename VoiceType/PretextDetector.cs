using System.Text;

namespace VoiceType;

public static class PretextDetector
{
    private const string DefaultTranscriptionPrompt = "The speaker is always English. Transcribe the audio as technical instructions for a large language model.";

    private static readonly string[] ModelPreambleLinesToStrip =
    [
        "\"You will receive additional context/instructions (separated by ### delimiters) from the user. Do not reply to the context/instructions and do not include it in the final transcription.\"",
        "You will receive additional context/instructions (separated by ### delimiters) from the user. Do not reply to the context/instructions and do not include it in the final transcription."
    ];

    private static readonly string[] PromptEchoCandidates =
    [
        DefaultTranscriptionPrompt,
        "The speaker is always English."
    ];

    public static string StripPromptEcho(string text, string? prompt)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var trimmedLeading = text.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmedLeading))
            return string.Empty;

        var candidates = new List<string>(PromptEchoCandidates);
        var promptCandidate = NormalizePrompt(prompt);
        if (!string.IsNullOrWhiteSpace(promptCandidate))
            candidates.Add(promptCandidate);
        while (true)
        {
            var matched = false;
            foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (trimmedLeading.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    trimmedLeading = trimmedLeading[candidate.Length..].TrimStart();
                    if (string.IsNullOrWhiteSpace(trimmedLeading))
                        return string.Empty;
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return trimmedLeading;
        }
    }

    public static string RemoveModelLeadingPreamble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var current = text;
        while (true)
        {
            var trimmed = current.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            var stripped = StripKnownLeadingPreamble(trimmed);
            if (stripped is null)
                return trimmed;

            current = stripped;
        }
    }

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

    private static string? NormalizePrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        var trimmed = prompt.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length > 1)
            return trimmed[1..^1];

        return trimmed;
    }

    private static string? StripKnownLeadingPreamble(string text)
    {
        foreach (var prefix in ModelPreambleLinesToStrip)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[prefix.Length..].TrimStart();
            }
        }

        return null;
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
