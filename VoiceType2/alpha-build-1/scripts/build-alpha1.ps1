param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Debug"
)

$projects = @(
    "src/VoiceType2.Core/VoiceType2.Core.csproj",
    "src/VoiceType2.Infrastructure/VoiceType2.Infrastructure.csproj",
    "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj",
    "src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj"
)

Write-Host "Building VoiceType2 Alpha 1 ($Configuration)"

Set-Location -Path (Join-Path $PSScriptRoot "..")
dotnet restore $projects
dotnet build $projects --no-restore --configuration $Configuration
