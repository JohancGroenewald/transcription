param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$ApiUrl = "",
    [string]$ClientConfig = "",
    [switch]$NoBuild
)

function Resolve-ConfigFilePath
{
    param([string]$FileName)

    $current = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $depth = 0

    while ($depth -lt 8 -and -not [string]::IsNullOrWhiteSpace($current))
    {
        $candidate = Join-Path $current $FileName
        if (Test-Path -Path $candidate -PathType Leaf)
        {
            return (Resolve-Path $candidate).Path
        }

        $parent = Split-Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current)
        {
            break
        }

        $current = $parent
        $depth++
    }

    return ""
}

function Resolve-ConfigValue
{
    param([string]$ApiUrlOverride)

    if (-not [string]::IsNullOrWhiteSpace($ApiUrlOverride))
    {
        return [pscustomobject]@{
            ApiUrl = $ApiUrlOverride.TrimEnd('/')
            SessionMode = "dictate"
        }
    }

    $configPaths = @(
        "RuntimeConfig.json",
        "RuntimeConfig.sample.json",
        "ClientConfig.json",
        "ClientConfig.sample.json"
    )

    foreach ($configPathName in $configPaths)
    {
        $configPath = Resolve-ConfigFilePath -FileName $configPathName
        if (-not (Test-Path -Path $configPath -PathType Leaf))
        {
            continue
        }

        try
        {
            $rawConfig = Get-Content -Raw $configPath
            $config = $rawConfig | ConvertFrom-Json -ErrorAction Stop
        }
        catch
        {
            continue
        }

        if ($configPathName.StartsWith("RuntimeConfig"))
        {
            $hostBinding = $config.HostBinding
            if ($null -ne $hostBinding -and $null -ne $hostBinding.Urls)
            {
                $urls = [string]$hostBinding.Urls
                if (-not [string]::IsNullOrWhiteSpace($urls))
                {
                    return [pscustomobject]@{
                        ApiUrl = ($urls -split ';')[0].Trim().TrimEnd('/')
                        SessionMode = "dictate"
                    }
                }
            }
        }
        else
        {
            $apiUrlFromClient = [string]$config.ApiUrl
            $sessionModeFromClient = [string]$config.SessionMode

            if (-not [string]::IsNullOrWhiteSpace($apiUrlFromClient) -and
                -not [string]::IsNullOrWhiteSpace($sessionModeFromClient))
            {
                return [pscustomobject]@{
                    ApiUrl = $apiUrlFromClient.TrimEnd('/')
                    SessionMode = $sessionModeFromClient
                }
            }
        }
    }

    return [pscustomobject]@{
        ApiUrl = ""
        SessionMode = "dictate"
    }
}

$ErrorActionPreference = "Stop"

$root = Join-Path $PSScriptRoot ".."
$apiProject = "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj"
$testProject = "tests/VoiceType2.Alpha1.Tests/VoiceType2.Alpha1.Tests.csproj"

Set-Location -Path $root

$resolvedConfig = Resolve-ConfigValue -ApiUrlOverride $ApiUrl
$resolvedApiUrl = $resolvedConfig.ApiUrl
$sessionMode = $resolvedConfig.SessionMode

if ([string]::IsNullOrWhiteSpace($ClientConfig))
{
    $ClientConfig = Resolve-ConfigFilePath -FileName "ClientConfig.json"
}

if (-not [string]::IsNullOrWhiteSpace($ClientConfig))
{
    try
    {
        $clientConfig = (Get-Content -Raw $ClientConfig | ConvertFrom-Json -ErrorAction Stop)
        if (-not [string]::IsNullOrWhiteSpace([string]$clientConfig.SessionMode))
        {
            $sessionMode = [string]$clientConfig.SessionMode
        }
    }
    catch
    {
    }
}

if ([string]::IsNullOrWhiteSpace($resolvedApiUrl))
{
    throw "Unable to determine API URL. Provide -ApiUrl or add a RuntimeConfig*.json/ClientConfig*.json file in the project tree."
}

if (-not $NoBuild)
{
    & (Join-Path $PSScriptRoot "build-alpha1.ps1") -Configuration $Configuration
}

if (-not [string]::IsNullOrWhiteSpace($ClientConfig))
{
    Write-Host "Using client config: $ClientConfig"
}

Write-Host "Running Alpha 1 unit tests..."
dotnet test $testProject --configuration $Configuration --no-restore

Write-Host "Starting API host for smoke verification..."
$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList @(
    "run", "--project", $apiProject, "--", "--mode", "service", "--urls", $resolvedApiUrl
) -PassThru -WindowStyle Minimized

try
{
    $deadline = (Get-Date).AddSeconds(20)
    do
    {
        try
        {
            $ready = Invoke-RestMethod -Uri ("$($resolvedApiUrl.TrimEnd('/'))/health/ready") -TimeoutSec 1
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
        sessionMode = $sessionMode
        correlationId = "alpha1-smoke-correlation"
        profile = $profile
    } | ConvertTo-Json -Depth 8

    $created = Invoke-RestMethod -Method Post -Uri "$($resolvedApiUrl.TrimEnd('/'))/v1/sessions" -Body $registerBody -ContentType "application/json"
    if (-not $created.sessionId -or -not $created.orchestratorToken)
    {
        throw "Session registration did not return expected payload."
    }

    $headers = @{ "x-orchestrator-token" = $created.orchestratorToken }

    Invoke-RestMethod -Method Post -Uri "$($resolvedApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)/start" -Headers $headers | Out-Null
    Start-Sleep -Milliseconds 250
    $startStatus = Invoke-RestMethod -Method Get -Uri "$($resolvedApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)" -Headers $headers
    if ($startStatus.state -notin @("Listening", "Running", "AwaitingDecision"))
    {
        throw "Unexpected state after start: $($startStatus.state)"
    }

    Invoke-RestMethod -Method Post -Uri "$($resolvedApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)/stop" -Headers $headers | Out-Null
    Start-Sleep -Milliseconds 100
    $finalStatus = Invoke-RestMethod -Method Get -Uri "$($resolvedApiUrl.TrimEnd('/'))/v1/sessions/$($created.sessionId)" -Headers $headers
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

