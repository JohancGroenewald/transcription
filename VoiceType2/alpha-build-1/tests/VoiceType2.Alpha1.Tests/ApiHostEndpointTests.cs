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
        Assert.True(json.RootElement.TryGetProperty("playbackDevices", out var playbackElement));
        Assert.Equal(JsonValueKind.Array, playbackElement.ValueKind);
        Assert.Equal(
            JsonValueKind.Array,
            json.RootElement.GetProperty("recordingDevices").ValueKind);
    }

    [Fact]
    public async Task Start_uses_session_selected_audio_devices_in_transcription()
    {
        var provider = new CapturingTranscriptionProvider();
        var bootstrapper = new TrackingAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:1"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        Assert.NotNull(provider.CapturedSelection);
        Assert.Equal("rec:0", provider.CapturedSelection!.RecordingDeviceId);
        Assert.Equal("play:1", provider.CapturedSelection.PlaybackDeviceId);
        Assert.True(bootstrapper.RecordingCaptureInitialized);
        Assert.True(bootstrapper.PlaybackInitialized);
        Assert.True(bootstrapper.ConfirmationTonePlayed);
    }

    [Fact]
    public async Task Start_without_audio_devices_uses_fallback_empty_audio()
    {
        var provider = new CapturingAudioBytesTranscriptionProvider();
        var bootstrapper = new TrackingAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(client);

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        Assert.True(bootstrapper.RecordingCaptureInitialized);
        Assert.True(bootstrapper.PlaybackInitialized);
        Assert.True(bootstrapper.ConfirmationTonePlayed);
        Assert.Null(bootstrapper.InitializedAudioDevices);
        Assert.Null(provider.CapturedSelection);
        Assert.NotNull(provider.CapturedAudio);
        Assert.Empty(provider.CapturedAudio);
    }

    [Fact]
    public async Task Start_transcribes_with_host_capture_stream()
    {
        var provider = new CapturingAudioBytesTranscriptionProvider();
        var bootstrapper = new FakeAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:1"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        Assert.True(bootstrapper.RecordingCaptureInitialized);
        Assert.True(bootstrapper.PlaybackInitialized);
        Assert.True(bootstrapper.ConfirmationTonePlayed);
        Assert.NotNull(provider.CapturedAudio);
        Assert.Equal(bootstrapper.CapturedAudioPayload, provider.CapturedAudio);
        Assert.Equal("rec:0", provider.CapturedSelection!.RecordingDeviceId);
    }

    [Fact]
    public async Task Start_initializes_host_audio_pipeline_with_session_device_selection()
    {
        var provider = new CapturingTranscriptionProvider();
        var bootstrapper = new TrackingAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:1"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        Assert.True(bootstrapper.RecordingCaptureInitialized);
        Assert.True(bootstrapper.PlaybackInitialized);
        Assert.True(bootstrapper.ConfirmationTonePlayed);
        Assert.NotNull(bootstrapper.InitializedAudioDevices);
        Assert.Equal("rec:0", bootstrapper.InitializedAudioDevices!.RecordingDeviceId);
        Assert.Equal("play:1", bootstrapper.InitializedAudioDevices!.PlaybackDeviceId);
    }

    [Fact]
    public async Task Start_falls_back_to_empty_audio_when_no_capture_session_is_created()
    {
        var provider = new CapturingAudioBytesTranscriptionProvider();
        var bootstrapper = new FakeAudioBootstrapper(captureEnabled: false);
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:9999",
                PlaybackDeviceId = "play:9999"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        Assert.Equal(1, bootstrapper.RecordingCaptureInitializedCount);
        Assert.Equal(1, bootstrapper.PlaybackInitializedCount);
        Assert.Equal(1, bootstrapper.ConfirmationTonePlayedCount);
        Assert.False(bootstrapper.RecordingCaptureStarted);
        Assert.True(bootstrapper.ConfirmationTonePlayed);
        Assert.NotNull(provider.CapturedAudio);
        Assert.Empty(provider.CapturedAudio);
        Assert.Equal("rec:9999", provider.CapturedSelection!.RecordingDeviceId);
        Assert.Equal("play:9999", provider.CapturedSelection!.PlaybackDeviceId);
    }

    [Fact]
    public async Task Resolve_retry_reinvokes_capture_pipeline()
    {
        var provider = new CapturingAudioBytesTranscriptionProvider();
        var bootstrapper = new FakeAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:1"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        var retry = await ResolveAsync(client, created.SessionId, created.OrchestratorToken, "retry");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(3));

        Assert.Equal(2, bootstrapper.RecordingCaptureInitializedCount);
        Assert.Equal(2, bootstrapper.PlaybackInitializedCount);
        Assert.Equal(2, bootstrapper.ConfirmationTonePlayedCount);
        Assert.Equal(2, provider.TranscribeInvocationCount);
        Assert.True(provider.CapturedAudio!.Length > 0);
    }

    [Fact]
    public async Task Stop_prevents_retry_from_restarting_capture_work()
    {
        var provider = new CapturingAudioBytesTranscriptionProvider();
        var bootstrapper = new FakeAudioBootstrapper();
        using var factory = CreateApiHostFactory(new RuntimeSecurityConfig(), provider, bootstrapper);
        using var client = factory.CreateClient();

        var created = await RegisterSessionAsync(
            client,
            audioDevices: new AudioDeviceSelection
            {
                RecordingDeviceId = "rec:0",
                PlaybackDeviceId = "play:1"
            });

        using var start = await StartAsync(client, created);
        start.EnsureSuccessStatusCode();

        await WaitForStateAsync(
            client,
            created.SessionId,
            created.OrchestratorToken,
            SessionState.AwaitingDecision,
            TimeSpan.FromSeconds(2));

        using var stop = await StopAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        var status = await GetStatusAsync(client, created.SessionId, created.OrchestratorToken);
        Assert.Equal(SessionState.Stopped.ToString(), status.State);

        var retry = await ResolveAsync(client, created.SessionId, created.OrchestratorToken, "retry");
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(1, bootstrapper.RecordingCaptureInitializedCount);
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

    private static WebApplicationFactory<ApiHostProgram> CreateApiHostFactory(
        RuntimeSecurityConfig runtimeSecurity,
        ITranscriptionProvider? transcriptionProvider = null,
        IHostAudioBootstrapper? audioBootstrapper = null)
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
                services.RemoveAll<ITranscriptionProvider>();
                services.RemoveAll<IHostAudioBootstrapper>();
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
                if (transcriptionProvider is null)
                {
                    services.AddSingleton<ITranscriptionProvider, MockTranscriptionProvider>();
                }
                else
                {
                    services.AddSingleton(transcriptionProvider);
                }

                services.AddSingleton(
                    audioBootstrapper ?? new HostAudioBootstrapper());
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

    private sealed class CapturingTranscriptionProvider : ITranscriptionProvider
    {
        public AudioDeviceSelection? CapturedSelection { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audioWav,
            string correlationId,
            TranscriptionOptions? options = null,
            AudioDeviceSelection? audioDevices = null,
            CancellationToken cancellationToken = default)
        {
            CapturedSelection = audioDevices;
            var result = new TranscriptionResult(
                "mock transcript text",
                "mock-provider",
                TimeSpan.Zero,
                true,
                null,
                null,
                null);
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingAudioBytesTranscriptionProvider : ITranscriptionProvider
    {
        public AudioDeviceSelection? CapturedSelection { get; private set; }
        public byte[]? CapturedAudio { get; private set; }
        public int TranscribeInvocationCount { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audioWav,
            string correlationId,
            TranscriptionOptions? options = null,
            AudioDeviceSelection? audioDevices = null,
            CancellationToken cancellationToken = default)
        {
            TranscribeInvocationCount += 1;
            using var copy = new MemoryStream();
            audioWav.CopyTo(copy);

            CapturedSelection = audioDevices;
            CapturedAudio = copy.ToArray();
            return Task.FromResult(
                new TranscriptionResult(
                    "mock transcript text",
                    "mock-provider",
                    TimeSpan.Zero,
                    true,
                    null,
                    null,
                    null));
        }
    }

    private sealed class TrackingAudioBootstrapper : IHostAudioBootstrapper
    {
        public AudioDeviceSelection? InitializedAudioDevices { get; private set; }
        public bool RecordingCaptureInitialized { get; private set; }
        public bool PlaybackInitialized { get; private set; }
        public bool ConfirmationTonePlayed { get; private set; }

        public Task<IHostAudioCaptureSession?> InitializeRecordingCaptureAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;
            InitializedAudioDevices = audioDevices;
            RecordingCaptureInitialized = true;

            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }

        public Task InitializePlaybackAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;
            InitializedAudioDevices = audioDevices;
            PlaybackInitialized = true;
            return Task.CompletedTask;
        }

        public Task PlayConfirmationToneAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = audioDevices;
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;

            ConfirmationTonePlayed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudioBootstrapper : IHostAudioBootstrapper
    {
        public bool RecordingCaptureStarted { get; private set; }
        public bool RecordingCaptureInitialized { get; private set; }
        public int RecordingCaptureInitializedCount { get; private set; }
        public bool PlaybackInitialized { get; private set; }
        public int PlaybackInitializedCount { get; private set; }
        public bool ConfirmationTonePlayed { get; private set; }
        public int ConfirmationTonePlayedCount { get; private set; }
        private readonly bool _captureEnabled;
        private readonly byte[]? _capturedAudioPayload;

        public FakeAudioBootstrapper(byte[]? capturedAudioPayload = null, bool captureEnabled = true)
        {
            _captureEnabled = captureEnabled;
            _capturedAudioPayload = capturedAudioPayload is null && captureEnabled ? [1, 2, 3, 4] : capturedAudioPayload;
        }

        public byte[]? CapturedAudioPayload => _capturedAudioPayload;

        public Task<IHostAudioCaptureSession?> InitializeRecordingCaptureAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = audioDevices;
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;

            RecordingCaptureInitialized = true;
            RecordingCaptureInitializedCount++;
            if (!_captureEnabled)
            {
                return Task.FromResult<IHostAudioCaptureSession?>(null);
            }

            if (_capturedAudioPayload is null || _capturedAudioPayload.Length == 0)
            {
                return Task.FromResult<IHostAudioCaptureSession?>(null);
            }

            RecordingCaptureStarted = true;
            return Task.FromResult<IHostAudioCaptureSession?>(new FakeAudioCaptureSession(_capturedAudioPayload));
        }

        public Task InitializePlaybackAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = audioDevices;
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;

            PlaybackInitialized = true;
            PlaybackInitializedCount++;
            return Task.CompletedTask;
        }

        public Task PlayConfirmationToneAsync(
            AudioDeviceSelection? audioDevices,
            string sessionId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            _ = audioDevices;
            _ = sessionId;
            _ = correlationId;
            _ = cancellationToken;

            ConfirmationTonePlayedCount++;
            ConfirmationTonePlayed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAudioCaptureSession : IHostAudioCaptureSession
    {
        private readonly byte[] _payload;

        public FakeAudioCaptureSession(byte[] payload)
        {
            _payload = payload;
        }

        public Task<Stream> GetAudioStreamAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<Stream>(new MemoryStream(_payload, false));
        }

        public void Dispose()
        {
        }
    }
}
