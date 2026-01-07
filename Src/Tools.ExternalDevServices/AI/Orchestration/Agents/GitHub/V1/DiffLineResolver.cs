using System.Collections;
using System.Text.RegularExpressions;

namespace Tools.ExternalDevServices.AI.Orchestration.Agents.GitHub.V1;

public static partial class DiffLineResolver
{
    // Matches unified-diff hunk headers:
    //   @@ -oldStart,oldLen +newStart,newLen @@ ...
    [GeneratedRegex(@"^@@\s+-(\d+)(?:,(\d+))?\s+\+(\d+)(?:,(\d+))?\s+@@")]
    public static partial Regex HunkHeaderRegex();

    // Matches any patch line within a hunk with a leading diff prefix and a marker:
    //   + [L123] code...
    //   - [L124] code...
    //     [L125] code...
    [GeneratedRegex(@"^\[L(\d+)\](.*)$")]
    private static partial Regex MarkedLineRegex();

    /// <summary>
    /// Resolves line numbers in a unified diff and updates the code comments accordingly.
    /// Assumption: The diff is a unified diff with line markers in the form of '[L123]'.
    /// </summary>
    /// <param name="diffText"></param>
    /// <param name="review"></param>
    /// <exception cref="InvalidDataException"></exception>
    /// <exception cref="KeyNotFoundException"></exception>
    public static void ResolveCodeReviewLines(string diffText, FileCodeReview review)
    {
        if (review.CodeComments.Length == 0) return;

        var pending = review.CodeComments
            .GroupBy(c => c.Line)
            .ToDictionary(g => g.Key, g => g.ToList());

        int? newStart = null;
        using var sr = new StringReader(diffText);
        var newLine = 1; // 1-based line number in the diff text

        while (sr.ReadLine() is { } line)
        {
            var hunk = HunkHeaderRegex().Match(line);
            if (hunk.Success)
            {
                newStart = int.Parse(hunk.Groups[3].Value); // +newStart
                newLine = newStart.Value;
                continue;
            }

            if (newStart is null)
                continue;

            var m = MarkedLineRegex().Match(line);
            if (!m.Success)
                throw new InvalidDataException($"Unmarked patch line inside hunk: '{line}'.");

            var marker = int.Parse(m.Groups[1].Value); // e.g., 541
            var payload = m.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(payload))
            {
                newLine++;
                continue;
            }
            payload = payload[1..];

            var isDeletion = payload.Length > 0 && payload[0] == '-';
            if (isDeletion) continue;// advance only for '+' or ' '
            // '-' (deletion) does not advance and produces no resolvable new-file line

            if (pending.Remove(marker, out var comments))
                foreach (var c in comments) c.Line = newLine;

            newLine++;
        }

        if (pending.Count <= 0) return;

        var missing = string.Join(", ", pending.Keys.OrderBy(k => k).Select(k => $"L{k}"));
        throw new KeyNotFoundException($"Markers not found in diff: {missing}");
    }

    public static IEnumerable<(ResolvedLineType resolvedLineType, int? lineIndex, string line)> ResolveLines(string diffText)
    {
        return new DiffLinesResolverEnumerable(diffText);
    }

    public enum ResolvedLineType
    {
        AdditionOrContext,
        Deletion,
        HunkHeader
    }
    
    /// <summary>
    /// Returns Enumerable of lines in a unified diff with resolved line numbers.
    /// When a hunk header is encountered, the line number will be null as it is not a real line in the file.
    /// </summary>
    private class DiffLinesResolverEnumerable : IEnumerable<(ResolvedLineType resolvedLineType, int? lineIndex, string line)>
    {
        private class Enumerator : IEnumerator<(ResolvedLineType, int?, string)>
        {
            private readonly string _diffText;

            private StringReader? _reader;
            private string? _currentLine;
            private int _currentResolvedLineIndex;
            private ResolvedLineType? _currentResolvedLineType;

            private int? _hunkStart;

            public Enumerator(string diffText)
            {
                _diffText = diffText;
            }
            
            public bool MoveNext()
            {
                _reader ??= new StringReader(_diffText);
                _currentLine = _reader.ReadLine();
                if (_currentLine is null) return false;

                var hunk = HunkHeaderRegex().Match(_currentLine);
                if (!hunk.Success)
                {
                    var isDeletion = _currentLine.Length > 0 && _currentLine[0] == '-';
                    if (isDeletion)
                    {
                        _currentResolvedLineType = ResolvedLineType.Deletion;
                        return true; // advance only for '+' or ' '
                    }

                    _currentResolvedLineIndex += 1;
                    _currentResolvedLineType = ResolvedLineType.AdditionOrContext;
                    return true;
                }
                
                _hunkStart = int.Parse(hunk.Groups[3].Value); 
                _currentResolvedLineIndex = _hunkStart.Value;
                _currentResolvedLineType = ResolvedLineType.HunkHeader;
                return true;
            }

            public void Reset()
            {
                _reader?.Dispose();
                _reader = null;
                _currentLine = null;
                _currentResolvedLineIndex = 0;
                _hunkStart = null;
            }

            (ResolvedLineType, int?, string) IEnumerator<(ResolvedLineType, int?, string)>.Current => CreateCurrent();

            object IEnumerator.Current => CreateCurrent();

            public void Dispose() => Reset();

            private (ResolvedLineType resolvedLineType, int? lineIndex, string line) CreateCurrent() =>
                _currentResolvedLineType is not null && _currentLine is not null
                    ? (resolvedLineType: _currentResolvedLineType.Value,
                        lineIndex: _currentResolvedLineIndex == _hunkStart ? null : _currentResolvedLineIndex,
                        line: _currentLine)
                    : throw new InvalidOperationException("Current line is not set");
        }
        
        private readonly string _diffText;

        public DiffLinesResolverEnumerable(string diffText)
        {
            _diffText = diffText;
        }

        public IEnumerator<(ResolvedLineType resolvedLineType, int? lineIndex, string line)> GetEnumerator() => new Enumerator(_diffText);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}