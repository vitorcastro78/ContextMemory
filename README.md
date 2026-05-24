# ContextMemory Middleware

**ContextMemory** is a transparent memory and context middleware for LLM applications. It sits between your clients and any compatible LLM backend (**Ollama** is the primary default; LM Studio and OpenAI API are also supported), enriches the `messages` array with conversation history, domain knowledge, user profiles, and safety rules, and forwards the request unchanged in shape to the real model.

Clients keep using the **native Ollama chat API** (`POST /api/chat`). They never need to know that a middleware exists.

---

## Why use it?

| Problem | How ContextMemory helps |
|--------|-------------------------|
| LLMs are stateless | Per-user conversation memory persisted on disk |
| Multiple users share one model | Strict isolation by `(appId, userId)` |
| Model lacks domain knowledge | RAG over Markdown wikis with ONNX embeddings |
| Generic answers | Dynamic system prompt (persona, rules, intent, session context) |
| Long sessions overflow context | Automatic session summarization |
| No feedback loop | Explicit and implicit feedback adjusts profiles and app config |
| Production gaps | Rate limiting, content filters, audit log, Prometheus metrics, admin API |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Clients (any stack: .NET, Python, Node, mobile, etc.)       │
│  POST /api/chat  +  X-App-Id · X-User-Id · Bearer API key    │
└────────────────────────────┬────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────┐
│  ContextMemory (ASP.NET Core 9)                              │
│  Auth → Rate limit → Telemetry → Context Engine              │
│    · Content filter (PRE/POST)                               │
│    · Prompt composer · RAG · Semantic memory                 │
│    · Profile learner · Session summarizer                    │
└────────────────────────────┬────────────────────────────────┘
                             ▼
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
         Ollama         LM Studio        OpenAI API
      (primary)      (OpenAI-compat)    (cloud)
```

### Multi-tenant model

- **`appId`** — A registered application (e.g. `helpdesk-prod-a1b2c3`). Each app has its own API key, wiki, prompt config, rate limits, and content rules.
- **`userId`** — End user within that app. Memory and profiles are never shared across users or apps.

The sample `kyc-dev` entry in `appsettings.json` is a **local development seed** from the project blueprint. Production apps should be registered via `POST /apps/register` or listed under `ContextMemory:Apps`.

---

## Features

### Core (Ollama-compatible gateway)

- `POST /api/chat` — Full Ollama request/response contract; supports streaming (NDJSON) and non-streaming JSON
- `POST /api/generate` — Ollama generate API pass-through (no conversation enrichment); streaming supported
- Only `messages[]` is modified; model metrics and response fields are preserved and piped through
- Pluggable LLM backends per app: `ollama` (primary) · `lmstudio` · `openai`

### Conversation & memory

- JSON persistence under `data/conversation-history/{appId}/{userId}.json`
- Configurable history window (`maxHistoryMessages`)
- **Semantic memory** — Long-term facts per user (embedding-backed)
- **Session summarizer** — Compresses old history via the LLM when message count exceeds threshold

### RAG (domain wiki)

- Markdown wikis chunked by headings (`##` / `###`)
- **ONNX** embedding model (local, no external API for vectors)
- Vector cache with separate hash file (no full-cache deserialize for validity checks)
- Hot-reload when wiki files change on disk
- `POST /apps/{appId}/wiki` — Upload `.md` files and re-index

### Dynamic prompts

Seven prompt blocks assembled per request:

1. Base persona  
2. Business rules  
3. Format rules  
4. User profile facts  
5. RAG wiki chunks  
6. Session context  
7. Intent-specific hints  

Config lives in `data/app-profiles/{appId}/` (`persona.md`, `business-rules.md`, `format-rules.md`, `config.json`).

### App lifecycle

- `POST /apps/register` — Create a new app at runtime (master key)
- `GET /apps/{appId}` — App metadata (wiki path, LLM backend, rate limits)
- `GET/PATCH /apps/{appId}/config` — Read/update runtime config
- `PUT /apps/{appId}/session-context` — Set ephemeral session context for a user

### Feedback (Phase 5)

- `POST /api/chat/feedback` — Thumbs up/down (`score`: -1, 0, or 1) tied to `messageId`
- **Implicit feedback** — Detects phrases like “that’s not what I meant” / “perfect, thanks” on the next user turn
- Adjusts `UserProfileStore` and, after repeated patterns, `AppConfigStore`

### Safety & guardrails

- **Content filter** — PRE (before LLM) and POST (before client)
- Per-app `content-rules.json`: blocked topics, disclaimers, max lengths, language enforcement
- **Audit log** — Append-only JSONL in `data/audit/`

### Admin & observability

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /health` | None | Liveness; checks Ollama reachability and loaded apps |
| `GET /metrics` | None | Prometheus text format |
| `GET /admin` | Master key | HTML dashboard (Alpine.js) |
| `GET /admin/apps` | Master key | All apps + telemetry snapshot |
| `GET /admin/apps/{appId}/stats` | Master key | Usage stats + feedback average |
| `GET /admin/apps/{appId}/users` | Master key | Users with fact counts |
| `GET /admin/apps/{appId}/users/{userId}` | Master key | Full user detail |
| `DELETE /admin/apps/{appId}/users/{userId}/memory` | Master key | GDPR memory wipe |
| `PATCH /admin/apps/{appId}/config` | Master key | Runtime config patch |
| `GET /admin/apps/{appId}/audit` | Master key | Filtered content audit entries |

### Rate limiting

- Per `appId`: requests/minute + tokens/minute (sliding window + token bucket)
- Per `userId` within app: requests/minute
- `429 Too Many Requests` with `Retry-After` header
- Configured in each app’s `config.json` → `rateLimits`

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- An LLM backend — **[Ollama](https://ollama.com)** recommended as primary; LM Studio and OpenAI optional per app
- Optional: ONNX embedding model for RAG (see [Embeddings setup](#embeddings-setup))

---

## Local evaluation (no Docker)

This is the recommended path to validate the middleware before any container deployment.

| Component | Local setup |
|-----------|-------------|
| **ContextMemory** | `dotnet run` from `src/ContextMemory.Api` → `http://localhost:5100` |
| **Ollama** | Primary backend; default `http://localhost:11434` |
| **LM Studio / OpenAI** | Optional; configure per app via `llmBackend` when needed |
| **Persistence** | **PostgreSQL** (`PersistenceProvider: Postgres`) or local JSON files (`File`) |
| **Vector cache / wikis** | Still on disk (`data/vector-cache`, `wikis/`) |
| **RAG embeddings** | Optional ONNX model via `./scripts/download-embedding-model.ps1` |

**Typical flow on Windows (isolated local machine):**

```powershell
# 1. Ollama (separate terminal or Windows service)
ollama serve          # if not already running
ollama pull llama3.2  # or your chosen model

# 2. Embeddings for wiki RAG (once)
./scripts/download-embedding-model.ps1

# 3. API
cd src/ContextMemory.Api
dotnet run
# → http://localhost:5100

# 4. Smoke test
curl http://localhost:5100/health
```

Use the `kyc-dev` seed app and wiki under `wikis/kyc/` (configured in `appsettings.json`). Register new apps via `POST /apps/register` when you outgrow the seed.

Copy `.env.example` to `.env` only if you prefer environment variables over JSON config.

### PostgreSQL (local)

1. Create the database:

```sql
CREATE DATABASE contextmemory;
```

2. Set the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ContextMemory": "Host=localhost;Port=5432;Database=contextmemory;Username=postgres;Password=YOUR_PASSWORD"
  },
  "ContextMemory": {
    "PersistenceProvider": "Postgres",
    ...
  }
}
```

3. Run the API — migrations apply automatically on startup (`Database.Migrate`).

| `PersistenceProvider` | Behaviour |
|----------------------|-----------|
| `Postgres` | Conversation history, profiles, semantic facts, feedback, audit, app config in PostgreSQL |
| `File` | Legacy JSON/MemoryPack under `data/` (used in tests) |

Wikis and vector cache remain on disk in both modes.

---

## Quick start

### 1. Clone and build

```bash
git clone <your-repo-url>
cd ContextMemoryMiddleware
dotnet build
```

### 2. Configure (optional seed app)

Copy `.env.example` to `.env` only if you prefer environment variables over `appsettings.json`.

All platform settings live in `src/ContextMemory.Api/appsettings.json`:

```json
{
  "ContextMemory": {
    "DataPath": "../../data",
    "WikiPath": "../../wikis",
    "OllamaEndpoint": "http://localhost:11434",
    "MasterKey": "change-me-in-production",
    "Apps": {
      "kyc-dev": { }
    },
    "Embeddings": {
      "ModelPath": "../ContextMemory.Embeddings/models/model.onnx",
      "VocabPath": "../ContextMemory.Embeddings/models/vocab.txt"
    }
  }
}
```

Paths are relative to `src/ContextMemory.Api` when you run `dotnet run` from that folder.

### 3. Run the API

```bash
cd src/ContextMemory.Api
dotnet run
```

Default URL: `http://localhost:5100` (see `launchSettings.json` and console output).

### 4. Register an application

```bash
curl -X POST http://localhost:5100/apps/register \
  -H "Authorization: Bearer change-me-in-production" \
  -H "Content-Type: application/json" \
  -d '{
    "appName": "My Helpdesk",
    "domain": "helpdesk",
    "defaultLanguage": "en-US",
    "llmBackend": "ollama",
    "llmModel": "llama3.2"
  }'
```

Response includes `appId`, `apiKey`, and `wikiUploadEndpoint`. Store the API key securely.

### 5. Chat (drop-in Ollama client)

```bash
curl -X POST http://localhost:5100/api/chat \
  -H "X-App-Id: helpdesk-prod-abc123" \
  -H "X-User-Id: user-42" \
  -H "Authorization: Bearer cm_live_..." \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama3.2",
    "messages": [{ "role": "user", "content": "Hello" }],
    "stream": false
  }'
```

**Headers (required for `/api/chat`):**

| Header | Description |
|--------|-------------|
| `X-App-Id` | Registered application ID (alphanumeric + hyphens, max 64 chars) |
| `X-User-Id` | End-user identifier (same format rules) |
| `Authorization` | `Bearer <app-api-key>` |

**Response headers:**

| Header | Description |
|--------|-------------|
| `X-Context-Memory-Message-Id` | Turn ID for feedback |
| `X-Response-Time-Ms` | Request duration |

---

## Docker

```bash
docker compose up --build
```

| Service | URL |
|---------|-----|
| ContextMemory | http://localhost:5100 |
| Ollama | http://localhost:11434 |

Data and wikis are mounted from `./data` and `./wikis`. Override settings with environment variables, e.g. `ContextMemory__MasterKey`, `ContextMemory__OllamaEndpoint`.

---

## Configuration reference

All settings use the `ContextMemory` section (or `ContextMemory__*` env vars).

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings:ContextMemory` | *(see appsettings)* | PostgreSQL when `PersistenceProvider` is `Postgres` |
| `PersistenceProvider` | `Postgres` | `Postgres` or `File` |
| `DataPath` | `./data` | Persistence root (vector cache, wikis path resolution) |
| `WikiPath` | `./wikis` | Default wiki root for new apps |
| `OllamaEndpoint` | `http://localhost:11434` | Ollama base URL (primary backend) |
| `LmStudioEndpoint` | `http://localhost:1234` | LM Studio OpenAI-compatible URL |
| `OpenAiEndpoint` | `https://api.openai.com` | OpenAI API base |
| `OpenAiApiKey` | *(empty)* | Used only when an app sets `llmBackend: openai` |
| `MasterKey` | *(required for admin/register)* | Bearer token for `/admin/*` and `/apps/register` |
| `MaxPayloadBytes` | `1048576` | Max request body (1 MB) |
| `MaxHistoryMessages` | `20` | Default conversation window |
| `SummarizeAfterMessages` | `50` | Trigger session summarization |
| `EnableContentFilter` | `true` | PRE/POST content pipeline |
| `EnableFeedback` | `true` | Feedback store + implicit detection |
| `EnableMetrics` | `true` | Telemetry collector |
| `DefaultRateLimitRpm` | `60` | Default requests/minute per app |
| `DefaultRateLimitTpm` | `100000` | Default tokens/minute per app |
| `Apps` | `{}` | Optional static app seeds (`appId` → apiKey, systemPrompt, wikiPath, …) |

### Per-app runtime config

File: `data/app-profiles/{appId}/config.json`

```json
{
  "defaultLanguage": "en-US",
  "llmModel": "llama3.2",
  "llmBackend": "ollama",
  "maxHistoryMessages": 20,
  "wikiChunksTopK": 5,
  "similarityThreshold": 0.65,
  "streamingEnabled": true,
  "rateLimits": {
    "requestsPerMinute": 60,
    "tokensPerMinute": 100000,
    "userRequestsPerMinute": 30
  }
}
```

Markdown files in the same folder: `persona.md`, `business-rules.md`, `format-rules.md`.

### Content rules

File: `data/app-profiles/{appId}/content-rules.json`

```json
{
  "blockedTopics": ["competitor names"],
  "sensitiveTopics": ["legal"],
  "requiredDisclaimer": "This is not legal advice.",
  "maxInputLength": 8000,
  "maxResponseLength": 4000,
  "enforceLanguage": "en-US"
}
```

---

## API overview

### Public (no auth)

- `GET /health`
- `GET /metrics`

### Master key (`Authorization: Bearer <MasterKey>`)

- `POST /apps/register`
- `GET /admin` and all `/admin/*` routes

### App key (`X-App-Id` + `X-User-Id` + `Authorization: Bearer <apiKey>`)

- `POST /api/chat`
- `POST /api/generate`
- `POST /api/chat/feedback`
- `GET /apps/{appId}`
- `GET|PATCH /apps/{appId}/config`
- `PUT /apps/{appId}/session-context`
- `POST /apps/{appId}/wiki` (multipart `.md` upload)

### Feedback example

```bash
curl -X POST http://localhost:5100/api/chat/feedback \
  -H "X-App-Id: helpdesk-prod-abc123" \
  -H "X-User-Id: user-42" \
  -H "Authorization: Bearer cm_live_..." \
  -H "Content-Type: application/json" \
  -d '{ "messageId": "<from X-Context-Memory-Message-Id>", "score": 1, "reason": "helpful" }'
```

### Wiki upload example

```bash
curl -X POST http://localhost:5100/apps/helpdesk-prod-abc123/wiki \
  -H "X-App-Id: helpdesk-prod-abc123" \
  -H "Authorization: Bearer cm_live_..." \
  -F "files=@./docs/faq.md"
```

---

## Embeddings setup

RAG requires a local ONNX model and vocabulary:

```powershell
./scripts/download-embedding-model.ps1
```

Paths are configured under `ContextMemory:Embeddings` in `appsettings.json` (nested `"Embeddings"` object). If the model is missing, the API starts but RAG is disabled until files are present.

---

## Integrating your application

Point any Ollama-compatible client at ContextMemory instead of Ollama:

| Ollama direct | ContextMemory |
|---------------|---------------|
| `http://localhost:11434/api/chat` | `http://localhost:5100/api/chat` |
| *(none)* | `X-App-Id`, `X-User-Id`, `Authorization` |

**Python (ollama library style via HTTP):**

```python
import requests

resp = requests.post(
    "http://localhost:5100/api/chat",
    headers={
        "X-App-Id": "myapp-prod-xyz",
        "X-User-Id": "user-1",
        "Authorization": "Bearer cm_live_...",
    },
    json={
        "model": "llama3.2",
        "messages": [{"role": "user", "content": "Summarize our refund policy."}],
        "stream": False,
    },
)
print(resp.json())
```

**Streaming:** set `"stream": true` and consume NDJSON lines (same as Ollama).

---

## Data layout

```
data/
├── app-profiles/{appId}/
│   ├── config.json
│   ├── content-rules.json
│   ├── persona.md
│   ├── business-rules.md
│   └── format-rules.md
├── conversation-history/{appId}/{userId}.json
├── user-profiles/{appId}/{userId}.json
├── user-profiles/{appId}/{userId}.semantic.bin
├── feedback/{appId}.json
├── audit/                    # JSONL audit entries
└── registered-apps/{appId}.json

wikis/
└── {appId}/                  # Markdown knowledge base
```

---

## Prometheus metrics

Exposed at `GET /metrics` (no auth):

- `cm_requests_total{appId,status}`
- `cm_tokens_prompt_total{appId}`
- `cm_tokens_completion_total{appId}`
- `cm_latency_ms{appId,percentile}` (p50, p95, p99)
- `cm_rag_hits_total{appId}`
- `cm_feedback_score{appId}`
- `cm_active_users{appId}`
- `cm_content_filtered_total{appId,reason}`

---

## Solution structure

```
ContextMemoryMiddleware/
├── src/
│   ├── ContextMemory.Api/          # Minimal API, middleware, endpoints
│   ├── ContextMemory.Core/         # Engine, memory, RAG, safety, feedback
│   ├── ContextMemory.Adapters/     # Ollama, LM Studio, OpenAI clients
│   └── ContextMemory.Embeddings/   # ONNX embedding engine
├── tests/
│   └── ContextMemory.Api.Tests/    # Integration tests (WebApplicationFactory)
├── scripts/
│   └── download-embedding-model.ps1
├── data/                           # Runtime data (gitignored in production)
├── wikis/                          # Per-app markdown wikis
├── Dockerfile
├── docker-compose.yml
└── Blueprint.md                    # Full product blueprint (Portuguese)
```

---

## Development

```bash
# Run tests (excludes Ollama E2E)
dotnet test --filter "Category!=OllamaE2E"

# Ollama E2E smoke (requires Ollama running locally)
OLLAMA_E2E=1 dotnet test --filter "Category=OllamaE2E"

# Optional: full chat E2E with a pulled model
OLLAMA_E2E=1 OLLAMA_E2E_MODEL=llama3.2 dotnet test --filter "Category=OllamaE2E"

# Run API with hot reload
cd src/ContextMemory.Api
dotnet watch run
```

`appsettings.json` uses paths like `../../data` so data and wikis resolve to the repository root when running from `src/ContextMemory.Api`.

---

## Security notes

- Never commit real `MasterKey` or API keys; use environment variables or a secret store in production.
- User message content is not logged; audit entries record filter reasons only.
- Payload size is capped (`MaxPayloadBytes`).
- `appId` and `userId` are validated (alphanumeric + hyphens, max 64 characters).
- Admin and cross-app data access require the master key; app routes require matching `X-App-Id`.

---

## Choosing an LLM backend

Set `llmBackend` in the app’s `config.json` (or at registration):

| Value | When to use |
|-------|-------------|
| `ollama` | Local models — **primary / default** |
| `lmstudio` | LM Studio OpenAI-compatible server |
| `openai` | OpenAI or compatible cloud API; set `OpenAiApiKey` at platform level |

Each app can use a different backend. Platform endpoints in `appsettings.json` are connection settings; use **Ollama** for the current local evaluation unless you explicitly configure another backend.

---

## License

Add your license here.

---

## Further reading

- `Blueprint.md` — Detailed architecture and phased implementation spec (Portuguese).
- `.cursorrules` — Engineering constraints for contributors.
