namespace ContextMemory.Embeddings.Configuration;

public sealed class EmbeddingsOptions
{
    public const string SectionName = "ContextMemory:Embeddings";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string ModelPath { get; set; } = "../ContextMemory.Embeddings/models/model.onnx";
    public string VocabPath { get; set; } = "../ContextMemory.Embeddings/models/vocab.txt";
    public int MaxSequenceLength { get; set; } = 256;
    public int Dimensions { get; set; } = 384;
}
