using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Memory;

public sealed class SessionSummarizer : ISessionSummarizer
{
    private readonly IConversationMemory _conversationMemory;
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly int _summarizeAfterMessages;
    private readonly int _keepRecentMessages;
    private readonly ILogger<SessionSummarizer> _logger;

    public SessionSummarizer(
        IConversationMemory conversationMemory,
        ILlmAdapterResolver adapterResolver,
        IOptions<ContextMemoryOptions> options,
        ILogger<SessionSummarizer> logger)
    {
        _conversationMemory = conversationMemory;
        _adapterResolver = adapterResolver;
        _summarizeAfterMessages = options.Value.SummarizeAfterMessages > 0
            ? options.Value.SummarizeAfterMessages
            : 50;
        _keepRecentMessages = options.Value.MaxHistoryMessages > 0
            ? options.Value.MaxHistoryMessages
            : 20;
        _logger = logger;
    }

    public async Task MaybeSummarizeAsync(
        string appId,
        string userId,
        string model,
        string llmBackend,
        CancellationToken cancellationToken = default)
    {
        var count = await _conversationMemory
            .GetMessageCountAsync(appId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (count <= _summarizeAfterMessages)
            return;

        try
        {
            var history = await _conversationMemory
                .GetHistoryAsync(appId, userId, count, cancellationToken)
                .ConfigureAwait(false);

            var messagesOnly = history
                .Where(m => !m.Content.StartsWith("[RESUMO DE SESSÕES ANTERIORES]", StringComparison.Ordinal))
                .ToList();

            var toSummarizeCount = messagesOnly.Count - _keepRecentMessages;
            if (toSummarizeCount <= 0)
                return;

            var toSummarize = messagesOnly.Take(toSummarizeCount).ToList();
            var remaining = messagesOnly.Skip(toSummarizeCount).ToList();

            var transcript = string.Join(
                "\n",
                toSummarize.Select(m => $"{m.Role}: {m.Content}"));

            var prompt =
                $"""
                Resume a seguinte conversa de forma concisa, preservando factos importantes, decisões e contexto de negócio.
                O resumo será usado como memória de longo prazo da sessão.

                Conversa:
                {transcript}
                """;

            var adapter = _adapterResolver.Resolve(llmBackend);
            var resolvedModel = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;

            var generateResponse = await adapter
                .GenerateAsync(
                    new OllamaGenerateRequest
                    {
                        Model = resolvedModel,
                        Prompt = prompt,
                        Stream = false
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var summary = generateResponse.Response
                ?? generateResponse.Message?.Content
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(summary))
                return;

            await _conversationMemory
                .ApplySummaryAsync(appId, userId, summary.Trim(), remaining, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Session summarized for {AppId}/{UserId} ({Summarized} messages compressed)",
                appId,
                userId,
                toSummarizeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session summarization failed for {AppId}/{UserId}", appId, userId);
        }
    }
}
