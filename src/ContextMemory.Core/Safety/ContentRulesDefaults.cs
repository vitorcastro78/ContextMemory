using ContextMemory.Core.Models;

namespace ContextMemory.Core.Safety;

internal static class ContentRulesDefaults
{
    public static ContentRules ForApp(string appId) =>
        appId.Contains("kyc", StringComparison.OrdinalIgnoreCase)
            ? new ContentRules
            {
                BlockedTopics = ["concorrentes", "preços internos"],
                RequiredDisclaimer = "Esta resposta não constitui aconselhamento jurídico vinculativo.",
                MaxInputLength = 8000,
                MaxResponseLength = 4000,
                EnforceLanguage = "pt-PT"
            }
            : new ContentRules
            {
                MaxInputLength = 8000,
                MaxResponseLength = 4000
            };
}
