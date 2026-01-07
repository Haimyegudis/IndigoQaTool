using Microsoft.Extensions.AI;
using System.Text;
using System.Text.RegularExpressions;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public static partial class ChatResponseExtensions
{
    [GeneratedRegex(@"<think\b[^>]*>([\s\S]*?)</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkingBlock();

    public static string RemoveThinking(this ChatResponse chatResponse, out string thinking)
    {
        var responseText = chatResponse.Text;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            thinking = string.Empty;
            return string.Empty;
        }

        var thinkingRegex = ThinkingBlock();

        // Extract all <think>...</think> blocks (join with a blank line if multiple)
        var sb = new StringBuilder();
        foreach (Match m in thinkingRegex.Matches(responseText))
        {
            var inner = m.Groups[1].Value.Trim();
            if (inner.Length == 0) continue;
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append(inner);
        }
        thinking = sb.ToString();

        // Remove the thinking blocks from the final text
        var cleaned = thinkingRegex.Replace(responseText, string.Empty);
        return cleaned.Trim();
    }

    public static ChatResponse WithDiagnostics(this ChatResponse chatResponse, string precedingText,
        DiagnosticsHelper? diagnosticsHelper)
    {
        diagnosticsHelper?.AddInformation(
            $"""
             {precedingText}
             Finish Reason: {chatResponse.FinishReason}
             Response: 
             {chatResponse.Text}
             """);
        return chatResponse;
    }
}