# Builds RogueLibsPlus, RCK Core and every system module, then installs them with Dzhake's unmodified RogueLibs
# v4.0.0-rc.3 (from ..\.ref\roguelibs, fetched and SHA256-checked by tools\get-roguelibs.ps1):
#   patchers\RogueLibsPatcher.Gen2.dll, plugins\RogueLibsCore.dll, plugins\RogueLibsPlus, plugins\RCK.
# It removes the pre-rename BepInEx\plugins\CCU folder, which would stop RCK loading, and carries its config over, and
# removes patchers\RogueLibsPatcher.dll from when we shipped our own RogueLibs fork.
# Refuses to install while the game is running (never kills the game).
param(
    [string]$Game = "C:\Program Files (x86)\Steam\steamapps\common\Streets of Rogue",
    [switch]$NoBuild,
    [switch]$BuildOnly,
    # Which commit the build is (build\BuildInfo.targets). freeze.ps1 and package-release.ps1 pass it, because their git
    # archives have no .git; otherwise it comes from this repository's working tree, or is "unknown" without one.
    [string]$BuildMetadata
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$failed = @()
$repo = Split-Path $root -Parent
$rl = Join-Path $repo ".ref\roguelibs"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "tools\get-roguelibs.ps1") -Dest $rl
if ($LASTEXITCODE -ne 0) { throw "could not get RogueLibs rc.3" }
if (-not $NoBuild) {
    if (-not $BuildMetadata) {
        . (Join-Path $repo "tools\build-id.ps1")
        if (Test-OwnRepository $repo) { $BuildMetadata = (Get-BuildId -Repo $repo -WorkingTree).Metadata } else { $BuildMetadata = "unknown" }
    }
    Write-Host "Build metadata: $BuildMetadata"
    $props = @("-p:RckBuildMetadata=$BuildMetadata")
    dotnet build (Join-Path $repo "RogueLibsPlus\RogueLibsPlus.csproj") -c Release -v q -nologo @props | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "RogueLibsPlus build failed" }
    dotnet build "$root\Core\RCK.csproj" -c Release -v q -nologo @props | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Core build failed" }
    foreach ($proj in Get-ChildItem "$root\Systems" -Recurse -Filter "RCK.*.csproj" | Where-Object { $_.Directory.Name -ne "_Template" }) {
        dotnet build $proj.FullName -c Release -v q -nologo @props | Out-Host
        if ($LASTEXITCODE -ne 0) { $failed += $proj.BaseName }
    }
}
if ($failed) { Write-Warning ("Modules that failed to build (skipped): " + ($failed -join ", ")) }
# A build-only run is a check, so a module that fails fails it (an older DLL in bin\ would otherwise pass for the new one).
if ($BuildOnly) {
    if ($failed) { throw "modules failed to build: $($failed -join ', ')" }
    return
}

if (Get-Process StreetsOfRogue -ErrorAction SilentlyContinue) { throw "Streets of Rogue is running; close it first." }
$bep = Join-Path $Game "BepInEx"
# Builds from before the rename used the original CCU's plugin GUID, so BepInEx would refuse to load RCK beside them.
# patchers\RogueLibsPatcher.dll is our old fork's copy of the patcher rc.3 ships as RogueLibsPatcher.Gen2.dll.
foreach ($rel in "plugins\CCU", "patchers\RogueLibsPatcher.dll") {
    $old = Join-Path $bep $rel
    if (Test-Path $old) { Remove-Item $old -Recurse -Force; Write-Host "removed old $old" }
}
Copy-Item (Join-Path $rl "RogueLibsPatcher.Gen2.dll") (Join-Path $bep "patchers") -Force
Copy-Item (Join-Path $rl "RogueLibsCore.dll") (Join-Path $bep "plugins") -Force
Copy-Item (Join-Path $repo "licenses\RogueLibs.LICENSE.txt") (Join-Path $bep "plugins\RogueLibsCore.LICENSE.txt") -Force
Write-Host "installed RogueLibs rc.3 (RogueLibsCore.dll, patchers\RogueLibsPatcher.Gen2.dll)"
$plus = Join-Path $bep "plugins\RogueLibsPlus"
New-Item -ItemType Directory $plus -Force | Out-Null
Copy-Item (Join-Path $repo "RogueLibsPlus\bin\Release\RogueLibsPlus.dll") $plus -Force
Copy-Item (Join-Path $repo "RogueLibsPlus\LICENSE") (Join-Path $plus "LICENSE.txt") -Force
Write-Host "installed RogueLibsPlus.dll"
$newCfg = Join-Path $bep "config\streetsofrogue.roguecontentkit.cfg"
$oldCfg = Get-ChildItem (Join-Path $bep "config") -Filter "*.streetsofrogue.CCU.cfg" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($oldCfg -and -not (Test-Path $newCfg)) { Copy-Item $oldCfg.FullName $newCfg; Write-Host "copied settings from $($oldCfg.Name)" }
$dest = Join-Path $bep "plugins\RCK"
New-Item -ItemType Directory $dest -Force | Out-Null
Get-ChildItem $dest -Filter "RCK*.dll" | Remove-Item
$files = @(Get-Item "$root\Core\bin\Release\RCK.dll")
$files += Get-ChildItem "$root\Systems" -Recurse -Filter "RCK.*.dll" |
    Where-Object { $_.Directory.Name -eq "Release" -and $_.Name -ne "RCK.Template.dll" -and $_.Directory.Parent.Parent.Name -ne "_Template" -and ($failed -notcontains $_.BaseName) }
foreach ($f in $files) { Copy-Item $f.FullName $dest; Write-Host "installed $($f.Name) ($($f.Length) B)" }
