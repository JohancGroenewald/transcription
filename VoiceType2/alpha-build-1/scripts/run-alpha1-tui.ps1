param(
    [string]$ApiUrl = "",
    [string]$Mode = "",
    [string]$ApiConfig = "",
    [int]$ApiTimeoutMs = 0,
    [int]$ShutdownTimeoutMs = 0,
    [string]$ManagedStart = "",
    [string]$SessionMode = "",
    [string]$ClientConfig = "",
    [string]$RecordingDeviceId = "",
    [string]$PlaybackDeviceId = ""
)

Set-Location -Path (Join-Path $PSScriptRoot "..")

$arguments = @(
    "run",
    "--project",
    "src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj",
    "--",
    "tui"
)

if (-not [string]::IsNullOrWhiteSpace($ApiUrl))
{
    $arguments += "--api-url"
    $arguments += $ApiUrl
}

if (-not [string]::IsNullOrWhiteSpace($Mode))
{
    $arguments += "--mode"
    $arguments += $Mode
}

if (-not [string]::IsNullOrWhiteSpace($SessionMode))
{
    $arguments += "--session-mode"
    $arguments += $SessionMode
}

if (-not [string]::IsNullOrWhiteSpace($RecordingDeviceId))
{
    $arguments += "--recording-device-id"
    $arguments += $RecordingDeviceId
}

if (-not [string]::IsNullOrWhiteSpace($PlaybackDeviceId))
{
    $arguments += "--playback-device-id"
    $arguments += $PlaybackDeviceId
}

if (-not [string]::IsNullOrWhiteSpace($ClientConfig))
{
    $arguments += "--client-config"
    $arguments += $ClientConfig
}

if ($ApiTimeoutMs -gt 0)
{
    $arguments += "--api-timeout-ms"
    $arguments += $ApiTimeoutMs
}

if ($ShutdownTimeoutMs -gt 0)
{
    $arguments += "--shutdown-timeout-ms"
    $arguments += $ShutdownTimeoutMs
}

if (-not [string]::IsNullOrWhiteSpace($ManagedStart))
{
    $arguments += "--managed-start"
    $arguments += $ManagedStart.ToLower()
}

if (-not [string]::IsNullOrWhiteSpace($ApiConfig))
{
    $arguments += "--api-config"
    $arguments += $ApiConfig
}

dotnet @arguments
