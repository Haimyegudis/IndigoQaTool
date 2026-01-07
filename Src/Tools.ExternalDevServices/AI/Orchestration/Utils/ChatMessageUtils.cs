using Microsoft.Extensions.AI;
using Tools.ExternalDevServices.Utils;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public static class ChatMessageUtils
{
    public static class SystemChatMessages
    {
        public static readonly ChatMessage MaxReasoning =
            new(ChatRole.System,
                "If you support reasoning/thinking, use your max reasoning/thinking capabilities when generating responses");
    }

    public static IReadOnlyCollection<ChatMessage> WithDiagnostics(this IReadOnlyCollection<ChatMessage> messages, string precedingText,
        DiagnosticsHelper? diagnostics)
    {
        diagnostics?.AddInformation(
            $"""
             {precedingText}
             {string.Join("\r\n\r\n", messages.Select(m => $"Role: {m.Role}\r\nText:\r\n{m.Text}"))}
             """);
        return messages;
    }

    public static ChatMessage WithDiagnostics(this ChatMessage message, string precedingText,
        DiagnosticsHelper? diagnostics)
    {
        diagnostics?.AddInformation(
            $"""
             {precedingText}
             Role: {message.Role}
             Text: 
             {message.Text}
             """);
        return message;
    }
}