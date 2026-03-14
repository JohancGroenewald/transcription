using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoiceType2.Alpha2.ApiHost;
using VoiceType2.Alpha2.Core;
using Xunit;

namespace VoiceType2.Alpha2.Tests;

public sealed class ApiHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHostTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Health_endpoints_are_available()
    {
        using var client = _factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();

        using var ready = await client.GetAsync("/health/ready");
        ready.EnsureSuccessStatusCode();

        var readyPayload = await JsonDocument.ParseAsync(await ready.Content.ReadAsStreamAsync());
        Assert.Equal("ready", readyPayload.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_start_stop_returns_transcript()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var start = await PostAuthorizedAsync(client, created, $"/v2/sessions/{created.SessionId}/start");
        start.EnsureSuccessStatusCode();

        await Task.Delay(50);

        using var stop = await PostAuthorizedAsync(client, created, $"/v2/sessions/{created.SessionId}/stop");
        stop.EnsureSuccessStatusCode();

        var status = await stop.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal(DictationSessionState.Completed.ToString(), status.State);
        Assert.Contains("mock transcript text", status.Transcript);
        Assert.NotNull(status.Audio);
        Assert.True(status.Audio.ByteLength > 0);
    }

    [Fact]
    public async Task Cancel_marks_session_cancelled()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var start = await PostAuthorizedAsync(client, created, $"/v2/sessions/{created.SessionId}/start");
        start.EnsureSuccessStatusCode();

        using var cancel = await PostAuthorizedAsync(client, created, $"/v2/sessions/{created.SessionId}/cancel");
        cancel.EnsureSuccessStatusCode();

        var status = await cancel.Content.ReadFromJsonAsync<SessionStatusResponse>();
        Assert.NotNull(status);
        Assert.Equal(DictationSessionState.Cancelled.ToString(), status.State);
    }

    [Fact]
    public async Task Status_requires_token_when_configured()
    {
        using var factory = CreateFactoryWithConfig(new RuntimeConfig
        {
            RuntimeSecurity = new RuntimeSecurityConfig { AuthMode = "token-required" }
        });
        using var client = factory.CreateClient();
        var created = await RegisterSessionAsync(client);

        using var unauthenticated = await client.GetAsync($"/v2/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, $"/v2/sessions/{created.SessionId}");
        authenticatedRequest.Headers.Add("x-orchestrator-token", created.OrchestratorToken);
        using var authenticated = await client.SendAsync(authenticatedRequest);
        authenticated.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Event_stream_returns_sse_payload()
    {
        using var client = _factory.CreateClient();
        var created = await RegisterSessionAsync(client);
        using var start = await PostAuthorizedAsync(client, created, $"/v2/sessions/{created.SessionId}/start");
        start.EnsureSuccessStatusCode();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/sessions/{created.SessionId}/events");
        request.Headers.Add("x-orchestrator-token", created.OrchestratorToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithConfig(RuntimeConfig config)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<RuntimeConfig>();
                services.AddSingleton(config);
            });
        });
    }

    private static async Task<SessionCreatedResponse> RegisterSessionAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/v2/sessions", new RegisterSessionRequest
        {
            Profile = new OrchestratorProfile
            {
                OrchestratorId = "alpha2-tests",
                Platform = "windows",
                Capabilities = new OrchestratorCapabilities(audioCapture: false)
            }
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<SessionCreatedResponse>();
        return created ?? throw new InvalidOperationException("No session created response returned.");
    }

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client,
        SessionCreatedResponse created,
        string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("x-orchestrator-token", created.OrchestratorToken);
        return await client.SendAsync(request);
    }
}
