using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VoiceType2.Alpha2.Core;

namespace VoiceType2.Alpha2.App.Cli;

public sealed class ApiHostException : Exception
{
    public ApiHostException(int statusCode, string message, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
}

internal sealed class ApiSessionClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ApiSessionClient(string apiUrl, string? token = null, HttpClient? client = null)
    {
        var normalizedBase = NormalizeBaseUrl(apiUrl);
        _ownsClient = client is null;
        _httpClient = client ?? new HttpClient { BaseAddress = new Uri(normalizedBase) };
        if (client is null)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Remove("x-orchestrator-token");
            _httpClient.DefaultRequestHeaders.Add("x-orchestrator-token", token);
        }
    }

    public Task<SessionCreatedResponse> RegisterAsync(OrchestratorProfile profile, CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionCreatedResponse>(
            HttpMethod.Post,
            "v2/sessions",
            new RegisterSessionRequest { Profile = profile },
            cancellationToken);
    }

    public Task<SessionStatusResponse> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionStatusResponse>(HttpMethod.Get, $"v2/sessions/{sessionId}", null, cancellationToken);
    }

    public Task<SessionStatusResponse> StartAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionStatusResponse>(HttpMethod.Post, $"v2/sessions/{sessionId}/start", new { }, cancellationToken);
    }

    public Task<SessionStatusResponse> StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionStatusResponse>(HttpMethod.Post, $"v2/sessions/{sessionId}/stop", new { }, cancellationToken);
    }

    public Task<SessionStatusResponse> CancelAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionStatusResponse>(HttpMethod.Post, $"v2/sessions/{sessionId}/cancel", new { }, cancellationToken);
    }

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        return IsHealthyAsync("health/ready", cancellationToken);
    }

    public async IAsyncEnumerable<SessionEventEnvelope> StreamEventsAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/sessions/{sessionId}/events");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await ThrowForErrorsAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
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
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: _jsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ThrowForErrorsAsync(response, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(responseStream, _jsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"No payload returned from {path}.");
    }

    private async Task<bool> IsHealthyAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task ThrowForErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(body, _jsonOptions);
        throw new ApiHostException(
            (int)response.StatusCode,
            envelope?.Detail ?? response.ReasonPhrase ?? "Request failed.",
            envelope?.ErrorCode);
    }

    private static string NormalizeBaseUrl(string apiUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiUrl) ? "http://127.0.0.1:5250" : apiUrl.TrimEnd('/');
        return baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/";
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
