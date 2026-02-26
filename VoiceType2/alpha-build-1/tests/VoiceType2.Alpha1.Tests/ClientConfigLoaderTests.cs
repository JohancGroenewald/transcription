using System.Text.Json;
using VoiceType2.App.Cli;
using Xunit;

namespace VoiceType2.Alpha1.Tests;

public sealed class ClientConfigLoaderTests
{
    private static readonly object CurrentDirectoryLock = new();

    [Fact]
    public void Load_with_explicit_path_uses_provided_values()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vt2-client-config-explicit-{Guid.NewGuid():N}"));
        var configPath = Path.Combine(tempDir.FullName, "custom-client-config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            ApiUrl = "http://127.0.0.1:7000",
            Mode = "managed",
            SessionMode = "command",
            ManagedStart = false,
            ApiTimeoutMs = 12345,
            ShutdownTimeoutMs = 4321
        }));

        try
        {
            var config = ClientConfigLoader.Load(configPath);

            Assert.Equal("http://127.0.0.1:7000", config.ApiUrl);
            Assert.Equal("managed", config.Mode);
            Assert.Equal("command", config.SessionMode);
            Assert.False(config.ManagedStart);
            Assert.Equal(12345, config.ApiTimeoutMs);
            Assert.Equal(4321, config.ShutdownTimeoutMs);
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Load_without_path_uses_sample_and_creates_default_config()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vt2-client-config-default-{Guid.NewGuid():N}"));
        var samplePath = Path.Combine(tempDir.FullName, ClientConfigLoader.SampleConfigFile);
        var generatedPath = Path.Combine(tempDir.FullName, ClientConfigLoader.DefaultConfigFile);
        var originalDirectory = Directory.GetCurrentDirectory();

        File.WriteAllText(samplePath, JsonSerializer.Serialize(new
        {
            ApiUrl = "http://127.0.0.1:7010",
            Mode = "attach",
            SessionMode = "dictate",
            ManagedStart = true,
            ApiTimeoutMs = 11111,
            ShutdownTimeoutMs = 2222
        }));

        try
        {
            lock (CurrentDirectoryLock)
            {
                Directory.SetCurrentDirectory(tempDir.FullName);
                var config = ClientConfigLoader.Load(null);

                Assert.Equal("http://127.0.0.1:7010", config.ApiUrl);
                Assert.Equal("attach", config.Mode);
                Assert.Equal("dictate", config.SessionMode);
                Assert.True(config.ManagedStart);
                Assert.Equal(11111, config.ApiTimeoutMs);
                Assert.Equal(2222, config.ShutdownTimeoutMs);
                Assert.True(File.Exists(generatedPath));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Load_with_missing_explicit_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() => ClientConfigLoader.Load("does-not-exist-abc.json"));
    }

    [Fact]
    public void Load_rejects_missing_required_values()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vt2-client-config-invalid-{Guid.NewGuid():N}"));
        var configPath = Path.Combine(tempDir.FullName, "client-config-invalid.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            ApiUrl = string.Empty,
            Mode = "attach",
            SessionMode = "dictate",
            ManagedStart = true,
            ApiTimeoutMs = 11111,
            ShutdownTimeoutMs = 2222
        }));

        try
        {
            Assert.Throws<InvalidOperationException>(() => ClientConfigLoader.Load(configPath));
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
        }
    }
}
