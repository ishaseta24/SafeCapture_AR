using System;
using System.Collections.Generic;

public static class CocoLabels
{
    private static readonly string[] Labels =
    {
        "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat",
        "traffic light","fire hydrant","stop sign","parking meter","bench","bird","cat",
        "dog","horse","sheep","cow","elephant","bear","zebra","giraffe","backpack",
        "umbrella","handbag","tie","suitcase","frisbee","skis","snowboard","sports ball",
        "kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket",
        "bottle","wine glass","cup","fork","knife","spoon","bowl","banana","apple",
        "sandwich","orange","broccoli","carrot","hot dog","pizza","donut","cake","chair",
        "couch","potted plant","bed","dining table","toilet","tv","laptop","mouse",
        "remote","keyboard","cell phone","microwave","oven","toaster","sink",
        "refrigerator","book","clock","vase","scissors","teddy bear","hair drier",
        "toothbrush"
    };

    private static readonly Dictionary<string, string> VoiceAliases = BuildVoiceAliases();

    public static string Get(int classIndex)
    {
        if (classIndex >= 0 && classIndex < Labels.Length)
            return Labels[classIndex];
        return $"class_{classIndex}";
    }

    public static bool TryNormalizeVoiceName(string value, out string canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = NormalizeWhitespace(value).ToLowerInvariant();
        return VoiceAliases.TryGetValue(normalized, out canonical);
    }

    private static Dictionary<string, string> BuildVoiceAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string label in Labels)
        {
            aliases[label] = label;
            aliases[Pluralize(label)] = label;
        }

        aliases["people"] = "person";
        aliases["mice"] = "mouse";
        aliases["knives"] = "knife";
        aliases["ski"] = "skis";
        aliases["scissor"] = "scissors";
        aliases["hair dryer"] = "hair drier";
        aliases["hair dryers"] = "hair drier";
        return aliases;
    }

    private static string Pluralize(string label)
    {
        if (label == "person") return "people";
        if (label == "mouse") return "mice";
        if (label == "knife") return "knives";
        if (label == "scissors" || label == "skis") return label;

        int lastSpace = label.LastIndexOf(' ');
        string prefix = lastSpace >= 0 ? label.Substring(0, lastSpace + 1) : "";
        string word = lastSpace >= 0 ? label.Substring(lastSpace + 1) : label;

        if (word.EndsWith("y", StringComparison.Ordinal) && word.Length > 1
            && !"aeiou".Contains(word[word.Length - 2].ToString()))
            return prefix + word.Substring(0, word.Length - 1) + "ies";
        if (word.EndsWith("s", StringComparison.Ordinal)
            || word.EndsWith("x", StringComparison.Ordinal)
            || word.EndsWith("z", StringComparison.Ordinal)
            || word.EndsWith("ch", StringComparison.Ordinal)
            || word.EndsWith("sh", StringComparison.Ordinal))
            return prefix + word + "es";
        return prefix + word + "s";
    }

    private static string NormalizeWhitespace(string value)
    {
        string[] words = value.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }
}
