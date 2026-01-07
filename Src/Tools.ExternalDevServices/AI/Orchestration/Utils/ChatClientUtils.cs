using Microsoft.Extensions.AI;
using Newtonsoft.Json;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public static class ChatClientUtils
{
    public static async Task<string> OptimizeSystemPromptForCurrentChatClientAsync(IChatClient chatClient, string prompt)
    {
        var optimizationPrompt =
            $"""
             Craft an optimized **system prompt** intended for **You, the LLM** (the same model producing this response) to use in future conversations.

             Requirements:
             - **Preserve** the original intent and instructions from the input; **do not add, remove, or change** them, **except** to make explicit that **You, the LLM, must operate at your highest supported reasoning/thinking effort**.
             - If the input is already optimal, **return it unchanged while ensuring** the explicit highest-reasoning instruction is present (if supported).
             - Use **whatever format is most effective for You, the LLM** (e.g., JSON, XML, Markdown, plain text).
             - **Output only the optimized system prompt** with no commentary or extra text.

             Input prompt:
             {prompt}
             """;

        return (await chatClient.GetResponseAsync(new ChatMessage(ChatRole.User, optimizationPrompt))).RemoveThinking(out _);
    }

    public static async Task<T> ToStructuredResponseAsync<T>(IChatClient chatClient, string response)
    {
        //First try parsing response as-is
        try
        {
            var result = JsonConvert.DeserializeObject<T>(response);
            if (result != null) return result;
        }
        catch
        {
            //
        }

        //Use LLM for strict JSON generation
        const int retries = 5;
        var messages = GetMessagesList();
        for (var retry = 0; retry < retries; retry++)
        {
            try
            {
                var chatResponse = await chatClient.GetResponseAsync(messages);
                messages.AddRange(chatResponse.Messages);
                var result = JsonConvert.DeserializeObject<T>(chatResponse.Text);
                if (result != null) return result;
            }
            catch
            {
                //
            }

            messages.Add(new ChatMessage(ChatRole.User, "Your response was not a valid JSON matching the provided schema, fix your response"));
        }

        throw new InvalidOperationException($"Failed to deserialize to {typeof(T).Name} after {retries} retries");

        List<ChatMessage> GetMessagesList() =>
        [
            new (ChatRole.System,
                """
                You are a strict JSON generator. Output JSON **only** (no code fences, no text).
                Follow the provided JSON Schema exactly. Do not include keys not in the schema.
                """),
            new (ChatRole.User,
                $"""
                 Convert the following response to a structured JSON response that matches the provided JSON schema.

                 Response:
                 {response}

                 Schema:
                 {JsonUtils.GetSchema<T>()}
                 """)
        ];
    }
}