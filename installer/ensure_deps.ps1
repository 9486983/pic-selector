param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"

function Read-Config {
    $configPath = Join-Path $PSScriptRoot "deps.config.json"
    if (-not (Test-Path $configPath)) {
        throw "Missing deps.config.json at $configPath"
    }
    return Get-Content $configPath -Raw | ConvertFrom-Json
}

function Ensure-Directory([string]$path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
    }
}

function Download-File([string]$url, [string]$dest) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $dest
}

function Expand-Zip([string]$zipPath, [string]$destDir) {
    Ensure-Directory $destDir
    Expand-Archive -Path $zipPath -DestinationPath $destDir -Force
}

function Has-SystemDotNet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    return $null -ne $cmd
}

function Has-SystemPython {
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    return $null -ne $cmd
}

$config = Read-Config
$runtimeDir = Join-Path $InstallDir "runtime"
$dotnetDir = Join-Path $runtimeDir "dotnet"
$pythonDir = Join-Path $runtimeDir "python"
$venvDir = Join-Path $runtimeDir "venv"

Ensure-Directory $runtimeDir

# .NET runtime
if (-not (Has-SystemDotNet) -and -not (Test-Path (Join-Path $dotnetDir "dotnet.exe"))) {
    Write-Host "Installing .NET runtime into $dotnetDir"
    $zip = Join-Path $env:TEMP "dotnet-runtime.zip"
    Download-File $config.dotnet.runtimeZipUrl $zip
    Expand-Zip $zip $dotnetDir
    Remove-Item $zip -Force
}
else {
    Write-Host ".NET runtime already available, skipping."
}

# Python runtime
if (-not (Has-SystemPython) -and -not (Test-Path (Join-Path $pythonDir "python.exe"))) {
    Write-Host "Installing Python into $pythonDir"
    $zip = Join-Path $env:TEMP "python-embed.zip"
    Download-File $config.python.embedZipUrl $zip
    Expand-Zip $zip $pythonDir
    Remove-Item $zip -Force

    $getPip = Join-Path $pythonDir "get-pip.py"
    Download-File $config.python.getPipUrl $getPip
    & (Join-Path $pythonDir "python.exe") $getPip
}
else {
    Write-Host "Python already available, skipping download."
}

# Create venv and install requirements
if (-not (Test-Path (Join-Path $venvDir "Scripts\\python.exe"))) {
    $pythonExe = if (Test-Path (Join-Path $pythonDir "python.exe")) { Join-Path $pythonDir "python.exe" } else { "python" }
    & $pythonExe -m venv $venvDir
}

$pip = Join-Path $venvDir "Scripts\\pip.exe"
if (Test-Path $pip) {
    $requirements = Join-Path $InstallDir "python-ai-service\\requirements.txt"
    if (Test-Path $requirements) {
        & $pip install -r $requirements
    }
}

