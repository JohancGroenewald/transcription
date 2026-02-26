param(
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [string]$Mode = "service",
    [string]$Config = ""
)

Set-Location -Path (Join-Path $PSScriptRoot "..")

$arguments = @(
    "run",
    "--project",
    "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj",
    "--",
    "--mode",
    $Mode,
    "--urls",
    $ApiUrl
)

if (-not [string]::IsNullOrWhiteSpace($Config))
{
    $arguments += "--config"
    $arguments += $Config
}

dotnet @arguments
