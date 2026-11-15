using System;

public enum VoiceMaskCommandType { Mask, Unmask }

public sealed class VoiceMaskCommand
{
    public VoiceMaskCommandType Type  { get; }
    public string               Label { get; } // null = "current object" (resolved by caller)

    public VoiceMaskCommand(VoiceMaskCommandType type, string label)
    {
        Type  = type;
        Label = label;
    }
}

// Parses natural-language voice transcripts into mask/unmask commands.
// Supported phrases (case-insensitive):
//   Mask action:   mask | hide | block | cover | blur | censor | protect
//   Unmask action: unmask | show | unhide | unblock | uncover | reveal | display
//   Object:        any COCO label (single or multi-word), optionally preceded by "the/a/an"
//   "this" / "that" / "it" → Label=null, caller supplies fallback from current detection
public static class VoiceCommandParser
{
    private static readonly string[] MaskWords =
        { "mask", "hide", "block", "cover", "blur", "censor", "protect" };
    private static readonly string[] UnmaskWords =
        { "unmask", "show", "unhide", "unblock", "uncover", "reveal", "display" };
    private static readonly string[] ThisWords =
        { "this", "that", "it" };
    private static readonly string[] Articles =
        { "the", "a", "an", "my", "that", "this" };

    // TryParse(transcript, out command, out error)
    // Label=null means "current object"; caller should resolve using most prominent detection.
    public static bool TryParse(string transcript, out VoiceMaskCommand command, out string error)
    {
        command = null;
        error   = null;

        if (string.IsNullOrWhiteSpace(transcript))
        {
            error = "No speech was recognized.";
            return false;
        }

        string lower = transcript.Trim().ToLowerInvariant();
        // Strip trailing punctuation
        lower = lower.TrimEnd('.', ',', '!', '?', ';', ':');

        // Determine action — scan for first keyword hit
        bool isMask   = false;
        bool isUnmask = false;
        int  maskIdx   = FirstWordIndex(lower, MaskWords);
        int  unmaskIdx = FirstWordIndex(lower, UnmaskWords);

        if (maskIdx   >= 0) isMask   = true;
        if (unmaskIdx >= 0) isUnmask = true;

        if (!isMask && !isUnmask)
        {
            error = "Say something like \"hide the person\" or \"show the laptop\".";
            return false;
        }
        if (isMask && isUnmask)
            isMask = maskIdx < unmaskIdx; // earlier keyword wins

        VoiceMaskCommandType cmdType = isMask
            ? VoiceMaskCommandType.Mask
            : VoiceMaskCommandType.Unmask;

        // Check for "this/that/it" → null label (caller resolves from current detection)
        if (ContainsWholeWord(lower, ThisWords))
        {
            command = new VoiceMaskCommand(cmdType, null);
            return true;
        }

        // Try to extract COCO label — strip action word(s) and articles, then match greedily
        string remaining = RemoveKeywords(lower, MaskWords);
        remaining        = RemoveKeywords(remaining, UnmaskWords);
        remaining        = RemoveArticles(remaining);
        remaining        = remaining.Trim();

        // Try two-word then one-word COCO match
        string canonical;
        string[] words = remaining.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int len = Math.Min(words.Length, 3); len >= 1; len--)
        {
            for (int start = 0; start <= words.Length - len; start++)
            {
                string phrase = string.Join(" ", words, start, len);
                if (CocoLabels.TryNormalizeVoiceName(phrase, out canonical))
                {
                    command = new VoiceMaskCommand(cmdType, canonical);
                    return true;
                }
            }
        }

        error = $"Object not recognized in: \"{transcript.Trim()}\". Try \"hide person\" or \"mask laptop\".";
        return false;
    }

    private static int FirstWordIndex(string text, string[] words)
    {
        int min = -1;
        foreach (string w in words)
        {
            int idx = text.IndexOf(w, StringComparison.Ordinal);
            if (idx < 0) continue;
            if (min < 0 || idx < min) min = idx;
        }
        return min;
    }

    private static bool ContainsWholeWord(string text, string[] words)
    {
        foreach (string w in words)
        {
            int idx = text.IndexOf(w, StringComparison.Ordinal);
            if (idx < 0) continue;
            bool beforeOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool afterOk  = idx + w.Length >= text.Length || !char.IsLetterOrDigit(text[idx + w.Length]);
            if (beforeOk && afterOk) return true;
        }
        return false;
    }

    private static string RemoveKeywords(string text, string[] words)
    {
        foreach (string w in words)
            text = text.Replace(w, " ");
        return text;
    }

    private static string RemoveArticles(string text)
    {
        foreach (string a in Articles)
        {
            // Remove standalone article words
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"\b" + a + @"\b", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return text;
    }
}
