$ErrorActionPreference = "Stop"

function Write-Info([string]$message) {
    Write-Host "[pre-commit] $message"
}

try {
    if ($env:SKIP_VERSION_BUMP -eq "1") {
        Write-Info "Skipping version bump because SKIP_VERSION_BUMP=1."
        exit 0
    }

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $projectPath = Join-Path $repoRoot "VoiceType/VoiceType.csproj"

    function Has-AppFileChanges([string]$Root) {
        $gitDir = Join-Path $Root ".git"
        if (-not (Test-Path $gitDir)) {
            return $false
        }

        try {
            $changedFiles = & git -C $Root diff --cached --name-only --diff-filter=ACMRD -- 2>$null
            return ($changedFiles | Where-Object { $_ -like "VoiceType/*" } | Select-Object -First 1) -ne $null
        }
        catch {
            return $false
        }
    }

    if (-not (Has-AppFileChanges $repoRoot)) {
        Write-Info "No staged app file changes detected under VoiceType/. Skipping version bump."
        exit 0
    }

    if (-not (Test-Path $projectPath)) {
        Write-Info "Project file not found at $projectPath. Skipping."
        exit 0
    }

    $content = Get-Content -Raw -Path $projectPath
    $newline = if ($content -match "`r`n") { "`r`n" } else { "`n" }

    $currentVersion = $null
    $versionMatch = [Regex]::Match($content, "<Version>\s*(?<value>[^<]+)\s*</Version>")
    if ($versionMatch.Success) {
        $currentVersion = $versionMatch.Groups["value"].Value.Trim()
    }
    else {
        $currentVersion = "1.0.0"
        $insertedVersionLine = "    <Version>$currentVersion</Version>$newline"
        $content = [Regex]::Replace(
            $content,
            "</PropertyGroup>",
            "$insertedVersionLine  </PropertyGroup>",
            1)
    }

    $semverMatch = [Regex]::Match(
        $currentVersion,
        "^\s*(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.\d+)?\s*$")

    if ($semverMatch.Success) {
        $major = [int]$semverMatch.Groups["major"].Value
        $minor = [int]$semverMatch.Groups["minor"].Value
        $patch = [int]$semverMatch.Groups["patch"].Value
    }
    else {
        $major = 1
        $minor = 0
        $patch = 0
    }

    $newVersion = "{0}.{1}.{2}" -f $major, $minor, ($patch + 1)
    $updated = [Regex]::Replace(
        $content,
        "(<Version>\s*)([^<]+)(\s*</Version>)",
        {
            param($match)
            "$($match.Groups[1].Value)$newVersion$($match.Groups[3].Value)"
        },
        1)

    if ($updated -ne $content) {
        Set-Content -Path $projectPath -Value $updated -Encoding UTF8
        git add -- "VoiceType/VoiceType.csproj" | Out-Null
        Write-Info "Version bumped: $currentVersion -> $newVersion"
    }
    else {
        Write-Info "Version unchanged."
    }
}
catch {
    Write-Error "[pre-commit] Failed to bump version: $($_.Exception.Message)"
    exit 1
}

exit 0
