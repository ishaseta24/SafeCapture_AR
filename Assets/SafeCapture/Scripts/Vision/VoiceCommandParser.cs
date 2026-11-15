using System;
using System.Text.RegularExpressions;

public enum VoiceMaskCommandType
{
    Mask,
    Unmask
}

public sealed class VoiceMaskCommand
{
    public VoiceMaskCommandType Type { get; private set; }
    public string Label { get; private set; }

    public VoiceMaskCommand(VoiceMaskCommandType type, string label)
    {
        Type = type;
        Label = label;
    }
}

public static class VoiceCommandParser
{
    private static readonly Regex CommandPattern =
        new Regex(@"^(mask|unmask)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParse(string transcript, out VoiceMaskCommand command, out string error)
    {
        command = null;
        error = null;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            error = "No speech was recognized.";
            return false;
        }

        Match match = CommandPattern.Match(transcript.Trim());
        if (!match.Success)
        {
            error = "Say “mask <object>” or “unmask <object>”.";
            return false;
        }

        string className = match.Groups[2].Value.Trim().TrimEnd('.', ',', '!', '?', ';', ':');
        string canonical;
        if (!CocoLabels.TryNormalizeVoiceName(className, out canonical))
        {
            error = $"Unsupported object class: {className}";
            return false;
        }

        command = new VoiceMaskCommand(
            string.Equals(match.Groups[1].Value, "mask", StringComparison.OrdinalIgnoreCase)
                ? VoiceMaskCommandType.Mask
                : VoiceMaskCommandType.Unmask,
            canonical);
        return true;
    }
}
