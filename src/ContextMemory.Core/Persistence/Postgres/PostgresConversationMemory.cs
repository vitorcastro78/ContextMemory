using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresConversationMemory : IConversationMemory
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public PostgresConversationMemory(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

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
            var file = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
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
            var file = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            file.Messages.AddRange(messages);
            file.Messages = Trim(file.Messages, maxMessages).ToList();
            await SaveAsync(appId, userId, file, cancellationToken).ConfigureAwait(false);
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
            var file = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
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
            var file = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var mergedSummary = string.IsNullOrWhiteSpace(file.SessionSummary)
                ? summary
                : $"{file.SessionSummary}\n\n{summary}";

            file.SessionSummary = mergedSummary.Trim();
            file.Messages = remainingMessages.ToList();
            await SaveAsync(appId, userId, file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ConversationHistoryFile> LoadAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.ConversationHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return new ConversationHistoryFile();

        var messages = JsonSerializer.Deserialize<List<OllamaMessage>>(row.MessagesJson, PostgresJson.CamelCase) ?? [];
        return new ConversationHistoryFile
        {
            SessionSummary = row.SessionSummary,
            Messages = messages
        };
    }

    private async Task SaveAsync(
        string appId,
        string userId,
        ConversationHistoryFile file,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.ConversationHistories
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var json = JsonSerializer.Serialize(file.Messages, PostgresJson.CamelCase);
        if (row is null)
        {
            db.ConversationHistories.Add(new ConversationHistoryEntity
            {
                AppId = appId,
                UserId = userId,
                SessionSummary = file.SessionSummary,
                MessagesJson = json
            });
        }
        else
        {
            row.SessionSummary = file.SessionSummary;
            row.MessagesJson = json;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));

    private static IReadOnlyList<OllamaMessage> Trim(List<OllamaMessage> messages, int maxMessages)
    {
        if (messages.Count <= maxMessages)
            return messages;

        return messages.TakeLast(maxMessages).ToList();
    }
}
