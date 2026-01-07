using System.Collections.Immutable;
using System.Text;
using DiffPlex.DiffBuilder.Model;

namespace Tools.ExternalDevServices.Utils;

public class DiffBlock
{
    public bool IsModificationBlock { get; }
    public string DiffDescriptor { get; }
    public string Diff { get; }

    public DiffBlock(List<(int sourceIndex, int targetIndex, DiffPiece line)> lines, string context, 
        string addedLineMarker = "+", string deletedLineMarker = "-", string unchangedLineMarker = " ")
    {
        if (lines.Count == 0)
        {
            IsModificationBlock = false;
            Diff = DiffDescriptor = "";
            return;
        }

        context = context.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

        var sb = new StringBuilder();
        var sourceIndex = lines.First().sourceIndex;
        var sourceChangeCount = 0;

        var targetIndex = lines.First().targetIndex;
        var targetChangeCount = 0;

        foreach (var line in lines)
        {
            switch (line.line.Type)
            {
                case ChangeType.Deleted:
                    sourceChangeCount += 1;
                    sb.AppendLine($"{deletedLineMarker}{line.line.Text}");
                    IsModificationBlock = true;
                    break;

                case ChangeType.Inserted:
                    targetChangeCount += 1;
                    sb.AppendLine($"{addedLineMarker}{line.line.Text}");
                    IsModificationBlock = true;
                    break;

                default:
                    sourceChangeCount += 1;
                    targetChangeCount += 1;
                    sb.AppendLine($"{unchangedLineMarker}{line.line.Text}");
                    break;
            }
        }

        DiffDescriptor = $"@@ -{sourceIndex + 1},{sourceChangeCount} +{targetIndex + 1},{targetChangeCount} @@ {context}"
            .Trim();
        Diff = sb.ToString().Trim();
    }

    public override string ToString() =>
        $"{nameof(IsModificationBlock)}: {IsModificationBlock}, {nameof(DiffDescriptor)}: {DiffDescriptor}";
}

public static class DiffUtils
{
    private class InlineDiffLinesGroup
    {
        public int StartIndexInSourceCollection { get; }
        public bool IsUnchangedLinesGroup { get; }
        public List<(DiffPiece diffPiece, string diffPrefix)> BlockDiffs { get; } = [];

        private InlineDiffLinesGroup(bool isUnchangedLinesGroup, int startIndexInSourceCollection)
        {
            IsUnchangedLinesGroup = isUnchangedLinesGroup;
            StartIndexInSourceCollection = startIndexInSourceCollection;
        }

        public bool TryAdd(DiffPiece diffPiece, string diffPrefix)
        {
            if (diffPiece.Type is ChangeType.Unchanged && !IsUnchangedLinesGroup)
                return false;
            if (diffPiece.Type is not ChangeType.Unchanged && IsUnchangedLinesGroup)
                return false;

            BlockDiffs.Add((diffPiece, diffPrefix));
            return true;
        }

        public static InlineDiffLinesGroup Create(int startIndexInSourceCollection, DiffPiece diffPiece, string diffPrefix)
        {
            var isUnchangedLinesGroup = diffPiece.Type is ChangeType.Unchanged;
            var group = new InlineDiffLinesGroup(isUnchangedLinesGroup, startIndexInSourceCollection);
            group.BlockDiffs.Add((diffPiece, diffPrefix));
            return group;
        }

        public override string ToString() =>
            $"Lines: {BlockDiffs.Count}, {nameof(IsUnchangedLinesGroup)}: {IsUnchangedLinesGroup}, {nameof(StartIndexInSourceCollection)}: {StartIndexInSourceCollection}";
    }

    public static string OptimizeInlineDiffWithSkippedLinesMarker(DiffPaneModel diff, int maxUnchangedLinesBefore = 5, int maxUnchangedLinesAfter = 5)
    {
        if (!diff.HasDifferences) return "No changes were made";

        var changes = new List<InlineDiffLinesGroup>();
        var toBranchIndex = 0;
        var fromBranchIndex = 0;
        for (var i = 0; i < diff.Lines.Count; i++)
        {
            var line = diff.Lines[i];
            var diffPrefix = line.Type switch
            {
                ChangeType.Deleted => $"{++toBranchIndex}\t \t-\t",
                ChangeType.Inserted => $" \t{++fromBranchIndex}\t+\t",
                _ => $"{++toBranchIndex}\t{++fromBranchIndex} \t"
            };
            var lastGroup = changes.LastOrDefault();
            if (lastGroup is null || !lastGroup.TryAdd(line, diffPrefix))
            {
                changes.Add(InlineDiffLinesGroup.Create(i, line, diffPrefix));
            }
        }

        var diffsStringBuilder = new StringBuilder();
        for (var i = 0; i < changes.Count; i++)
        {
            var group = changes[i];
            if (group.IsUnchangedLinesGroup)
            {
                if (i == 0)
                {
                    var skip = maxUnchangedLinesBefore >= group.BlockDiffs.Count ? 0 : group.BlockDiffs.Count - maxUnchangedLinesBefore;
                    var lines = group.BlockDiffs.Skip(skip);
                    if (skip > 0)
                    {
                        diffsStringBuilder.AppendLine(
                            $"--- Skipped {skip} unchanged lines ---\r\n");
                    }
                    foreach (var line in lines)
                    {
                        diffsStringBuilder.AppendLine($"{line.diffPrefix}{line.diffPiece.Text}");
                    }
                }
                else if (i == changes.Count - 1)
                {
                    var lines = group.BlockDiffs.Take(maxUnchangedLinesAfter);
                    foreach (var line in lines)
                    {
                        diffsStringBuilder.AppendLine($"{line.diffPrefix}{line.diffPiece.Text}");
                    }
                    if (group.BlockDiffs.Count > maxUnchangedLinesAfter)
                    {
                        diffsStringBuilder.AppendLine(
                            $"\r\n--- Skipped {group.BlockDiffs.Count - maxUnchangedLinesAfter} unchanged lines ---");
                    }
                }
                else
                {
                    var lines = group.BlockDiffs.Count > maxUnchangedLinesBefore + maxUnchangedLinesAfter ?
                        group.BlockDiffs.Take(maxUnchangedLinesAfter).Concat(group.BlockDiffs.TakeLast(maxUnchangedLinesBefore)) :
                        group.BlockDiffs;

                    var index = 0;
                    foreach (var line in lines)
                    {
                        if (index == maxUnchangedLinesAfter && group.BlockDiffs.Count > maxUnchangedLinesBefore + maxUnchangedLinesAfter)
                        {
                            diffsStringBuilder.AppendLine(
                                $"\r\n--- Skipped {group.BlockDiffs.Count - (maxUnchangedLinesAfter + maxUnchangedLinesBefore)} unchanged lines ---\r\n");
                        }

                        index += 1;
                        diffsStringBuilder.AppendLine($"{line.diffPrefix}{line.diffPiece.Text}");
                    }
                }
            }
            else
            {
                foreach (var line in group.BlockDiffs)
                {
                    diffsStringBuilder.AppendLine($"{line.diffPrefix}{line.diffPiece.Text}");
                }
            }
        }

        return diffsStringBuilder.ToString();
    }

    public static DiffBlock
        ToSingleDiffBlock(this DiffPaneModel diff, string addedLineMarker = "+", string deletedLineMarker = "-",
            string unchangedLineMarker = " ") =>
        ToDiffBlocks(diff, ImmutableDictionary<int, string>.Empty, contextSizeBefore: int.MaxValue,
                contextSizeAfter: int.MaxValue, addedLineMarker: addedLineMarker, deletedLineMarker: deletedLineMarker,
                unchangedLineMarker: unchangedLineMarker)
            .SingleOrDefault() ??
        throw new InvalidOperationException("Failed creating a single diff block from input diff");
    
    public static IEnumerable<DiffBlock> ToDiffBlocks(this DiffPaneModel diff,
        IReadOnlyDictionary<int, string> lineContextMap,
        int contextSizeBefore = 3,
        int contextSizeAfter = 3,
        string addedLineMarker = "+", 
        string deletedLineMarker = "-", 
        string unchangedLineMarker = " ")
    {
        var sourceIndex = 0;
        var targetIndex = 0;
        var isInModificationScope = false;

        var changed = new List<(int sourceIndex, int targetIndex, DiffPiece line)>();
        var currentChangedContext = "";
        var unchanged = new List<(int sourceIndex, int targetIndex, DiffPiece line)>();

        for (var lineIndex = 0; lineIndex < diff.Lines.Count; lineIndex++)
        {
            if (IsPartOfDiffBlock(diff.Lines, lineIndex, contextSizeBefore, contextSizeAfter))
            {
                if (!isInModificationScope)
                {
                    if (unchanged.Any())
                    {
                        yield return new DiffBlock(unchanged, "", addedLineMarker, deletedLineMarker, unchangedLineMarker);
                        unchanged.Clear();
                    }

                    changed.Clear();
                    isInModificationScope = true;
                }

                changed.Add((sourceIndex, targetIndex, diff.Lines[lineIndex]));
            }
            else
            {
                if (isInModificationScope)
                {
                    isInModificationScope = false;

                    if (changed.Any())
                    {
                        yield return new DiffBlock(changed, currentChangedContext, addedLineMarker, deletedLineMarker, unchangedLineMarker);
                    }
                    changed.Clear();
                }

                unchanged.Add((sourceIndex, targetIndex, diff.Lines[lineIndex]));
            }

            if (lineContextMap.TryGetValue(sourceIndex, out var declaration))
                currentChangedContext = declaration;

            sourceIndex += diff.Lines[lineIndex].Type is ChangeType.Deleted or ChangeType.Unchanged ? 1 : 0;
            targetIndex += diff.Lines[lineIndex].Type is ChangeType.Inserted or ChangeType.Unchanged ? 1 : 0;
        }

        if (changed.Any())
        {
            yield return new DiffBlock(changed, "", addedLineMarker, deletedLineMarker, unchangedLineMarker);
        }
        else if (unchanged.Any())
        {
            yield return new DiffBlock(unchanged, "", addedLineMarker, deletedLineMarker, unchangedLineMarker);
        }
    }

    private static bool IsPartOfDiffBlock(List<DiffPiece> diffLines, int lineIndex, int contextSizeBefore, int contextSizeAfter)
    {
        switch (contextSizeBefore)
        {
            case 0 when contextSizeAfter == 0:
                // Only consider the current line for diff block inclusion
                return diffLines[lineIndex].Type is not ChangeType.Unchanged;
            case int.MaxValue when contextSizeAfter == int.MaxValue:
                // Consider the entire diff for diff block inclusion
                return true;
        }

        var start = Math.Max(0, lineIndex - contextSizeBefore);
        var end = Math.Min(diffLines.Count - 1, lineIndex + contextSizeAfter);
        return diffLines[start..end].Any(l => l.Type is not ChangeType.Unchanged);
    }
}