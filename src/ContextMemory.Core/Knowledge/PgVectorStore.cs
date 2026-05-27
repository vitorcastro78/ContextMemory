using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace ContextMemory.Core.Knowledge;

public sealed class PgVectorStore : IPgVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgVectorStore> _logger;

    public PgVectorStore(string connectionString, ILogger<PgVectorStore> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
        _logger = logger;
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync().ConfigureAwait(false);

    public async Task<bool> TryLoadFromCacheAsync(string appId, string wikiPath, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM wiki_chunks WHERE app_id = @appId", conn);
        cmd.Parameters.AddWithValue("appId", appId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    public async Task UpsertChunksAsync(
        string appId,
        string wikiPath,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var del = new NpgsqlCommand("DELETE FROM wiki_chunks WHERE app_id = @appId", conn, tx))
        {
            del.Parameters.AddWithValue("appId", appId);
            await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var chunk in chunks)
            await InsertChunkAsync(conn, tx, appId, chunk, cancellationToken).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("PgVector upsert {Count} chunks for {AppId}", chunks.Count, appId);
    }

    public async Task ReplaceFileChunksAsync(
        string appId,
        string wikiPath,
        string source,
        IReadOnlyList<WikiChunkVector> chunks,
        CancellationToken cancellationToken = default)
    {
        await DeleteBySourceAsync(appId, source, cancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
            return;

        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in chunks)
            await InsertChunkAsync(conn, tx, appId, chunk, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBySourceAsync(string appId, string source, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM wiki_chunks WHERE app_id = @appId AND source = @source",
            conn);
        cmd.Parameters.AddWithValue("appId", appId);
        cmd.Parameters.AddWithValue("source", source);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<WikiChunkVector>> SearchAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default) =>
        SearchInternalAsync(appId, query, topK, threshold, learnedOnly: false, cancellationToken);

    public Task<IReadOnlyList<WikiChunkVector>> SearchLearnedAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default) =>
        SearchInternalAsync(appId, query, topK, threshold, learnedOnly: true, cancellationToken);

    public async Task<int> GetChunkCountAsync(string appId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM wiki_chunks WHERE app_id = @appId", conn);
        cmd.Parameters.AddWithValue("appId", appId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<WikiChunkVector>> SearchInternalAsync(
        string appId,
        float[] query,
        int topK,
        float threshold,
        bool learnedOnly,
        CancellationToken cancellationToken)
    {
        await using var conn = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var learnedFilter = learnedOnly ? "AND is_learned = TRUE" : string.Empty;
        await using var cmd = new NpgsqlCommand(
            $"""
             SELECT source, header_path, content, is_learned,
                    1 - (embedding <=> @query) AS similarity
             FROM wiki_chunks
             WHERE app_id = @appId
               AND 1 - (embedding <=> @query) >= @threshold
               {learnedFilter}
             ORDER BY embedding <=> @query
             LIMIT @topK
             """,
            conn);

        cmd.Parameters.AddWithValue("appId", appId);
        cmd.Parameters.AddWithValue("query", new Vector(query));
        cmd.Parameters.AddWithValue("threshold", threshold);
        cmd.Parameters.AddWithValue("topK", topK);

        var results = new List<WikiChunkVector>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new WikiChunkVector
            {
                Source = reader.GetString(0),
                HeaderPath = reader.GetString(1),
                Content = reader.GetString(2),
                Vector = query,
                IsLearned = reader.GetBoolean(3),
                Similarity = reader.GetFloat(4)
            });
        }

        return results;
    }

    private static async Task InsertChunkAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string appId,
        WikiChunkVector chunk,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wiki_chunks (app_id, source, header_path, content, embedding, is_learned, created_at)
            VALUES (@appId, @source, @header, @content, @embedding, @learned, @created)
            """,
            conn,
            tx);
        cmd.Parameters.AddWithValue("appId", appId);
        cmd.Parameters.AddWithValue("source", chunk.Source);
        cmd.Parameters.AddWithValue("header", chunk.HeaderPath);
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("embedding", new Vector(chunk.Vector));
        cmd.Parameters.AddWithValue("learned", chunk.IsLearned);
        cmd.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
}
