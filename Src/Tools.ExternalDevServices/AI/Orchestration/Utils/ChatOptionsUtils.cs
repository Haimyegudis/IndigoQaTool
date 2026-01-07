using Microsoft.Extensions.AI;

namespace Tools.ExternalDevServices.AI.Orchestration.Utils;

public static class ChatOptionsUtils
{
    /// <summary>
    /// Creates a Greedy/legalistic (very strict) chat options for the AI.
    /// </summary>
    /// <param name="seed"></param>
    /// <returns></returns>
    public static ChatOptions CreateGreedyChatOptions(int seed = 12) => new()
    {
        Temperature = 0.1f,
        TopP = 0.1f,
        TopK = 1,
        Seed = seed
    };

    /// <summary>
    /// Creates a Controlled (strict yet somewhat flexible) chat options for the AI.
    /// </summary>
    /// <param name="seed"></param>
    /// <returns></returns>
    public static ChatOptions CreateControlledChatOptions(int seed = 7) => new()
    {
        Temperature = 0.2f,
        TopP = 0.9f,
        TopK = 30,
        Seed = seed
    };

    /// <summary>
    /// Creates a deterministic, low-entropy chat options for the AI.
    /// </summary>
    /// <param name="seed"></param>
    /// <returns></returns>
    public static ChatOptions CreateLowEntropyChatOptions(int seed = 76) => new()
    {
        Temperature = 0.1f,
        TopP = 0.9f,
        TopK = 50,
        Seed = seed
    };
}