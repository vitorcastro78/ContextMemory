# ContextMemory — Cursor AI Blueprint

> **Produto:** ContextMemory — middleware de memória de contexto estendida para LLMs locais  
> **Stack:** .NET 9 · ASP.NET Core Minimal API · ONNX Runtime · Markdig · Microsoft.ML  
> **LLM Backend:** Ollama · LM Studio · OpenAI API (adaptadores intercambiáveis)  
> **Contrato de API:** 100% compatível com Ollama (`/api/chat` · `/api/generate`)  
> **Modelo Cursor:** Composer 2.0  
> **Versão:** 1.0 · Maio 2026

---

## Como usar este documento com o Cursor

1. Coloca este ficheiro na raiz do repositório como `BLUEPRINT.md`
2. No Cursor, abre o modo **Composer** (Composer 2.0)
3. Inicia com: `@BLUEPRINT.md Implementa a fase 1 conforme descrito`
4. Avança fase a fase — o Composer mantém contexto entre iterações
5. O ficheiro `.cursorrules` da Secção 13 deve estar na raiz — é aplicado automaticamente

---

## 1. Visão do Produto

O ContextMemory é um **middleware transparente** que se posiciona entre qualquer aplicação cliente e um LLM local (Ollama, LM Studio, etc.), tornando o modelo aparentemente mais inteligente sem alterar o modelo em si.

### O problema que resolve

Os LLMs locais são **stateless** — esquecem tudo entre chamadas. Quando múltiplos utilizadores usam o mesmo modelo, não existe isolamento de contexto entre eles. O modelo não conhece o domínio de negócio da aplicação. O resultado é um modelo genérico que parece limitado mesmo sendo capaz.

### Como resolve

O middleware intercepta cada chamada, **enriquece o campo `messages`** com contexto relevante, e encaminha ao LLM real. O cliente nunca sabe que existe um middleware — recebe exactamente o mesmo payload que receberia do Ollama directamente.

### O que o torna "aparentemente mais inteligente"

```
Sem middleware:  modelo nu → resposta genérica
Com middleware:  modelo + sistema de prompt dinâmico
                       + memória conversacional por utilizador
                       + RAG sobre wiki de domínio
                       + memória semântica de longo prazo
                       + summarização automática de sessões antigas
                       + perfil do utilizador aprendido ao longo do tempo
                → resposta especializada, contextualizada, personalizada
```

---

## 2. Arquitectura Geral

```
┌─────────────────────────────────────────────────────────────┐
│  CLIENTES (qualquer stack, qualquer linguagem)               │
│  KYC Blazor · Helpdesk React · ERP Java · Mobile Flutter    │
└────────────────────────┬────────────────────────────────────┘
                         │ POST /api/chat  (payload Ollama nativo)
                         │ Headers: X-App-Id · X-User-Id · Authorization
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  ContextMemory Platform  (ASP.NET Core Minimal API)          │
│                                                             │
│  ┌─────────────┐   ┌──────────────┐   ┌─────────────────┐  │
│  │ API Gateway │→  │Context Engine│→  │  LLM Adapter    │  │
│  │ auth·routing│   │ monta prompt │   │Ollama/LMStudio/ │  │
│  └─────────────┘   └──────┬───────┘   │  OpenAI         │  │
│                           │           └─────────────────┘  │
│              ┌────────────┼────────────┐                   │
│              ▼            ▼            ▼                   │
│       MemoryStore   KnowledgeStore  SessionStore           │
│       (histórico)   (wiki vetorial) (metadados)            │
│              │            │            │                   │
│       UserProfileStore  AppConfigStore                     │
│       (perfil user)     (config app)                       │
└─────────────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────┐
              │   Ollama :11434  │  (ou LM Studio · OpenAI)
              └──────────────────┘
```

---

## 3. Estrutura do Projecto

```
ContextMemory/
├── src/
│   ├── ContextMemory.Api/              # ASP.NET Core Minimal API
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   ├── ChatEndpoint.cs         # POST /api/chat
│   │   │   ├── GenerateEndpoint.cs     # POST /api/generate
│   │   │   ├── AppsEndpoint.cs         # POST /apps/register · GET /apps/{id}
│   │   │   └── WikiEndpoint.cs         # POST /apps/{id}/wiki
│   │   ├── Middleware/
│   │   │   ├── AuthMiddleware.cs       # valida X-App-Id + API key
│   │   │   └── TelemetryMiddleware.cs
│   │   └── appsettings.json
│   │
│   ├── ContextMemory.Core/             # lógica de negócio — sem dependências externas
│   │   ├── Engine/
│   │   │   ├── ContextEngine.cs        # orquestra todo o pipeline
│   │   │   ├── PromptComposer.cs       # monta system prompt dinâmico
│   │   │   └── IntentDetector.cs       # detecta intent da mensagem
│   │   ├── Memory/
│   │   │   ├── ConversationMemory.cs   # sliding window de histórico
│   │   │   ├── SemanticMemory.cs       # memória de longo prazo por embeddings
│   │   │   └── SessionSummarizer.cs    # comprime histórico antigo
│   │   ├── Knowledge/
│   │   │   ├── WikiLoader.cs           # lê + faz chunking dos .md
│   │   │   ├── VectorStore.cs          # in-memory vector store por appId
│   │   │   └── SimilaritySearch.cs     # cosine similarity top-K
│   │   ├── Profile/
│   │   │   ├── UserProfileStore.cs     # factos sobre cada userId
│   │   │   ├── AppConfigStore.cs       # config dinâmica por appId
│   │   │   └── ProfileLearner.cs       # extrai factos das conversas
│   │   └── Models/
│   │       ├── OllamaRequest.cs        # espelho exacto do contrato Ollama
│   │       ├── OllamaResponse.cs
│   │       ├── AppProfile.cs
│   │       └── UserProfile.cs
│   │
│   ├── ContextMemory.Embeddings/       # ONNX Runtime + embeddings locais
│   │   ├── OnnxEmbeddingEngine.cs      # all-MiniLM-L6-v2 via ONNX
│   │   ├── TokenizerService.cs
│   │   └── models/                     # modelo ONNX exportado (não commitar >100MB)
│   │
│   └── ContextMemory.Adapters/         # adaptadores LLM intercambiáveis
│       ├── Contracts/
│       │   └── ILlmAdapter.cs
│       ├── OllamaAdapter.cs
│       ├── LmStudioAdapter.cs
│       └── OpenAiAdapter.cs
│
├── wikis/                              # wikis por appId (FileSystemWatcher)
│   ├── kyc/
│   │   ├── regulamentacao/
│   │   │   ├── amld6.md
│   │   │   └── banco-portugal.md
│   │   ├── procedimentos/
│   │   │   ├── pep-procedimento.md
│   │   │   └── onboarding-empresa.md
│   │   └── index.md
│   ├── helpdesk/
│   └── erp/
│
├── data/                               # persistência local (gitignore)
│   ├── app-profiles/                   # JSON por appId
│   ├── user-profiles/                  # JSON por appId/userId
│   ├── vector-cache/                   # índices serializados (.bin)
│   └── conversation-history/           # histórico por appId/userId
│
├── tests/
│   ├── ContextMemory.Core.Tests/
│   └── ContextMemory.Api.Tests/
│
├── BLUEPRINT.md                        # este ficheiro
└── .cursorrules
```

---

## 4. Contrato de API — 100% Ollama Compatible

### 4.1 POST /api/chat

O middleware aceita **exactamente** o mesmo payload que o Ollama nativo. O cliente não muda nada — só aponta para o middleware em vez do Ollama.

**Request (idêntico ao Ollama):**
```json
{
  "model": "llama3.2",
  "messages": [
    { "role": "user", "content": "Qual o procedimento para um cliente PEP?" }
  ],
  "stream": true,
  "options": {
    "temperature": 0.7,
    "top_p": 0.9,
    "num_ctx": 4096,
    "top_k": 40,
    "repeat_penalty": 1.1
  },
  "format": "",
  "keep_alive": "5m"
}
```

**Headers obrigatórios (únicos campos adicionais):**
```
X-App-Id: kyc-prod-a1b2
X-User-Id: ana
Authorization: Bearer cm_live_xxxxxxxxxxxx
```

**O que o middleware faz ao `messages`:**
```json
[
  {
    "role": "system",
    "content": "[BLOCO FIXO] És um especialista KYC/AML...\n[REGRAS APP] Nível analista sénior...\n[PERFIL USER] Ana prefere respostas concisas...\n[WIKI RAG] AMLD6 art.13: due diligence reforçada para PEPs...\n[CONTEXTO ACTIVO] Caso: Acme Lda | Score: 72 | Flags: PEP, UBO offshore\n[SITUACIONAL] 17 Mai 2026 14:32 | Turno: tarde"
  },
  { "role": "user",      "content": "Há quanto tempo trabalhas em KYC?" },
  { "role": "assistant", "content": "Estou aqui para ajudar com análises KYC..." },
  { "role": "user",      "content": "Qual o procedimento para um cliente PEP?" }
]
```

**Response (idêntica ao Ollama — pass-through total):**
```json
{
  "model": "llama3.2",
  "created_at": "2026-05-17T14:32:01Z",
  "message": {
    "role": "assistant",
    "content": "Para clientes PEP, segundo a AMLD6 art.13..."
  },
  "done": true,
  "done_reason": "stop",
  "total_duration": 4321000000,
  "load_duration": 12000000,
  "prompt_eval_count": 312,
  "prompt_eval_duration": 280000000,
  "eval_count": 187,
  "eval_duration": 4000000000,
  "context": [1, 2, 3]
}
```

**Streaming (NDJSON — pipe linha-a-linha em tempo real):**
```
{"model":"llama3.2","message":{"role":"assistant","content":"Para"},"done":false}
{"model":"llama3.2","message":{"role":"assistant","content":" clientes"},"done":false}
...
{"model":"llama3.2","message":{"role":"assistant","content":""},"done":true,"total_duration":4321000000,...}
```

### 4.2 POST /api/generate

Suporte ao endpoint de geração simples (sem histórico de mensagens), também 100% compatível.

### 4.3 POST /apps/register

**Único endpoint próprio do middleware — chamado uma vez por aplicação.**

```json
POST /apps/register
Authorization: Bearer cm_master_key

{
  "appName": "KYC Platform",
  "domain": "kyc",
  "defaultLanguage": "pt-PT",
  "wikiPath": "wikis/kyc",
  "llmBackend": "ollama",
  "llmModel": "llama3.2",
  "promptPersona": "És um especialista KYC/AML com 15 anos de experiência."
}
```

**Response:**
```json
{
  "appId": "kyc-prod-a1b2c3",
  "apiKey": "cm_live_xxxxxxxxxxxxxxxxxxxx",
  "wikiUploadEndpoint": "/apps/kyc-prod-a1b2c3/wiki",
  "status": "ready"
}
```

### 4.4 POST /apps/{appId}/wiki

Upload de ficheiros `.md` para (re)indexar a wiki de uma aplicação.

```
POST /apps/kyc-prod-a1b2c3/wiki
Content-Type: multipart/form-data

files: [amld6.md, pep-procedimento.md, onboarding-empresa.md]
```

---

## 5. Modelos C# — Contrato Ollama Completo

```csharp
// OllamaRequest.cs
public record OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; init; }

    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; init; }
}

public record OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;     // system | user | assistant | tool

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("images")]
    public List<string>? Images { get; init; }            // base64

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; init; }
}

public record OllamaOptions
{
    [JsonPropertyName("temperature")]    public float?  Temperature   { get; init; }
    [JsonPropertyName("top_p")]          public float?  TopP          { get; init; }
    [JsonPropertyName("top_k")]          public int?    TopK          { get; init; }
    [JsonPropertyName("num_ctx")]        public int?    NumCtx        { get; init; }
    [JsonPropertyName("repeat_penalty")] public float?  RepeatPenalty { get; init; }
    [JsonPropertyName("seed")]           public int?    Seed          { get; init; }
    [JsonPropertyName("stop")]           public List<string>? Stop   { get; init; }
    [JsonPropertyName("num_predict")]    public int?    NumPredict    { get; init; }
    [JsonPropertyName("tfs_z")]          public float?  TfsZ          { get; init; }
    [JsonPropertyName("mirostat")]       public int?    Mirostat      { get; init; }
}

// OllamaResponse.cs
public record OllamaResponse
{
    [JsonPropertyName("model")]                  public string       Model               { get; init; } = string.Empty;
    [JsonPropertyName("created_at")]             public string       CreatedAt           { get; init; } = string.Empty;
    [JsonPropertyName("message")]                public OllamaMessage? Message           { get; init; }
    [JsonPropertyName("done")]                   public bool         Done                { get; init; }
    [JsonPropertyName("done_reason")]            public string?      DoneReason          { get; init; }
    [JsonPropertyName("total_duration")]         public long?        TotalDuration       { get; init; }
    [JsonPropertyName("load_duration")]          public long?        LoadDuration        { get; init; }
    [JsonPropertyName("prompt_eval_count")]      public int?         PromptEvalCount     { get; init; }
    [JsonPropertyName("prompt_eval_duration")]   public long?        PromptEvalDuration  { get; init; }
    [JsonPropertyName("eval_count")]             public int?         EvalCount           { get; init; }
    [JsonPropertyName("eval_duration")]          public long?        EvalDuration        { get; init; }
    [JsonPropertyName("context")]                public List<int>?   Context             { get; init; }
}
```

---

## 6. Context Engine — Pipeline de Enriquecimento

```csharp
// ContextEngine.cs
public class ContextEngine
{
    // Pipeline executado em cada chamada:
    // 1. Extrair appId + userId dos headers
    // 2. Carregar AppConfig (regras, persona, modelo)
    // 3. Carregar UserProfile (preferências aprendidas)
    // 4. Detectar intent da última mensagem do user
    // 5. RAG: buscar chunks relevantes da wiki (top-5 por cosine similarity)
    // 6. Carregar histórico conversacional (sliding window N mensagens)
    // 7. PromptComposer: montar system prompt dinâmico
    // 8. Injectar system + histórico + mensagem actual no messages[]
    // 9. Encaminhar ao LLM Adapter
    // 10. Receber resposta (stream ou não)
    // 11. Guardar resposta no MemoryStore do userId
    // 12. ProfileLearner: extrair factos novos sobre o utilizador (async, não bloqueia)
    // 13. Devolver resposta ao cliente (pass-through total)
}
```

---

## 7. PromptComposer — System Prompt Dinâmico

O system prompt nunca é texto fixo. É **montado peça a peça** em runtime para cada chamada.

```csharp
// PromptComposer.cs
public class PromptComposer
{
    public string Compose(PromptContext ctx)
    {
        var sb = new StringBuilder();

        // BLOCO 1 — Fixo por appId (editável sem re-deploy)
        sb.AppendLine(ctx.AppConfig.BasePersona);

        // BLOCO 2 — Regras de negócio da aplicação
        if (ctx.AppConfig.BusinessRules.Any())
            sb.AppendLine(ctx.AppConfig.BusinessRules);

        // BLOCO 3 — Perfil do utilizador (aprendido ao longo do tempo)
        if (ctx.UserProfile.Facts.Any())
        {
            sb.AppendLine("[PERFIL DO UTILIZADOR]");
            foreach (var fact in ctx.UserProfile.Facts.TakeLast(10))
                sb.AppendLine($"- {fact}");
        }

        // BLOCO 4 — Chunks da wiki relevantes para esta query (RAG query-time)
        if (ctx.WikiChunks.Any())
        {
            sb.AppendLine("[CONHECIMENTO DE DOMÍNIO]");
            foreach (var chunk in ctx.WikiChunks)
                sb.AppendLine(chunk.Content);
        }

        // BLOCO 5 — Contexto activo da sessão (caso KYC, ticket aberto, etc.)
        if (ctx.SessionContext is not null)
        {
            sb.AppendLine("[CONTEXTO ACTIVO]");
            sb.AppendLine(ctx.SessionContext);
        }

        // BLOCO 6 — Situacional (data, hora, língua)
        sb.AppendLine($"[SITUACIONAL] {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC");
        sb.AppendLine($"Responde sempre em {ctx.AppConfig.DefaultLanguage}.");

        // BLOCO 7 — Instruções de formato condicionais ao intent detectado
        sb.AppendLine(ctx.Intent switch
        {
            Intent.GenerateReport  => "Responde com um relatório estruturado em secções.",
            Intent.QuickQuestion   => "Responde de forma concisa, máximo 3 parágrafos.",
            Intent.ListProcedure   => "Responde com uma lista numerada de passos.",
            _                      => string.Empty
        });

        return sb.ToString().Trim();
    }
}
```

### Blocos configuráveis por `appId`

Cada bloco vive num ficheiro editável — sem re-deploy, com `FileSystemWatcher` a recarregar:

```
data/app-profiles/kyc-prod-a1b2c3/
  ├── persona.md          → persona e tom
  ├── business-rules.md   → regras de negócio específicas
  ├── format-rules.md     → instruções de formato
  └── config.json         → modelo, língua, limites
```

---

## 8. Memory System — Três Camadas

### 8.1 Memória Conversacional (Sliding Window)

```csharp
// ConversationMemory.cs
// Guarda as últimas N mensagens por (appId, userId)
// N configurável por appId (default: 20 mensagens)
// Persistência: JSON em data/conversation-history/{appId}/{userId}.json
// Thread-safe via ConcurrentDictionary + SemaphoreSlim por userId
```

### 8.2 Memória Semântica de Longo Prazo

```csharp
// SemanticMemory.cs
// Factos importantes sobre o utilizador guardados como embeddings
// Exemplos: "prefere respostas curtas", "trabalha com clientes de alto risco"
// Recuperados por cosine similarity com a mensagem actual (top-3)
// Persistência: JSON + vectores serializados em data/user-profiles/
```

### 8.3 Summarização Automática de Sessões Antigas

```csharp
// SessionSummarizer.cs
// Quando histórico > threshold (ex: 50 mensagens), sumariza as mais antigas
// Usa o próprio LLM local para gerar o resumo (POST /api/generate internamente)
// O resumo substitui o histórico antigo no contexto
// Resultado: memória "infinita" sem explodir o context window do modelo
```

---

## 9. Knowledge System — RAG sobre Wiki `.md`

### 9.1 WikiLoader — Chunking Inteligente

```csharp
// WikiLoader.cs
// Usa Markdig para parsear markdown
// Estratégia de chunking: por header (## e ###)
// Cada chunk inclui: path do ficheiro + header hierárquico + conteúdo
// Tamanho máximo por chunk: 512 tokens (configurável)
// Overlap entre chunks: 50 tokens para preservar contexto
// FileSystemWatcher: re-indexa automaticamente quando .md é alterado
```

### 9.2 VectorStore — In-Memory por AppId

```csharp
// VectorStore.cs
// Um vector store isolado por appId
// Estrutura: List<VectorEntry> { float[] Vector, string Text, string Source, string AppId }
// Cache em disco: data/vector-cache/{appId}.bin (serializado com MemoryPack)
// Invalidação: hash SHA256 de todos os .md — se mudar, re-indexa e grava novo cache
// Startup rápido: deserializa do .bin se hash válido (~50ms para 1000 chunks)
```

### 9.3 SimilaritySearch — Cosine Similarity

```csharp
// SimilaritySearch.cs
// Embedding da query com o mesmo modelo ONNX (all-MiniLM-L6-v2)
// Cosine similarity contra todos os vectores do appId
// Devolve top-K chunks (default: 5) acima de threshold (default: 0.65)
// SIMD optimizado via System.Numerics.Vector para performance
```

---

## 10. Profile System — Aprendizagem Automática

### 10.1 AppConfigStore

```csharp
// AppConfigStore.cs
// Configuração dinâmica por appId — recarregada sem restart
// Campos: basePersona, businessRules, defaultLanguage, llmModel,
//         llmBackend, maxHistoryMessages, wikiChunksTopK, streamingEnabled
// Persistência: data/app-profiles/{appId}/config.json
// API de gestão: GET/PATCH /apps/{appId}/config
```

### 10.2 UserProfileStore + ProfileLearner

```csharp
// UserProfileStore.cs + ProfileLearner.cs
// ProfileLearner corre async após cada resposta (não bloqueia o cliente)
// Analisa a conversa e extrai factos: língua preferida, tom, domínio de trabalho
// Factos guardados com timestamp e score de confiança
// Factos obsoletos expiram ao fim de 30 dias sem confirmação
// Persistência: data/user-profiles/{appId}/{userId}.json
```

### 10.3 Auto-registo de Novas Aplicações

```
Fluxo zero-config:
1. App faz POST /apps/register com appName + domain
2. Middleware gera appId + API key automaticamente
3. Cria pasta wiki/{appId}/ vazia
4. Aplica defaults do domain (se reconhecido: kyc, helpdesk, erp, etc.)
5. Devolve appId + apiKey — app guarda e usa dali em diante

A partir daí, o middleware auto-aprende:
- Semana 1: defaults genéricos
- Semana 2+: prompt afinado ao padrão de uso da aplicação
- Wiki pode ser carregada a qualquer momento via POST /apps/{appId}/wiki
```

---

## 11. LLM Adapters — Interface Unificada

```csharp
// ILlmAdapter.cs
public interface ILlmAdapter
{
    Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken ct);
    IAsyncEnumerable<OllamaResponse> ChatStreamAsync(OllamaRequest request, CancellationToken ct);
    Task<OllamaResponse> GenerateAsync(OllamaGenerateRequest request, CancellationToken ct);
    Task<bool> IsHealthyAsync(CancellationToken ct);
}

// Implementações:
// OllamaAdapter    → http://localhost:11434  (default)
// LmStudioAdapter  → http://localhost:1234   (OpenAI-compat endpoint)
// OpenAiAdapter    → https://api.openai.com  (cloud fallback)

// Selecção por appId em config.json → "llmBackend": "ollama"
// Suporte a múltiplos backends em simultâneo (appId A → Ollama, appId B → LM Studio)
```

---

## 12. Fases de Implementação

### Fase 1 — Core MVP (implementar primeiro)
- [ ] Estrutura do projecto + solution .NET 9
- [ ] Modelos `OllamaRequest` / `OllamaResponse` completos
- [ ] `POST /api/chat` com pass-through simples ao Ollama
- [ ] `AuthMiddleware` (X-App-Id + X-User-Id + API key)
- [ ] `ConversationMemory` (sliding window em memória)
- [ ] `PromptComposer` básico (system prompt fixo por appId)
- [ ] `OllamaAdapter` com streaming NDJSON pipe
- [ ] Persistência de histórico em JSON local

### Fase 2 — RAG e Wiki
- [ ] `WikiLoader` com chunking por header via Markdig
- [ ] `OnnxEmbeddingEngine` (all-MiniLM-L6-v2 exportado para ONNX)
- [ ] `VectorStore` in-memory por appId com cache .bin
- [ ] `SimilaritySearch` cosine similarity + SIMD
- [ ] `FileSystemWatcher` para hot-reload da wiki
- [ ] Integração RAG no `ContextEngine`

### Fase 3 — Prompt Dinâmico e Perfis
- [ ] `PromptComposer` dinâmico com todos os blocos
- [ ] `IntentDetector` (classifica intent da mensagem)
- [ ] `AppConfigStore` (config editável por appId)
- [ ] `UserProfileStore` (factos por userId)
- [ ] `ProfileLearner` async (extrai factos das conversas)

### Fase 4 — Memória Longa e Auto-registo
- [ ] `SemanticMemory` (embeddings de longo prazo por userId)
- [ ] `SessionSummarizer` (comprime histórico antigo via LLM)
- [ ] `POST /apps/register` (auto-registo de novas apps)
- [ ] `POST /apps/{appId}/wiki` (upload e re-indexação de wiki)
- [ ] `LmStudioAdapter` + `OpenAiAdapter`

### Fase 5 — Técnicas Big Tech + Produção

#### 5A — FeedbackStore (replica ChatGPT thumbs up/down)
- [ ] `POST /api/chat/feedback` — endpoint para receber feedback explícito por `messageId`
- [ ] `ImplicitFeedbackDetector` — detecta feedback negativo implícito (utilizador reformula, pede para repetir, diz "não era isso")
- [ ] `FeedbackStore` — regista feedback por `(appId, userId, messageId)` com score e motivo
- [ ] Integração com `ProfileLearner` — feedback negativo ajusta factos do `UserProfileStore`
- [ ] Integração com `AppConfigStore` — padrões de feedback repetidos ajustam blocos do prompt da aplicação

```csharp
// FeedbackStore.cs
// Estrutura: { MessageId, AppId, UserId, Score (-1|0|1), Reason?, Timestamp }
// ImplicitFeedbackDetector analisa a mensagem seguinte do utilizador:
//   "não era isso" / "repete" / "mais curto" / "em formato diferente" → Score = -1
//   "perfeito" / "obrigado" / "exactamente" → Score = +1
// ProfileLearner consome FeedbackStore async e ajusta UserProfile.Facts
// Exemplo: 3x feedback negativo em respostas longas → adiciona facto "prefere respostas curtas"
```

#### 5B — ContentFilter / Guardrails (replica safety layers do Claude e ChatGPT)
- [ ] `ContentFilter` — filtra conteúdo antes de enviar ao LLM e antes de devolver ao cliente
- [ ] Configurável por `appId` — cada aplicação define as suas regras em `content-rules.json`
- [ ] Tipos de filtro: `BlockedTopics`, `RequiredDisclaimer`, `MaxResponseLength`, `LanguageEnforcement`
- [ ] `AuditLog` — regista todas as mensagens filtradas com motivo (append-only, por `appId`)

```csharp
// ContentFilter.cs — pipeline de filtros por appId
// PRE-FILTER (antes de enviar ao LLM):
//   - valida tamanho da mensagem (max configurável)
//   - detecta tópicos bloqueados (lista de keywords por appId)
//   - injeta avisos obrigatórios se detectar tópicos sensíveis
// POST-FILTER (antes de devolver ao cliente):
//   - valida tamanho da resposta
//   - injeta disclaimers obrigatórios (ex: "Esta resposta não constitui aconselhamento jurídico")
//   - enforça língua da resposta (se appId exige PT-PT)
// content-rules.json por appId:
//   { "blockedTopics": ["concorrentes", "preços"], "requiredDisclaimer": "...", "maxLength": 2000 }
```

#### 5C — AdminDashboard (replica consola de gestão das big tech)
- [ ] `GET /admin/apps` — lista todas as aplicações registadas com métricas
- [ ] `GET /admin/apps/{appId}/stats` — tokens consumidos, utilizadores activos, latência média
- [ ] `GET /admin/apps/{appId}/users` — lista userId com nº de sessões e factos aprendidos
- [ ] `PATCH /admin/apps/{appId}/config` — editar config da aplicação em runtime
- [ ] `DELETE /admin/apps/{appId}/users/{userId}/memory` — apagar memória de um utilizador (GDPR)
- [ ] Interface HTML mínima servida em `GET /admin` (sem framework — HTML + Alpine.js CDN)

```
Admin endpoints (protegidos por CONTEXT_MEMORY_MASTER_KEY):
GET  /admin                              → dashboard HTML
GET  /admin/apps                         → JSON com todas as apps
GET  /admin/apps/{appId}/stats           → métricas de uso
GET  /admin/apps/{appId}/users           → utilizadores da app
GET  /admin/apps/{appId}/users/{userId}  → perfil completo do utilizador
DELETE /admin/apps/{appId}/users/{userId}/memory  → apagar memória (GDPR)
PATCH /admin/apps/{appId}/config         → editar config em runtime
GET  /admin/apps/{appId}/audit           → log de conteúdo filtrado
```

#### 5D — Observabilidade (replica telemetria interna das big tech)
- [ ] `GET /health` — liveness + readiness (verifica Ollama acessível + stores carregados)
- [ ] `GET /metrics` — formato Prometheus com métricas chave
- [ ] `TelemetryMiddleware` — regista latência, tokens, erros por `appId` em `ConcurrentDictionary`
- [ ] Métricas expostas:
  - `cm_requests_total{appId, status}` — total de pedidos
  - `cm_tokens_prompt_total{appId}` — tokens de prompt consumidos
  - `cm_tokens_completion_total{appId}` — tokens de resposta gerados
  - `cm_latency_ms{appId, percentile}` — latência p50/p95/p99
  - `cm_rag_hits_total{appId}` — queries que encontraram chunks relevantes
  - `cm_feedback_score{appId}` — score médio de feedback por aplicação
  - `cm_content_filtered_total{appId, reason}` — mensagens filtradas por motivo

#### 5E — Rate Limiting (replica quotas das big tech)
- [ ] Rate limiting por `appId` — tokens por minuto e pedidos por minuto
- [ ] Rate limiting por `userId` dentro de `appId` — pedidos por minuto por utilizador
- [ ] Configurável em `config.json` por appId: `"rateLimits": { "requestsPerMinute": 60, "tokensPerMinute": 100000 }`
- [ ] Response `429 Too Many Requests` com header `Retry-After` (idêntico ao comportamento do Ollama/OpenAI)
- [ ] Implementação: `SlidingWindowRateLimiter` de `System.Threading.RateLimiting` (.NET 9 nativo)

#### 5F — Testes e Docker
- [ ] Testes de integração end-to-end com `WebApplicationFactory` (simula cliente real)
- [ ] Testes de contrato Ollama — verifica que request/response são 100% idênticos ao Ollama nativo
- [ ] Testes de isolamento — verifica que userId A nunca acede a contexto de userId B
- [ ] `Dockerfile` multi-stage (build + runtime, imagem final < 200MB)
- [ ] `docker-compose.yml` completo com Ollama + GPU support (já na Secção 16)

---

## 13. Prompts para o Cursor Composer 2.0

Usa estes prompts directamente no Composer, por ordem:

```
FASE 1 — SETUP
"@BLUEPRINT.md Cria a estrutura do projecto .NET 9 com os 4 projectos descritos na Secção 3. 
Inclui os ficheiros .csproj com as dependências correctas (Markdig, Microsoft.ML.OnnxRuntime, 
MemoryPack, System.Numerics). Não implementes lógica ainda — apenas a estrutura."

FASE 1 — MODELOS
"@BLUEPRINT.md Implementa os modelos OllamaRequest, OllamaResponse, OllamaMessage e OllamaOptions 
em ContextMemory.Core/Models/ exactamente como descrito na Secção 5, com todos os campos 
JsonPropertyName correctos."

FASE 1 — ENDPOINT CHAT
"@BLUEPRINT.md Implementa o endpoint POST /api/chat em ChatEndpoint.cs. Por agora faz pass-through 
directo ao Ollama sem enriquecimento. Deve suportar streaming NDJSON linha-a-linha e 
resposta normal. Usa o OllamaAdapter."

FASE 1 — AUTH + MEMÓRIA
"@BLUEPRINT.md Implementa o AuthMiddleware (X-App-Id + X-User-Id + Bearer token) e o 
ConversationMemory com sliding window de 20 mensagens, persistência JSON em data/conversation-history/ 
e thread-safety via ConcurrentDictionary."

FASE 2 — WIKI RAG
"@BLUEPRINT.md Implementa o WikiLoader usando Markdig para chunking por header ## e ###. 
Depois implementa o VectorStore in-memory com cache .bin via MemoryPack e invalidação por 
SHA256. Por fim o SimilaritySearch com cosine similarity optimizado com SIMD."

FASE 3 — PROMPT DINÂMICO
"@BLUEPRINT.md Implementa o PromptComposer com todos os 7 blocos descritos na Secção 7. 
Inclui o IntentDetector baseado em keywords e o AppConfigStore com FileSystemWatcher 
para recarregar config.json sem restart."

FASE 4 — REGISTO E MEMÓRIA LONGA
"@BLUEPRINT.md Implementa o endpoint POST /apps/register, o UserProfileStore, o ProfileLearner 
async e o SessionSummarizer que usa o próprio OllamaAdapter para sumarizar sessões antigas."

FASE 5A — FEEDBACK (técnica ChatGPT Memory + thumbs)
"@BLUEPRINT.md Implementa o FeedbackStore e o ImplicitFeedbackDetector conforme descrito na 
Fase 5A. O detector analisa a mensagem seguinte do utilizador para inferir feedback implícito. 
Integra com o ProfileLearner para ajustar UserProfile.Facts automaticamente."

FASE 5B — GUARDRAILS (técnica safety do Claude/ChatGPT)
"@BLUEPRINT.md Implementa o ContentFilter com pipeline PRE e POST conforme Fase 5B. 
Deve ser configurável por appId via content-rules.json e registar tudo em AuditLog 
append-only. Integra no ContextEngine antes e depois da chamada ao LLM."

FASE 5C — ADMIN DASHBOARD
"@BLUEPRINT.md Implementa todos os endpoints /admin descritos na Fase 5C protegidos pelo 
MASTER_KEY. A interface HTML em GET /admin deve usar Alpine.js via CDN, mostrar lista de apps, 
métricas de uso, e ter botão de apagar memória de utilizador (GDPR)."

FASE 5D — OBSERVABILIDADE
"@BLUEPRINT.md Implementa o TelemetryMiddleware e o endpoint GET /metrics em formato 
Prometheus com todas as métricas descritas na Fase 5D. Implementa GET /health com 
verificação de conectividade ao Ollama e estado dos VectorStores."

FASE 5E — RATE LIMITING
"@BLUEPRINT.md Implementa rate limiting por appId e por userId usando SlidingWindowRateLimiter 
do .NET 9. Configura via config.json por appId. Responde 429 com Retry-After idêntico ao 
comportamento da API do Ollama/OpenAI."

FASE 5F — TESTES E DOCKER
"@BLUEPRINT.md Cria os testes de integração com WebApplicationFactory cobrindo: 
(1) contrato Ollama 100% idêntico, (2) isolamento de contexto entre userIds, 
(3) pipeline RAG completo. Cria também o Dockerfile multi-stage com imagem final < 200MB."
```

---

## 14. Variáveis de Ambiente

```bash
# .env (nunca commitar — adicionar ao .gitignore)
CONTEXT_MEMORY_MASTER_KEY="cm_master_xxxxxxxxxxxxxxxxxxxx"
CONTEXT_MEMORY_OLLAMA_ENDPOINT="http://localhost:11434"
CONTEXT_MEMORY_DATA_PATH="./data"
CONTEXT_MEMORY_WIKI_PATH="./wikis"
CONTEXT_MEMORY_DEFAULT_LLM_BACKEND="ollama"
CONTEXT_MEMORY_DEFAULT_LLM_MODEL="llama3.2"
CONTEXT_MEMORY_MAX_HISTORY_MESSAGES="20"
CONTEXT_MEMORY_WIKI_CHUNKS_TOP_K="5"
CONTEXT_MEMORY_SIMILARITY_THRESHOLD="0.65"
CONTEXT_MEMORY_SUMMARIZE_AFTER_MESSAGES="50"
# Fase 5 — novas variáveis
CONTEXT_MEMORY_ENABLE_CONTENT_FILTER="true"
CONTEXT_MEMORY_ENABLE_FEEDBACK="true"
CONTEXT_MEMORY_ENABLE_METRICS="true"
CONTEXT_MEMORY_ADMIN_ENABLED="true"
CONTEXT_MEMORY_DEFAULT_RATE_LIMIT_RPM="60"
CONTEXT_MEMORY_DEFAULT_RATE_LIMIT_TPM="100000"
CONTEXT_MEMORY_AUDIT_LOG_PATH="./data/audit"
```

---

## 15. .cursorrules

```
# ContextMemory Middleware — Cursor Rules

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
- SEMPRE usar SIMD (System.Numerics.Vector) no cálculo de cosine similarity
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

## 16. Docker Compose (desenvolvimento local)

```yaml
# docker-compose.yml
services:
  context-memory:
    build: .
    ports:
      - "5100:8080"
    environment:
      - CONTEXT_MEMORY_OLLAMA_ENDPOINT=http://ollama:11434
      - CONTEXT_MEMORY_MASTER_KEY=cm_master_dev_key
    volumes:
      - ./data:/app/data
      - ./wikis:/app/wikis
    depends_on:
      - ollama

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
  ollama_data:
```

---

*ContextMemory Blueprint v1.1 — Maio 2026*  
*Gerado a partir das sessões de arquitectura com Claude Sonnet 4.6*  
*v1.1 — Fase 5 adicionada: FeedbackStore, ContentFilter, AdminDashboard, Observabilidade, Rate Limiting*
