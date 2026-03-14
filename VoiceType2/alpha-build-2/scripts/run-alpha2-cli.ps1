param(
    [string]$ApiUrl = "http://127.0.0.1:5250",
    [string]$Mode = "attach",
    [string]$ApiConfig = "../RuntimeConfig.sample.json",
    [int]$ApiTimeoutMs = 15000,
    [int]$ShutdownTimeoutMs = 10000
)

$root = Join-Path $PSScriptRoot ".."
Set-Location -Path $root

dotnet run --project src/VoiceType2.Alpha2.App.Cli/VoiceType2.Alpha2.App.Cli.csproj -- run --api-url $ApiUrl --mode $Mode --api-config $ApiConfig --api-timeout-ms $ApiTimeoutMs --shutdown-timeout-ms $ShutdownTimeoutMs
