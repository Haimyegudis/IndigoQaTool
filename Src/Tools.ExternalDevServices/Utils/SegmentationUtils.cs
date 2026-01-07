using System.Text.RegularExpressions;

namespace Tools.ExternalDevServices.Utils;

public static partial class SegmentationUtils
{
    [GeneratedRegex(@"^(#{1,})\s(?:(?:\d+\.){1,}\s)?(.+)$", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeaderRegex();

    public static IReadOnlyCollection<(string key, string content)> ConvertMarkdownToSegments(string markdown)
    {
        var segments = new List<(string key, string content)>();
        var headerStack = new List<string>();
        var sb = new System.Text.StringBuilder();

        foreach (var line in markdown.Split('\n'))
        {
            var match = MarkdownHeaderRegex().Match(line);
            if (match.Success)
            {
                // Flush previous segment if any
                if (sb.Length > 0)
                {
                    var key = string.Join(" > ", headerStack);
                    segments.Add((key, sb.ToString().Trim()));
                    sb.Clear();
                }

                var headerLevel = match.Groups[1].Value.Length;
                var headerText = match.Groups[2].Value.Trim();

                // Adjust header stack to current level
                if (headerStack.Count >= headerLevel)
                    headerStack.RemoveRange(headerLevel - 1, headerStack.Count - (headerLevel - 1));

                headerStack.Add(headerText);
            }
            else
            {
                sb.AppendLine(line.Trim());
            }
        }

        // Add last segment
        if (sb.Length > 0)
        {
            var key = string.Join(" > ", headerStack);
            segments.Add((key, sb.ToString().Trim()));
        }

        return segments;
    }
}