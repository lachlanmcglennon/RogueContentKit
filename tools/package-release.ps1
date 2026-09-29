# Builds the player release: release\RCK-Pack-<version>.zip, which extracts straight into the Streets of Rogue folder.
#   powershell -File tools\package-release.ps1 [-Ref HEAD] [-Out <folder>]
# It builds from a clean `git archive` of the ref (uncommitted changes are not included), like tools\freeze.ps1, and
# packs the unmodified official BepInEx 5.4.23.5 files, Dzhake's unmodified RogueLibs v4.0.0-rc.3 (tools\get-roguelibs.ps1),
# RogueLibsPlus, RCK, RCK-README.txt and RCK-licenses\. Every file must pass an allow-list, and game or reference DLLs
# fail the build outright.
# PDBs are left out: Release builds of RCK and RogueLibsPlus make none (DebugType none), and BepInEx's log already names
# the method in every stack trace, which is what bug reports need.
param(
    [string]$Ref = "HEAD",
    [string]$Out
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path $repo "release" }

$bepVersion = "5.4.23.5"
$bepZipName = "BepInEx_win_x64_$bepVersion.zip"
$bepUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$bepVersion/$bepZipName"
$bepSha = "82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4"
$roguelibsCoreSha = "357B53E477A63FFE282AFA631B1D2BF13EEB4389B1D544FFF8117FE714259E1C"
$roguelibsPatcherSha = "9135EF2EE2538816CF2BEFAAB5F84B275196AB8D59D0CC8F3C611C131CEF0E78"
$bepRootFiles = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")

function Get-Sha([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToUpperInvariant() }

$hash = (git -C $repo rev-parse --short $Ref)
if ($LASTEXITCODE -ne 0 -or -not $hash) { throw "Unknown ref '$Ref'" }
$hash = $hash.Trim()
$props = (git -C $repo show "${Ref}:RCK/Directory.Build.props") -join "`n"
$version = [regex]::Match($props, "<Version>([^<]+)</Version>").Groups[1].Value
if (-not $version) { throw "No <Version> in RCK/Directory.Build.props at $Ref" }
$rlpProj = (git -C $repo show "${Ref}:RogueLibsPlus/RogueLibsPlus.csproj") -join "`n"
$rlpVersion = [regex]::Match($rlpProj, "<Version>([^<]+)</Version>").Groups[1].Value
if (-not $rlpVersion) { throw "No <Version> in RogueLibsPlus/RogueLibsPlus.csproj at $Ref" }
$dirty = git -C $repo status --porcelain
if ($dirty) { Write-Warning "The working tree has uncommitted changes; the pack is built from $Ref ($hash) without them." }
Write-Host "Packing RCK Pack $version from $hash"

# The official BepInEx zip lives in .ref\bepinex (gitignored) and must match the pinned SHA256.
$bepDir = Join-Path $repo ".ref\bepinex"
$bepZip = Join-Path $bepDir $bepZipName
if (-not (Test-Path $bepZip) -or (Get-Sha $bepZip) -ne $bepSha) {
    New-Item -ItemType Directory -Force $bepDir | Out-Null
    $tmpZip = "$bepZip.download"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Write-Host "Downloading $bepUrl"
    Invoke-WebRequest -UseBasicParsing -Uri $bepUrl -OutFile $tmpZip
    $sha = Get-Sha $tmpZip
    if ($sha -ne $bepSha) { Remove-Item $tmpZip; throw "$bepZipName SHA256 is $sha, expected $bepSha" }
    Move-Item $tmpZip $bepZip -Force
}
Write-Host "BepInEx $bepVersion zip OK ($bepSha)"

$work = Join-Path $env:TEMP ("rck-release-$hash-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$src = Join-Path $work "src"
$stage = Join-Path $Out "stage"
try {
    New-Item -ItemType Directory $src -Force | Out-Null
    $tarball = Join-Path $work "src.tar"
    git -C $repo archive --format=tar -o $tarball $Ref
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
    & "$env:SystemRoot\System32\tar.exe" -xf $tarball -C $src
    if ($LASTEXITCODE -ne 0) { throw "tar extract failed" }
    Remove-Item $tarball
    # .ref holds the game's DLLs the build compiles against; none of them go into the pack (see the gate below).
    Copy-Item (Join-Path $repo ".ref") (Join-Path $src ".ref") -Recurse
    # The temp build can't find the sorcampaigns checkout beside this repository on its own.
    $scData = Join-Path (Split-Path $repo -Parent) "sorcampaigns\data"
    if (-not $env:SORCAMPAIGNS_DATA -and (Test-Path $scData)) { $env:SORCAMPAIGNS_DATA = $scData }
    # The clean-room gate (no CCU description wording in RCK) is a licence blocker, so a release needs the CCU clone.
    $ccuClone = Join-Path $repo "upstream\CCU"
    if (-not (Test-Path $ccuClone)) { throw "upstream\CCU is missing; clone https://github.com/Freiling87/CCU there so the clean-room description check can run" }
    $env:RCK_CCU_UPSTREAM = $ccuClone

    & (Join-Path $src "RCK\deploy.ps1") -BuildOnly
    $modules = @(Get-ChildItem (Join-Path $src "RCK\Systems") -Recurse -Filter "RCK.*.csproj" | Where-Object { $_.Directory.Name -ne "_Template" })
    $missing = @($modules | Where-Object { -not (Test-Path (Join-Path $_.DirectoryName "bin\Release\$($_.BaseName).dll")) } | ForEach-Object BaseName)
    if ($missing) { throw "modules failed to build: $($missing -join ', ')" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $src "tools\verify-ccu.ps1") -SkipPrivateAccess
    if ($LASTEXITCODE -ne 0) { throw "RogueLibs, RogueLibsPlus or RCK has patch errors" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $src "tools\check-private-access.ps1")
    if ($LASTEXITCODE -ne 0) { throw "private member access found; the build would fail in game" }

    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory "$stage\BepInEx\core", "$stage\BepInEx\patchers", "$stage\BepInEx\plugins\RogueLibsPlus",
        "$stage\BepInEx\plugins\RCK", "$stage\RCK-licenses" -Force | Out-Null

    # BepInEx: the doorstop root files and BepInEx\core, byte for byte from the official zip.
    $bepFiles = @{}
    $zip = [IO.Compression.ZipFile]::OpenRead($bepZip)
    try {
        foreach ($e in $zip.Entries) {
            $name = $e.FullName -replace "/", "\"
            if ($name.EndsWith("\")) { continue }
            if ($bepRootFiles -contains $name -or ($name -like "BepInEx\core\*" -and $name.Split("\").Count -eq 3)) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $stage $name), $true)
                $bepFiles[$name] = $true
            }
        }
    } finally { $zip.Dispose() }
    foreach ($f in $bepRootFiles + "BepInEx\core\BepInEx.dll", "BepInEx\core\BepInEx.Preloader.dll", "BepInEx\core\0Harmony.dll") {
        if (-not $bepFiles.ContainsKey($f)) { throw "$bepZipName has no $f" }
    }

    $rl = Join-Path $src ".ref\roguelibs"
    Copy-Item (Join-Path $rl "RogueLibsPatcher.Gen2.dll") "$stage\BepInEx\patchers"
    Copy-Item (Join-Path $rl "RogueLibsCore.dll") "$stage\BepInEx\plugins"
    if ((Get-Sha "$stage\BepInEx\plugins\RogueLibsCore.dll") -ne $roguelibsCoreSha) { throw "RogueLibsCore.dll is not Dzhake's v4.0.0-rc.3" }
    if ((Get-Sha "$stage\BepInEx\patchers\RogueLibsPatcher.Gen2.dll") -ne $roguelibsPatcherSha) { throw "RogueLibsPatcher.Gen2.dll is not the one in rc.3" }
    Copy-Item (Join-Path $src "RogueLibsPlus\bin\Release\RogueLibsPlus.dll") "$stage\BepInEx\plugins\RogueLibsPlus"
    Copy-Item (Join-Path $src "RCK\Core\bin\Release\RCK.dll") "$stage\BepInEx\plugins\RCK"
    foreach ($m in $modules) { Copy-Item (Join-Path $m.DirectoryName "bin\Release\$($m.BaseName).dll") "$stage\BepInEx\plugins\RCK" }

    # Notepad needs the BOM to show the Russian and Chinese text.
    $readme = [IO.File]::ReadAllText((Join-Path $src "tools\release\RCK-README.txt"), [Text.Encoding]::UTF8)
    $readme = ($readme -replace "\r?\n", "`r`n").Replace("{VERSION}", $version).Replace("{RLPLUS_VERSION}", $rlpVersion)
    # The title's underline matches the title once the version is filled in.
    $title = ($readme -split "`r`n")[0]
    $readme = [regex]::Replace($readme, "\A([^\r\n]+)\r\n=+", ($title + "`r`n" + ("=" * $title.Length)))
    if ($readme -match "\{[A-Z_]+\}") { throw "RCK-README.txt has an unfilled placeholder: $($Matches[0])" }
    [IO.File]::WriteAllText("$stage\RCK-README.txt", $readme, (New-Object Text.UTF8Encoding($true)))

    Copy-Item (Join-Path $src "licenses\*.LICENSE.txt") "$stage\RCK-licenses"
    Copy-Item (Join-Path $src "LICENSE") "$stage\RCK-licenses\LICENSE.txt"
    Copy-Item (Join-Path $src "RCK\LICENSE") "$stage\RCK-licenses\RCK.LICENSE.txt"
    Copy-Item (Join-Path $src "RogueLibsPlus\LICENSE") "$stage\RCK-licenses\RogueLibsPlus.LICENSE.txt"
    Copy-Item (Join-Path $src "THIRD-PARTY-NOTICES.md") "$stage\RCK-licenses"

    # Safety gate. Hard failures first: game or reference DLLs, anything built from .ref, dev tools, configs, scripts.
    $refHashes = @{}
    Get-ChildItem (Join-Path $repo ".ref") -Recurse -File | Where-Object {
        $_.FullName -notlike (Join-Path $repo ".ref\bepinex\*") -and $_.FullName -notlike (Join-Path $repo ".ref\roguelibs\*")
    } | ForEach-Object { $refHashes[(Get-Sha $_.FullName)] = $_.FullName.Substring($repo.Length + 1) }
    $files = @(Get-ChildItem $stage -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($stage.Length + 1) } | Sort-Object)
    $hardFail = @()
    $notAllowed = @()
    foreach ($f in $files) {
        $leaf = Split-Path $f -Leaf
        $sha = Get-Sha (Join-Path $stage $f)
        if ($leaf -match "^(Assembly-CSharp|UnityEngine|Unity\.|Mirror|Steamworks|com\.rlabrecque|Facepunch)") { $hardFail += "$f (game assembly)"; continue }
        if ($leaf -match "\.(pdb|ps1|exe|bat|cmd|cfg)$") { $hardFail += "$f (file type not allowed)"; continue }
        if ($f -match "^BepInEx\\config\\" -or $leaf -match "SelfTest|HangDiag|AiDiag|SaveRoundTrip|PatchVerifier|ButtonCheck") { $hardFail += "$f (config or dev tool)"; continue }
        # BepInEx's core DLLs are also in .ref\static; they're allowed when they are the official zip's bytes.
        if ($refHashes.ContainsKey($sha) -and -not $bepFiles.ContainsKey($f)) { $hardFail += "$f (same bytes as $($refHashes[$sha]))"; continue }
        $ok = ($bepFiles.ContainsKey($f)) -or
            ($f -eq "BepInEx\patchers\RogueLibsPatcher.Gen2.dll") -or
            ($f -eq "BepInEx\plugins\RogueLibsCore.dll") -or
            ($f -eq "BepInEx\plugins\RogueLibsPlus\RogueLibsPlus.dll") -or
            ($f -match "^BepInEx\\plugins\\RCK\\RCK(\.[A-Za-z]+)?\.dll$") -or
            ($f -eq "RCK-README.txt") -or
            ($f -match "^RCK-licenses\\[A-Za-z0-9.\-]+\.txt$") -or
            ($f -eq "RCK-licenses\THIRD-PARTY-NOTICES.md")
        if (-not $ok) { $notAllowed += $f }
    }
    if ($hardFail) { throw "Safety gate: forbidden files in the pack:`n  " + ($hardFail -join "`n  ") }
    if ($notAllowed) { throw "Safety gate: files not on the allow-list:`n  " + ($notAllowed -join "`n  ") }
    $required = $bepRootFiles + @("BepInEx\core\BepInEx.dll", "BepInEx\core\BepInEx.Preloader.dll",
        "BepInEx\patchers\RogueLibsPatcher.Gen2.dll", "BepInEx\plugins\RogueLibsCore.dll", "BepInEx\plugins\RogueLibsPlus\RogueLibsPlus.dll",
        "BepInEx\plugins\RCK\RCK.dll", "RCK-README.txt", "RCK-licenses\THIRD-PARTY-NOTICES.md", "RCK-licenses\LICENSE.txt",
        "RCK-licenses\BepInEx.LICENSE.txt", "RCK-licenses\UnityDoorstop.LICENSE.txt", "RCK-licenses\RogueLibs.LICENSE.txt")
    $absent = @($required | Where-Object { $files -notcontains $_ })
    if ($absent) { throw "Safety gate: required files missing: $($absent -join ', ')" }
    $placeholders = @($files | Where-Object { $_ -match "\.(txt|md|ini)$" } | Where-Object {
        [IO.File]::ReadAllText((Join-Path $stage $_)) -match "<owner>|<repo>" })
    if ($placeholders) { throw "Safety gate: URL placeholder <owner> or <repo> left in: $($placeholders -join ', ')" }
    Write-Host "Allow-list check passed: $($files.Count) files"

    # Zip entries use forward slashes and the commit's date, so the same commit gives the same file list and times.
    $zipPath = Join-Path $Out "RCK-Pack-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    $when = [DateTimeOffset]::Parse((git -C $repo log -1 --format=%cI $Ref))
    $fs = [IO.File]::Open($zipPath, "CreateNew")
    $archive = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $files) {
            $entry = $archive.CreateEntry(($f -replace "\\", "/"), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $when
            $w = $entry.Open()
            try { $bytes = [IO.File]::ReadAllBytes((Join-Path $stage $f)); $w.Write($bytes, 0, $bytes.Length) } finally { $w.Dispose() }
        }
    } finally { $archive.Dispose(); $fs.Dispose() }
    $zipSha = Get-Sha $zipPath
    Set-Content -Path "$zipPath.sha256" -Value "$($zipSha.ToLowerInvariant())  RCK-Pack-$version.zip" -Encoding ASCII

    Write-Host ""
    Write-Host "RCK Pack $version ($hash): $zipPath"
    Write-Host "SHA256: $zipSha"
    Write-Host "Files:"
    foreach ($f in $files) { Write-Host ("  {0,-60} {1,9:N0} B" -f $f, (Get-Item (Join-Path $stage $f)).Length) }
} finally {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
