using System.Text;
using System.Text.RegularExpressions;

namespace ContextMemory.Embeddings;

public sealed partial class TokenizerService
{
    private const string ClsToken = "[CLS]";
    private const string SepToken = "[SEP]";
    private const string PadToken = "[PAD]";
    private const string UnkToken = "[UNK]";

    private readonly Dictionary<string, int> _vocab;
    private readonly int _maxLength;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _padId;
    private readonly int _unkId;

    public bool IsAvailable => _vocab.Count > 0;

    public TokenizerService(string vocabPath, int maxLength)
    {
        _maxLength = maxLength;
        _vocab = [];

        if (!File.Exists(vocabPath))
            return;

        var lines = File.ReadAllLines(vocabPath);
        for (var i = 0; i < lines.Length; i++)
            _vocab[lines[i].Trim()] = i;

        _clsId = _vocab.GetValueOrDefault(ClsToken, 101);
        _sepId = _vocab.GetValueOrDefault(SepToken, 102);
        _padId = _vocab.GetValueOrDefault(PadToken, 0);
        _unkId = _vocab.GetValueOrDefault(UnkToken, 100);
    }

    public TokenizedInput Tokenize(string text)
    {
        var tokens = new List<int> { _clsId };

        foreach (var word in BasicTokenize(text))
        {
            foreach (var piece in WordPieceTokenize(word))
                tokens.Add(_vocab.GetValueOrDefault(piece, _unkId));
        }

        tokens.Add(_sepId);

        var length = Math.Min(tokens.Count, _maxLength);
        if (tokens.Count > _maxLength)
            tokens = tokens.Take(_maxLength - 1).Append(_sepId).ToList();

        length = tokens.Count;

        var inputIds = new long[_maxLength];
        var attentionMask = new long[_maxLength];
        var tokenTypeIds = new long[_maxLength];

        for (var i = 0; i < _maxLength; i++)
        {
            if (i < length)
            {
                inputIds[i] = tokens[i];
                attentionMask[i] = 1;
            }
            else
            {
                inputIds[i] = _padId;
                attentionMask[i] = 0;
            }

            tokenTypeIds[i] = 0;
        }

        return new TokenizedInput(inputIds, attentionMask, tokenTypeIds, length);
    }

    private IEnumerable<string> BasicTokenize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var cleaned = WhitespaceRegex().Replace(normalized, " ").Trim();
        if (string.IsNullOrEmpty(cleaned))
            yield break;

        foreach (var token in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = PunctuationRegex().Split(token);
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;
                yield return part;
            }
        }
    }

    private IEnumerable<string> WordPieceTokenize(string word)
    {
        if (word.Length > 200)
            word = word[..200];

        if (_vocab.ContainsKey(word))
        {
            yield return word;
            yield break;
        }

        var start = 0;
        var subTokens = new List<string>();
        while (start < word.Length)
        {
            var end = word.Length;
            string? current = null;

            while (start < end)
            {
                var substr = start == 0
                    ? word[start..end]
                    : "##" + word[start..end];

                if (_vocab.ContainsKey(substr))
                {
                    current = substr;
                    break;
                }

                end--;
            }

            if (current is null)
            {
                subTokens.Clear();
                subTokens.Add(UnkToken);
                break;
            }

            subTokens.Add(current);
            start = end;
        }

        foreach (var token in subTokens)
            yield return token;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"([.,!?;:'""()\[\]{}])")]
    private static partial Regex PunctuationRegex();
}

public sealed record TokenizedInput(long[] InputIds, long[] AttentionMask, long[] TokenTypeIds, int Length);
