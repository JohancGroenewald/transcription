param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$ApiUrl = "",
    [switch]$NoBuild,
    [int]$ApiTimeoutMs = 0,
    [string]$ClientConfig = "",
    [string]$RecordingDeviceId = "",
    [string]$PlaybackDeviceId = ""
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

function Resolve-ApiUrl
{
    param([string]$ApiUrlOverride)

    if (-not [string]::IsNullOrWhiteSpace($ApiUrlOverride))
    {
        return $ApiUrlOverride.TrimEnd('/')
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
                    return ($urls -split ';')[0].Trim().TrimEnd('/')
                }
            }
        }
        else
        {
            if (-not [string]::IsNullOrWhiteSpace([string]$config.ApiUrl))
            {
                return [string]$config.ApiUrl.TrimEnd('/')
            }
        }
    }

    return ""
}

$root = Join-Path $PSScriptRoot ".."
$apiProject = "src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj"
$cliProject = "src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj"

Set-Location -Path $root

if (-not $NoBuild)
{
    & (Join-Path $PSScriptRoot "build-alpha1.ps1") -Configuration $Configuration
}

$resolvedApiUrl = Resolve-ApiUrl -ApiUrlOverride $ApiUrl
if ([string]::IsNullOrWhiteSpace($resolvedApiUrl))
{
    throw "Unable to determine API URL. Provide -ApiUrl or add a RuntimeConfig*.json/ClientConfig*.json file in the project tree."
}

$apiArguments = @(
    "run",
    "--project",
    $apiProject,
    "--",
    "--mode",
    "service",
    "--urls",
    $resolvedApiUrl
)

$apiProcess = Start-Process -FilePath "dotnet" -ArgumentList $apiArguments -PassThru -WindowStyle Minimized

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
        Write-Error "API did not become ready within 20s."
        exit 1
    }

    $cliArguments = @(
        "run",
        "--project",
        $cliProject,
        "--",
        "run",
        "--mode",
        "managed",
        "--api-url",
        $resolvedApiUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($ClientConfig))
    {
        $cliArguments += "--client-config"
        $cliArguments += $ClientConfig
    }

    if ($ApiTimeoutMs -gt 0)
    {
        $cliArguments += "--api-timeout-ms"
        $cliArguments += $ApiTimeoutMs
    }

    if (-not [string]::IsNullOrWhiteSpace($RecordingDeviceId))
    {
        $cliArguments += "--recording-device-id"
        $cliArguments += $RecordingDeviceId
    }

    if (-not [string]::IsNullOrWhiteSpace($PlaybackDeviceId))
    {
        $cliArguments += "--playback-device-id"
        $cliArguments += $PlaybackDeviceId
    }

    dotnet @cliArguments
}
finally
{
    if ($apiProcess -and -not $apiProcess.HasExited)
    {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
}
