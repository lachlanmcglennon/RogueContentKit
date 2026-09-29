# Freezes a commit into a standalone RogueLibs + RogueLibsPlus + RCK test build that later edits in this repo can't change.
# RogueLibs is Dzhake's unmodified v4.0.0-rc.3 release (SHA256-checked by tools\get-roguelibs.ps1); we don't build it.
#   powershell -File tools\freeze.ps1            build HEAD into ..\sor-ccu-frozen\<hash>\dist (next to this repository)
#   powershell -File tools\freeze.ps1 -Install   ...then install it (refuses while the game is running)
# The dist folder has its own install.ps1, so the build can be installed or removed later without this repo.
param(
    [string]$Ref = "HEAD",
    [string]$Out,
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path (Split-Path $repo -Parent) "sor-ccu-frozen" }
$hash = (git -C $repo rev-parse --short $Ref)
if ($LASTEXITCODE -ne 0 -or -not $hash) { throw "Unknown ref '$Ref'" }
$hash = $hash.Trim()
$dir = Join-Path $Out $hash
$dist = Join-Path $dir "dist"

# Only one freeze per commit at a time. A second run would delete the first run's files mid-build.
New-Item -ItemType Directory $Out -Force | Out-Null
$lockPath = Join-Path $Out "$hash.lock"
try { $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
catch { throw "Another freeze of $hash is already running (lock: $lockPath)" }
try {
if (Test-Path "$dist\FROZEN.txt") {
    Write-Host "Already frozen: $dist"
} else {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory $dir -Force | Out-Null
    $tarball = Join-Path $Out "$hash.tar"
    git -C $repo archive --format=tar -o $tarball $Ref
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
    # Expand-Archive takes minutes on this tree; bsdtar takes seconds.
    & "$env:SystemRoot\System32\tar.exe" -xf $tarball -C $dir
    if ($LASTEXITCODE -ne 0) { throw "tar extract failed" }
    Remove-Item $tarball
    # .ref holds the game's DLLs (gitignored); the build compiles against them.
    Copy-Item (Join-Path $repo ".ref") (Join-Path $dir ".ref") -Recurse

    # deploy -BuildOnly re-checks the SHA256 of Dzhake's rc.3 DLL in .ref\roguelibs and of the RogueLibsPatcher.Gen2.dll
    # extracted from it (downloading them if the copied .ref lacks them), then builds RogueLibsPlus and RCK.
    $rlDir = Join-Path $dir ".ref\roguelibs"
    & (Join-Path $dir "RCK\deploy.ps1") -BuildOnly

    # Checks RogueLibsCore, RogueLibsPlus and every RCK DLL.
    & powershell -NoProfile -File (Join-Path $dir "tools\verify-ccu.ps1") -SkipPrivateAccess
    if ($LASTEXITCODE -ne 0) { throw "RogueLibs, RogueLibsPlus or RCK has patch errors" }
    # Code compiled against the publicized .ref can still touch private members, which Mono rejects at runtime.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir "tools\check-private-access.ps1")
    if ($LASTEXITCODE -ne 0) { throw "private member access found (see above); the build would fail in game" }

    New-Item -ItemType Directory "$dist\BepInEx\patchers", "$dist\BepInEx\plugins\RCK", "$dist\BepInEx\plugins\RogueLibsPlus" -Force | Out-Null
    Copy-Item (Join-Path $rlDir "RogueLibsPatcher.Gen2.dll") "$dist\BepInEx\patchers"
    Copy-Item (Join-Path $rlDir "RogueLibsCore.dll") "$dist\BepInEx\plugins"
    Copy-Item (Join-Path $dir "licenses\RogueLibs.LICENSE.txt") "$dist\BepInEx\plugins\RogueLibsCore.LICENSE.txt"
    Copy-Item (Join-Path $dir "RogueLibsPlus\bin\Release\RogueLibsPlus.dll") "$dist\BepInEx\plugins\RogueLibsPlus"
    Copy-Item (Join-Path $dir "RogueLibsPlus\LICENSE") "$dist\BepInEx\plugins\RogueLibsPlus\LICENSE.txt"
    Copy-Item (Join-Path $dir "RCK\Core\bin\Release\RCK.dll") "$dist\BepInEx\plugins\RCK"
    Get-ChildItem (Join-Path $dir "RCK\Systems") -Recurse -Filter "RCK.*.dll" |
        Where-Object { $_.Directory.Name -eq "Release" -and $_.Directory.Parent.Name -eq "bin" -and $_.Directory.Parent.Parent.Name -ne "_Template" } |
        Copy-Item -Destination "$dist\BepInEx\plugins\RCK"

    $subject = (git -C $repo log -1 --format="%h %ad %s" --date=short $Ref)
    @(
        "Frozen RogueLibs rc.3 (Dzhake, unmodified) + RogueLibsPlus + RCK test build"
        "Commit: $subject"
        "Built:  $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
        ""
        "Files:"
    ) + (Get-ChildItem "$dist\BepInEx" -Recurse -File | ForEach-Object {
        "  BepInEx\" + $_.FullName.Substring("$dist\BepInEx\".Length) + "  " + (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 12)
    }) | Set-Content "$dist\FROZEN.txt" -Encoding UTF8
    Copy-Item (Join-Path $PSScriptRoot "frozen-install.ps1") "$dist\install.ps1"
    Write-Host "Frozen $hash -> $dist"
}
} finally {
    $lock.Dispose()
    Remove-Item $lockPath -ErrorAction SilentlyContinue
}

if ($Install) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$dist\install.ps1" }
