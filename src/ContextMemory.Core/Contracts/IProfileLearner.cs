namespace ContextMemory.Core.Contracts;

public interface IProfileLearner
{
    void LearnFromTurn(
        string appId,
        string userId,
        string userMessage,
        string assistantMessage);
}
