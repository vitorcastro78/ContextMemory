using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IContentRulesStore
{
    ContentRules GetRules(string appId);
    void Reload(string appId);
}
