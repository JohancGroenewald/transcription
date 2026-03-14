param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$root = Join-Path $PSScriptRoot ".."
Set-Location -Path $root

$projects = @(
    "src/VoiceType2.Alpha2.Core/VoiceType2.Alpha2.Core.csproj",
    "src/VoiceType2.Alpha2.Infrastructure/VoiceType2.Alpha2.Infrastructure.csproj",
    "src/VoiceType2.Alpha2.ApiHost/VoiceType2.Alpha2.ApiHost.csproj",
    "src/VoiceType2.Alpha2.App.Cli/VoiceType2.Alpha2.App.Cli.csproj"
)

Write-Host "Building VoiceType2 Alpha 2 ($Configuration)"
foreach ($project in $projects)
{
    dotnet build $project --configuration $Configuration
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}
