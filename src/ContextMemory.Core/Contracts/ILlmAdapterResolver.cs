namespace ContextMemory.Core.Contracts;

public interface ILlmAdapterResolver
{
    ILlmAdapter Resolve(string llmBackend);
}
