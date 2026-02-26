param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [switch]$NoBuild,
    [int]$ApiTimeoutMs = 15000
)

$root = Join-Path $PSScriptRoot ".."
$apiProject = "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj"
$cliProject = "src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj"

Set-Location -Path $root

if (-not $NoBuild)
{
    & (Join-Path $PSScriptRoot "build-alpha1.ps1") -Configuration $Configuration
}

$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList @(
    "run", "--project", $apiProject, "--", "--mode", "service", "--urls", $ApiUrl
) -PassThru -WindowStyle Minimized

try
{
    $deadline = (Get-Date).AddSeconds(20)
    do
    {
        try
        {
            $ready = Invoke-RestMethod -Uri ("$($ApiUrl.TrimEnd('/'))/health/ready") -TimeoutSec 1
            if ($ready.status -eq "ready")
            {
                break
            }
        }
        catch
        {
        }

        Start-Sleep -Milliseconds 250
    }
    while ((Get-Date) -lt $deadline)

    if ((Get-Date) -ge $deadline)
    {
        Write-Error "API did not become ready within 20s."
        exit 1
    }

    dotnet run --project $cliProject -- tui --api-url $ApiUrl --mode managed --api-timeout-ms $ApiTimeoutMs
}
finally
{
    if ($apiProcess -and -not $apiProcess.HasExited)
    {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
}
