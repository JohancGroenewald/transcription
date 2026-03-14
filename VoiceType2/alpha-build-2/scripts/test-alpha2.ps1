param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$root = Join-Path $PSScriptRoot ".."
Set-Location -Path $root

dotnet test tests/VoiceType2.Alpha2.Tests/VoiceType2.Alpha2.Tests.csproj --configuration $Configuration -p:UseSharedCompilation=false
