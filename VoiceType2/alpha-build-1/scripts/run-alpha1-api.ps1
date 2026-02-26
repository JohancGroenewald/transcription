param(
    [string]$ApiUrl = "",
    [string]$Mode = "",
    [string]$Config = ""
)

Set-Location -Path (Join-Path $PSScriptRoot "..")

$arguments = @(
    "run",
    "--project",
    "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj",
    "--"
)

if (-not [string]::IsNullOrWhiteSpace($Mode))
{
    $arguments += "--mode"
    $arguments += $Mode
}

if (-not [string]::IsNullOrWhiteSpace($ApiUrl))
{
    $arguments += "--urls"
    $arguments += $ApiUrl
}

if (-not [string]::IsNullOrWhiteSpace($Config))
{
    $arguments += "--config"
    $arguments += $Config
}

dotnet @arguments
