using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IContentFilter
{
    ContentFilterResult FilterPre(string appId, string userId, string content, ContentRules rules);
    ContentFilterResult FilterPost(string appId, string userId, string content, ContentRules rules, string defaultLanguage);
}
