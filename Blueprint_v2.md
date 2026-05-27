# ContextMemory — Cursor AI Blueprint v2.0

> **Produto:** ContextMemory — middleware de memória de contexto estendida para LLMs locais
> **Stack:** .NET 9 · ASP.NET Core Minimal API · ONNX Runtime · Markdig · Microsoft.ML · pgvector
> **LLM Backend:** Ollama · LM Studio · OpenAI API (adaptadores intercambiáveis)
> **Contrato de API:** 100% compatível com Ollama (`/api/chat` · `/api/generate`)
> **Modelo Cursor:** Composer 2.0
> **Versão:** 2.0 · Maio 2026

---

## Changelog v1.0 → v2.0

| Área | Adição |
|---|---|
| **KnowledgeLoop** | Pipeline de reingestão de conhecimento conversacional na wiki (Fase 6 — nova) |
| **pgvector** | Substituição do VectorStore em memória por PostgreSQL + pgvector (Fase 7 — nova) |
| **Hermes tool_call** | Suporte estruturado a `<tool_call>` XML do Hermes 4 (Fase 7 — nova) |
| **Webhook/eventos** | `POST /apps/{appId}/events` para injecção de contexto externo (Fase 7 — nova) |
| **Billing multi-tenant** | `PlanStore` + quotas por plano (Fase 7 — nova) |
| **ProfileLearner LLM** | Extracção de factos via prompt estruturado em vez de keywords (Fase 7 — nova) |
| **Gaps fechados** | `.cursorrules` actualizado com regras das novas fases |

---

## 1. Visão do Produto

O ContextMemory é um **middleware transparente** que se posiciona entre qualquer aplicação cliente e um LLM local, tornando o modelo aparentemente mais inteligente sem alterar o modelo em si.

### O problema que resolve

Os LLMs locais são **stateless** — esquecem tudo entre chamadas. Quando múltiplos utilizadores usam o mesmo modelo, não existe isolamento de contexto entre eles. O modelo não conhece o domínio de negócio. Além disso, o conhecimento aprendido nas conversas desaparece — nunca é reutilizado para enriquecer o modelo de domínio.

### Como resolve (v2.0)

O middleware intercepta cada chamada, **enriquece o campo `messages`** com contexto relevante, e encaminha ao LLM real. O cliente nunca sabe que existe middleware. Na v2.0, o conhecimento gerado nas conversas **retroalimenta a wiki de domínio** através do KnowledgeLoop.

### O que o torna "aparentemente mais inteligente"

```
Sem middleware:  modelo nu → resposta genérica

Com middleware:  modelo + sistema de prompt dinâmico
                       + memória conversacional por utilizador
                       + RAG sobre wiki de domínio (pgvector)
                       + memória semântica de longo prazo
                       + summarização automática de sessões antigas
                       + perfil do utilizador aprendido ao longo do tempo
                       + KnowledgeLoop: conversas → wiki (novo v2.0)
                       + tool_call nativo Hermes 4 (novo v2.0)
                → resposta especializada, contextualizada, personalizada,
                  que melhora com o tempo sem intervenção manual
```

---

## 2. Arquitectura Geral (v2.0)

```
┌─────────────────────────────────────────────────────────────────────┐
│  CLIENTES (qualquer stack, qualquer linguagem)                       │
│  KYC Blazor · Helpdesk React · ERP Java · Mobile Flutter            │
└────────────────────────┬────────────────────────────────────────────┘
                         │ POST /api/chat  (payload Ollama nativo)
                         │ Headers: X-App-Id · X-User-Id · Authorization
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│  ContextMemory Platform  (ASP.NET Core Minimal API)                 │
│                                                                     │
│  ┌─────────────┐   ┌──────────────┐   ┌─────────────────┐          │
│  │ API Gateway │→  │Context Engine│→  │  LLM Adapter    │          │
│  │auth·rate·tel│   │ monta prompt │   │Ollama/Hermes4/  │          │
│  └─────────────┘   └──────┬───────┘   │  OpenAI         │          │
│                           │           └─────────────────┘          │
│              ┌────────────┼──────────────┐                          │
│              ▼            ▼              ▼                          │
│       MemoryStore   KnowledgeStore  SessionStore                    │
│       (histórico)   (pgvector RAG)  (metadados)                     │
│              │            │              │                          │
│       UserProfileStore  AppConfigStore  PlanStore (novo)            │
│       (perfil user)     (config app)    (billing)                   │
│              │            │                                         │
│              └──────────→ KnowledgeLoop (novo v2.0) ──────────→    │
│                           Extrai · Avalia · Reingeriu na Wiki       │
└─────────────────────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────┐
              │   Ollama :11434  │  (ou LM Studio · OpenAI)
              │   Hermes 4 / 8B  │  (tool_call nativo)
              └──────────────────┘
```

---

## 3. Estrutura do Projecto (v2.0)

```
ContextMemory/
├── src/
│   ├── ContextMemory.Api/
│   │   ├── Endpoints/
│   │   │   ├── ChatEndpoint.cs
│   │   │   ├── GenerateEndpoint.cs
│   │   │   ├── AppsEndpoint.cs
│   │   │   ├── AppsRegisterEndpoint.cs
│   │   │   ├── AppsWikiEndpoint.cs
│   │   │   ├── AppsConfigEndpoint.cs
│   │   │   ├── FeedbackEndpoint.cs
│   │   │   ├── AdminEndpoint.cs
│   │   │   ├── EventsEndpoint.cs          ← NOVO v2.0
│   │   │   └── KnowledgeLoopEndpoint.cs   ← NOVO v2.0
│   │   └── ...
│   │
│   ├── ContextMemory.Core/
│   │   ├── Engine/
│   │   │   ├── ContextEngine.cs
│   │   │   ├── PromptComposer.cs
│   │   │   ├── IntentDetector.cs
│   │   │   └── ToolCallParser.cs          ← NOVO v2.0 (Hermes <tool_call>)
│   │   ├── KnowledgeLoop/                 ← NOVO v2.0 (pasta completa)
│   │   │   ├── IKnowledgeLoop.cs
│   │   │   ├── ConversationEvaluator.cs
│   │   │   ├── KnowledgeExtractor.cs
│   │   │   ├── KnowledgeMerger.cs
│   │   │   ├── WikiIngestionService.cs
│   │   │   └── KnowledgeLoopOrchestrator.cs
│   │   ├── Memory/
│   │   │   ├── ConversationMemory.cs
│   │   │   ├── SemanticMemory.cs
│   │   │   └── SessionSummarizer.cs
│   │   ├── Knowledge/
│   │   │   ├── WikiLoader.cs
│   │   │   ├── VectorStore.cs             (mantido para File provider)
│   │   │   ├── PgVectorStore.cs           ← NOVO v2.0
│   │   │   └── SimilaritySearch.cs
│   │   ├── Profile/
│   │   │   ├── UserProfileStore.cs
│   │   │   ├── AppConfigStore.cs
│   │   │   ├── AppRegistry.cs
│   │   │   ├── ProfileLearner.cs          (actualizado: LLM-based extraction)
│   │   │   └── PlanStore.cs               ← NOVO v2.0
│   │   ├── Billing/                       ← NOVO v2.0 (pasta completa)
│   │   │   ├── IPlanStore.cs
│   │   │   ├── PlanDefinition.cs
│   │   │   └── QuotaEnforcer.cs
│   │   ├── Tools/                         ← NOVO v2.0 (Hermes tool_call)
│   │   │   ├── IToolRegistry.cs
│   │   │   ├── ToolDefinition.cs
│   │   │   ├── ToolExecutor.cs
│   │   │   └── BuiltinTools/
│   │   │       ├── WikiSearchTool.cs
│   │   │       ├── UserProfileTool.cs
│   │   │       └── ExternalEventTool.cs
│   │   └── ...
│   │
│   ├── ContextMemory.Adapters/
│   │   ├── OllamaAdapter.cs
│   │   ├── LmStudioAdapter.cs
│   │   ├── OpenAiAdapter.cs
│   │   └── HermesAdapter.cs              ← NOVO v2.0 (tool_call XML parser)
│   │
│   ├── ContextMemory.Embeddings/
│   └── ...
│
├── wikis/
│   └── {appId}/
│       ├── manual/                        ← wiki carregada manualmente
│       └── learned/                       ← NOVO v2.0 (gerada pelo KnowledgeLoop)
│           ├── _index.md
│           ├── 2026-05-topic-a.md
│           └── 2026-05-topic-b.md
│
├── data/
│   ├── app-profiles/
│   ├── knowledge-loop/                    ← NOVO v2.0
│   │   └── {appId}/
│   │       ├── pending-evaluation.json    (conversas aguardam avaliação)
│   │       ├── approved.json              (aprovadas para ingestão)
│   │       └── rejected.json              (rejeitadas)
│   └── ...
│
└── BLUEPRINT.md
```

---

## 4. Fase 6 — KnowledgeLoop (reingestão de conhecimento conversacional)

### 4.1 Conceito

O KnowledgeLoop transforma conversas em conhecimento de domínio estruturado que enriquece a wiki RAG. O ciclo completo é:

```
CONVERSA
    │
    ▼
ConversationEvaluator
    │  "esta conversa tem conhecimento novo?"
    │  Score 0–1 via LLM prompt estruturado
    ▼
KnowledgeExtractor
    │  Extrai factos, procedimentos, definições
    │  Gera markdown estruturado
    ▼
KnowledgeMerger
    │  Verifica duplicados via cosine similarity (pgvector)
    │  Merge com chunk existente se similaridade > 0.85
    │  Cria novo chunk se < 0.85
    ▼
WikiIngestionService
    │  Escreve .md em wikis/{appId}/learned/
    │  Chama WikiIndexService.ReindexFileAsync()
    ▼
VectorStore actualizado → RAG enriquecido nas próximas conversas
```

### 4.2 Interfaces C#

```csharp
// IKnowledgeLoop.cs
public interface IKnowledgeLoop
{
    /// <summary>
    /// Avalia uma sessão e enfileira para extracção se tiver valor.
    /// Chamado assincronamente após cada sessão.
    /// </summary>
    Task EvaluateSessionAsync(
        string appId,
        string userId,
        IReadOnlyList<OllamaMessage> sessionMessages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processa a fila de sessões aprovadas e ingere na wiki.
    /// Chamado por background worker ou trigger manual.
    /// </summary>
    Task ProcessPendingAsync(
        string appId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeLoopStats> GetStatsAsync(string appId, CancellationToken ct = default);
}

// ConversationEvaluationResult.cs
public record ConversationEvaluationResult
{
    public bool HasNewKnowledge { get; init; }
    public float Score { get; init; }            // 0–1
    public string Reasoning { get; init; } = string.Empty;
    public List<string> ExtractedTopics { get; init; } = [];
}

// ExtractedKnowledge.cs
public record ExtractedKnowledge
{
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }   // markdown gerado
    public required string Category { get; init; }  // "procedure", "fact", "definition", "example"
    public float Confidence { get; init; }
    public DateTimeOffset ExtractedAt { get; init; }
    public string SourceSessionId { get; init; } = string.Empty;
}

// KnowledgeLoopStats.cs
public record KnowledgeLoopStats
{
    public int SessionsEvaluated { get; init; }
    public int SessionsApproved { get; init; }
    public int ChunksCreated { get; init; }
    public int ChunksMerged { get; init; }
    public int ChunksRejected { get; init; }
    public DateTimeOffset LastRunAt { get; init; }
}
```

### 4.3 ConversationEvaluator

```csharp
// ConversationEvaluator.cs
public sealed class ConversationEvaluator
{
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<ConversationEvaluator> _logger;

    // Prompt enviado ao LLM para avaliação
    // Usa formato JSON estruturado para parse determinístico
    private const string EvaluationPromptTemplate = """
        Analisa a seguinte conversa e determina se contém conhecimento de domínio
        novo e reutilizável (factos, procedimentos, definições, exemplos concretos).

        Responde APENAS com JSON válido, sem texto adicional:
        {
          "has_new_knowledge": true|false,
          "score": 0.0-1.0,
          "reasoning": "explicação em 1-2 frases",
          "topics": ["tópico1", "tópico2"]
        }

        Critérios para score > 0.6 (aprovação):
        - Contém factos específicos do domínio não óbvios
        - Descreve procedimentos ou passos concretos
        - Esclarece conceitos com exemplos reais
        - Resolve um problema de forma documentável

        Critérios para rejeição (score < 0.4):
        - Apenas saudações ou perguntas genéricas
        - Informação já conhecida e trivial
        - Conversas pessoais sem conteúdo de domínio
        - Erros ou desinformação

        CONVERSA:
        {conversation}
        """;

    public async Task<ConversationEvaluationResult> EvaluateAsync(
        string appId,
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var transcript = FormatTranscript(messages);
        var prompt = EvaluationPromptTemplate.Replace("{conversation}", transcript);

        var response = await adapter.GenerateAsync(new OllamaGenerateRequest
        {
            Model = config.LlmModel,
            Prompt = prompt,
            Stream = false,
            Format = "json"    // força JSON output no Ollama
        }, cancellationToken).ConfigureAwait(false);

        return ParseEvaluationResponse(response.Response ?? string.Empty);
    }

    private static string FormatTranscript(IReadOnlyList<OllamaMessage> messages) =>
        string.Join("\n", messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => $"[{m.Role.ToUpper()}]: {m.Content}"));

    private static ConversationEvaluationResult ParseEvaluationResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ConversationEvaluationResult
            {
                HasNewKnowledge = root.GetProperty("has_new_knowledge").GetBoolean(),
                Score = root.GetProperty("score").GetSingle(),
                Reasoning = root.GetProperty("reasoning").GetString() ?? string.Empty,
                ExtractedTopics = root.GetProperty("topics")
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList()
            };
        }
        catch
        {
            return new ConversationEvaluationResult { HasNewKnowledge = false, Score = 0 };
        }
    }
}
```

### 4.4 KnowledgeExtractor

```csharp
// KnowledgeExtractor.cs
public sealed class KnowledgeExtractor
{
    private const string ExtractionPromptTemplate = """
        Com base na conversa abaixo, extrai o conhecimento de domínio reutilizável
        e formata-o como um documento Markdown estruturado.

        Responde APENAS com JSON válido:
        {
          "title": "título conciso do conhecimento (max 60 chars)",
          "category": "procedure|fact|definition|example|troubleshooting",
          "confidence": 0.0-1.0,
          "content": "## Título\n\nConteúdo em markdown...\n\n### Passos\n1. ...\n\n### Notas\n..."
        }

        Regras para o content:
        - Usa headers markdown (##, ###)
        - Remove referências pessoais ("o utilizador disse", "eu respondi")
        - Generaliza para ser reutilizável em contextos futuros
        - Inclui exemplos concretos quando presentes
        - Máximo 800 palavras
        - Língua: {language}

        CONVERSA:
        {conversation}
        """;

    public async Task<ExtractedKnowledge?> ExtractAsync(
        string appId,
        string userId,
        string sessionId,
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var prompt = ExtractionPromptTemplate
            .Replace("{conversation}", FormatTranscript(messages))
            .Replace("{language}", config.DefaultLanguage);

        var response = await adapter.GenerateAsync(new OllamaGenerateRequest
        {
            Model = config.LlmModel,
            Prompt = prompt,
            Stream = false,
            Format = "json"
        }, cancellationToken).ConfigureAwait(false);

        return ParseExtractionResponse(appId, userId, sessionId, response.Response ?? string.Empty);
    }
}
```

### 4.5 KnowledgeMerger

```csharp
// KnowledgeMerger.cs
// Verifica duplicados antes de criar novo chunk.
// Se similaridade cosine > 0.85 com chunk existente, faz merge em vez de criar.
public sealed class KnowledgeMerger
{
    private const float MergeThreshold = 0.85f;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly IPgVectorStore _vectorStore;         // usa pgvector em produção
    private readonly ILlmAdapterResolver _adapterResolver;

    public async Task<MergeResult> MergeOrCreateAsync(
        string appId,
        ExtractedKnowledge knowledge,
        CancellationToken cancellationToken = default)
    {
        // 1. Embeder o novo conhecimento
        var newVector = await _embeddingEngine.EmbedAsync(
            knowledge.Title + "\n" + knowledge.Content, cancellationToken).ConfigureAwait(false);

        // 2. Procurar chunks similares na wiki learned
        var similar = await _vectorStore.SearchLearnedAsync(
            appId, newVector, topK: 3, threshold: MergeThreshold, cancellationToken).ConfigureAwait(false);

        if (similar.Count > 0)
        {
            // 3a. Merge com o chunk mais similar
            var best = similar[0];
            var merged = await MergeWithLlmAsync(appId, best.Content, knowledge.Content, cancellationToken)
                .ConfigureAwait(false);

            return new MergeResult
            {
                Action = MergeAction.Merged,
                TargetPath = best.Source,
                Content = merged
            };
        }

        // 3b. Criar novo chunk
        return new MergeResult
        {
            Action = MergeAction.Created,
            TargetPath = GenerateWikiPath(appId, knowledge),
            Content = knowledge.Content
        };
    }

    private async Task<string> MergeWithLlmAsync(
        string appId, string existing, string newContent, CancellationToken ct)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var prompt = $"""
            Merge os dois documentos de conhecimento abaixo num único documento
            coerente em Markdown. Elimina redundâncias, mantém todos os factos
            únicos de ambos. Responde APENAS com o markdown resultante.

            DOCUMENTO EXISTENTE:
            {existing}

            NOVO CONHECIMENTO:
            {newContent}
            """;

        var response = await adapter.GenerateAsync(new OllamaGenerateRequest
        {
            Model = config.LlmModel,
            Prompt = prompt,
            Stream = false
        }, ct).ConfigureAwait(false);

        return response.Response ?? existing;
    }

    private static string GenerateWikiPath(string appId, ExtractedKnowledge k)
    {
        var slug = Regex.Replace(k.Title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        var date = k.ExtractedAt.ToString("yyyy-MM");
        return $"learned/{date}-{slug}.md";
    }
}

public enum MergeAction { Created, Merged }
public record MergeResult
{
    public MergeAction Action { get; init; }
    public required string TargetPath { get; init; }
    public required string Content { get; init; }
}
```

### 4.6 KnowledgeLoopOrchestrator

```csharp
// KnowledgeLoopOrchestrator.cs
// Orquestra o pipeline completo. Chamado:
//   (A) assincronamente após cada sessão (trigger automático)
//   (B) manualmente via POST /apps/{appId}/knowledge-loop/run
//   (C) por scheduled job (background service)

public sealed class KnowledgeLoopOrchestrator : IKnowledgeLoop
{
    private readonly ConversationEvaluator _evaluator;
    private readonly KnowledgeExtractor _extractor;
    private readonly KnowledgeMerger _merger;
    private readonly WikiIngestionService _ingestion;
    private readonly IKnowledgeLoopStore _store;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<KnowledgeLoopOrchestrator> _logger;

    // Score mínimo para aprovação automática
    private const float AutoApproveThreshold = 0.75f;
    // Score para aprovação com revisão humana (Admin UI)
    private const float ManualReviewThreshold = 0.50f;

    public async Task EvaluateSessionAsync(
        string appId,
        string userId,
        IReadOnlyList<OllamaMessage> sessionMessages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        if (!config.KnowledgeLoopEnabled)
            return;

        // Só avalia sessões com substância (mínimo N mensagens)
        if (sessionMessages.Count < config.KnowledgeLoopMinMessages)
            return;

        try
        {
            var evaluation = await _evaluator.EvaluateAsync(appId, sessionMessages, cancellationToken)
                .ConfigureAwait(false);

            var entry = new KnowledgeLoopEntry
            {
                AppId = appId,
                UserId = userId,
                SessionId = Guid.NewGuid().ToString("N"),
                Messages = sessionMessages.ToList(),
                Evaluation = evaluation,
                Status = evaluation.Score >= AutoApproveThreshold
                    ? KnowledgeLoopStatus.PendingExtraction
                    : evaluation.Score >= ManualReviewThreshold
                        ? KnowledgeLoopStatus.PendingReview
                        : KnowledgeLoopStatus.Rejected,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _store.SaveEntryAsync(entry, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "KnowledgeLoop eval {AppId}/{SessionId}: score={Score:F2} status={Status}",
                appId, entry.SessionId, evaluation.Score, entry.Status);

            // Auto-processar se aprovado
            if (entry.Status == KnowledgeLoopStatus.PendingExtraction)
                _ = ProcessEntryAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KnowledgeLoop evaluation failed for {AppId}", appId);
        }
    }

    public async Task ProcessPendingAsync(string appId, CancellationToken cancellationToken = default)
    {
        var pending = await _store.GetPendingAsync(appId, KnowledgeLoopStatus.PendingExtraction, cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessEntryAsync(KnowledgeLoopEntry entry, CancellationToken ct)
    {
        try
        {
            // 1. Extrair conhecimento estruturado
            var knowledge = await _extractor.ExtractAsync(
                entry.AppId, entry.UserId, entry.SessionId, entry.Messages, ct).ConfigureAwait(false);

            if (knowledge is null)
            {
                await _store.UpdateStatusAsync(entry.SessionId, KnowledgeLoopStatus.Rejected, ct)
                    .ConfigureAwait(false);
                return;
            }

            // 2. Verificar duplicados e fazer merge se necessário
            var mergeResult = await _merger.MergeOrCreateAsync(entry.AppId, knowledge, ct)
                .ConfigureAwait(false);

            // 3. Escrever na wiki e re-indexar
            await _ingestion.IngestAsync(entry.AppId, mergeResult, ct).ConfigureAwait(false);

            // 4. Actualizar status
            await _store.UpdateStatusAsync(entry.SessionId, KnowledgeLoopStatus.Ingested, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "KnowledgeLoop ingested {Action} chunk '{Path}' for {AppId}",
                mergeResult.Action, mergeResult.TargetPath, entry.AppId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KnowledgeLoop processing failed for session {SessionId}", entry.SessionId);
            await _store.UpdateStatusAsync(entry.SessionId, KnowledgeLoopStatus.Failed, ct)
                .ConfigureAwait(false);
        }
    }
}
```

### 4.7 WikiIngestionService

```csharp
// WikiIngestionService.cs
// Escreve o markdown gerado na pasta wikis/{appId}/learned/
// e chama o re-indexador existente (hot-reload por ficheiro).

public sealed class WikiIngestionService
{
    private readonly IWikiIndexService _wikiIndex;
    private readonly IAppRegistry _appRegistry;
    private readonly ILogger<WikiIngestionService> _logger;

    public async Task IngestAsync(
        string appId,
        MergeResult mergeResult,
        CancellationToken cancellationToken = default)
    {
        if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
            throw new InvalidOperationException($"App '{appId}' not found.");

        // Garante que a pasta learned/ existe
        var learnedDir = Path.Combine(app.WikiPath, "learned");
        Directory.CreateDirectory(learnedDir);

        var fileName = Path.GetFileName(mergeResult.TargetPath);
        var fullPath = Path.Combine(app.WikiPath, mergeResult.TargetPath);

        // Adiciona header de metadados ao markdown
        var contentWithMeta = $"""
            ---
            generated: true
            generated_at: {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            action: {mergeResult.Action.ToString().ToLower()}
            ---

            {mergeResult.Content}
            """;

        await File.WriteAllTextAsync(fullPath, contentWithMeta, cancellationToken).ConfigureAwait(false);

        // Re-indexa apenas este ficheiro (hot-reload eficiente)
        var relativePath = Path.GetRelativePath(app.WikiPath, fullPath).Replace('\\', '/');
        await _wikiIndex.ReindexFileAsync(appId, app.WikiPath, relativePath, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Ingested {Action} knowledge chunk: {Path} for {AppId}",
            mergeResult.Action, relativePath, appId);
    }
}
```

### 4.8 Endpoints do KnowledgeLoop

```
POST /apps/{appId}/knowledge-loop/run
    → Processa manualmente todas as sessões pendentes
    → Auth: app API key

GET  /apps/{appId}/knowledge-loop/stats
    → Estatísticas: sessões avaliadas, aprovadas, chunks criados
    → Auth: app API key

GET  /apps/{appId}/knowledge-loop/pending
    → Lista sessões aguardando revisão humana (score 0.50–0.75)
    → Auth: master key

POST /apps/{appId}/knowledge-loop/approve/{sessionId}
    → Aprovação manual de sessão em revisão
    → Auth: master key

DELETE /apps/{appId}/knowledge-loop/reject/{sessionId}
    → Rejeição manual de sessão em revisão
    → Auth: master key

GET /admin/apps/{appId}/knowledge-loop
    → Dashboard do KnowledgeLoop no Admin UI
    → Auth: master key
```

### 4.9 Configuração por appId

```json
// data/app-profiles/{appId}/config.json — campos adicionados
{
  "knowledgeLoopEnabled": true,
  "knowledgeLoopMinMessages": 6,
  "knowledgeLoopAutoApproveThreshold": 0.75,
  "knowledgeLoopManualReviewThreshold": 0.50,
  "knowledgeLoopMaxChunksPerDay": 20,
  "knowledgeLoopCategories": ["procedure", "fact", "definition", "example", "troubleshooting"]
}
```

### 4.10 Background Service

```csharp
// KnowledgeLoopBackgroundService.cs
// Processa a fila de todas as apps de hora em hora
public sealed class KnowledgeLoopBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var app in _appRegistry.GetAllApps())
            {
                var config = _appConfigStore.GetConfig(app.AppId);
                if (!config.KnowledgeLoopEnabled)
                    continue;

                await _orchestrator.ProcessPendingAsync(app.AppId, stoppingToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
```

---

## 5. Fase 7 — Gaps Fechados

### 5A — pgvector (substituição do VectorStore em memória)

**Problema:** O `VectorStore` actual usa `ConcurrentDictionary<string, List<VectorEntry>>` em RAM. Com wikis grandes, o consumo cresce sem limite e não persiste entre restarts.

**Solução:**

```sql
-- Migration: adicionar extensão e coluna
CREATE EXTENSION IF NOT EXISTS vector;

ALTER TABLE semantic_facts DROP COLUMN vector;
ALTER TABLE semantic_facts ADD COLUMN vector vector(384);

CREATE INDEX semantic_facts_vector_idx
  ON semantic_facts USING hnsw (vector vector_cosine_ops);

-- Nova tabela para chunks de wiki
CREATE TABLE wiki_chunks (
    id          BIGSERIAL PRIMARY KEY,
    app_id      VARCHAR(64) NOT NULL,
    source      TEXT NOT NULL,
    header_path TEXT NOT NULL,
    content     TEXT NOT NULL,
    vector      vector(384),
    is_learned  BOOLEAN NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX wiki_chunks_vector_idx
  ON wiki_chunks USING hnsw (vector vector_cosine_ops);

CREATE INDEX wiki_chunks_app_idx ON wiki_chunks (app_id);
```

```csharp
// IPgVectorStore.cs
public interface IPgVectorStore
{
    Task UpsertChunksAsync(string appId, IReadOnlyList<WikiChunkVector> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<WikiChunkVector>> SearchAsync(string appId, float[] query, int topK, float threshold, CancellationToken ct = default);
    Task<IReadOnlyList<WikiChunkVector>> SearchLearnedAsync(string appId, float[] query, int topK, float threshold, CancellationToken ct = default);
    Task DeleteBySourceAsync(string appId, string source, CancellationToken ct = default);
}

// PgVectorStore.cs — usa Npgsql com pgvector
public sealed class PgVectorStore : IPgVectorStore
{
    public async Task<IReadOnlyList<WikiChunkVector>> SearchAsync(
        string appId, float[] query, int topK, float threshold, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        // Operador <=> = cosine distance; 1 - distância = similaridade
        var cmd = new NpgsqlCommand("""
            SELECT source, header_path, content, 1 - (vector <=> @query) AS similarity
            FROM wiki_chunks
            WHERE app_id = @appId AND 1 - (vector <=> @query) >= @threshold
            ORDER BY vector <=> @query
            LIMIT @topK
            """, conn);

        cmd.Parameters.AddWithValue("appId", appId);
        cmd.Parameters.AddWithValue("query", new Pgvector.Vector(query));
        cmd.Parameters.AddWithValue("threshold", threshold);
        cmd.Parameters.AddWithValue("topK", topK);

        // ... read results
    }
}
```

**Dependência NuGet a adicionar:**
```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.Npgsql" Version="0.3.2" />
```

---

### 5B — Hermes 4 tool_call nativo

**Problema:** O Hermes 4 gera respostas com `<tool_call>` XML para invocar tools registadas. O `ILlmAdapter` actual faz pipe da resposta raw sem interpretar estes blocos.

**Solução:**

```csharp
// ToolDefinition.cs
public record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Func<Dictionary<string, string>, Task<string>> Handler { get; init; }
    public List<ToolParameter> Parameters { get; init; } = [];
}

// IToolRegistry.cs
public interface IToolRegistry
{
    void Register(string appId, ToolDefinition tool);
    IReadOnlyList<ToolDefinition> GetTools(string appId);
    Task<string> ExecuteAsync(string appId, string toolName, Dictionary<string, string> args, CancellationToken ct = default);
}

// ToolCallParser.cs
// Detecta e parseia <tool_call> na resposta do Hermes
public sealed class ToolCallParser
{
    private static readonly Regex ToolCallRegex = new(
        @"<tool_call>\s*\{[^}]+\}\s*</tool_call>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public bool TryParse(string response, out ToolCall? toolCall)
    {
        toolCall = null;
        var match = ToolCallRegex.Match(response);
        if (!match.Success) return false;

        var json = match.Value.Replace("<tool_call>", "").Replace("</tool_call>", "").Trim();
        try
        {
            toolCall = JsonSerializer.Deserialize<ToolCall>(json);
            return toolCall is not null;
        }
        catch { return false; }
    }
}

// ContextEngine.cs — integração (adicionar ao pipeline existente)
// Após receber resposta do LLM:
if (_toolCallParser.TryParse(response.Message?.Content ?? "", out var toolCall) && toolCall is not null)
{
    var toolResult = await _toolRegistry.ExecuteAsync(appId, toolCall.Name, toolCall.Arguments, ct)
        .ConfigureAwait(false);

    // Re-invocar o LLM com o resultado da tool
    var toolResultMessage = new OllamaMessage { Role = "tool", Content = toolResult };
    // ... continua o pipeline
}
```

**Tools built-in registadas por default:**

| Tool | Descrição |
|---|---|
| `wiki_search` | Pesquisa na wiki do appId |
| `user_profile_get` | Lê factos do perfil do utilizador |
| `session_context_set` | Define contexto de sessão activo |
| `knowledge_loop_submit` | Submete conhecimento manual para ingestão |

---

### 5C — Webhook / eventos externos

**Problema:** Não há forma de injectar contexto externo em tempo real (ex: ticket resolvido, alerta de sistema, mudança de estado).

**Solução:**

```csharp
// EventsEndpoint.cs
app.MapPost("/apps/{appId}/events", HandleExternalEventAsync).DisableAntiforgery();

private static async Task<IResult> HandleExternalEventAsync(
    string appId,
    ExternalEventRequest body,
    IUserProfileStore userProfileStore,
    IConversationMemory conversationMemory,
    IKnowledgeLoop knowledgeLoop,
    IAppRegistry appRegistry,
    CancellationToken cancellationToken)
{
    // Adiciona o evento como mensagem de sistema na sessão do userId
    // para que o próximo chat tenha contexto do evento
    if (!string.IsNullOrWhiteSpace(body.UserId))
    {
        var eventMessage = new OllamaMessage
        {
            Role = "system",
            Content = $"[EVENTO EXTERNO - {body.EventType}] {body.Payload}"
        };
        await conversationMemory.AppendAsync(appId, body.UserId, [eventMessage], 100, cancellationToken)
            .ConfigureAwait(false);
    }

    // Se o evento contém conhecimento de domínio, submete ao KnowledgeLoop
    if (body.IngestAsKnowledge)
    {
        var syntheticMessage = new OllamaMessage { Role = "user", Content = body.Payload };
        await knowledgeLoop.EvaluateSessionAsync(appId, "system-event", [syntheticMessage], cancellationToken)
            .ConfigureAwait(false);
    }

    return Results.Ok(new { status = "processed", eventId = Guid.NewGuid().ToString("N") });
}

public record ExternalEventRequest
{
    public string? UserId { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public bool IngestAsKnowledge { get; init; }
}
```

---

### 5D — Billing multi-tenant

**Problema:** Rate limiting por `appId` existe, mas não há modelo de planos/quotas para SaaS.

**Solução:**

```csharp
// PlanDefinition.cs
public record PlanDefinition
{
    public required string PlanId { get; init; }           // "free", "pro", "enterprise"
    public int DailyRequestLimit { get; init; }
    public int DailyTokenLimit { get; init; }
    public int MaxWikiSizeMb { get; init; }
    public int MaxUsersPerApp { get; init; }
    public bool KnowledgeLoopEnabled { get; init; }
    public bool CustomToolsEnabled { get; init; }
    public int KnowledgeLoopMaxChunksPerDay { get; init; }
}

// Planos padrão:
// free:       1k req/dia · 100k tokens · 10MB wiki · 5 users · KnowledgeLoop: off
// pro:        50k req/dia · 5M tokens · 500MB wiki · 100 users · KnowledgeLoop: 50 chunks/dia
// enterprise: unlimited · pgvector dedicado · KnowledgeLoop: unlimited

// IPlanStore.cs
public interface IPlanStore
{
    Task<PlanDefinition> GetPlanAsync(string appId, CancellationToken ct = default);
    Task SetPlanAsync(string appId, string planId, CancellationToken ct = default);
    Task<UsageSnapshot> GetDailyUsageAsync(string appId, CancellationToken ct = default);
    Task<bool> CheckQuotaAsync(string appId, int estimatedTokens, CancellationToken ct = default);
}

// QuotaEnforcer.cs — integrado no RateLimitMiddleware existente
// Verifica IPlanStore antes de processar o request
// Retorna 429 com Retry-After e reason: "daily_quota_exceeded"
```

---

### 5E — ProfileLearner baseado em LLM

**Problema:** O `ProfileLearner.ExtractFacts()` usa keywords hardcoded em português — frágil e não generaliza para outros domínios ou línguas.

**Solução:**

```csharp
// ProfileLearner.cs — método actualizado
private const string FactExtractionPrompt = """
    Analisa a seguinte mensagem do utilizador e identifica factos
    reutilizáveis sobre as suas preferências, competências ou contexto de trabalho.

    Responde APENAS com um array JSON de strings (pode ser vazio []):
    ["facto 1", "facto 2"]

    Exemplos de factos válidos:
    - "Prefere respostas em inglês"
    - "Trabalha com Zuora e Azure"
    - "Prefere listas numeradas a texto corrido"
    - "Nível avançado em C# e .NET"

    Exemplos de não-factos (ignorar):
    - Perguntas genéricas sem contexto pessoal
    - Emoções ou expressões passageiras
    - Informações sobre terceiros

    MENSAGEM: {message}
    """;

private async Task<List<string>> ExtractFactsWithLlmAsync(
    string appId, string userMessage, CancellationToken ct)
{
    var config = _appConfigStore.GetConfig(appId);
    var adapter = _adapterResolver.Resolve(config.LlmBackend);

    var response = await adapter.GenerateAsync(new OllamaGenerateRequest
    {
        Model = config.LlmModel,
        Prompt = FactExtractionPrompt.Replace("{message}", userMessage),
        Stream = false,
        Format = "json"
    }, ct).ConfigureAwait(false);

    try
    {
        return JsonSerializer.Deserialize<List<string>>(response.Response ?? "[]") ?? [];
    }
    catch { return []; }
}
```

---

## 6. Contrato de API completo (v2.0)

### Endpoints existentes (inalterados)

```
POST /api/chat
POST /api/generate
POST /api/chat/feedback
GET  /apps/{appId}
GET|PATCH /apps/{appId}/config
PUT  /apps/{appId}/session-context
POST /apps/{appId}/wiki
POST /apps/register
GET  /health
GET  /metrics
GET|POST|DELETE /admin/*
```

### Endpoints novos v2.0

```
POST /apps/{appId}/events
    → Injecta evento externo na sessão de um userId
    → Body: { userId?, eventType, payload, ingestAsKnowledge }
    → Auth: app API key

POST /apps/{appId}/knowledge-loop/run
    → Processa manualmente fila de sessões aprovadas
    → Auth: app API key

GET  /apps/{appId}/knowledge-loop/stats
    → { sessionsEvaluated, approved, chunksCreated, chunksMerged, lastRunAt }
    → Auth: app API key

GET  /admin/apps/{appId}/knowledge-loop
    → Lista todas as entradas (pending, approved, rejected, ingested, failed)
    → Auth: master key

GET  /admin/apps/{appId}/knowledge-loop/pending-review
    → Sessões aguardando revisão humana (score 0.50–0.75)
    → Auth: master key

POST /admin/apps/{appId}/knowledge-loop/approve/{sessionId}
    → Aprovação manual → move para PendingExtraction
    → Auth: master key

DELETE /admin/apps/{appId}/knowledge-loop/reject/{sessionId}
    → Rejeição manual → move para Rejected
    → Auth: master key

GET  /admin/apps/{appId}/plan
    → Plano actual e uso diário
    → Auth: master key

PATCH /admin/apps/{appId}/plan
    → Altera plano da app
    → Body: { planId: "free|pro|enterprise" }
    → Auth: master key
```

---

## 7. Variáveis de Ambiente (v2.0)

```bash
# .env (nunca commitar)

# Existentes
CONTEXT_MEMORY_MASTER_KEY="cm_master_xxxx"
CONTEXT_MEMORY_OLLAMA_ENDPOINT="http://localhost:11434"
CONTEXT_MEMORY_DATA_PATH="./data"
CONTEXT_MEMORY_WIKI_PATH="./wikis"
CONTEXT_MEMORY_DEFAULT_LLM_BACKEND="ollama"
CONTEXT_MEMORY_DEFAULT_LLM_MODEL="hermes4:8b"

# Persistência
CONTEXT_MEMORY_PERSISTENCE_PROVIDER="Postgres"
ConnectionStrings__ContextMemory="Host=...;Database=contextmemory;..."

# KnowledgeLoop (novo v2.0)
CONTEXT_MEMORY_KNOWLEDGE_LOOP_ENABLED="true"
CONTEXT_MEMORY_KNOWLEDGE_LOOP_MIN_MESSAGES="6"
CONTEXT_MEMORY_KNOWLEDGE_LOOP_AUTO_APPROVE_THRESHOLD="0.75"
CONTEXT_MEMORY_KNOWLEDGE_LOOP_MANUAL_REVIEW_THRESHOLD="0.50"
CONTEXT_MEMORY_KNOWLEDGE_LOOP_PROCESS_INTERVAL_HOURS="1"

# pgvector (novo v2.0)
CONTEXT_MEMORY_PGVECTOR_ENABLED="true"
CONTEXT_MEMORY_PGVECTOR_HNSW_EF_CONSTRUCTION="64"
CONTEXT_MEMORY_PGVECTOR_HNSW_M="16"

# Hermes tool_call (novo v2.0)
CONTEXT_MEMORY_TOOL_CALL_ENABLED="true"
CONTEXT_MEMORY_TOOL_CALL_MAX_ITERATIONS="5"

# Billing (novo v2.0)
CONTEXT_MEMORY_BILLING_ENABLED="false"
CONTEXT_MEMORY_DEFAULT_PLAN="pro"
```

---

## 8. Fases de Implementação (v2.0)

### Fases 1–5 (existentes — todas completas ✅)

Conforme blueprint v1.0: setup, RAG, prompts dinâmicos, memória longa, feedback, guardrails, admin, observabilidade, rate limiting, testes, Docker.

### Fase 6 — KnowledgeLoop (nova)

```
FASE 6A — Estrutura e avaliação
"@BLUEPRINT.md Implementa a estrutura base do KnowledgeLoop conforme descrito na
Fase 6: IKnowledgeLoop, KnowledgeLoopEntry, IKnowledgeLoopStore (File + Postgres),
ConversationEvaluator com prompt JSON. Integra o trigger no SessionSummarizer —
após sumarizar uma sessão, chama EvaluateSessionAsync."

FASE 6B — Extracção e merge
"@BLUEPRINT.md Implementa KnowledgeExtractor e KnowledgeMerger conforme Fase 6.
O Merger usa IPgVectorStore.SearchLearnedAsync para verificar duplicados
com threshold 0.85. Implementa WikiIngestionService que escreve em
wikis/{appId}/learned/ e chama ReindexFileAsync."

FASE 6C — Orquestrador e endpoints
"@BLUEPRINT.md Implementa KnowledgeLoopOrchestrator completo com os dois thresholds
(auto-approve 0.75, manual-review 0.50). Implementa KnowledgeLoopBackgroundService
(processa de hora em hora). Implementa todos os endpoints de /knowledge-loop
descritos na Fase 6 (run, stats, pending-review, approve, reject)."

FASE 6D — Admin UI
"@BLUEPRINT.md Adiciona secção KnowledgeLoop ao Admin Blazor e ao dashboard
Alpine.js: lista de entradas por status, botões approve/reject para pending-review,
stats card com sessões avaliadas/aprovadas/chunks criados. Adiciona métricas
Prometheus: cm_knowledge_loop_sessions_total, cm_knowledge_loop_chunks_total."
```

### Fase 7 — Gaps fechados (nova)

```
FASE 7A — pgvector
"@BLUEPRINT.md Substitui o VectorStore in-memory por pgvector conforme Fase 5A.
Adiciona dependências Pgvector e Pgvector.Npgsql. Cria migration AddPgVector
com extensão, nova coluna vector(384) em semantic_facts, tabela wiki_chunks
com índice HNSW. Implementa PgVectorStore que implementa IPgVectorStore.
O WVikiIndexService usa PgVectorStore quando PersistenceProvider=Postgres."

FASE 7B — Hermes tool_call
"@BLUEPRINT.md Implementa o suporte a tool_call do Hermes 4 conforme Fase 5B:
ToolCallParser (regex <tool_call>), IToolRegistry, ToolExecutor. Regista as
4 built-in tools (wiki_search, user_profile_get, session_context_set,
knowledge_loop_submit). Integra no ContextEngine após receber a resposta LLM."

FASE 7C — Eventos externos e billing
"@BLUEPRINT.md Implementa EventsEndpoint (POST /apps/{appId}/events) conforme Fase 5C.
Implementa PlanStore, PlanDefinition com 3 planos (free/pro/enterprise) e
QuotaEnforcer integrado no RateLimitMiddleware existente conforme Fase 5D."

FASE 7D — ProfileLearner LLM-based
"@BLUEPRINT.md Substitui ExtractFacts() keyword-based por LLM-based conforme Fase 5E.
O método ExtractFactsWithLlmAsync usa format=json e faz parse do array de strings.
Mantém o método original como fallback se o LLM falhar ou timeout."
```

---

## 9. Modelos de dados novos (v2.0)

### KnowledgeLoopEntry

```csharp
public sealed class KnowledgeLoopEntry
{
    public required string SessionId { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required List<OllamaMessage> Messages { get; set; }
    public ConversationEvaluationResult? Evaluation { get; set; }
    public KnowledgeLoopStatus Status { get; set; }
    public string? IngestedPath { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public enum KnowledgeLoopStatus
{
    PendingExtraction,  // score >= 0.75, aguarda processamento automático
    PendingReview,      // score 0.50–0.75, aguarda revisão humana
    Rejected,           // score < 0.50 ou rejeitado manualmente
    Ingested,           // processado com sucesso
    Failed              // erro durante processamento
}
```

### WikiChunkVector (pgvector)

```csharp
public record WikiChunkVector
{
    public required string Source { get; init; }
    public required string HeaderPath { get; init; }
    public required string Content { get; init; }
    public required float[] Vector { get; init; }
    public bool IsLearned { get; init; }
    public float Similarity { get; init; }
}
```

### AppRuntimeConfig (campos adicionados)

```csharp
// Adicionar a AppRuntimeConfig.cs existente:
public bool KnowledgeLoopEnabled { get; init; } = false;
public int KnowledgeLoopMinMessages { get; init; } = 6;
public float KnowledgeLoopAutoApproveThreshold { get; init; } = 0.75f;
public float KnowledgeLoopManualReviewThreshold { get; init; } = 0.50f;
public int KnowledgeLoopMaxChunksPerDay { get; init; } = 20;
public bool ToolCallEnabled { get; init; } = false;
public int ToolCallMaxIterations { get; init; } = 5;
public string PlanId { get; init; } = "pro";
```

---

## 10. Métricas Prometheus (v2.0 — adições)

```
# KnowledgeLoop
cm_knowledge_loop_sessions_evaluated_total{appId}
cm_knowledge_loop_sessions_approved_total{appId}
cm_knowledge_loop_sessions_rejected_total{appId}
cm_knowledge_loop_chunks_created_total{appId}
cm_knowledge_loop_chunks_merged_total{appId}
cm_knowledge_loop_processing_duration_ms{appId,percentile}

# Tool calls (Hermes)
cm_tool_calls_total{appId,tool_name,status}
cm_tool_call_duration_ms{appId,tool_name}

# Billing
cm_quota_exceeded_total{appId,reason}
cm_daily_requests_used{appId}
cm_daily_tokens_used{appId}

# pgvector
cm_vector_search_duration_ms{appId,store}  (wiki vs semantic)
cm_vector_chunks_total{appId,type}          (manual vs learned)
```

---

## 11. .cursorrules (v2.0)

```
# ContextMemory Middleware — Cursor Rules v2.0

## Arquitectura
- NUNCA criar dependências de Api para Core directamente — usar interfaces
- NUNCA guardar estado em variáveis estáticas — usar ConcurrentDictionary injectado
- NUNCA bloquear o thread principal com .Result ou .Wait() — sempre async/await
- SEMPRE usar CancellationToken em todos os métodos de I/O e HTTP
- SEMPRE usar records para modelos imutáveis (OllamaRequest, OllamaResponse, etc.)

## Contrato Ollama
- NUNCA alterar campos do request que não sejam messages[]
- NUNCA modificar a response do Ollama — apenas fazer pipe ao cliente
- SEMPRE preservar todos os campos de métricas (total_duration, eval_count, etc.)
- SEMPRE propagar erros HTTP do Ollama com o mesmo status code ao cliente
- SEMPRE suportar streaming e non-streaming no mesmo endpoint

## Isolamento de Contexto
- NUNCA misturar histórico de utilizadores diferentes
- NUNCA misturar wikis de appIds diferentes
- SEMPRE usar a chave composta (appId, userId) para qualquer operação de memória
- SEMPRE validar appId + API key antes de qualquer acesso a dados

## Performance
- NUNCA deserializar o vector cache completo para verificar validade — usar hash file separado
- NUNCA re-indexar a wiki completa quando só um ficheiro muda — usar hot-reload por ficheiro
- SEMPRE usar SIMD (System.Numerics.Vector) no cálculo de cosine similarity (File provider)
- SEMPRE usar índice HNSW do pgvector para similarity search (Postgres provider)
- SEMPRE fazer ProfileLearner async e desacoplado — não pode atrasar a resposta ao cliente

## Segurança
- NUNCA loggar conteúdo de mensagens dos utilizadores
- NUNCA expor API keys em logs ou responses de erro
- SEMPRE validar tamanho do payload de entrada (max 1MB por request)
- SEMPRE sanitizar appId e userId (apenas alfanumérico + hífens, max 64 chars)

## Feedback e Guardrails
- NUNCA bloquear o pipeline principal com operações de FeedbackStore — sempre async fire-and-forget
- NUNCA loggar conteúdo de mensagens filtradas além do motivo do filtro
- SEMPRE aplicar ContentFilter PRE antes de enriquecer o contexto
- SEMPRE aplicar ContentFilter POST antes de devolver ao cliente
- SEMPRE registar em AuditLog quando ContentFilter bloqueia ou modifica uma mensagem

## KnowledgeLoop (v2.0)
- NUNCA bloquear o pipeline principal com operações do KnowledgeLoop — sempre fire-and-forget
- NUNCA ingerir conhecimento sem passar pelo ConversationEvaluator primeiro
- NUNCA criar chunks duplicados — sempre verificar similaridade >= 0.85 antes de criar
- SEMPRE escrever chunks learned em wikis/{appId}/learned/ separado do manual/
- SEMPRE incluir metadados YAML frontmatter nos chunks gerados (generated, generated_at, action)
- SEMPRE chamar ReindexFileAsync após escrever novo chunk (hot-reload eficiente)
- SEMPRE respeitar KnowledgeLoopMaxChunksPerDay para não saturar a wiki
- NUNCA ingerir sessões com menos de KnowledgeLoopMinMessages mensagens

## Tool Calls Hermes (v2.0)
- NUNCA executar tool_call sem validar o nome da tool contra IToolRegistry
- NUNCA permitir mais de ToolCallMaxIterations iterações por request (evitar loops)
- SEMPRE incluir o resultado da tool como mensagem role=tool antes de re-invocar o LLM
- SEMPRE fazer timeout de tools em 30 segundos

## pgvector (v2.0)
- NUNCA usar float[] comparisons em C# quando Postgres provider está activo — usar pgvector
- SEMPRE usar índice HNSW para queries, não sequential scan
- SEMPRE usar vector(384) como dimensão (all-MiniLM-L6-v2)
- NUNCA criar embeddings sem normalização L2

## Billing (v2.0)
- NUNCA processar request sem verificar QuotaEnforcer primeiro
- SEMPRE retornar 429 com reason: "daily_quota_exceeded" quando quota atingida
- SEMPRE incluir headers: X-Quota-Remaining, X-Quota-Reset-At nas respostas

## Admin e Observabilidade
- NUNCA expor endpoints /admin sem validação do MASTER_KEY
- NUNCA expor dados de um appId nos endpoints de outro appId
- SEMPRE incluir appId em todas as métricas Prometheus (label obrigatória)
- SEMPRE implementar GET /health como liveness check real (não apenas 200 OK estático)

## Rate Limiting
- NUNCA implementar rate limiting com Thread.Sleep ou bloqueios síncronos
- SEMPRE usar SlidingWindowRateLimiter nativo do .NET 9
- SEMPRE incluir header Retry-After na resposta 429
- SEMPRE contar tokens estimados (chars / 4) quando tokens reais não disponíveis
```

---

## 12. Docker Compose (v2.0)

```yaml
services:
  context-memory:
    build: .
    ports:
      - "5100:8080"
    environment:
      - ContextMemory__OllamaEndpoint=http://ollama:11434
      - ContextMemory__MasterKey=cm_master_dev_key
      - ContextMemory__PersistenceProvider=Postgres
      - ContextMemory__KnowledgeLoopEnabled=true
      - ContextMemory__ToolCallEnabled=true
      - ConnectionStrings__ContextMemory=Host=postgres;Database=contextmemory;Username=cm;Password=cm_dev
    volumes:
      - ./data:/app/data
      - ./wikis:/app/wikis
    depends_on:
      postgres:
        condition: service_healthy
      ollama:
        condition: service_started

  postgres:
    image: pgvector/pgvector:pg16
    environment:
      POSTGRES_DB: contextmemory
      POSTGRES_USER: cm
      POSTGRES_PASSWORD: cm_dev
    ports:
      - "5432:5432"
    volumes:
      - pg_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U cm -d contextmemory"]
      interval: 5s
      timeout: 5s
      retries: 10

  ollama:
    image: ollama/ollama:latest
    ports:
      - "11434:11434"
    volumes:
      - ollama_data:/root/.ollama
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]

volumes:
  pg_data:
  ollama_data:
```

**Nota:** Usar `pgvector/pgvector:pg16` em vez de `postgres:16` — já inclui a extensão vector.

---

*ContextMemory Blueprint v2.0 — Maio 2026*
*Gerado a partir das sessões de arquitectura com Claude Sonnet 4.6*
*v2.0 — Fase 6: KnowledgeLoop (reingestão conversacional) · Fase 7: pgvector, Hermes tool_call, Webhooks, Billing, ProfileLearner LLM-based*
