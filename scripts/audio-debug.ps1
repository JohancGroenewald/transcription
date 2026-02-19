param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Args
)

$project = Join-Path (Get-Location) "tools/audio-debug/AudioDebug.csproj"
if (-not (Test-Path $project))
{
    throw "Could not locate tools/audio-debug/AudioDebug.csproj."
}

dotnet run --project $project -- @Args
