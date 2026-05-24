using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class ChatClient
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly HttpClient _http;
    private readonly AdminSession _adminSession;

    public ChatClient(HttpClient http, AdminSession adminSession)
    {
        _http = http;
        _adminSession = adminSession;
    }

    public async Task<AppDetailDto?> GetAppAsync(ChatTestSettings settings, CancellationToken cancellationToken = default)
    {
        using var request = CreateAppRequest(HttpMethod.Get, settings, $"/apps/{Uri.EscapeDataString(settings.AppId.Trim())}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AdminApiException((int)response.StatusCode, body);
        return JsonSerializer.Deserialize<AppDetailDto>(body, WireOptions);
    }

    public Task<ChatExchangeResult> ChatAsync(
        ChatTestSettings settings,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildChatRequest(settings, userMessage, stream: false);
        return SendChatAsync(settings, request, cancellationToken);
    }

    public async Task<ChatExchangeResult> ChatStreamAsync(
        ChatTestSettings settings,
        string userMessage,
        Action<ChatUiMessage> onAssistantUpdate,
        ChatUiMessage assistantMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildChatRequest(settings, userMessage, stream: true);
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/chat");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new AdminApiException((int)response.StatusCode, err);
        }

        var sb = new StringBuilder();
        string? streamMessageId = null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaResponse>(line, WireOptions);
            }
            catch
            {
                continue;
            }

            if (chunk?.Message?.Content is { Length: > 0 } part)
            {
                sb.Append(part);
                assistantMessage.Content = sb.ToString();
                onAssistantUpdate(assistantMessage);
            }
            else if (!string.IsNullOrEmpty(chunk?.Response))
            {
                sb.Append(chunk.Response);
                assistantMessage.Content = sb.ToString();
                onAssistantUpdate(assistantMessage);
            }

            if (chunk?.ContextMemory?.MessageId is { Length: > 0 } id)
                streamMessageId = id;
        }

        sw.Stop();
        var messageId = GetHeader(response, "X-Context-Memory-Message-Id") ?? streamMessageId;
        assistantMessage.MessageId = messageId;
        assistantMessage.ElapsedMs = sw.ElapsedMilliseconds;
        assistantMessage.IsStreaming = false;

        return new ChatExchangeResult
        {
            Content = sb.ToString(),
            MessageId = messageId,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = sb.ToString(),
            Meta = null
        };
    }

    public Task<ChatExchangeResult> GenerateAsync(
        ChatTestSettings settings,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var request = BuildGenerateRequest(settings, prompt, stream: false);
        return SendGenerateAsync(settings, request, cancellationToken);
    }

    public async Task<ChatExchangeResult> GenerateStreamAsync(
        ChatTestSettings settings,
        string prompt,
        Action<ChatUiMessage> onAssistantUpdate,
        ChatUiMessage assistantMessage,
        CancellationToken cancellationToken = default)
    {
        var request = BuildGenerateRequest(settings, prompt, stream: true);
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/generate");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new AdminApiException((int)response.StatusCode, err);
        }

        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaResponse>(line, WireOptions);
            }
            catch
            {
                continue;
            }

            if (chunk?.Response is { Length: > 0 } part)
            {
                sb.Append(part);
                assistantMessage.Content = sb.ToString();
                onAssistantUpdate(assistantMessage);
            }
        }

        sw.Stop();
        assistantMessage.ElapsedMs = sw.ElapsedMilliseconds;
        assistantMessage.IsStreaming = false;

        return new ChatExchangeResult
        {
            Content = sb.ToString(),
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = sb.ToString()
        };
    }

    public async Task SendFeedbackAsync(
        ChatTestSettings settings,
        string messageId,
        int score,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = new FeedbackRequest
        {
            MessageId = messageId,
            Score = score,
            Reason = reason
        };

        using var request = CreateAppRequest(HttpMethod.Post, settings, "/api/chat/feedback");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, WireOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AdminApiException((int)response.StatusCode, responseBody);
    }

    private async Task<ChatExchangeResult> SendChatAsync(
        ChatTestSettings settings,
        OllamaRequest request,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/chat");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");
        var messageId = GetHeader(response, "X-Context-Memory-Message-Id");

        if (!response.IsSuccessStatusCode)
        {
            return new ChatExchangeResult
            {
                Content = TryExtractError(responseBody),
                MessageId = messageId,
                ElapsedMs = sw.ElapsedMilliseconds,
                ResponseTimeHeaderMs = responseTimeHeader,
                RawRequestJson = requestJson,
                RawResponseJson = responseBody,
                IsError = true,
                StatusCode = (int)response.StatusCode
            };
        }

        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody, WireOptions);
        var content = ollamaResponse?.Message?.Content ?? ollamaResponse?.Response ?? responseBody;

        return new ChatExchangeResult
        {
            Content = content,
            MessageId = messageId,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = PrettyJson(responseBody),
            Meta = ToMeta(ollamaResponse)
        };
    }

    private async Task<ChatExchangeResult> SendGenerateAsync(
        ChatTestSettings settings,
        OllamaGenerateRequest request,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(request, WireOptions);
        using var httpRequest = CreateAppRequest(HttpMethod.Post, settings, "/api/generate");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var responseTimeHeader = GetHeader(response, "X-Response-Time-Ms");

        if (!response.IsSuccessStatusCode)
        {
            return new ChatExchangeResult
            {
                Content = TryExtractError(responseBody),
                ElapsedMs = sw.ElapsedMilliseconds,
                ResponseTimeHeaderMs = responseTimeHeader,
                RawRequestJson = requestJson,
                RawResponseJson = responseBody,
                IsError = true,
                StatusCode = (int)response.StatusCode
            };
        }

        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody, WireOptions);
        var content = ollamaResponse?.Response ?? ollamaResponse?.Message?.Content ?? responseBody;

        return new ChatExchangeResult
        {
            Content = content,
            ElapsedMs = sw.ElapsedMilliseconds,
            ResponseTimeHeaderMs = responseTimeHeader,
            RawRequestJson = requestJson,
            RawResponseJson = PrettyJson(responseBody),
            Meta = ToMeta(ollamaResponse)
        };
    }

    private OllamaRequest BuildChatRequest(ChatTestSettings settings, string userMessage, bool stream)
    {
        var messages = new List<OllamaMessage>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            messages.Add(new OllamaMessage { Role = "system", Content = settings.SystemPrompt.Trim() });

        messages.Add(new OllamaMessage { Role = "user", Content = userMessage.Trim() });

        return new OllamaRequest
        {
            Model = settings.Model.Trim(),
            Messages = messages,
            Stream = stream,
            Format = string.IsNullOrWhiteSpace(settings.Format) ? null : settings.Format.Trim(),
            KeepAlive = string.IsNullOrWhiteSpace(settings.KeepAlive) ? null : settings.KeepAlive.Trim(),
            Options = BuildOptions(settings)
        };
    }

    private static OllamaGenerateRequest BuildGenerateRequest(ChatTestSettings settings, string prompt, bool stream) =>
        new()
        {
            Model = settings.Model.Trim(),
            Prompt = prompt.Trim(),
            Stream = stream,
            Format = string.IsNullOrWhiteSpace(settings.Format) ? null : settings.Format.Trim(),
            KeepAlive = string.IsNullOrWhiteSpace(settings.KeepAlive) ? null : settings.KeepAlive.Trim(),
            Options = BuildOptions(settings)
        };

    private static OllamaOptions BuildOptions(ChatTestSettings settings) =>
        new()
        {
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            TopK = settings.TopK,
            NumCtx = settings.NumCtx,
            RepeatPenalty = settings.RepeatPenalty,
            NumPredict = settings.NumPredict
        };

    private HttpRequestMessage CreateAppRequest(HttpMethod method, ChatTestSettings settings, string path)
    {
        ValidateSettings(settings);
        var baseUrl = _adminSession.Settings.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.TryAddWithoutValidation("X-App-Id", settings.AppId.Trim());
        request.Headers.TryAddWithoutValidation("X-User-Id", settings.UserId.Trim());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        return request;
    }

    private void ValidateSettings(ChatTestSettings settings)
    {
        if (!_adminSession.IsConfigured)
            throw new InvalidOperationException("Configure API URL in Settings.");
        if (string.IsNullOrWhiteSpace(settings.AppId)
            || string.IsNullOrWhiteSpace(settings.UserId)
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("AppId, UserId, API key and model are required.");
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static OllamaResponseMeta? ToMeta(OllamaResponse? r) =>
        r is null ? null : new OllamaResponseMeta
        {
            Model = r.Model,
            DoneReason = r.DoneReason,
            PromptEvalCount = r.PromptEvalCount,
            EvalCount = r.EvalCount,
            TotalDuration = r.TotalDuration,
            EvalDuration = r.EvalDuration
        };

    private static string TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? body;
        }
        catch
        {
            // ignore
        }

        return body;
    }

    private static string PrettyJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, DisplayOptions);
        }
        catch
        {
            return json;
        }
    }
}
