using System.Runtime.CompilerServices;
using System.Text.Json;
using VoiceType2.Core.Contracts;

namespace VoiceType2.App.Cli;

public sealed class ApiHostException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }

    public ApiHostException(int statusCode, string message, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

internal sealed class ApiSessionClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiSessionClient(string apiUrl, string? token = null, HttpClient? client = null)
    {
        var normalizedBase = NormalizeBaseUrl(apiUrl);
        _ownsClient = client is null;
        _httpClient = client ?? new HttpClient { BaseAddress = new Uri(normalizedBase) };
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Remove("x-orchestrator-token");
            _httpClient.DefaultRequestHeaders.Add("x-orchestrator-token", token);
        }

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public Task<SessionCreatedResponse> RegisterAsync(OrchestratorProfile profile, string sessionMode, CancellationToken ct = default)
    {
        var request = new RegisterSessionRequest
        {
            SessionMode = sessionMode,
            Profile = profile
        };

        return SendAsync<SessionCreatedResponse>(HttpMethod.Post, "v1/sessions", request, ct);
    }

    public Task<SessionStatusResponse> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        return SendAsync<SessionStatusResponse>(HttpMethod.Get, $"v1/sessions/{sessionId}", null, ct);
    }

    public Task StartAsync(string sessionId, CancellationToken ct = default)
    {
        return SendNoResponseAsync(HttpMethod.Post, $"v1/sessions/{sessionId}/start", ct);
    }

    public Task StopAsync(string sessionId, CancellationToken ct = default)
    {
        return SendNoResponseAsync(HttpMethod.Post, $"v1/sessions/{sessionId}/stop", ct);
    }

    public Task ResolveAsync(string sessionId, string action, CancellationToken ct = default)
    {
        var request = new ResolveRequest { Action = action };
        return SendNoResponseAsync(HttpMethod.Post, $"v1/sessions/{sessionId}/resolve", request, ct);
    }

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        return IsHealthyAsync("health/ready", ct);
    }

    public async IAsyncEnumerable<SessionEventEnvelope> StreamEventsAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/sessions/{sessionId}/events");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await ThrowForErrorsAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            var evt = JsonSerializer.Deserialize<SessionEventEnvelope>(payload, _jsonOptions);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? requestBody,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: _jsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, ct);
        await ThrowForErrorsAsync(response, ct);

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<T>(responseStream, _jsonOptions, ct);
        if (payload is null)
        {
            throw new InvalidOperationException($"No payload returned from {path}.");
        }

        return payload;
    }

    private async Task SendNoResponseAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await _httpClient.SendAsync(request, ct);
        await ThrowForErrorsAsync(response, ct);
    }

    private async Task SendNoResponseAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        await ThrowForErrorsAsync(response, ct);
    }

    private async Task<bool> IsHealthyAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task ThrowForErrorsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(body, _jsonOptions);
        var detail = envelope?.Detail ?? response.ReasonPhrase ?? "Request failed.";
        throw new ApiHostException((int)response.StatusCode, detail, envelope?.ErrorCode);
    }

    private static string NormalizeBaseUrl(string apiUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiUrl) ? "http://127.0.0.1:5240" : apiUrl.TrimEnd('/');
        return baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/";
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }

        await Task.CompletedTask;
    }
}
