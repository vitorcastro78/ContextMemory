using ContextMemory.Core.Contracts;
using ContextMemory.Embeddings.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ContextMemory.Embeddings;

public sealed class OnnxEmbeddingEngine : IEmbeddingEngine, IDisposable
{
    private readonly InferenceSession? _session;
    private readonly TokenizerService? _tokenizer;
    private readonly ILogger<OnnxEmbeddingEngine> _logger;
    private readonly int _dimensions;
    private readonly string? _outputName;

    public bool IsAvailable => _session is not null && _tokenizer?.IsAvailable == true;
    public int Dimensions { get; }

    public OnnxEmbeddingEngine(IOptions<EmbeddingsOptions> options, ILogger<OnnxEmbeddingEngine> logger)
    {
        _logger = logger;
        var config = options.Value;
        _dimensions = config.Dimensions;

        var modelPath = ResolvePath(config.ModelPath, config.ContentRootPath);
        var vocabPath = ResolvePath(config.VocabPath, config.ContentRootPath);

        if (!File.Exists(modelPath) || !File.Exists(vocabPath))
        {
            logger.LogWarning(
                "ONNX embedding model not found. Place model.onnx and vocab.txt in {ModelPath}. RAG disabled until configured.",
                Path.GetDirectoryName(modelPath));
            Dimensions = _dimensions;
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath);
            _tokenizer = new TokenizerService(vocabPath, config.MaxSequenceLength);
            _outputName = _session.OutputMetadata.Keys.First();
            Dimensions = _dimensions;
            logger.LogInformation("ONNX embedding engine loaded from {ModelPath}", modelPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load ONNX embedding model from {ModelPath}", modelPath);
        }
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable || _session is null || _tokenizer is null)
            throw new InvalidOperationException("Embedding engine is not available.");

        return Task.FromResult(EmbedCore(text));
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable || _session is null || _tokenizer is null)
            throw new InvalidOperationException("Embedding engine is not available.");

        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            results[i] = EmbedCore(texts[i]);

        return Task.FromResult<IReadOnlyList<float[]>>(results);
    }

    private float[] EmbedCore(string text)
    {
        var tokenized = _tokenizer!.Tokenize(text);
        var inputIds = CreateTensor(tokenized.InputIds, tokenized.Length);
        var attentionMask = CreateTensor(tokenized.AttentionMask, tokenized.Length);
        var tokenTypeIds = CreateTensor(tokenized.TokenTypeIds, tokenized.Length);

        using var results = _session!.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        ]);

        var output = results.First().AsTensor<float>();
        return MeanPoolAndNormalize(output, tokenized.AttentionMask, tokenized.Length);
    }

    private static DenseTensor<long> CreateTensor(long[] values, int length)
    {
        var tensor = new DenseTensor<long>([1, length]);
        for (var i = 0; i < length; i++)
            tensor[0, i] = values[i];
        return tensor;
    }

    private float[] MeanPoolAndNormalize(Tensor<float> hiddenStates, long[] attentionMask, int seqLen)
    {
        var dims = hiddenStates.Dimensions.ToArray();
        var hiddenSize = dims[^1];
        var embedding = new float[hiddenSize];
        var tokenCount = 0f;

        for (var i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 0)
                continue;

            tokenCount++;
            for (var j = 0; j < hiddenSize; j++)
                embedding[j] += hiddenStates[0, i, j];
        }

        if (tokenCount > 0)
        {
            for (var j = 0; j < hiddenSize; j++)
                embedding[j] /= tokenCount;
        }

        L2Normalize(embedding);
        return embedding;
    }

    private static void L2Normalize(Span<float> vector)
    {
        var norm = 0f;
        foreach (var value in vector)
            norm += value * value;

        norm = MathF.Sqrt(norm);
        if (norm <= 0f)
            return;

        for (var i = 0; i < vector.Length; i++)
            vector[i] /= norm;
    }

    private static string ResolvePath(string path, string contentRoot) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRoot);

    public void Dispose() => _session?.Dispose();
}
