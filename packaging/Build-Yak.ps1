# Builds a local Windows Yak package for ARCHWALK (does not publish).
param(
    [string]$Configuration = "Debug",
    [string]$YakExe = "C:\Program Files\Rhino 825\System\Yak.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pluginProj = Join-Path $root "src\ArchWalk.Rhino\ArchWalk.Rhino.csproj"
$pluginOut = Join-Path $root "src\ArchWalk.Rhino\bin\plugin\net8.0-windows"
$stage = Join-Path $root "packaging\stage"
$dist = Join-Path $root "packaging\dist"

if (-not (Test-Path $YakExe)) {
    throw "Yak not found: $YakExe"
}

Write-Host "Building plugin ($Configuration)..."
dotnet build $pluginProj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$rhp = Join-Path $pluginOut "ArchWalk.rhp"
if (-not (Test-Path $rhp)) { throw "Missing $rhp" }

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Path $stage | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Copy-Item (Join-Path $pluginOut "ArchWalk.rhp") $stage
Copy-Item (Join-Path $pluginOut "ArchWalk.Core.dll") $stage
Copy-Item (Join-Path $pluginOut "ArchWalk.WindowsInput.dll") $stage
Copy-Item (Join-Path $PSScriptRoot "manifest.yml") $stage

$misc = Join-Path $stage "misc"
New-Item -ItemType Directory -Path $misc | Out-Null
Copy-Item (Join-Path $root "INSTALL.md") $misc
Copy-Item (Join-Path $root "CONTROLS.md") $misc
Copy-Item (Join-Path $root "RELEASE_NOTES.md") $misc

Push-Location $stage
try {
    Get-ChildItem *.yak -ErrorAction SilentlyContinue | Remove-Item -Force
    & $YakExe build --platform win
    if ($LASTEXITCODE -ne 0) { throw "yak build failed" }
    $yak = Get-ChildItem *.yak | Select-Object -First 1
    if ($null -eq $yak) { throw "No .yak produced in $stage" }
    $dest = Join-Path $dist $yak.Name
    Copy-Item $yak.FullName $dest -Force
    Write-Host "Package: $dest"
    Write-Host ("Size: {0:N0} bytes" -f $yak.Length)
}
finally {
    Pop-Location
}
