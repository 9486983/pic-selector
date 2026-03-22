param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $root "dist\msi"
$stagingRoot = Join-Path $root ("dist\msi-staging\" + [Guid]::NewGuid().ToString("N"))
$payload = Join-Path $stagingRoot "payload"
$setupProject = Join-Path $root "src\PhotoSelector.Setup\PhotoSelector.Setup.wixproj"

New-Item -ItemType Directory -Path $payload -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Write-Host "Publishing main app (self-contained)..."
dotnet publish "$root\src\PhotoSelector.App\PhotoSelector.App.csproj" -c $Configuration -r win-x64 --self-contained true -o $payload
if ($LASTEXITCODE -ne 0) { throw "Failed to publish app." }

Write-Host "Copying python-ai-service..."
Copy-Item "$root\python-ai-service" -Destination (Join-Path $payload "python-ai-service") -Recurse

Write-Host "Copying dependency scripts..."
New-Item -ItemType Directory -Path (Join-Path $payload "deps") | Out-Null
Copy-Item "$root\installer\ensure_deps.ps1" -Destination (Join-Path $payload "deps\ensure_deps.ps1")
Copy-Item "$root\installer\deps.config.json" -Destination (Join-Path $payload "deps\deps.config.json")
Copy-Item "$root\installer\launch.cmd" -Destination (Join-Path $payload "launch.cmd")

Write-Host "Building MSI package..."
dotnet build $setupProject -c $Configuration /p:PayloadRoot="$payload"
if ($LASTEXITCODE -ne 0) { throw "Failed to build MSI project." }

$msi = Get-ChildItem -Path (Join-Path $root "src\PhotoSelector.Setup\bin\$Configuration") -Recurse -Filter *.msi |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $msi) {
    throw "MSI file was not generated."
}

$finalMsi = Join-Path $distRoot $msi.Name
Copy-Item $msi.FullName $finalMsi -Force
Write-Host "MSI ready: $finalMsi"
