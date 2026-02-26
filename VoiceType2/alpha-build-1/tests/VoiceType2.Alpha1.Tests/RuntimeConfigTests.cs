using System.Text.Json;
using VoiceType2.ApiHost;
using Xunit;

namespace VoiceType2.Alpha1.Tests;

public class RuntimeConfigTests
{
    [Fact]
    public void Load_reads_valid_file_and_applies_values()
    {
        var payload = JsonSerializer.Serialize(new
        {
            HostBinding = new
            {
                Urls = "http://127.0.0.1:7000",
                UseHttps = true
            },
            SessionPolicy = new
            {
                MaxConcurrentSessions = 2,
                DefaultSessionTimeoutMs = 5000,
                SessionIdleTimeoutMs = 1000
            },
            RuntimeSecurity = new
            {
                AuthMode = "token-optional",
                EnableCorrelationIds = true,
                StructuredErrorEnvelope = true
            },
            TranscriptionDefaults = new
            {
                Provider = "mock",
                DefaultLanguage = "en-GB",
                DefaultPrompt = "test",
                DefaultTimeoutMs = 900
            }
        });

        var filePath = Path.Combine(Path.GetTempPath(), $"vt2-runtime-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(filePath, payload);

        try
        {
            var config = RuntimeConfig.Load(filePath);
            Assert.Equal("http://127.0.0.1:7000", config.HostBinding.Urls);
            Assert.True(config.HostBinding.UseHttps);
            Assert.Equal(2, config.SessionPolicy.MaxConcurrentSessions);
            Assert.Equal(5000, config.SessionPolicy.DefaultSessionTimeoutMs);
            Assert.Equal(1000, config.SessionPolicy.SessionIdleTimeoutMs);
            Assert.Equal("mock", config.TranscriptionDefaults.Provider);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Load_with_empty_path_uses_defaults()
    {
        var config = RuntimeConfig.Load(null);
        Assert.Equal("http://127.0.0.1:5240", config.HostBinding.Urls);
    }

    [Fact]
    public void Load_overlays_on_sample_config_when_path_values_are_missing()
    {
        var payload = JsonSerializer.Serialize(new
        {
            HostBinding = new
            {
                Urls = "http://127.0.0.1:7000"
            },
            SessionPolicy = new
            {
                MaxConcurrentSessions = 2
            }
        });

        var filePath = Path.Combine(Path.GetTempPath(), $"vt2-runtime-config-overlay-{Guid.NewGuid():N}.json");
        File.WriteAllText(filePath, payload);

        try
        {
            var config = RuntimeConfig.Load(filePath);

            Assert.Equal("http://127.0.0.1:7000", config.HostBinding.Urls);
            Assert.Equal(2, config.SessionPolicy.MaxConcurrentSessions);
            Assert.Equal(300000, config.SessionPolicy.DefaultSessionTimeoutMs);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Load_throws_if_file_missing()
    {
        Assert.Throws<FileNotFoundException>(() => RuntimeConfig.Load("does-not-exist-abc.json"));
    }

    [Fact]
    public void Validate_rejects_non_positive_session_limit()
    {
        var config = new RuntimeConfig
        {
            HostBinding = new HostBindingConfig { Urls = "http://127.0.0.1:5240" },
            RuntimeSecurity = new RuntimeSecurityConfig { AuthMode = "none" },
            SessionPolicy = new SessionPolicyConfig { MaxConcurrentSessions = 0 }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("MaxConcurrentSessions", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_invalid_urls()
    {
        var config = new RuntimeConfig
        {
            HostBinding = new HostBindingConfig { Urls = "not-a-url" },
            RuntimeSecurity = new RuntimeSecurityConfig { AuthMode = "none" },
            SessionPolicy = new SessionPolicyConfig { MaxConcurrentSessions = 1 }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Invalid host URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_unsupported_auth_mode()
    {
        var config = new RuntimeConfig
        {
            RuntimeSecurity = new RuntimeSecurityConfig
            {
                AuthMode = "unsupported"
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Unsupported RuntimeSecurity.AuthMode", ex.Message, StringComparison.Ordinal);
    }
}
