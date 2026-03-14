param(
    [string]$ApiUrl = "http://127.0.0.1:5250",
    [string]$Config = "../RuntimeConfig.sample.json"
)

$root = Join-Path $PSScriptRoot ".."
Set-Location -Path $root

dotnet run --project src/VoiceType2.Alpha2.ApiHost/VoiceType2.Alpha2.ApiHost.csproj -- --urls $ApiUrl --config $Config
