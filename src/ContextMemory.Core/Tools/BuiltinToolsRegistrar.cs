using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Tools;

public sealed class BuiltinToolsRegistrar
{
    private readonly ConcurrentDictionary<string, byte> _registered = new(StringComparer.Ordinal);
    private readonly IToolRegistry _registry;
    private readonly IWikiIndexService _wikiIndex;
    private readonly IUserProfileStore _userProfileStore;
    private readonly IKnowledgeLoop _knowledgeLoop;
    private readonly IAppRegistry _appRegistry;

    public BuiltinToolsRegistrar(
        IToolRegistry registry,
        IWikiIndexService wikiIndex,
        IUserProfileStore userProfileStore,
        IKnowledgeLoop knowledgeLoop,
        IAppRegistry appRegistry)
    {
        _registry = registry;
        _wikiIndex = wikiIndex;
        _userProfileStore = userProfileStore;
        _knowledgeLoop = knowledgeLoop;
        _appRegistry = appRegistry;
    }

    public void EnsureRegistered(string appId, AppRuntimeConfig config)
    {
        if (!_registered.TryAdd(appId, 0))
            return;
        RegisterForApp(appId, config);
    }

    public void RegisterForApp(string appId, AppRuntimeConfig config)
    {
        _registry.Register(appId, new ToolDefinition(
            "wiki_search",
            "Search the app wiki for relevant domain knowledge",
            async (args, ct) =>
            {
                if (!args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
                    return "Missing query argument.";

                if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
                    return "App not found.";

                await _wikiIndex.EnsureIndexedAsync(appId, app.WikiPath, ct).ConfigureAwait(false);

                var chunks = await _wikiIndex
                    .SearchAsync(appId, query, config.WikiChunksTopK, config.SimilarityThreshold, ct)
                    .ConfigureAwait(false);

                return chunks.Count == 0
                    ? "No wiki chunks found."
                    : string.Join("\n---\n", chunks.Select(c => c.Content));
            }));

        _registry.Register(appId, new ToolDefinition(
            "user_profile_get",
            "Get learned facts about the current user",
            async (args, ct) =>
            {
                if (!args.TryGetValue("userId", out var userId) || string.IsNullOrWhiteSpace(userId))
                    return "Missing userId argument.";

                var profile = await _userProfileStore.GetProfileAsync(appId, userId, ct).ConfigureAwait(false);
                return profile.Facts.Count == 0
                    ? "No profile facts."
                    : JsonSerializer.Serialize(profile.Facts.Select(f => f.Text));
            }));

        _registry.Register(appId, new ToolDefinition(
            "session_context_set",
            "Set active session context for the user",
            async (args, ct) =>
            {
                if (!args.TryGetValue("userId", out var userId) || string.IsNullOrWhiteSpace(userId))
                    return "Missing userId.";
                if (!args.TryGetValue("context", out var context))
                    return "Missing context.";

                await _userProfileStore
                    .SetSessionContextAsync(appId, userId, context, ct)
                    .ConfigureAwait(false);
                return "Session context updated.";
            }));

        _registry.Register(appId, new ToolDefinition(
            "knowledge_loop_submit",
            "Submit manual knowledge for ingestion",
            async (args, ct) =>
            {
                if (!args.TryGetValue("content", out var content) || string.IsNullOrWhiteSpace(content))
                    return "Missing content.";

                var userId = args.TryGetValue("userId", out var u) ? u : "manual-submit";
                await _knowledgeLoop
                    .EvaluateSessionAsync(appId, userId, [new OllamaMessage { Role = "user", Content = content }], ct)
                    .ConfigureAwait(false);
                return "Knowledge submitted for evaluation.";
            }));
    }
}
