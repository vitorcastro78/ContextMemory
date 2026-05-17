using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IIntentDetector
{
    MessageIntent Detect(string? message);
}
