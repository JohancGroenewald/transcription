using OpenAI.Audio;

namespace VoiceType;

public class TranscriptionService
{
    private readonly AudioClient _client;
    private readonly bool _enablePrompt;
    private readonly string? _prompt;

    public TranscriptionService(string apiKey, string model, bool enablePrompt = true, string? prompt = null)
    {
        _client = new AudioClient(model, apiKey);
        _enablePrompt = enablePrompt;
        _prompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
    }

    public async Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(wavAudio);

        var result = await _client.TranscribeAudioAsync(
            stream,
            "recording.wav",
            new AudioTranscriptionOptions
            {
                Language = "en",
                Prompt = _enablePrompt ? _prompt : null,
                ResponseFormat = AudioTranscriptionFormat.Text
            },
            cancellationToken);

        return result.Value.Text?.Trim() ?? string.Empty;
    }
}
