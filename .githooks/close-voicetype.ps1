param(
    [int]$MaxWaitMilliseconds = 10000,
    [int]$RetryDelayMilliseconds = 250
)

$ErrorActionPreference = "Stop"
$CloseCompletedEventNamePrefix = "VoiceType_SingleInstance_CloseCompleted_"

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

$closeSignals = @()
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

        try
        {
            $closeSignal = [System.Threading.EventWaitHandle]::OpenExisting(
                "$CloseCompletedEventNamePrefix$voiceTypePid")
            $closeSignals += [PSCustomObject]@{
                Pid = $voiceTypePid
                Event = $closeSignal
                Signaled = $false
            }
            Write-Host "[close-voicetype] Will wait for close-completion signal for PID $voiceTypePid."
        }
        catch
        {
            # Older/mismatched app versions may not emit this event; we still fall back to process polling.
        }
    }
    catch
    {
        # Process may have already exited; best-effort continue.
    }
}

$deadline = [DateTime]::UtcNow.AddMilliseconds($MaxWaitMilliseconds)
while ([DateTime]::UtcNow -lt $deadline)
{
    $remaining = Get-VoiceTypeProcesses -repoRoot $repoRoot
    if ($remaining.Count -eq 0)
    {
        foreach ($closeSignal in $closeSignals)
        {
            if ($closeSignal.Event -ne $null)
            {
                $closeSignal.Event.Dispose();
            }
        }

        Write-Host "[close-voicetype] VoiceType closed."
        exit 0;
    }

    foreach ($closeSignal in @($closeSignals))
    {
        if (-not $closeSignal.Signaled -and $closeSignal.Event -ne $null)
        {
            try
            {
                if ($closeSignal.Event.WaitOne(0))
                {
                    $closeSignal.Signaled = $true;
                }
            }
            catch
            {
                $closeSignal.Signaled = $true;
            }
        }
    }

    Start-Sleep -Milliseconds $RetryDelayMilliseconds
}

$remaining = Get-VoiceTypeProcesses -repoRoot $repoRoot
foreach ($closeSignal in $closeSignals)
{
    if ($closeSignal.Event -ne $null)
    {
        $closeSignal.Event.Dispose();
    }
}

if ($remaining.Count -eq 0)
{
    Write-Host "[close-voicetype] VoiceType closed."
    exit 0;
}

Write-Host "[close-voicetype] Unable to close VoiceType in time. Remaining PIDs: $($remaining -join ', ')"
Write-Host "[close-voicetype] Build will not continue while VoiceType is still running to avoid file lock conflicts."
exit 1
