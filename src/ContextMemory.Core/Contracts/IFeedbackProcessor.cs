namespace ContextMemory.Core.Contracts;

public interface IFeedbackProcessor
{
    void ProcessFeedbackAsync(string appId, string userId, string messageId, int score, string? reason);
}
