# Installs the frozen RogueLibs + RCK build in this folder into Streets of Rogue. Copied into each frozen dist by
# tools\freeze.ps1. It refuses to run while the game is open.
#   install.ps1              back up the mod files it replaces and your saves, then install
#   install.ps1 -Uninstall   back up and remove RogueLibs and RCK from BepInEx (saves are left alone)
# Both also remove BepInEx\plugins\CCU, left by builds from before the RCK rename (it is backed up first).
param(
    [string]$Game = "C:\Program Files (x86)\Steam\steamapps\common\Streets of Rogue",
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"
if (Get-Process StreetsOfRogue -ErrorAction SilentlyContinue) { throw "Streets of Rogue is running; close it first." }
$bep = Join-Path $Game "BepInEx"
if (-not (Test-Path "$bep\core\BepInEx.dll")) { throw "BepInEx not found in $Game" }

$rlSha = "357B53E477A63FFE282AFA631B1D2BF13EEB4389B1D544FFF8117FE714259E1C"
if (-not $Uninstall -and (Get-FileHash "$PSScriptRoot\BepInEx\plugins\RogueLibsCore.dll" -Algorithm SHA256).Hash -ne $rlSha) {
    throw "BepInEx\plugins\RogueLibsCore.dll in this folder is not Dzhake's RogueLibs v4.0.0-rc.3 release"
}
$backup = Join-Path (Split-Path $PSScriptRoot -Parent) ("backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
# plugins\CCU is this mod before its rename. It used the original CCU's plugin GUID, so RCK won't load beside it.
# patchers\RogueLibsPatcher.dll is our old RogueLibs fork's preloader; rc.3 ships the same patcher as RogueLibsPatcher.Gen2.dll.
$ours = "patchers\RogueLibsPatcher.dll", "patchers\RogueLibsPatcher.Gen2.dll", "plugins\RogueLibsCore.dll",
    "plugins\RogueLibsCore.LICENSE.txt", "plugins\RogueLibsPlus", "plugins\RCK", "plugins\CCU"
foreach ($rel in $ours) {
    $src = Join-Path $bep $rel
    if (Test-Path $src) {
        $dst = Join-Path "$backup\BepInEx" $rel
        New-Item -ItemType Directory (Split-Path $dst -Parent) -Force | Out-Null
        Copy-Item $src $dst -Recurse -Force
    }
}

if ($Uninstall) {
    foreach ($rel in $ours) { Remove-Item (Join-Path $bep $rel) -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "Removed RogueLibs, RogueLibsPlus and RCK. The removed files are in $backup"
    return
}

# Saves, settings, characters and your own campaigns. Workshop downloads are skipped (Steam can re-download them).
$saves = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Streets of Rogue"
if (Test-Path $saves) {
    New-Item -ItemType Directory "$backup\saves" -Force | Out-Null
    Get-ChildItem $saves | Where-Object { $_.Name -notin "CampaignDownloads", "ChunkPackDownloads" } |
        Copy-Item -Destination "$backup\saves" -Recurse -Force
}

foreach ($rel in "plugins\CCU", "plugins\RCK", "plugins\RogueLibsPlus", "patchers\RogueLibsPatcher.dll") {
    Remove-Item (Join-Path $bep $rel) -Recurse -Force -ErrorAction SilentlyContinue
}
# Carry the settings over from the pre-rename config file.
$newCfg = Join-Path $bep "config\streetsofrogue.roguecontentkit.cfg"
$oldCfg = Get-ChildItem (Join-Path $bep "config") -Filter "*.streetsofrogue.CCU.cfg" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($oldCfg -and -not (Test-Path $newCfg)) { Copy-Item $oldCfg.FullName $newCfg }
Copy-Item "$PSScriptRoot\BepInEx\*" $bep -Recurse -Force
Copy-Item "$PSScriptRoot\FROZEN.txt" (Join-Path $bep "plugins\RCK\FROZEN.txt") -Force
Get-Content "$PSScriptRoot\FROZEN.txt" | Select-Object -First 2 | Write-Host
# A second RogueLibsCore.dll (e.g. from a mod bundle) makes BepInEx pick one copy by version, which may not be rc.3.
Get-ChildItem (Join-Path $bep "plugins") -Recurse -Filter "RogueLibsCore.dll" |
    Where-Object { $_.DirectoryName -ne (Join-Path $bep "plugins") } |
    ForEach-Object { Write-Warning "Another RogueLibsCore.dll is installed at $($_.FullName); remove it so only rc.3 loads." }
Write-Host "Installed. Backup of the previous mod files and your saves: $backup"
Write-Host "Logs: $bep\LogOutput.log and $env:USERPROFILE\AppData\LocalLow\Streets of Rogue\Streets of Rogue\Player.log"
