using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VoiceType2.Core.Contracts;
using System.Net.Http;
using Xunit;
using VoiceType2.App.Cli;

namespace VoiceType2.Alpha1.Tests;

public sealed class CliStartupAndInteractionTests
{
    private const string UnreachableApiUrl = "http://127.0.0.1:59999";

    [Fact]
    public async Task RunAsync_returns_error_when_api_is_not_reachable_in_attach_mode()
    {
        var method = CliProgramTestHelpers.RunAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("dictate");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "attach", false, managedApiConfig, 250, 1000, null, null };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_managed_start_is_disabled()
    {
        var method = CliProgramTestHelpers.RunAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("command");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "managed", false, managedApiConfig, 250, 1000, null, null };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task TuiAsync_returns_error_when_managed_start_is_disabled()
    {
        var method = CliProgramTestHelpers.TuiAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("dictate");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "managed", false, managedApiConfig, 250, 1000, null, null };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("submit", "submit")]
    [InlineData("s", "submit")]
    [InlineData("cancel", "cancel")]
    [InlineData("c", "cancel")]
    [InlineData("retry", "retry")]
    [InlineData("r", "retry")]
    public void TryNormalizeAction_supports_known_inputs(string input, string expected)
    {
        var normalizeResult = CliProgramTestHelpers.TryNormalizeAction(input, out var normalized);

        Assert.True(normalizeResult);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalizeAction_rejects_unknown_input()
    {
        var normalizeResult = CliProgramTestHelpers.TryNormalizeAction("nonsense", out var normalized);

        Assert.False(normalizeResult);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public async Task ResolveAsync_returns_error_when_action_is_missing_or_unsupported()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "ResolveAsync",
            parameterCount: 4,
            returnType: typeof(Task<int>));

        Assert.Equal(
            1,
            await (Task<int>)method.Invoke(null, [UnreachableApiUrl, "session-1", Array.Empty<string>(), (string?)null])!);

        Assert.Equal(
            1,
            await (Task<int>)method.Invoke(null, [UnreachableApiUrl, "session-1", new[] { "bad-action" }, (string?)null])!);
    }

    [Fact]
    public void PrintRunMenu_prints_all_repl_commands()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "PrintRunMenu",
            parameterCount: 0);

        var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            method.Invoke(null, null);
            var output = writer.ToString();

            Assert.Contains("submit", output);
            Assert.Contains("cancel", output);
            Assert.Contains("retry", output);
            Assert.Contains("status", output);
            Assert.Contains("quit", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task HandleRunDeviceCommands_prints_device_inventory_for_devices_command()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "HandleRunDeviceCommandsAsync",
            parameterCount: 5,
            returnType: typeof(Task<bool>));
        var selections = CliProgramTestHelpers.CreateAudioDeviceSelectionState();
        var observedPaths = new List<string>();

        using var handler = new StubHttpMessageHandler((request, ct) =>
        {
            observedPaths.Add(request.RequestUri!.AbsolutePath);

            if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/v1/devices")
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                new HostDevicesResponse
                {
                    RecordingDevices =
                    [
                        new HostAudioDevice { DeviceId = "rec:0", Name = "Mic One" },
                        new HostAudioDevice { DeviceId = "rec:1", Name = "Mic Two" }
                    ],
                    PlaybackDevices =
                    [
                        new HostAudioDevice { DeviceId = "play:0", Name = "Speaker One" }
                    ]
                }));
        });

        var writer = new StringWriter();
        var originalOut = Console.Out;
        var result = false;

        try
        {
            Console.SetOut(writer);
            var client = new ApiSessionClient(
                "http://127.0.0.1:5240",
                client: new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

            result = await (Task<bool>)method.Invoke(
                null,
                ["devices", null, selections, "sess-1", client])!;
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString();

        Assert.True(result);
        Assert.Contains("Recording devices:", output);
        Assert.Contains("rec:0", output);
        Assert.Contains("Mic Two", output);
        Assert.Contains("Playback devices:", output);
        Assert.Contains("play:0", output);
        Assert.Contains("Speaker One", output);
        Assert.Contains("/v1/devices", string.Join(",", observedPaths));
    }

    [Fact]
    public async Task HandleRunDeviceCommands_accepts_run_mode_recording_device_command()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "HandleRunDeviceCommandsAsync",
            parameterCount: 5,
            returnType: typeof(Task<bool>));
        var selections = CliProgramTestHelpers.CreateAudioDeviceSelectionState();
        string? postedBody = null;

        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri is null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/v1/devices")
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    new HostDevicesResponse
                    {
                        RecordingDevices = [new HostAudioDevice { DeviceId = "rec:0", Name = "Mic One" }],
                        PlaybackDevices = [new HostAudioDevice { DeviceId = "play:0", Name = "Speaker One" }]
                    });
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri.AbsolutePath == "/v1/sessions/sess-1/devices")
            {
                postedBody = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            client: new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var accepted = await (Task<bool>)method.Invoke(
            null,
            ["recording-device", "1", selections, "sess-1", client])!;

        Assert.True(accepted);
        Assert.Equal("rec:0", CliProgramTestHelpers.GetAudioDevice(selections, "RecordingDeviceId"));
        Assert.Null(CliProgramTestHelpers.GetAudioDevice(selections, "PlaybackDeviceId"));
        Assert.NotNull(postedBody);
        Assert.Contains("\"recordingDeviceId\":\"rec:0\"", postedBody);
    }

    [Fact]
    public async Task HandleRunDeviceCommands_accepts_run_mode_playback_device_command()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "HandleRunDeviceCommandsAsync",
            parameterCount: 5,
            returnType: typeof(Task<bool>));
        var selections = CliProgramTestHelpers.CreateAudioDeviceSelectionState();
        string? postedBody = null;

        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri is null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/v1/devices")
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    new HostDevicesResponse
                    {
                        RecordingDevices = [new HostAudioDevice { DeviceId = "rec:0", Name = "Mic One" }],
                        PlaybackDevices =
                        [
                            new HostAudioDevice { DeviceId = "play:0", Name = "Speaker One" },
                            new HostAudioDevice { DeviceId = "play:1", Name = "Headset" }
                        ]
                    });
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri.AbsolutePath == "/v1/sessions/sess-1/devices")
            {
                postedBody = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new ApiSessionClient(
            "http://127.0.0.1:5240",
            client: new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5240/") });

        var accepted = await (Task<bool>)method.Invoke(
            null,
            ["playback-device", "2", selections, "sess-1", client])!;

        Assert.True(accepted);
        Assert.Equal("play:1", CliProgramTestHelpers.GetAudioDevice(selections, "PlaybackDeviceId"));
        Assert.NotNull(postedBody);
        Assert.Contains("\"playbackDeviceId\":\"play:1\"", postedBody);
    }

    [Fact]
    public void PrintTuiMenu_prints_device_actions()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "PrintTuiMenu",
            parameterCount: 0);

        var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            method.Invoke(null, null);
            var output = writer.ToString();
            Assert.Contains("set recording device", output);
            Assert.Contains("set playback device", output);
            Assert.Contains("show devices", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object payload)
    {
        return new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(payload, options: JsonDefaults.Options)
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync
            = sendAsync;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }

    private static class JsonDefaults
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
