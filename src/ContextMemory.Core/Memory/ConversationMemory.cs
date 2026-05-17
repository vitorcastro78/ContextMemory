using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Memory;

public sealed class ConversationMemory : IConversationMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _historyRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ConversationMemory(IOptions<ContextMemoryOptions> options)
    {
        _historyRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "conversation-history");
        Directory.CreateDirectory(_historyRoot);
    }

    public async Task<IReadOnlyList<OllamaMessage>> GetHistoryAsync(
        string appId,
        string userId,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var result = new List<OllamaMessage>();

            if (!string.IsNullOrWhiteSpace(file.SessionSummary))
            {
                result.Add(new OllamaMessage
                {
                    Role = "assistant",
                    Content = $"[RESUMO DE SESSÕES ANTERIORES]\n{file.SessionSummary}"
                });
            }

            result.AddRange(file.Messages);
            return Trim(result, maxMessages);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendAsync(
        string appId,
        string userId,
        IEnumerable<OllamaMessage> messages,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            file.Messages.AddRange(messages);
            file.Messages = Trim(file.Messages, maxMessages).ToList();
            await SaveFileAsync(appId, userId, file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> GetMessageCountAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            return file.Messages.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplySummaryAsync(
        string appId,
        string userId,
        string summary,
        IReadOnlyList<OllamaMessage> remainingMessages,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadFileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var mergedSummary = string.IsNullOrWhiteSpace(file.SessionSummary)
                ? summary
                : $"{file.SessionSummary}\n\n{summary}";

            file.SessionSummary = mergedSummary.Trim();
            file.Messages = remainingMessages.ToList();

            await SaveFileAsync(appId, userId, file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));

    private string GetFilePath(string appId, string userId)
    {
        var appDir = Path.Combine(_historyRoot, appId);
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, $"{userId}.json");
    }

    private async Task<ConversationHistoryFile> LoadFileAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        var path = GetFilePath(appId, userId);
        if (!File.Exists(path))
            return new ConversationHistoryFile();

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer
            .DeserializeAsync<ConversationHistoryFile>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new ConversationHistoryFile();

        file.Messages ??= [];
        return file;
    }

    private async Task SaveFileAsync(
        string appId,
        string userId,
        ConversationHistoryFile file,
        CancellationToken cancellationToken)
    {
        var path = GetFilePath(appId, userId);
        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, file, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<OllamaMessage> Trim(List<OllamaMessage> messages, int maxMessages)
    {
        if (messages.Count <= maxMessages)
            return messages;

        return messages.TakeLast(maxMessages).ToList();
    }
}
