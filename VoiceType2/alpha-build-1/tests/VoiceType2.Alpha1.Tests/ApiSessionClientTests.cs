using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VoiceType2.App.Cli;
using VoiceType2.Core.Contracts;
using Xunit;

namespace VoiceType2.Alpha1.Tests;

public class ApiSessionClientTests
{
    [Fact]
    public async Task RegisterAsync_sends_request_and_returns_session()
    {
        var profile = CreateProfile("unit-cli");
        SessionCreatedResponse? observedResponse = null;
        var calls = new List<HttpRequestMessage>();

        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            calls.Add(request);
            if (request.RequestUri is null || request.Method != HttpMethod.Post)
            {
                throw new InvalidOperationException("Unexpected request method.");
            }

            if (request.RequestUri.AbsolutePath != "/v1/sessions")
            {
                throw new InvalidOperationException($"Unexpected request path: {request.RequestUri.AbsolutePath}");
            }

            var body = await request.Content!.ReadAsStringAsync(ct);
            var requestPayload = JsonSerializer.Deserialize<RegisterSessionRequest>(body, JsonDefaults.Options);
            Assert.Equal("dictate", requestPayload!.SessionMode);
            Assert.Equal(profile.OrchestratorId, requestPayload.Profile!.OrchestratorId);

            observedResponse = new SessionCreatedResponse
            {
                SessionId = "sess-1",
                OrchestratorToken = "token-1",
                State = SessionState.Listening.ToString(),
                CorrelationId = "corr-1"
            };

            return JsonResponse(HttpStatusCode.OK, observedResponse);
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            client: new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var created = await client.RegisterAsync(profile, "dictate");
        Assert.Equal("sess-1", created.SessionId);
        Assert.Equal("token-1", created.OrchestratorToken);
        Assert.Equal("corr-1", created.CorrelationId);
        Assert.Single(calls);
    }

    [Fact]
    public async Task GetStatusAsync_uses_expected_request_shape()
    {
        var calls = new List<HttpRequestMessage>();

        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            calls.Add(request);
            if (request.RequestUri is null || request.Method != HttpMethod.Get)
            {
                throw new InvalidOperationException("Unexpected request type.");
            }

            Assert.Equal("/v1/sessions/sess-1", request.RequestUri.AbsolutePath);
            Assert.True(request.Headers.TryGetValues("x-orchestrator-token", out var values));
            Assert.Contains("tok", values);

            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                new SessionStatusResponse
                {
                    SessionId = "sess-1",
                    State = SessionState.Listening.ToString(),
                    CorrelationId = "corr-1",
                    LastEvent = "started",
                    Revision = 7
                }));
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var status = await client.GetStatusAsync("sess-1");
        Assert.Equal("sess-1", status.SessionId);
        Assert.Equal("started", status.LastEvent);
        Assert.Equal(7, status.Revision);
        Assert.Single(calls);
    }

    [Fact]
    public async Task StreamEventsAsync_parses_server_sent_events()
    {
        var ssePayload = string.Join("\n", new[]
        {
            "data: " + JsonSerializer.Serialize(new SessionEventEnvelope
            {
                EventType = "status",
                SessionId = "sess-1",
                CorrelationId = "corr-1",
                State = "listening",
                Text = "ready"
            }, JsonDefaults.Options),
            "data: " + JsonSerializer.Serialize(new SessionEventEnvelope
            {
                EventType = "transcript",
                SessionId = "sess-1",
                CorrelationId = "corr-1",
                Text = "hello"
            }, JsonDefaults.Options),
            ""
        });

        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(ssePayload + "\n"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            });
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var events = new List<SessionEventEnvelope>();
        await foreach (var evt in client.StreamEventsAsync("sess-1", CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("status", events[0].EventType);
        Assert.Equal("hello", events[1].Text);
    }

    [Fact]
    public async Task ResolveAsync_throws_api_error_with_status_and_code()
    {
        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(JsonResponse(
                HttpStatusCode.Conflict,
                new ErrorEnvelope
                {
                    Type = "about:blank",
                    Title = "INVALID_TRANSITION",
                    Status = 409,
                    Detail = "Resolve is only valid when awaiting decision.",
                    ErrorCode = "INVALID_TRANSITION",
                    CorrelationId = "corr-1",
                    SessionId = "sess-1"
                }));
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var ex = await Assert.ThrowsAsync<ApiHostException>(() => client.ResolveAsync("sess-1", "submit"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("INVALID_TRANSITION", ex.ErrorCode);
    }

    [Fact]
    public async Task IsReadyAsync_reflects_health_status()
    {
        var calls = 0;
        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(calls % 2 == 1 ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable));
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            client: new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        Assert.True(await client.IsReadyAsync());
        Assert.False(await client.IsReadyAsync());
    }

    [Fact]
    public async Task StartAsync_sends_expected_request_and_token()
    {
        var calls = new List<HttpRequestMessage>();
        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            calls.Add(request);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/sessions/sess-1/start", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.TryGetValues("x-orchestrator-token", out var values));
            Assert.Contains("tok", values);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        await client.StartAsync("sess-1");
        Assert.Single(calls);
    }

    [Fact]
    public async Task StopAsync_sends_expected_request_and_token()
    {
        var calls = new List<HttpRequestMessage>();
        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            calls.Add(request);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/sessions/sess-1/stop", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.TryGetValues("x-orchestrator-token", out var values));
            Assert.Contains("tok", values);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        await client.StopAsync("sess-1");
        Assert.Single(calls);
    }

    [Fact]
    public async Task ResolveAsync_includes_payload_and_token()
    {
        string? observedAction = null;
        var calls = new List<HttpRequestMessage>();

        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            calls.Add(request);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/sessions/sess-1/resolve", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.TryGetValues("x-orchestrator-token", out var values));
            Assert.Contains("tok", values);

            var body = await request.Content!.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<ResolveRequest>(body, JsonDefaults.Options);
            observedAction = payload?.Action;

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            "tok",
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        await client.ResolveAsync("sess-1", "submit");
        Assert.Single(calls);
        Assert.Equal("submit", observedAction);
    }

    private static OrchestratorProfile CreateProfile(string id)
        => new OrchestratorProfile
        {
            OrchestratorId = id,
            Platform = "windows",
            Capabilities = new OrchestratorCapabilities(
                hotkeys: false,
                tray: false,
                clipboard: true,
                notifications: false,
                audioCapture: false,
                uiShell: false)
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object payload)
        => new HttpResponseMessage(status) { Content = JsonContent.Create(payload, options: JsonDefaults.Options) };
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _sendAsync = sendAsync;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _sendAsync(request, cancellationToken);
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
