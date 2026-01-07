using System.Text.RegularExpressions;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Tools.ExternalDevServices.Utils;

// -----------------------------------------------------------------------------
// This code is still a work in progress and should not be used in production.
// -----------------------------------------------------------------------------

public readonly record struct FilterResult(bool Filter, string Reason);

internal static class PerFileDiffTriage
{
    private static readonly IFileDiffClassifier[] _chain =
    {
        new CSharpDiffClassifier(),
        new ReactDiffClassifier(),
        new JsonXmlConfigClassifier(),
        new MarkdownClassifier()
    };

    /// <summary>
    /// Tries to classify a single-file unified diff. Returns true if a classifier handled it.
    /// </summary>
    public static bool TryClassify(string filePath, string unifiedDiff, out FilterResult result)
    {
        foreach (var c in _chain)
        {
            if (c.TryClassify(filePath, unifiedDiff, out result))
                return true;
        }
        result = default;
        return false;
    }
}

// -----------------------------------------------------------------------------
// Classifier contract
// -----------------------------------------------------------------------------

public interface IFileDiffClassifier
{
    /// <summary>
    /// Returns true if this classifier is applicable and produced a result.
    /// </summary>
    bool TryClassify(string filePath, string unifiedDiff, out FilterResult result);
}

// -----------------------------------------------------------------------------
// Label catalog & final decision logic
// -----------------------------------------------------------------------------

internal static class LabelCatalog
{
    public static readonly HashSet<string> NI = new(StringComparer.Ordinal)
    {
        "deletions-only","whitespace-only","comments-only","imports-only","regions-only",
        "pure-rename","typo-fix","generated-artifact","project-noise","config-formatting-only",
        "snapshots-formatting-only","license-header-only","callsite-adaptation-only","tests-refactor-only"
    };

    public static readonly HashSet<string> I = new(StringComparer.Ordinal)
    {
        "logic-changed","signature-or-visibility-changed","control-flow-changed","concurrency-changed",
        "error-handling-logging-changed","security-or-boundary-changed","data-access-changed",
        "build-or-runtime-values-changed","tests-behavior-changed","bad-rename","uncertain-keep"
    };

    public static FilterResult Decide(HashSet<string> labels)
    {
        var norm = labels
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Select(s => s.Length <= 60 ? s : s[..60])
            .Distinct()
            .ToList();

        var ni = norm.Where(NI.Contains).ToList();
        var i = norm.Where(I.Contains).ToList();

        // Rule 6: avoid "logic-changed" with NI rename/callsite-only unless other I exists
        if (i.Contains("logic-changed") &&
            (ni.Contains("pure-rename") || ni.Contains("typo-fix") || ni.Contains("callsite-adaptation-only")) &&
            i.Count == 1)
        {
            i.Remove("logic-changed");
        }

        List<string> final;
        bool filter;

        if (i.Count == 0 && ni.Count > 0) { final = ni; filter = false; }
        else if (i.Count > 0 && ni.Count == 0) { final = i; filter = true; }
        else if (i.Count > 0 && ni.Count > 0) { final = new() { "mixed-keep" }; final.AddRange(i); final.AddRange(ni); filter = true; }
        else { final = new() { "uncertain-keep" }; filter = true; }

        return new FilterResult(filter, string.Join(";", final));
    }
}

// -----------------------------------------------------------------------------
// Unified diff (single-file, minimal parser)
// -----------------------------------------------------------------------------

internal static class UnifiedDiff
{
    private static readonly Regex HunkHeader = new(@"^@@\s+-\d+(?:,\d+)?\s+\+\d+(?:,\d+)?\s+@@", RegexOptions.Compiled);

    public static Parsed Parse(string diff)
    {
        var d = new Parsed();
        var curAdded = new List<string>();
        var curRemoved = new List<string>();

        foreach (var raw in (diff ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (raw.StartsWith("--- ") || raw.StartsWith("+++ ")) continue;

            if (HunkHeader.IsMatch(raw))
            {
                Flush(d, curRemoved, curAdded);
                curAdded = new(); curRemoved = new();
                continue;
            }

            if (raw.Length == 0) continue;
            var tag = raw[0];
            var payload = raw.Length > 1 ? raw[1..] : string.Empty;

            switch (tag)
            {
                case '-': curRemoved.Add(payload); d.Removed.Add(payload); break;
                case '+': curAdded.Add(payload); d.Added.Add(payload); break;
                case ' ': default: Flush(d, curRemoved, curAdded); curAdded = new(); curRemoved = new(); break;
            }
        }

        Flush(d, curRemoved, curAdded);
        return d;
    }

    private static void Flush(Parsed d, List<string> removed, List<string> added)
    {
        if (removed.Count == 0 && added.Count == 0) return;
        d.Changes.Add((removed, added));
    }

    public sealed class Parsed
    {
        public List<(List<string> removed, List<string> added)> Changes { get; } = new();
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();
    }
}

// -----------------------------------------------------------------------------
// Token model & tokenizer contract
// -----------------------------------------------------------------------------

internal enum TokKind { Identifier, Keyword, Literal, Punct, Other }
internal readonly record struct Tok(TokKind Kind, string Val);

internal interface ITokenizer
{
    List<Tok> LexLine(string line);
    bool IsImportLine(string line);
    bool IsCommentLine(string line);
    bool IsRegionLine(string line);
    string NormalizeSpace(string s);
}

// -----------------------------------------------------------------------------
// Diff alignment helper (DiffPlex)
// -----------------------------------------------------------------------------

internal static class DiffAlign
{
    public static IEnumerable<(string r, string a)> PairLines(List<string> removed, List<string> added)
    {
        var differ = new Differ();
        var builder = new SideBySideDiffBuilder(differ);
        var sbs = builder.BuildDiffModel(string.Join("\n", removed), string.Join("\n", added));

        var rLines = sbs.OldText.Lines;
        var aLines = sbs.NewText.Lines;

        int i = 0, j = 0;
        while (i < rLines.Count || j < aLines.Count)
        {
            var rl = i < rLines.Count ? rLines[i] : null;
            var al = j < aLines.Count ? aLines[j] : null;

            var rText = rl?.Text ?? "";
            var aText = al?.Text ?? "";

            if (rl is not null && rl.Type == ChangeType.Unchanged) { i++; continue; }
            if (al is not null && al.Type == ChangeType.Unchanged) { j++; continue; }

            if (rl is not null && rl.Type != ChangeType.Unchanged &&
                al is not null && al.Type != ChangeType.Unchanged)
            { yield return (rText, aText); i++; j++; }
            else if (rl is not null && rl.Type != ChangeType.Unchanged)
            { yield return (rText, ""); i++; }
            else if (al is not null && al.Type != ChangeType.Unchanged)
            { yield return ("", aText); j++; }
        }
    }
}

// -----------------------------------------------------------------------------
// Small utils
// -----------------------------------------------------------------------------

internal static class StringUtil
{
    public static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            prev = cur;
        }
        return prev[b.Length];
    }
}

// -----------------------------------------------------------------------------
// Tokenizers (C# and JS/TS/JSX/TSX)
// -----------------------------------------------------------------------------

internal sealed class CSharpTokenizer : ITokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
        "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
        "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long",
        "namespace","new","null","object","operator","out","override","params","private","protected","public","readonly",
        "ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw",
        "true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while",
        "async","await","record","var","dynamic","when","nameof","get","set","init","add","remove"
    };

    private static readonly Regex RxSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RxLineComment = new(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RxBlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RxString = new("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Compiled);
    private static readonly Regex RxNumber = new(@"\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][\+\-]?\d+)?)\b", RegexOptions.Compiled);
    private static readonly Regex RxIdent = new(@"\b[_A-Za-z][_\w]*\b", RegexOptions.Compiled);
    private static readonly Regex RxPunct = new(@"[~!%^&*\(\)\-\+=\{\}\[\]\|:;,<>\?\.\/\\]", RegexOptions.Compiled);

    private static readonly Regex RxUsing = new(@"^\s*using\s+[\w\.\=]+", RegexOptions.Compiled);
    private static readonly Regex RxRegion = new(@"^\s*#(region|endregion)\b", RegexOptions.Compiled);

    public string NormalizeSpace(string s) => RxSpace.Replace(s ?? string.Empty, "");

    public bool IsImportLine(string line) => RxUsing.IsMatch(line);

    public bool IsCommentLine(string line)
    {
        var t = (line ?? "").Trim();
        return t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*/");
    }

    public bool IsRegionLine(string line) => RxRegion.IsMatch(line);

    public List<Tok> LexLine(string line)
    {
        var s = line ?? string.Empty;
        s = RxLineComment.Replace(s, "");
        s = RxBlockComment.Replace(s, "");

        var toks = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s, i)) { i++; continue; }

            var mStr = RxString.Match(s, i);
            if (mStr.Success && mStr.Index == i) { toks.Add(new(TokKind.Literal, "<str>")); i += mStr.Length; continue; }

            var mNum = RxNumber.Match(s, i);
            if (mNum.Success && mNum.Index == i) { toks.Add(new(TokKind.Literal, "<num>")); i += mNum.Length; continue; }

            var mId = RxIdent.Match(s, i);
            if (mId.Success && mId.Index == i)
            {
                var t = mId.Value;
                toks.Add(Keywords.Contains(t) ? new(TokKind.Keyword, t) : new(TokKind.Identifier, t));
                i += mId.Length; continue;
            }

            var mP = RxPunct.Match(s, i);
            if (mP.Success && mP.Index == i) { toks.Add(new(TokKind.Punct, mP.Value)); i += mP.Length; continue; }

            toks.Add(new(TokKind.Other, s[i].ToString())); i++;
        }
        return toks;
    }
}

internal sealed class JsTsReactTokenizer : ITokenizer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break","case","catch","class","const","continue","debugger","default","delete","do","else","export","extends",
        "finally","for","function","if","import","in","instanceof","new","return","super","switch","this","throw","try",
        "typeof","var","void","while","with","yield","let","enum","await","async","implements","interface","package",
        "private","protected","public","null","true","false"
    };

    private static readonly Regex RxSpace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RxLineComment = new(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RxBlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RxString = new("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`", RegexOptions.Compiled);
    private static readonly Regex RxNumber = new(@"\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][\+\-]?\d+)?)\b", RegexOptions.Compiled);
    private static readonly Regex RxIdent = new(@"\b[$_A-Za-z][$\w]*\b", RegexOptions.Compiled);
    private static readonly Regex RxPunct = new(@"[~!%^&*\(\)\-\+=\{\}\[\]\|:;,<>\?\.\/\\]", RegexOptions.Compiled);

    private static readonly Regex RxImportExport = new(@"^\s*(import|export)\b", RegexOptions.Compiled);
    private static readonly Regex RxRegion = new(@"^\s*#(region|endregion)\b", RegexOptions.Compiled); // rarely used

    public string NormalizeSpace(string s) => RxSpace.Replace(s ?? string.Empty, "");

    public bool IsImportLine(string line) => RxImportExport.IsMatch(line);

    public bool IsCommentLine(string line)
    {
        var t = (line ?? "").Trim();
        return t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*/");
    }

    public bool IsRegionLine(string line) => RxRegion.IsMatch(line);

    public List<Tok> LexLine(string line)
    {
        var s = line ?? string.Empty;
        s = RxLineComment.Replace(s, "");
        s = RxBlockComment.Replace(s, "");

        var toks = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s, i)) { i++; continue; }

            var mStr = RxString.Match(s, i);
            if (mStr.Success && mStr.Index == i) { toks.Add(new(TokKind.Literal, "<str>")); i += mStr.Length; continue; }

            var mNum = RxNumber.Match(s, i);
            if (mNum.Success && mNum.Index == i) { toks.Add(new(TokKind.Literal, "<num>")); i += mNum.Length; continue; }

            var mId = RxIdent.Match(s, i);
            if (mId.Success && mId.Index == i)
            {
                var t = mId.Value;
                toks.Add(Keywords.Contains(t) ? new(TokKind.Keyword, t) : new(TokKind.Identifier, t));
                i += mId.Length; continue;
            }

            var mP = RxPunct.Match(s, i);
            if (mP.Success && mP.Index == i) { toks.Add(new(TokKind.Punct, mP.Value)); i += mP.Length; continue; }

            toks.Add(new(TokKind.Other, s[i].ToString())); i++;
        }
        return toks;
    }
}

// -----------------------------------------------------------------------------
// Shared analysis engine (language-agnostic, plug-in tokenizer)
// -----------------------------------------------------------------------------

internal sealed class DiffAnalysisEngine
{
    private readonly ITokenizer _tok;
    private readonly string _filePath;
    private readonly bool _isTestFile;

    // Evidence
    private bool _sawWhitespaceOnly = true;
    private bool _sawCommentsOnly = true;
    private bool _sawImportsOnly = true;
    private bool _sawRegionsOnly = true;

    private bool _literalChanged;
    private bool _operatorOrKeywordChanged;
    private bool _controlFlowChanged;
    private bool _concurrencyChanged;
    private bool _signatureOrVisibilityChanged;
    private bool _errorHandlingLoggingChanged;
    private bool _dataAccessChanged;
    private bool _securityBoundaryChanged;
    private bool _buildOrRuntimeValuesChanged;
    private bool _testsBehaviorChanged;

    // Rename
    private readonly Dictionary<string, string> _renameMap = new(StringComparer.Ordinal);
    private bool _renameConsistent = true;
    private bool _anyRenameDetected;
    private bool _singleRenameTyposize;

    // Callsite-only / propagated param
    private readonly HashSet<string> _newParamNames = new(StringComparer.Ordinal);
    private bool _propagatedParamSeen;
    private bool _sawCallsiteAdaptPair;
    private bool _sawNonAdaptInvocation;

    // Heuristics
    private static readonly HashSet<string> ControlFlow = new(StringComparer.Ordinal)
    { "if","else","for","foreach","while","switch","case","default","return","throw","try","catch","finally","using","await","lock","yield","break","continue" };

    private static readonly HashSet<string> Concurrency = new(StringComparer.Ordinal)
    { "await","async","Task","Thread","lock","Monitor","Interlocked","Parallel","Semaphore","Channel","IAsyncEnumerable","Promise","then","setTimeout","setInterval" };

    private static readonly HashSet<string> Visibility = new(StringComparer.Ordinal)
    { "public","private","protected","internal","export","default" };

    private static readonly Regex RxCSharpAssert = new(@"\bAssert\.", RegexOptions.Compiled);
    private static readonly Regex RxJsAssert = new(@"\b(expect|assert)\b", RegexOptions.Compiled);

    public DiffAnalysisEngine(ITokenizer tokenizer, string filePath)
    {
        _tok = tokenizer;
        _filePath = (filePath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        _isTestFile = IsLikelyTestFile(_filePath);
    }

    public FilterResult Analyze(UnifiedDiff.Parsed parsed, IEnumerable<string> pathNoiseLabels)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in pathNoiseLabels) labels.Add(l);

        if (parsed.Changes.Count == 0)
        {
            labels.Add("whitespace-only");
            return LabelCatalog.Decide(labels);
        }

        if (parsed.Added.Count == 0 && parsed.Removed.Count > 0)
            labels.Add("deletions-only");

        foreach (var (removed, added) in parsed.Changes)
        {
            foreach (var r in removed) ObserveLineKind(r);
            foreach (var a in added) ObserveLineKind(a);

            foreach (var (r, a) in DiffAlign.PairLines(removed, added))
                AnalyzePair(r, a);
        }

        // NI basics
        if (_sawWhitespaceOnly) labels.Add("whitespace-only");
        if (_sawCommentsOnly) labels.Add("comments-only");
        if (_sawImportsOnly) labels.Add("imports-only");
        if (_sawRegionsOnly) labels.Add("regions-only");

        // Rename
        if (_anyRenameDetected && _renameConsistent && !_operatorOrKeywordChanged && !_literalChanged && !_controlFlowChanged)
            labels.Add(_singleRenameTyposize ? "typo-fix" : "pure-rename");
        else if (_anyRenameDetected && !_renameConsistent)
            labels.Add("bad-rename");

        // Callsite-only (only if we saw at least one clean pair and no non-adapt invocations)
        if ((_sawCallsiteAdaptPair || _propagatedParamSeen) && !_sawNonAdaptInvocation && !_literalChanged && !_controlFlowChanged)
            labels.Add("callsite-adaptation-only");

        // Tests (only for test-looking files)
        if (_testsBehaviorChanged)
            labels.Add("tests-behavior-changed");
        else if (_isTestFile)
            labels.Add("tests-refactor-only");

        // I labels
        if (_operatorOrKeywordChanged || _literalChanged) labels.Add("logic-changed");
        if (_signatureOrVisibilityChanged) labels.Add("signature-or-visibility-changed");
        if (_controlFlowChanged) labels.Add("control-flow-changed");
        if (_concurrencyChanged) labels.Add("concurrency-changed");
        if (_errorHandlingLoggingChanged) labels.Add("error-handling-logging-changed");
        if (_dataAccessChanged) labels.Add("data-access-changed");
        if (_securityBoundaryChanged) labels.Add("security-or-boundary-changed");
        if (_buildOrRuntimeValuesChanged) labels.Add("build-or-runtime-values-changed");

        if (labels.Count == 0) labels.Add("uncertain-keep");
        return LabelCatalog.Decide(labels);
    }

    private static List<Tok> SliceInvocationRegion(List<Tok> ts)
    {
        for (int i = 0; i < ts.Count - 1; i++)
        {
            bool isCtor = ts[i].Kind == TokKind.Keyword && ts[i].Val == "new";
            bool isCall = ts[i].Kind == TokKind.Identifier && ts[i + 1].Kind == TokKind.Punct && ts[i + 1].Val == "(";

            if (isCtor || isCall)
            {
                // Find the opening '('
                int j = i + 1;
                if (isCtor)
                {
                    // Skip type chain after 'new' until '('
                    while (j < ts.Count && !(ts[j].Kind == TokKind.Punct && ts[j].Val == "(")) j++;
                }
                if (j >= ts.Count || ts[j].Val != "(") break;

                // Capture balanced parens "( ... )"
                int start = j, depth = 0;
                for (int k = j; k < ts.Count; k++)
                {
                    if (ts[k].Kind == TokKind.Punct && ts[k].Val == "(") depth++;
                    else if (ts[k].Kind == TokKind.Punct && ts[k].Val == ")")
                    {
                        depth--;
                        if (depth == 0) return ts.GetRange(start, k - start + 1);
                    }
                }
                break; // no closing ')'
            }
        }
        return ts;
    }

    private static bool IsLikelyTestFile(string p)
    {
        return p.Contains("/test/") || p.Contains("/tests/") || p.Contains("__tests__/")
            || p.EndsWith("tests.cs") || p.EndsWith("test.cs")
            || p.EndsWith(".spec.ts") || p.EndsWith(".spec.tsx")
            || p.EndsWith(".test.ts") || p.EndsWith(".test.tsx")
            || p.EndsWith(".spec.js") || p.EndsWith(".test.js")
            || p.EndsWith(".spec.jsx") || p.EndsWith(".test.jsx");
    }

    private void ObserveLineKind(string line)
    {
        if (!_tok.IsCommentLine(line))
            _sawCommentsOnly = false;

        if (!_tok.IsImportLine(line))
            _sawImportsOnly = false;

        if (!_tok.IsRegionLine(line))
            _sawRegionsOnly = false;
    }

    private void AnalyzePair(string r, string a)
    {
        var rT = _tok.LexLine(r);
        var aT = _tok.LexLine(a);

        if (_tok.NormalizeSpace(r) != _tok.NormalizeSpace(a))
            _sawWhitespaceOnly = false;

        // ---------- 1) Callsite adaptation FIRST ----------
        var looksInv = LooksLikeInvocation(rT) || LooksLikeInvocation(aT);
        bool isAdapt = false;

        if (looksInv)
        {
            if (IsCallsiteAdaptationOnly(rT, aT))
            {
                _sawCallsiteAdaptPair = true;
                isAdapt = true;

                // Propagation of newly-added params (if known)
                var addIds = aT.Where(t => t.Kind == TokKind.Identifier).Select(t => t.Val)
                               .ToHashSet(StringComparer.Ordinal);
                if (_newParamNames.Overlaps(addIds))
                    _propagatedParamSeen = true;
            }
            else
            {
                _sawNonAdaptInvocation = true;
            }
        }

        // ---------- 2) Rename pairing (independent) ----------
        var (sigR, idsR) = BuildSignature(rT);
        var (sigA, idsA) = BuildSignature(aT);
        if (idsR.Count > 0 || idsA.Count > 0)
        {
            var pairs = PairIds(idsR, idsA);
            if (pairs.Count > 0)
            {
                _anyRenameDetected = true;
                foreach (var (oldId, newId) in pairs)
                {
                    if (oldId == newId) continue;
                    if (_renameMap.TryGetValue(oldId, out var mapped))
                    {
                        if (!string.Equals(mapped, newId, StringComparison.Ordinal))
                            _renameConsistent = false;
                    }
                    else
                    {
                        _renameMap[oldId] = newId;
                    }
                }
                if (_renameMap.Count == 1)
                {
                    var kv = _renameMap.First();
                    if (StringUtil.Levenshtein(kv.Key, kv.Value) <= 2)
                        _singleRenameTyposize = true;
                }
            }
        }

        // If this line is a clean callsite adaptation, skip semantics/signature checks
        if (isAdapt)
            return;

        // ---------- 3) Signature (only if not an invocation) ----------
        bool looksSigR = !looksInv && LooksLikeSignature(rT);
        bool looksSigA = !looksInv && LooksLikeSignature(aT);
        if (looksSigR || looksSigA)
        {
            bool methodR = rT.Any(t => t.Kind == TokKind.Punct && t.Val == "(");
            bool methodA = aT.Any(t => t.Kind == TokKind.Punct && t.Val == "(");

            if (methodR || methodA)
            {
                var oldParams = ParamNamesFromParens(rT);
                var newParams = ParamNamesFromParens(aT);

                foreach (var p in newParams.Except(oldParams, StringComparer.Ordinal))
                    _newParamNames.Add(p);

                if (!oldParams.SequenceEqual(newParams))
                    _signatureOrVisibilityChanged = true;
            }
            else
            {
                // properties / events / indexers
                if (!sigR.SequenceEqual(sigA))
                    _signatureOrVisibilityChanged = true;
            }
        }

        // ---------- 4) Generic semantics (operators/keywords/literals) ----------
        if (!sigR.SequenceEqual(sigA))
            CompareSemantics(rT, aT);

        // Tests behavior
        var anyAssert = RxCSharpAssert.IsMatch(r) || RxCSharpAssert.IsMatch(a) ||
                        RxJsAssert.IsMatch(r) || RxJsAssert.IsMatch(a);
        if (anyAssert && (_literalChanged || _operatorOrKeywordChanged))
            _testsBehaviorChanged = true;
    }

    private static (List<string> sig, List<string> ids) BuildSignature(List<Tok> ts)
    {
        var sig = new List<string>(ts.Count);
        var ids = new List<string>();
        foreach (var t in ts)
        {
            switch (t.Kind)
            {
                case TokKind.Identifier: sig.Add("ID"); ids.Add(t.Val); break;
                case TokKind.Keyword: sig.Add("K:" + t.Val); break;
                case TokKind.Literal: sig.Add("LIT"); break;
                case TokKind.Punct: sig.Add("P:" + t.Val); break;
            }
        }
        return (sig, ids);
    }

    private static List<(string oldId, string newId)> PairIds(List<string> oldIds, List<string> newIds)
    {
        var n = Math.Min(oldIds.Count, newIds.Count);
        var pairs = new List<(string, string)>(n);
        for (int i = 0; i < n; i++) pairs.Add((oldIds[i], newIds[i]));
        return pairs;
    }

    private void CompareSemantics(List<Tok> rT, List<Tok> aT)
    {
        var rKw = rT.Where(t => t.Kind == TokKind.Keyword).Select(t => t.Val).ToList();
        var aKw = aT.Where(t => t.Kind == TokKind.Keyword).Select(t => t.Val).ToList();
        var rOp = rT.Where(t => t.Kind == TokKind.Punct).Select(t => t.Val).ToList();
        var aOp = aT.Where(t => t.Kind == TokKind.Punct).Select(t => t.Val).ToList();

        var rLit = rT.Count(t => t.Kind == TokKind.Literal);
        var aLit = aT.Count(t => t.Kind == TokKind.Literal);
        if (rLit != aLit) _literalChanged = true;

        if (!rOp.SequenceEqual(aOp) || !rKw.SequenceEqual(aKw))
            _operatorOrKeywordChanged = true;

        if (rKw.Concat(aKw).Any(ControlFlow.Contains))
            _controlFlowChanged |= !rKw.SequenceEqual(aKw);

        if (rKw.Concat(aKw).Any(Concurrency.Contains) ||
            rT.Any(t => t.Val is "Task" or "await" or "async") ||
            aT.Any(t => t.Val is "Task" or "await" or "async"))
            _concurrencyChanged |= !rKw.SequenceEqual(aKw);

        if (rKw.Concat(aKw).Any(Visibility.Contains))
            _signatureOrVisibilityChanged |= !rKw.SequenceEqual(aKw);

        if (ContainsAny(rT, "throw", "catch", "finally", "ILogger", "Log", "Console", "Debug") ||
            ContainsAny(aT, "throw", "catch", "finally", "ILogger", "Log", "Console", "Debug"))
        {
            if (!rKw.SequenceEqual(aKw) || !rOp.SequenceEqual(aOp)) _errorHandlingLoggingChanged = true;
        }

        if (ContainsAny(rT, "Sql", "Db", "DbSet", "ExecuteSql", "SqlCommand", "SaveChanges", "Find", "Add", "Update", "Remove", "fetch", "axios") ||
            ContainsAny(aT, "Sql", "Db", "DbSet", "ExecuteSql", "SqlCommand", "SaveChanges", "Find", "Add", "Update", "Remove", "fetch", "axios"))
        {
            if (!rKw.SequenceEqual(aKw) || !rOp.SequenceEqual(aOp) || _literalChanged) _dataAccessChanged = true;
        }

        if (ContainsAny(rT, "Authorize", "Authentication", "Jwt", "Cors", "CORS", "Cookie", "X-Frame-Options", "Content-Security-Policy") ||
            ContainsAny(aT, "Authorize", "Authentication", "Jwt", "Cors", "CORS", "Cookie", "X-Frame-Options", "Content-Security-Policy"))
        {
            if (!rKw.SequenceEqual(aKw) || !rOp.SequenceEqual(aOp) || _literalChanged) _securityBoundaryChanged = true;
        }

        _buildOrRuntimeValuesChanged |= _literalChanged;
    }

    private static bool ContainsAny(List<Tok> ts, params string[] needles)
    {
        var set = new HashSet<string>(needles, StringComparer.Ordinal);
        return ts.Any(t => set.Contains(t.Val));
    }

    // Strict: must look like a declaration, not just any '('
    private static bool LooksLikeSignature(List<Tok> ts)
    {
        bool hasVisibility = ts.Any(t => t.Kind == TokKind.Keyword &&
                                         (t.Val is "public" or "private" or "protected" or "internal" or "export"));
        bool hasFunctionKw = ts.Any(t => t.Kind == TokKind.Keyword && t.Val == "function");
        bool hasParens = ts.Any(t => t.Kind == TokKind.Punct && (t.Val == "(" || t.Val == ")"));

        // C# property/event/indexer pattern
        bool propertyPattern =
            ts.Any(t => t.Kind == TokKind.Punct && t.Val == "{") &&
            ts.Any(t => t.Kind == TokKind.Punct && t.Val == "}") &&
            ts.Any(t => t.Kind == TokKind.Keyword && (t.Val is "get" or "set" or "init" or "add" or "remove"));

        if ((hasVisibility || hasFunctionKw) && hasParens) return true;
        return propertyPattern;
    }

    private static List<string> ParamNamesFromParens(List<Tok> ts)
    {
        var names = new List<string>();
        int depth = 0;
        for (int i = 0; i < ts.Count; i++)
        {
            var t = ts[i];
            if (t.Kind == TokKind.Punct && t.Val == "(") { depth++; continue; }
            if (t.Kind == TokKind.Punct && t.Val == ")")
            {
                if (depth == 1) break;
                if (depth > 0) depth--;
                continue;
            }
            if (depth == 1 && t.Kind == TokKind.Identifier) names.Add(t.Val);
        }
        return names;
    }

    private static bool LooksLikeInvocation(List<Tok> ts)
    {
        for (int i = 0; i < ts.Count - 1; i++)
            if (ts[i].Kind == TokKind.Identifier && ts[i + 1].Kind == TokKind.Punct && ts[i + 1].Val == "(") return true;
        return ts.Any(t => t.Kind == TokKind.Keyword && t.Val == "new");
    }

    private static bool IsCallsiteAdaptationOnly(List<Tok> rFull, List<Tok> aFull)
    {
        // Focus only on the invocation’s "(...)" region to ignore 'var', lhs, etc.
        var r = SliceInvocationRegion(rFull);
        var a = SliceInvocationRegion(aFull);

        bool Forbidden(List<Tok> ts) =>
            ts.Any(t => t.Kind == TokKind.Keyword && (t.Val is "await" or "throw" or "try" or "catch" or "lock" or "using")) ||
            ts.Any(t => t.Kind == TokKind.Punct && t.Val == "=>");
        if (Forbidden(a) || Forbidden(r)) return false;

        // Allow simple args: ids, simple literals, null/default/new, and tame punctuators
        var allowedPunct = new HashSet<string>(new[]
            { ".", ",", ":", "=", "(", ")", "<", ">", "{", "}", "[", "]", ";" }); // ← includes ';'

        bool Allowed(Tok t) =>
            t.Kind is TokKind.Identifier or TokKind.Literal ||
            (t.Kind == TokKind.Keyword && (t.Val is "null" or "default" or "new")) ||
            (t.Kind == TokKind.Punct && allowedPunct.Contains(t.Val));

        if (!(r.All(Allowed) && a.All(Allowed))) return false;

        // No added nesting: arguments should not introduce deeper '(' nesting
        int Depth(List<Tok> ts)
        {
            int d = 0, m = 0;
            foreach (var t in ts)
            {
                if (t.Kind == TokKind.Punct && t.Val == "(") { d++; m = Math.Max(m, d); }
                else if (t.Kind == TokKind.Punct && t.Val == ")") { d = Math.Max(0, d - 1); }
            }
            return m;
        }

        return Depth(a) <= Depth(r);
    }
}

// -----------------------------------------------------------------------------
// Concrete classifiers
// -----------------------------------------------------------------------------

public sealed class CSharpDiffClassifier : IFileDiffClassifier
{
    public bool TryClassify(string filePath, string unifiedDiff, out FilterResult result)
    {
        var fp = filePath.ToLowerInvariant();
        if (!fp.EndsWith(".cs")) { result = default; return false; }

        // Language-specific noise / artifacts
        var noise = new HashSet<string>(StringComparer.Ordinal);
        var norm = fp.Replace('\\', '/');
        if (norm.EndsWith(".designer.cs") || norm.EndsWith(".g.cs") || norm.EndsWith(".generated.cs") ||
            norm.Contains("/obj/") || norm.Contains("/bin/"))
            noise.Add("generated-artifact");

        if (norm.EndsWith(".csproj") || norm.EndsWith(".props") || norm.EndsWith(".targets"))
            noise.Add("config-formatting-only");

        if (norm.EndsWith("license") || norm.EndsWith("license.txt") || norm.EndsWith("license.md"))
            noise.Add("license-header-only");
        if (norm.Contains("/.github/") || norm.EndsWith(".md") || norm.EndsWith(".txt"))
            noise.Add("project-noise");

        var parsed = UnifiedDiff.Parse(unifiedDiff);
        var engine = new DiffAnalysisEngine(new CSharpTokenizer(), filePath);
        result = engine.Analyze(parsed, noise);
        return true;
    }
}

public sealed class ReactDiffClassifier : IFileDiffClassifier
{
    public bool TryClassify(string filePath, string unifiedDiff, out FilterResult result)
    {
        var fp = filePath.ToLowerInvariant();
        if (!(fp.EndsWith(".js") || fp.EndsWith(".ts") || fp.EndsWith(".jsx") || fp.EndsWith(".tsx")))
        { result = default; return false; }

        var noise = new HashSet<string>(StringComparer.Ordinal);
        var norm = fp.Replace('\\', '/');

        if (norm.EndsWith(".min.js")) noise.Add("generated-artifact");
        if (norm.EndsWith(".lock") || norm.EndsWith("yarn.lock") || norm.EndsWith("package-lock.json") || norm.EndsWith("pnpm-lock.yaml"))
            noise.Add("snapshots-formatting-only");

        if (norm.EndsWith(".editorconfig") || norm.EndsWith(".eslintrc") ||
            norm.EndsWith("tsconfig.json") || norm.EndsWith("webpack.config.js"))
            noise.Add("config-formatting-only");

        if (norm.Contains("/.github/") || norm.EndsWith(".md"))
            noise.Add("project-noise");

        var parsed = UnifiedDiff.Parse(unifiedDiff);
        var engine = new DiffAnalysisEngine(new JsTsReactTokenizer(), filePath);
        result = engine.Analyze(parsed, noise);
        return true;
    }
}

public sealed class JsonXmlConfigClassifier : IFileDiffClassifier
{
    public bool TryClassify(string filePath, string unifiedDiff, out FilterResult result)
    {
        var fp = (filePath ?? string.Empty).ToLowerInvariant();
        if (!(fp.EndsWith(".json") || fp.EndsWith(".xml"))) { result = default; return false; }

        // Reserved for future config-aware logic; currently not handled.
        result = default;
        return false;
    }
}

public sealed class MarkdownClassifier : IFileDiffClassifier
{
    public bool TryClassify(string filePath, string unifiedDiff, out FilterResult result)
    {
        var fp = (filePath ?? string.Empty).ToLowerInvariant();
        if (!(fp.EndsWith(".md") || fp.EndsWith(".mdx"))) { result = default; return false; }

        // Reserved for future prompt/instructions markdown logic; currently not handled.
        result = default;
        return false;
    }
}