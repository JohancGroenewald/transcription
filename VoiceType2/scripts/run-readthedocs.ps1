param(
    [int]$Port = 8000,
    [switch]$ReinstallDeps,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$docsDir = Join-Path $root "docs"
$requirements = Join-Path $docsDir "requirements.txt"
$venvPath = Join-Path $root ".venv"
$venvPython = Join-Path $venvPath "Scripts\python.exe"

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Error "uv not found in PATH. Install uv (https://astral.sh/uv) and retry."
    exit 1
}

if (-not (Test-Path $venvPython)) {
    & uv venv --seed $venvPath
}

function Invoke-UvCommand {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArgs)
    & uv run --python $venvPython -- @CommandArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($ReinstallDeps) {
    & uv pip install --python $venvPython -r $requirements
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($Clean -and (Test-Path (Join-Path $docsDir "_build"))) {
    Remove-Item (Join-Path $docsDir "_build") -Recurse -Force
}

Push-Location $docsDir
try {
    Invoke-UvCommand sphinx-build -b html . .\_build\html

    Write-Host "Docs built successfully."
    Write-Host "Preview: http://localhost:$Port"
    Invoke-UvCommand python -m http.server $Port --directory .\_build\html
}
finally {
    Pop-Location
}
