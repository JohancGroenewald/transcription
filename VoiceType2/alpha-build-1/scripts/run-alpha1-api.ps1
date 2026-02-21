param(
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [string]$Mode = "service",
    [string]$Config = "RuntimeConfig.sample.json"
)

Set-Location -Path (Join-Path $PSScriptRoot "..")
dotnet run --project src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj -- --mode $Mode --urls $ApiUrl --config $Config
