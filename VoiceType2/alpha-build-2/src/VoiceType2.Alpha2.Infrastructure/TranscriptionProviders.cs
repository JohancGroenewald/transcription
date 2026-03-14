using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;
using VoiceType2.Alpha2.Core;

namespace VoiceType2.Alpha2.Infrastructure;

public sealed class MockTranscriptionProvider(ILogger<MockTranscriptionProvider> logger) : ITranscriptionProvider
{
    private readonly ILogger<MockTranscriptionProvider> _logger = logger;

    public Task<TranscriptionResult> TranscribeAsync(
        AudioCaptureResult capture,
        TranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using mock transcription provider for session {CorrelationId}.",
            request.CorrelationId);

        var text =
            $"mock transcript text (bytes={capture.Summary.ByteLength}, duration={capture.Summary.Metrics.DurationSeconds:F2}s)";

        return Task.FromResult(new TranscriptionResult(
            text,
            "mock",
            0,
            true));
    }
}

public sealed class OpenAiTranscriptionProvider(
    string apiKeyEnvironmentVariable,
    ILogger<OpenAiTranscriptionProvider> logger) : ITranscriptionProvider
{
    private readonly string _apiKeyEnvironmentVariable = apiKeyEnvironmentVariable;
    private readonly ILogger<OpenAiTranscriptionProvider> _logger = logger;

    public async Task<TranscriptionResult> TranscribeAsync(
        AudioCaptureResult capture,
        TranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(_apiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new TranscriptionResult(
                string.Empty,
                "openai",
                0,
                false,
                "OPENAI_API_KEY_MISSING",
                $"Environment variable '{_apiKeyEnvironmentVariable}' is not set.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var client = new AudioClient(request.Model, apiKey);
            using var stream = new MemoryStream(capture.WavAudio);
            var result = await client.TranscribeAudioAsync(
                stream,
                "recording.wav",
                new AudioTranscriptionOptions
                {
                    Language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language,
                    Prompt = request.EnablePrompt ? request.Prompt : null,
                    ResponseFormat = AudioTranscriptionFormat.Text
                },
                cancellationToken);

            stopwatch.Stop();
            var text = result.Value.Text?.Trim() ?? string.Empty;

            return new TranscriptionResult(
                text,
                $"openai:{request.Model}",
                stopwatch.Elapsed.TotalMilliseconds,
                true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "OpenAI transcription failed for session {CorrelationId}.", request.CorrelationId);

            return new TranscriptionResult(
                string.Empty,
                $"openai:{request.Model}",
                stopwatch.Elapsed.TotalMilliseconds,
                false,
                "OPENAI_TRANSCRIPTION_FAILED",
                ex.Message);
        }
    }
}
