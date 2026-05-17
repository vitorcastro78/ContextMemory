namespace ContextMemory.Core.Contracts;

public interface IImplicitFeedbackDetector
{
    bool TryDetect(string userMessage, out int score, out string? reason);
}
