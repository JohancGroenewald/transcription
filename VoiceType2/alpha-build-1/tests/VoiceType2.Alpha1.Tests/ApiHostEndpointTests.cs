extern alias ApiHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using VoiceType2.ApiHost;
using VoiceType2.ApiHost.Services;
using VoiceType2.Core.Contracts;
using VoiceType2.Infrastructure.Transcription;

using ApiHostProgram = ApiHost::Program;

namespace VoiceType2.Alpha1.Tests;

public sealed class ApiHostEndpointTests : IClassFixture<WebApplicationFactory<ApiHostProgram>>
{
    private readonly WebApplicationFactory<ApiHostProgram> _factory;

    public ApiHostEndpointTests(WebApplicationFactory<ApiHostProgram> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Health_endpoints_are_available()
    {
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();

        var livePayload = await JsonDocument.ParseAsync(await live.Content.ReadAsStreamAsync());
        Assert.Equal("live", livePayload.RootElement.GetProperty("status").GetString());

        var ready = await client.GetAsync("/health/ready");
        ready.EnsureSuccessStatusCode();

        var readyPayload = await JsonDocument.ParseAsync(await ready.Content.ReadAsStreamAsync());
        Assert.Equal("ready", readyPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Session_status_requires_orchestrator_token()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/sessions/{created.SessionId}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Start_requires_orchestrator_token()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{created.SessionId}/start");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sessions_require_authorization_when_token_required()
    {
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig { AuthMode = "token-required" });
        using var client = factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var unauthenticated = await client.GetAsync($"/v1/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/sessions/{created.SessionId}");
        request.Headers.Add("x-orchestrator-token", created.OrchestratorToken);
        using var authenticated = await client.SendAsync(request);
        authenticated.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Register_start_stop_and_resolve_are_compatible()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        var startResponse = await StartAsync(client, created);
        startResponse.EnsureSuccessStatusCode();

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.True(status.State == SessionState.Listening.ToString() || status.State == SessionState.Running.ToString());

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        var resolve = await ResolveAsync(client, created.SessionId, created.OrchestratorToken, "submit");
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(SessionState.Completed.ToString(), status.State);
    }

    [Theory]
    [InlineData("dictate")]
    [InlineData("command")]
    public async Task Register_accepts_custom_session_mode(string sessionMode)
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client, sessionMode);

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(SessionState.Registered.ToString(), status.State);
    }

    [Fact]
    public async Task Session_status_contains_audio_device_selection()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:0"
            });

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.NotNull(status.AudioDevices);
        Assert.Equal("rec:0", status.AudioDevices!.RecordingDeviceId);
        Assert.Equal("play:0", status.AudioDevices.PlaybackDeviceId);
    }

    [Fact]
    public async Task Session_device_update_endpoint_updates_session_selection()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/sessions/{created.SessionId}/devices");
        request.Headers.Add("x-orchestrator-token", created.OrchestratorToken);
        request.Content = JsonContent.Create(new AudioDeviceSelection
        {
            RecordingDeviceId = "rec:1",
            PlaybackDeviceId = "play:2"
        });

        using var update = await client.SendAsync(request);
        update.EnsureSuccessStatusCode();

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.NotNull(status.AudioDevices);
        Assert.Equal("rec:1", status.AudioDevices!.RecordingDeviceId);
        Assert.Equal("play:2", status.AudioDevices.PlaybackDeviceId);
    }

    [Fact]
    public async Task Device_endpoint_returns_available_devices_shape()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/v1/devices");
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        Assert.True(json.RootElement.TryGetProperty("recordingDevices", out _));
        Assert.True(json.RootElement.TryGetProperty("playbackDevices", out _));
    }

    [Fact]
    public async Task Stop_is_idempotent_in_terminal_transition()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);
        _ = await StartAsync(client, created);

        using var firstStop = await StopAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(HttpStatusCode.OK, firstStop.StatusCode);

        using var secondStop = await StopAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(HttpStatusCode.OK, secondStop.StatusCode);
    }

    [Fact]
    public async Task Resolve_before_awaiting_decision_returns_conflict()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);
        _ = await StartAsync(client, created);

        var resolve = await ResolveAsync(client, created.SessionId, created.OrchestratorToken, "submit");
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.True(status.State == SessionState.Listening.ToString() || status.State == SessionState.Running.ToString());
    }

    [Fact]
    public async Task Events_stream_returns_initial_sse_envelope()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);
        _ = await StartAsync(client, created);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/sessions/{created.SessionId}/events");
        request.Headers.Add("x-orchestrator-token", created.OrchestratorToken);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var firstLine = await reader.ReadLineAsync();
        Assert.True(firstLine?.StartsWith("data: ") is true);
    }

    private static OrchestratorProfile CreateProfile()
    {
        return new OrchestratorProfile
        {
            OrchestratorId = "alpha1-smoke-cli",
            Platform = "windows",
            Capabilities = new OrchestratorCapabilities(
                hotkeys: false,
                tray: false,
                clipboard: true,
                notifications: false,
                audioCapture: false,
                uiShell: false)
        };
    }

    private static WebApplicationFactory<ApiHostProgram> CreateApiHostFactory(RuntimeSecurityConfig runtimeSecurity)
    {
        return new WebApplicationFactory<ApiHostProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<RuntimeConfig>();
                services.RemoveAll<RuntimeSecurityConfig>();
                services.RemoveAll<SessionPolicyConfig>();
                services.RemoveAll<TranscriptionDefaultsConfig>();
                services.RemoveAll<SessionService>();

                var config = new RuntimeConfig
                {
                    RuntimeSecurity = runtimeSecurity,
                    SessionPolicy = new SessionPolicyConfig
                    {
                        MaxConcurrentSessions = 2,
                        DefaultSessionTimeoutMs = 300000
                    }
                };

                services.AddSingleton(config);
                services.AddSingleton(config.SessionPolicy);
                services.AddSingleton(config.RuntimeSecurity);
                services.AddSingleton(config.TranscriptionDefaults);
                services.AddSingleton<SessionService>();
                services.AddSingleton<ITranscriptionProvider, MockTranscriptionProvider>();
            });
        });
    }

    private async Task<SessionCreatedResponse> RegisterSessionAsync(
        HttpClient client,
        string sessionMode = "dictate",
        AudioDeviceSelection? audioDevices = null)
    {
        var payload = new
        {
            sessionMode,
            correlationId = "alpha1-test-correlation",
            profile = CreateProfile(),
            audioDevices
        };

        using var response = await client.PostAsJsonAsync("/v1/sessions", payload);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<SessionCreatedResponse>();
        Assert.NotNull(created);
        Assert.NotNull(created.OrchestratorToken);
        return created;
    }

    private async Task<HttpResponseMessage> StartAsync(HttpClient client, SessionCreatedResponse session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{session.SessionId}/start");
        request.Headers.Add("x-orchestrator-token", session.OrchestratorToken);
        return await client.SendAsync(request);
    }

    private async Task<SessionStatusResponse> GetStatusAsync(HttpClient client, string sessionId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/sessions/{sessionId}");
        request.Headers.Add("x-orchestrator-token", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(status);
        return status;
    }

    private async Task<HttpResponseMessage> ResolveAsync(
        HttpClient client,
        string sessionId,
        string token,
        string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{sessionId}/resolve");
        request.Headers.Add("x-orchestrator-token", token);
        request.Content = JsonContent.Create(new ResolveRequest { Action = action });
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> StopAsync(HttpClient client, string sessionId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{sessionId}/stop");
        request.Headers.Add("x-orchestrator-token", token);
        return await client.SendAsync(request);
    }

    private async Task WaitForStateAsync(
        HttpClient client,
        string sessionId,
        string token,
        SessionState expectedState,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await GetStatusAsync(client, sessionId, token);
            if (status.State == expectedState.ToString())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Expected state '{expectedState}' was not reached before timeout.");
    }
}
