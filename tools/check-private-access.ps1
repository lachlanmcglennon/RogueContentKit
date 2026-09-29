# Builds RogueLibsPlus and RCK (Core + every module) against an injected but NOT publicized Assembly-CSharp.
# The normal .ref DLL is publicized, so code like __instance.agent compiles even when the field is private.
# At runtime Mono throws FieldAccessException for those, silently (e.g. every NPC's AI tick failing).
# Any CS1061/CS0122/CS0271/CS0272/CS1540 here is a non-public member access: use Harmony ___field injection or
# AccessTools instead. Any other build error makes the check inconclusive, so it fails too (exit 2).
# Usage: powershell -File tools\check-private-access.ps1 [-Game "<SoR folder>"]
param(
    [string]$Game = 'C:\Program Files (x86)\Steam\steamapps\common\Streets of Rogue'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $env:TEMP ('sor-ccu-privcheck-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
New-Item -ItemType Junction -Path (Join-Path $work 'static') -Target (Join-Path $root '.ref\static') | Out-Null

$src = Join-Path $Game 'StreetsOfRogue_Data\Managed\Assembly-CSharp.dll'
$ref = Join-Path $work 'Assembly-CSharp.dll'
dotnet run -c Release --project (Join-Path $root 'tools\RefBuilder') -- $src $ref | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'RefBuilder failed' }

$hits = @()
$other = @()
$privateCodes = 'CS1061', 'CS0122', 'CS0117', 'CS0271', 'CS0272', 'CS1540'
function Collect([string]$name, $out) {
    foreach ($m in ($out | Select-String ': error ')) {
        if ($m.Line -match '([^\\]+\.cs)\((\d+),\d+\): error (CS\d+): (.*?) \[') {
            $s = "${name}: $($matches[1]):$($matches[2]) $($matches[4])"
            if ($privateCodes -contains $matches[3]) { $script:hits += $s } else { $script:other += "$s ($($matches[3]))" }
        } else { $script:other += "${name}: $($m.Line.Trim())" }
    }
}
function Build([string]$name, [string]$proj, [string[]]$extra) {
    $out = & dotnet build $proj -c Release --no-restore "-p:OutDir=$work\out\$name\" "-p:IntermediateOutputPath=$work\obj\$name\" @extra 2>&1
    Collect $name $out
}

# RogueLibsPlus, then RCK against it. Dzhake's RogueLibsCore (.ref\roguelibs) is a finished build, so it isn't checked here.
New-Item -ItemType Junction -Path (Join-Path $work 'roguelibs') -Target (Join-Path $root '.ref\roguelibs') | Out-Null
Build 'RogueLibsPlus' (Join-Path $root 'RogueLibsPlus\RogueLibsPlus.csproj') @("-p:RefDir=$work\")
$plus = @("-p:RefDir=$work\", "-p:RogueLibsPlusDir=$work\out\RogueLibsPlus\")

Build 'RCK' (Join-Path $root 'RCK\Core\RCK.csproj') $plus
Get-ChildItem (Join-Path $root 'RCK\Systems') -Directory | Where-Object { $_.Name -ne '_Template' } | ForEach-Object {
    $proj = Get-ChildItem $_.FullName -Filter *.csproj | Select-Object -First 1
    if ($proj) { Build $_.Name $proj.FullName $plus }
}

# junctions first, so removing the work folder can't reach into .ref
foreach ($j in 'static', 'roguelibs') { cmd /c rmdir (Join-Path $work $j) 2>$null }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
$hits = @($hits | Sort-Object -Unique)
$other = @($other | Sort-Object -Unique)
if ($hits.Count) {
    Write-Host "PRIVATE ACCESS: $($hits.Count) site(s) will throw at runtime:" -ForegroundColor Red
    $hits | ForEach-Object { Write-Host "  $_" }
}
if ($other.Count) {
    Write-Host "private-access check INCONCLUSIVE: $($other.Count) other build error(s):" -ForegroundColor Red
    $other | ForEach-Object { Write-Host "  $_" }
}
if ($hits.Count) { exit 1 }
if ($other.Count) { exit 2 }
Write-Host 'private-access check: 0 sites' -ForegroundColor Green
