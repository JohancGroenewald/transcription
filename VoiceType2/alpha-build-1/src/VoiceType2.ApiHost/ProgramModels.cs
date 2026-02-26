using System.Threading;
using System.Threading.Tasks;

public sealed partial class Program;

public sealed class ApiHostOptions
{
    private ApiHostOptions(string mode, string? urls, string? configPath, bool showHelp)
    {
        Mode = mode;
        Urls = urls;
        ConfigPath = configPath;
        ShowHelp = showHelp;
    }

    public string Mode { get; }
    public string? Urls { get; }
    public string? ConfigPath { get; }
    public bool ShowHelp { get; }

    public static ApiHostOptions Parse(string[] args)
    {
        var mode = "service";
        string? urls = null;
        string? configPath = null;
        var showHelp = false;

        var i = 0;
        while (i < args.Length)
        {
            var current = args[i];
            if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (current.Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    mode = args[i + 1];
                    i++;
                }
            }
            else if (current.Equals("--urls", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    urls = args[i + 1];
                    i++;
                }
            }
            else if (current.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    configPath = args[i + 1];
                    i++;
                }
            }

            i++;
        }

        return new ApiHostOptions(mode, urls, configPath, showHelp);
    }
}

internal sealed class SessionWorkItem
{
    public required CancellationTokenSource CancellationTokenSource { get; init; }
    public Task ProcessingTask { get; set; } = Task.CompletedTask;
}
