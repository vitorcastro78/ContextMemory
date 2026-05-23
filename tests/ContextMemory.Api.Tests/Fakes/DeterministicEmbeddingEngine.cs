using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Tests.Fakes;

/// <summary>
/// Produces identical unit vectors so cosine similarity is 1.0 for RAG tests without ONNX.
/// </summary>
public sealed class DeterministicEmbeddingEngine : IEmbeddingEngine
{
    private readonly float[] _unitVector;

    public DeterministicEmbeddingEngine()
    {
        Dimensions = 8;
        _unitVector = new float[Dimensions];
        _unitVector[0] = 1f;
    }

    public bool IsAvailable => true;
    public int Dimensions { get; }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult((float[])_unitVector.Clone());

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<float[]> vectors = texts.Select(_ => (float[])_unitVector.Clone()).ToList();
        return Task.FromResult(vectors);
    }
}
