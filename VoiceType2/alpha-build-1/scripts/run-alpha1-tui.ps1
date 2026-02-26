param(
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [string]$Mode = "attach",
    [string]$ApiConfig = "",
    [int]$ApiTimeoutMs = 15000,
    [int]$ShutdownTimeoutMs = 10000,
    [switch]$ManagedStart = $true
)

Set-Location -Path (Join-Path $PSScriptRoot "..")
dotnet run --project src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj -- tui --api-url $ApiUrl --mode $Mode --api-timeout-ms $ApiTimeoutMs --shutdown-timeout-ms $ShutdownTimeoutMs --managed-start $($ManagedStart.ToString().ToLower()) --api-config $ApiConfig
