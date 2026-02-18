param(
    [switch]$InstallDotnet = $false,
    [switch]$RunTests = $false,
    [switch]$ConfigureHooks = $false,
    [switch]$FailOnWarning = $false,
    [string]$ReportPath = "$(Split-Path $PSScriptRoot -Parent)\install-report.json",
    [string]$SolutionRoot = ""
)

Set-StrictMode -Version Latest

$root = if ([string]::IsNullOrWhiteSpace($SolutionRoot)) { Split-Path -Parent $PSScriptRoot } else { $SolutionRoot }
$projectPath = Join-Path $root "VoiceType/VoiceType.csproj"
$testsProjectPath = Join-Path $root "VoiceType.Tests/VoiceType.Tests.csproj"
$start = Get-Date

$steps = @()
$success = $true
$actions = @()
$warningCount = 0
$failedStepCount = 0

function Add-Step {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Details
    )

    $script:steps += [ordered]@{
            step = $Name
            status = $Status
            details = $Details
        }
}

function Add-Failure {
    param(
        [string]$Name,
        [string]$Details,
        [bool]$Critical = $true
    )

    Add-Step -Name $Name -Status "failed" -Details $Details
    $script:failedStepCount++
    if ($Critical) {
        $script:success = $false
        $script:actions += "Fix the failure above and rerun: .\scripts\install-voiceType.ps1"
    } else {
        $script:warningCount++
        if ($script:FailOnWarning) {
            $script:success = $false
            $script:actions += "FAIL: Optional step failed: $Name. Rerun with -FailOnWarning removed (or fix warning)."
        } else {
            $script:actions += "Optional step skipped or partially failed: $Name"
        }
    }
}

function Run-CommandCapture {
    param(
        [string]$FileName = "dotnet",
        [string[]]$Arguments,
        [string]$WorkingDirectory = $null
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = [string]::Join(" ", $Arguments)
    $psi.WorkingDirectory = if ($WorkingDirectory) { $WorkingDirectory } else { $root }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi

    [void]$proc.Start()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        Output = ($stdout + $stderr).Trim()
    }
}

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-Net9 {
    $sdks = & dotnet --list-sdks 2>$null | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $hasNet9 = $sdks | Where-Object { $_ -match '^\s*9\.' } | Select-Object -First 1
    return @{
        HasNet9 = [bool]$hasNet9
        Versions = @($sdks)
        Selected = if ($hasNet9) { $hasNet9 } else { "" }
    }
}

function Install-Net9 {
    $installOutput = @()

    if (Test-Command winget) {
        $installOutput += "Using winget..."
        $result = Run-CommandCapture -FileName "winget" -Arguments @("install","--id","Microsoft.DotNet.SDK.9","--source","winget","--accept-source-agreements","--accept-package-agreements","--silent")
        if ($result.ExitCode -ne 0) {
            throw "winget install failed. Output: $($result.Output)"
        }
        return $installOutput + "winget install completed."
    }

    if (Test-Command choco) {
        $installOutput += "winget not found; using Chocolatey..."
        $result = Run-CommandCapture -FileName "choco" -Arguments @("install","dotnet-9-sdk","-y")
        if ($result.ExitCode -ne 0) {
            throw "chocolatey install failed. Output: $($result.Output)"
        }
        return $installOutput + "Chocolatey install completed."
    }

    throw "No package manager available. Open https://dotnet.microsoft.com/en-us/download/dotnet/9.0 and install the .NET 9 SDK, then rerun."
}

Write-Host "== VoiceType installer =="

if (Test-Command dotnet) {
    Add-Step -Name "dotnet SDK check" -Status "passed" -Details "dotnet command found."
} elseif ($InstallDotnet) {
    try {
        Add-Step -Name "dotnet command check" -Status "failed" -Details "dotnet command missing from PATH."
        $installMsg = Install-Net9
        Start-Sleep -Milliseconds 1000
        if (-not (Test-Command dotnet)) {
            Add-Failure -Name "dotnet SDK install" -Details "Installed attempted via: $($installMsg -join ', '), but dotnet is still not on PATH."
        } else {
            Add-Step -Name "dotnet SDK check" -Status "passed" -Details "dotnet command found after install."
        }
    } catch {
        Add-Failure -Name "dotnet SDK install" -Details $_.Exception.Message
    }
} else {
    Add-Failure -Name "dotnet SDK check" -Details "dotnet command not found on PATH. Re-run with -InstallDotnet."
}

if ($success) {
    $net9 = Test-Net9
    if (-not $net9.HasNet9) {
        if ($InstallDotnet) {
            try {
                $installMsg = Install-Net9
                Start-Sleep -Milliseconds 1000
                $net9 = Test-Net9
                if (-not $net9.HasNet9) {
                    Add-Failure -Name ".NET 9 SDK installation verification" -Details "Installation attempted but .NET 9 SDK was not detected after install."
                } else {
                    Add-Step -Name ".NET 9 SDK installation" -Status "passed" -Details "Installed: $($installMsg -join ' ')" 
                    Add-Step -Name "Detected .NET versions" -Status "passed" -Details (($net9.Versions -join "; "))
                }
            } catch {
                Add-Failure -Name ".NET 9 SDK installation" -Details $_.Exception.Message
            }
        } else {
            Add-Failure -Name ".NET 9 SDK verification" -Details "No .NET 9 SDK found. Re-run with -InstallDotnet to attempt installation."
        }
    } else {
        Add-Step -Name ".NET 9 SDK verification" -Status "passed" -Details "Detected .NET 9 SDK version: $($net9.Selected)"
        Add-Step -Name "Detected .NET versions" -Status "passed" -Details (($net9.Versions -join "; "))
    }
}

if ($success) {
    if (-not (Test-Path $projectPath)) {
        Add-Failure -Name "Project lookup" -Details "Missing project path: $projectPath"
    } else {
        Add-Step -Name "Project lookup" -Status "passed" -Details "Project found at $projectPath"
        try {
            $restore = Run-CommandCapture -Arguments @("restore", $projectPath)
            if ($restore.ExitCode -ne 0) {
                Add-Failure -Name "dotnet restore" -Details $restore.Output
            } else {
                Add-Step -Name "dotnet restore" -Status "passed" -Details $restore.Output
            }
        } catch {
            Add-Failure -Name "dotnet restore" -Details $_.Exception.Message
        }

        if ($success) {
            try {
                $build = Run-CommandCapture -Arguments @("build", $projectPath, "-c", "Debug")
                if ($build.ExitCode -ne 0) {
                    Add-Failure -Name "dotnet build" -Details $build.Output
                } else {
                    Add-Step -Name "dotnet build" -Status "passed" -Details $build.Output
                }
            } catch {
                Add-Failure -Name "dotnet build" -Details $_.Exception.Message
            }
        }

        if ($RunTests) {
            if (-not (Test-Path $testsProjectPath)) {
                Add-Failure -Name "Test project lookup" -Details "Missing tests project path: $testsProjectPath" -Critical $false
            } else {
                try {
                    $test = Run-CommandCapture -Arguments @("test", $testsProjectPath)
                    if ($test.ExitCode -ne 0) {
                        Add-Failure -Name "dotnet test" -Details $test.Output -Critical $false
                    } else {
                        Add-Step -Name "dotnet test" -Status "passed" -Details $test.Output
                    }
                } catch {
                    Add-Failure -Name "dotnet test" -Details $_.Exception.Message -Critical $false
                }
            }
        }
    }
}

if ($ConfigureHooks -and $success) {
    if (Test-Command git) {
        try {
            Push-Location $root
            $hookOutput = & git config core.hooksPath .githooks 2>&1
            if ($LASTEXITCODE -ne 0) {
                Add-Failure -Name "Configure Git hooks" -Details ($hookOutput -join " ") -Critical $false
            } else {
                Add-Step -Name "Configure Git hooks" -Status "passed" -Details "core.hooksPath set to .githooks"
            }
        } catch {
            Add-Failure -Name "Configure Git hooks" -Details $_.Exception.Message -Critical $false
        } finally {
            Pop-Location
        }
    } else {
        Add-Failure -Name "Configure Git hooks" -Details "git command not found. Install Git to configure core.hooksPath." -Critical $false
    }
}

if (-not $success) {
    $actions += "Run with -InstallDotnet to install .NET 9, then rerun this script."
}

if ($actions.Count -eq 0) {
    $actions += "Run: dotnet run --project $projectPath"
}

$elapsed = (Get-Date) - $start
$overallResult = if ($failedStepCount -eq 0) { "passed" } else { "failed" }
$report = @{
    startedUtc = $start.ToUniversalTime().ToString("o")
    finishedUtc = (Get-Date).ToUniversalTime().ToString("o")
    elapsedSeconds = [math]::Round($elapsed.TotalSeconds, 2)
    root = $root
    result = $overallResult
    failedStepCount = $failedStepCount
    optionalStepFailures = $warningCount
    steps = @($steps)
    recommendedNextActions = @($actions)
}

if (Test-Path (Split-Path $ReportPath -Parent)) {
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding UTF8
} else {
    $parent = Split-Path $ReportPath
    $parent = if ([string]::IsNullOrWhiteSpace($parent)) { $root } else { $parent }
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding UTF8
}

if ($failedStepCount -eq 0) {
    Write-Host "Installer completed successfully."
    if ($warningCount -gt 0) {
        Write-Host "Warnings: $warningCount optional check(s) did not pass."
        if (-not $FailOnWarning) {
            Write-Host "To fail CI on these, rerun with -FailOnWarning."
        }
    }
    Write-Host "Report: $ReportPath"
} else {
    Write-Host "Installer completed with failures."
    Write-Host "Report: $ReportPath"
    Write-Host "Review failed steps, correct issues, and rerun."
}

if (-not $success) {
    exit 1
}
exit 0
