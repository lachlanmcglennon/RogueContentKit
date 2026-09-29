# Fetches Dzhake's RogueLibs v4.0.0-rc.3 release DLL (unmodified, MIT) into .ref\roguelibs, checks its SHA256,
# and extracts the RogueLibsPatcher.Gen2.dll preloader that the DLL carries as an embedded resource.
# RogueLibsPlus and RCK compile against this DLL, and freeze/deploy install it as-is.
# Usage: powershell -File tools\get-roguelibs.ps1 [-Source <local RogueLibsCore.dll>] [-Dest <folder>] [-Force]
param(
    [string]$Source,
    [string]$Dest,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Dest) { $Dest = Join-Path $root '.ref\roguelibs' }

$url = 'https://github.com/Dzhake/RogueLibs/releases/download/v4.0.0-rc.3/RogueLibsCore.dll'
$coreSha = '357B53E477A63FFE282AFA631B1D2BF13EEB4389B1D544FFF8117FE714259E1C'
$patcherSha = '9135EF2EE2538816CF2BEFAAB5F84B275196AB8D59D0CC8F3C611C131CEF0E78'

function Get-Sha([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToUpperInvariant() }

$core = Join-Path $Dest 'RogueLibsCore.dll'
$patcher = Join-Path $Dest 'RogueLibsPatcher.Gen2.dll'
if (-not $Force -and -not $Source -and (Test-Path $core) -and (Test-Path $patcher) -and
    (Get-Sha $core) -eq $coreSha -and (Get-Sha $patcher) -eq $patcherSha) {
    Write-Host "RogueLibs rc.3 already in $Dest"
    return
}

New-Item -ItemType Directory -Force $Dest | Out-Null
$tmp = Join-Path $env:TEMP ('roguelibs-rc3-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.dll')
try {
    if ($Source) {
        Copy-Item -LiteralPath $Source $tmp -Force
    } else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Write-Host "Downloading $url"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $tmp
    }
    $sha = Get-Sha $tmp
    if ($sha -ne $coreSha) { throw "RogueLibsCore.dll SHA256 is $sha, expected $coreSha (not Dzhake's v4.0.0-rc.3 release)" }

    # The patcher sits in RogueLibsCore.Properties.Resources.resources under the key "RogueLibsPatcher".
    $bytes = [IO.File]::ReadAllBytes($tmp)
    $asm = [Reflection.Assembly]::Load($bytes)
    $stream = $asm.GetManifestResourceStream('RogueLibsCore.Properties.Resources.resources')
    if (-not $stream) { throw 'RogueLibsCore.dll has no RogueLibsCore.Properties.Resources.resources' }
    $patcherBytes = $null
    $reader = New-Object System.Resources.ResourceReader($stream)
    try {
        $e = $reader.GetEnumerator()
        while ($e.MoveNext()) { if ($e.Key -eq 'RogueLibsPatcher') { $patcherBytes = [byte[]]$e.Value } }
    } finally { $reader.Close() }
    if (-not $patcherBytes) { throw 'RogueLibsCore.dll has no embedded RogueLibsPatcher' }

    Copy-Item -LiteralPath $tmp $core -Force
    [IO.File]::WriteAllBytes($patcher, $patcherBytes)
    $sha = Get-Sha $patcher
    if ($sha -ne $patcherSha) { throw "Extracted RogueLibsPatcher.Gen2.dll SHA256 is $sha, expected $patcherSha" }
    Write-Host "RogueLibs rc.3 -> $Dest (RogueLibsCore.dll, RogueLibsPatcher.Gen2.dll)"
} finally {
    Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue
}
