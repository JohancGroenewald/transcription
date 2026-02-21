param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$ApiUrl = "http://127.0.0.1:5240",
    [switch]$NoBuild
)

Set-Location -Path (Join-Path $PSScriptRoot "..")

$apiProject = "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj"
$testProject = "tests/VoiceType2.Alpha1.Tests/VoiceType2.Alpha1.Tests.csproj"

if (-not $NoBuild)
{
    & (Join-Path $PSScriptRoot "build-alpha1.ps1") -Configuration $Configuration
}

Write-Host "Running Alpha 1 unit tests..."
dotnet test $testProject --configuration $Configuration --no-restore

Write-Host "Starting API host for smoke verification..."
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
        throw "API host did not become ready in time."
    }

    Write-Host "Running runtime smoke checks..."
    $platform = "linux"
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows))
    {
        $platform = "windows"
    }

    $profile = @{
        orchestratorId = "alpha1-smoke-cli"
        platform = $platform
        capabilities = @{
            hotkeys = $false
            tray = $false
            clipboard = $true
            notifications = $false
            audioCapture = $false
            uiShell = $false
        }
    }

    $registerBody = @{
        sessionMode = "dictate"
        correlationId = "alpha1-smoke-correlation"
        profile = $profile
    } | ConvertTo-Json -Depth 8

    $created = Invoke-RestMethod -Method Post -Uri "$($ApiUrl.TrimEnd('/'))/v1/sessions" -Body $registerBody -ContentType "application/json"
    if (-not $created.sessionId -or -not $created.orchestratorToken)
    {
        throw "Session registration did not return expected payload."
    }

    $headers = @{ "x-orchestrator-token" = $created.orchestratorToken }

    Invoke-RestMethod -Method Post -Uri "$($ApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)/start" -Headers $headers | Out-Null
    Start-Sleep -Milliseconds 250
    $startStatus = Invoke-RestMethod -Method Get -Uri "$($ApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)" -Headers $headers
    if ($startStatus.state -notin @("Listening", "Running", "AwaitingDecision"))
    {
        throw "Unexpected state after start: $($startStatus.state)"
    }

    Invoke-RestMethod -Method Post -Uri "$($ApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)/stop" -Headers $headers | Out-Null
    Start-Sleep -Milliseconds 100
    $finalStatus = Invoke-RestMethod -Method Get -Uri "$($ApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)" -Headers $headers
    if ($finalStatus.state -ne "Stopped")
    {
        throw "Session stop did not transition to Stopped. Actual: $($finalStatus.state)"
    }

    Write-Host "Alpha 1 smoke checks passed."
}
finally
{
    if ($apiProcess -and -not $apiProcess.HasExited)
    {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
}
