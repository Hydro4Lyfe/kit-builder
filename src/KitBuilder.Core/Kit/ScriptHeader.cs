namespace KitBuilder.Core.Kit;

/// <summary>
/// What a script says about itself in its opening comment, so the app can describe each
/// script in plain words without keeping a separate list that would drift from the files.
/// </summary>
/// <remarks>
/// Kit convention (see the kit's Scripts/README.md): the first comment paragraph says what
/// the script does in one sentence; a comment line starting <c>Important:</c> says what
/// breaks if it is switched off, and makes the app ask before switching it off.
/// </remarks>
public sealed record ScriptHeader(string? Description, string? Important)
{
    private static readonly HashSet<string> ReadableExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ps1", ".reg", ".cmd", ".bat" };

    public static readonly ScriptHeader None = new(null, null);

    public static ScriptHeader Read(string path)
    {
        if (!ReadableExtensions.Contains(Path.GetExtension(path)) || !File.Exists(path))
            return None;
        try
        {
            return Parse(File.ReadLines(path).Take(60));
        }
        catch (IOException) { return None; }
        catch (UnauthorizedAccessException) { return None; }
    }

    /// <summary>Reads the leading comment block; stops at the first line of code.</summary>
    public static ScriptHeader Parse(IEnumerable<string> lines)
    {
        var paragraphs = new List<List<string>> { new() };
        string? important = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line == "Windows Registry Editor Version 5.00")
            {
                // A blank line ends the comment block only once it has started.
                if (paragraphs.Any(p => p.Count > 0)) paragraphs.Add([]);
                continue;
            }
            if (StripComment(line) is not { } text)
                break;
            if (text.Length == 0)
            {
                paragraphs.Add([]);
                continue;
            }
            if (text.StartsWith("Important:", StringComparison.OrdinalIgnoreCase))
            {
                important ??= text["Important:".Length..].Trim();
                paragraphs.Add([]);
                continue;
            }
            paragraphs[^1].Add(text);
        }

        var first = paragraphs.FirstOrDefault(p => p.Count > 0);
        var description = first is null ? null : FirstSentence(string.Join(" ", first));
        return new ScriptHeader(description, string.IsNullOrEmpty(important) ? null : important);
    }

    // The comment text without its marker, or null when the line is not a comment.
    private static string? StripComment(string line)
    {
        foreach (var marker in new[] { "@rem", "rem ", "::", "#", ";" })
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return line[marker.Length..].Trim();
        }
        return line.Equals("rem", StringComparison.OrdinalIgnoreCase) ? "" : null;
    }

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? text : text[..(end + 1)];
    }
}
