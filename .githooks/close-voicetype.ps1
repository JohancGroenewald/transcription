param(
    [int]$MaxWaitMilliseconds = 3000,
    [int]$RetryDelayMilliseconds = 250
)

$ErrorActionPreference = "Stop"

function Get-VoiceTypeProcesses($repoRoot)
{
    try
    {
        return @(Get-CimInstance Win32_Process -Filter "Name='VoiceType.exe'" |
            Where-Object {
                $_.ExecutablePath -and
                $_.ExecutablePath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -ExpandProperty ProcessId);
    }
    catch
    {
        return @(Get-Process -Name "VoiceType" -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Path -and $_.Path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -ExpandProperty Id);
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$pids = @(Get-VoiceTypeProcesses -repoRoot $repoRoot)
if ($pids.Count -eq 0)
{
    Write-Host "[close-voicetype] No running VoiceType process found for repo."
    exit 0
}

foreach ($voiceTypePid in $pids)
{
    try
    {
        $process = Get-Process -Id $voiceTypePid -ErrorAction Stop
        if (-not $process.Path)
        {
            continue;
        }

        Write-Host "[close-voicetype] Requesting close for PID $voiceTypePid"
        Start-Process -FilePath $process.Path -ArgumentList "--close" -WindowStyle Hidden | Out-Null
    }
    catch
    {
        # Process may have already exited; best-effort continue
    }
}

$deadline = [DateTime]::UtcNow.AddMilliseconds($MaxWaitMilliseconds)
while ([DateTime]::UtcNow -lt $deadline)
{
    $remaining = Get-VoiceTypeProcesses -repoRoot $repoRoot
    if ($remaining.Count -eq 0)
    {
        Write-Host "[close-voicetype] VoiceType closed gracefully."
        exit 0;
    }

    Start-Sleep -Milliseconds $RetryDelayMilliseconds
}

Write-Host "[close-voicetype] VoiceType did not close in time. It may still be starting up or unresponsive."
exit 0
