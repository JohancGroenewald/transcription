param(
    [int]$Port = 8000,
    [switch]$ReinstallDeps,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$docsDir = Join-Path $root "docs"
$requirements = Join-Path $docsDir "requirements.txt"
$venvPython = Join-Path $root ".venv\Scripts\python.exe"

if (Test-Path $venvPython) {
    $python = $venvPython
} else {
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if (-not $pythonCommand) {
        Write-Error "Python not found in PATH. Install Python 3.11+ or create .venv in VoiceType2."
        exit 1
    }

    $python = $pythonCommand.Path
}

if ($ReinstallDeps) {
    & $python -m pip install -r $requirements
}

if ($Clean -and (Test-Path (Join-Path $docsDir "_build"))) {
    Remove-Item (Join-Path $docsDir "_build") -Recurse -Force
}

Push-Location $docsDir
try {
    & $python -m sphinx.cmd.build -b html $docsDir "$docsDir\_build\html"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "Docs built successfully."
    Write-Host "Preview: http://localhost:$Port"
    & $python -m http.server $Port --directory "$docsDir\_build\html"
}
finally {
    Pop-Location
}
