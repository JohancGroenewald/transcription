param(
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [string]$Mode = "attach"
)

Set-Location -Path (Join-Path $PSScriptRoot "..")
dotnet run --project src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj -- run --api-url $ApiUrl --mode $Mode
