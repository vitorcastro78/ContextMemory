namespace ContextMemory.Core.Contracts;

public interface IProfileLearner
{
    void LearnFromTurn(
        string appId,
        string userId,
        string userMessage,
        string assistantMessage);

    void LearnFromFeedback(string appId, string userId, int score, string? reason);
}
