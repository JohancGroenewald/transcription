using VoiceType2.Core.Contracts;

namespace VoiceType2.Infrastructure.Transcription;

public sealed class MockTranscriptionProvider : ITranscriptionProvider
{
    public Task<TranscriptionResult> TranscribeAsync(
        Stream audioWav,
        string correlationId,
        TranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
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
