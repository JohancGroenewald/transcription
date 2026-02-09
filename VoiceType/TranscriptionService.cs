using OpenAI.Audio;

namespace VoiceType;

public class TranscriptionService
{
    private readonly AudioClient _client;

    public TranscriptionService(string apiKey, string model)
    {
        _client = new AudioClient(model, apiKey);
    }

    public async Task<string> TranscribeAsync(byte[] wavAudio)
    {
        using var stream = new MemoryStream(wavAudio);

        var result = await _client.TranscribeAudioAsync(
            stream,
            "recording.wav",
            new AudioTranscriptionOptions
            {
                Language = "en",
                ResponseFormat = AudioTranscriptionFormat.Text
            });

        return result.Value.Text?.Trim() ?? string.Empty;
    }
}
