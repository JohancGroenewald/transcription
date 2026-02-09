$ErrorActionPreference = "Stop"

function Write-Info([string]$message) {
    Write-Host "[post-commit] $message"
}

try {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $projectPath = Join-Path $repoRoot "VoiceType/VoiceType.csproj"
    $debugExePath = Join-Path $repoRoot "VoiceType/bin/Debug/net9.0-windows/VoiceType.exe"
    $runningCandidates = @()

    try {
        $runningCandidates = @(Get-CimInstance Win32_Process -Filter "Name='VoiceType.exe'" |
            Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -ExpandProperty ExecutablePath -Unique)
    }
    catch {
        # Best effort discovery
    }

    foreach ($runningExe in $runningCandidates) {
        try {
            Start-Process -FilePath $runningExe -ArgumentList "--close" -WindowStyle Hidden
        }
        catch {
            # Best effort close
        }
    }
    Start-Sleep -Milliseconds 900

    if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path $projectPath)) {
        Write-Info "Building Debug app before restart..."
        & dotnet build $projectPath -c Debug | Out-Null
    }

    $preferredPaths = @(
        $debugExePath,
        (Join-Path $repoRoot "VoiceType/bin/Release/net9.0-windows/VoiceType.exe"),
        (Join-Path $repoRoot "VoiceType/bin/_verifyhost2/VoiceType.exe"),
        (Join-Path $repoRoot "VoiceType/bin/_verifyhost/VoiceType.exe")
    )
    $exePath = $preferredPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($exePath)) {
        Write-Info "VoiceType executable not found, skipping restart."
        exit 0
    }

    Write-Info "Restarting VoiceType from $exePath"
    Start-Process -FilePath $exePath -WindowStyle Hidden
    Write-Info "Restart requested."
}
catch {
    Write-Info "Restart failed: $($_.Exception.Message)"
}

exit 0
