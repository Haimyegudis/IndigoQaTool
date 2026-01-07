using SharpToken;
using System.Collections.Concurrent;
using System.Text;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public enum ContextFitEstimation
{
    /// <summary>
    /// Guaranteed fit in context, no need to tokenize for accurate tokens count.
    /// </summary>
    FitGuaranteed,

    /// <summary>
    /// Likely exceed context, no need to tokenize for accurate tokens count.
    /// </summary>
    ExceedLikely,

    /// <summary>
    /// Ambiguous, need to tokenize for accurate tokens count.
    /// </summary>
    Ambiguous
}

public static class BytesPerToken
{
    /// <summary>
    /// English prose
    /// </summary>
    public const double Optimistic = 4.0;

    /// <summary>
    /// English + typical code
    /// </summary>
    public const double Typical = 3.3;

    /// <summary>
    /// Dense/symbol-heavy code
    /// </summary>
    public const double Pessimistic = 2.3;

    /// <summary>
    /// Very dense code
    /// </summary>
    public const double CodeSafeFactor = 2.0;

    /// Prose with spaces
    public const double EnglishSafeFactor = 3.0;

    /// <summary>
    /// Worst-case multilingual/emoji
    /// </summary>
    public const double GenericUtf8SafeFactor = 1.5;
}

public static class ContextUtils
{
    // Cache encodings to avoid repeated allocations/initialization
    private static readonly ConcurrentDictionary<string, GptEncoding> EncodingsCache = new();

    /// <summary>
    /// Try to get an exact token count using SharpToken for an OpenAI model id.
    /// Returns true if exact count is available; false if model is unknown/unsupported.
    /// </summary>
    public static bool TryCountTokensExact(string model, string text, out int tokenCount)
    {
        tokenCount = 0;
        if (string.IsNullOrWhiteSpace(model)) return false;

        try
        {
            var enc = EncodingsCache.GetOrAdd(model, GptEncoding.GetEncodingForModel);
            tokenCount = enc.Encode(text).Count;
            return true;
        }
        catch
        {
            // Model not recognized by SharpToken (e.g., OSS models like LLaMA/Mistral)
            return false;
        }
    }

    /// <summary>
    /// Heuristic estimate for tokens when tokenizer is unknown.
    /// Default ~3.3 chars/token works well for English code.
    /// </summary>
    public static int EstimateTokens(string text, double bytesPerToken)
    {
        if (bytesPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerToken), bytesPerToken,
                $"{nameof(bytesPerToken)} cannot be zero or negative number");
        }

        return (int)Math.Ceiling(Encoding.UTF8.GetByteCount(text) / bytesPerToken);
    }

    /// <summary>
    /// Quick decision (no tokenizer):
    /// - FitGuaranteed if UTF-8 byte count ≤ maxTokens (strict upper bound on tokens).
    /// - ExceedLikely if charCount / charsPerToken ≥ maxTokens.
    /// - Unknown otherwise (tokenize or chunk).
    /// </summary>
    public static ContextFitEstimation EstimateContextFit(IReadOnlyCollection<string> texts, 
        int maxTokens,
        double estimatedBytesPerToken,
        double minBytesPerTokenForFitCheck,
        out double? estimatedTokens)
    {
        estimatedTokens = null;
        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens,
                $"{nameof(maxTokens)} cannot be zero or negative number");
        }
        if (estimatedBytesPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedBytesPerToken), estimatedBytesPerToken,
                $"{nameof(estimatedBytesPerToken)} cannot be zero or negative number");
        }

        // 1) Guaranteed-fit gate: tokens ≤ UTF-8 bytes
        var utf8Bytes = texts.Sum(text => Encoding.UTF8.GetByteCount(text));
        if (utf8Bytes <= maxTokens * minBytesPerTokenForFitCheck) return ContextFitEstimation.FitGuaranteed;

        // 2) Likely-exceed gate: conservative chars/token for English code
        estimatedTokens = utf8Bytes / estimatedBytesPerToken;
        return estimatedTokens.Value >= maxTokens
            ? ContextFitEstimation.ExceedLikely
            : ContextFitEstimation.Ambiguous;
    }

    /// <summary>
    /// Convenience: returns exact if possible; otherwise heuristic.
    /// </summary>
    public static int CountTokensBestEffort(string? model, string text, double fallbackBytesPerToken)
    {
        if (!string.IsNullOrWhiteSpace(model) && TryCountTokensExact(model, text, out var exact))
            return exact;

        if (fallbackBytesPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fallbackBytesPerToken), fallbackBytesPerToken,
                $"{nameof(fallbackBytesPerToken)} cannot be zero or negative number");
        }

        return EstimateTokens(text, fallbackBytesPerToken);
    }
}