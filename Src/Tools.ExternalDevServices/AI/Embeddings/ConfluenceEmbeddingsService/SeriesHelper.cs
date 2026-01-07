using System.Text.RegularExpressions;

namespace Tools.ExternalDevServices.AI.Embeddings.ConfluenceEmbeddingsService;

public static partial class SeriesHelper
{
    [GeneratedRegex(@"\b(S3|S4|S5|S6|Series 3|Series 4|Series 5|Series 6|series_3|series_4|series_5|series_6)\b", RegexOptions.IgnoreCase)]
    public static partial Regex S4S5S6Regex();

    public static IEnumerable<string> GetSeriesOccurrences(string input)
    {
        if (string.IsNullOrEmpty(input))
            yield break;

        var seenMask = 0;
        foreach (Match match in S4S5S6Regex().Matches(input))
        {
            var span = match.ValueSpan;
            
            var first = char.IsUpper(span[0])? span[0] : char.ToUpper(span[0]);
            var last = span[^1];
            
            var bit = 1 << (last - '0');
            if ((seenMask & bit) != 0) continue;
            seenMask |= bit;

            yield return (span.Length == 2 && char.IsUpper(span[0]))
                ? match.Value
                : string.Create(length: 2, state: (first, last), action: static (resultSpan, state) =>
                {
                    resultSpan[0] = state.first;
                    resultSpan[1] = state.last;
                });
        }
    }

    public static bool HasSupportedSeries(string input) => S4S5S6Regex().IsMatch(input);
}