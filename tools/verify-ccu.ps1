# Static check of every built RCK DLL's Harmony patch targets against the current game Assembly-CSharp, plus those of
# RogueLibsPlus and of the unmodified RogueLibs rc.3 DLL (.ref\roguelibs, from tools\get-roguelibs.ps1) RCK runs on.
# Usage: powershell -File tools\verify-ccu.ps1 [RCK.X.dll ...] [-SkipPrivateAccess]
# With no DLLs it checks RogueLibsCore, RogueLibsPlus and every RCK DLL, then runs tools\check-game-strings, the offline faction
# rule tests (tools\FactionTests), the version, trait-description and repo-URL placeholder checks, and
# tools\check-private-access.ps1 (all hard failures; private access can be skipped).
# RCK DLLs also go through tools\ButtonCheck: every VanillaButtons/CustomButtons name must exist in the game or have a label.
param([string[]]$Dll, [switch]$SkipPrivateAccess)
$root = Split-Path $PSScriptRoot -Parent
$full = -not $Dll
$verifier = "$root\tools\PatchVerifier\bin\Release\net9.0\PatchVerifier.dll"
if (-not (Test-Path $verifier)) { dotnet build "$root\tools\PatchVerifier" -c Release -v q -nologo | Out-Host }
if (-not $Dll) {
    $Dll = @("$root\.ref\roguelibs\RogueLibsCore.dll", "$root\RogueLibsPlus\bin\Release\RogueLibsPlus.dll", "$root\RCK\Core\bin\Release\RCK.dll") + (Get-ChildItem "$root\RCK\Systems" -Recurse -Filter "RCK.*.dll" |
        Where-Object { $_.Directory.Name -eq "Release" -and $_.Directory.Parent.Name -eq "bin" -and $_.Directory.Parent.Parent.Name -ne "_Template" } | ForEach-Object FullName)
}
$bad = 0
foreach ($d in $Dll) {
    $out = dotnet $verifier $d "$root\.ref\Assembly-CSharp.dll" --refdir "$root\.ref\static" --refdir "$root\.ref\roguelibs" --refdir "$root\RogueLibsPlus\bin\Release" --refdir "$root\RCK\Core\bin\Release" 2>&1
    $sum = ($out | Select-String "^(HarmonyPatch attribute patches|Direct Harmony patches|RoguePatcher sites|ERRORs|WARNs):") -join "; "
    Write-Host "$(Split-Path $d -Leaf): $sum"
    $errs = $out | Select-String "^\s*(ERROR|WARN) "
    $errs | ForEach-Object { Write-Host "   $($_.Line.Trim())" }
    if ($out | Select-String "^ERRORs: [1-9]") { $bad++ }
}
if ($bad) { Write-Host "$bad DLL(s) with patch ERRORs"; exit 1 }
$rckDlls = @($Dll | Where-Object { (Split-Path $_ -Leaf) -like "RCK*.dll" })
if ($rckDlls.Count) {
    $buttonCheck = "$root\tools\ButtonCheck\bin\Release\net9.0\ButtonCheck.dll"
    if (-not (Test-Path $buttonCheck)) { dotnet build "$root\tools\ButtonCheck" -c Release -v q -nologo | Out-Host }
    $out = dotnet $buttonCheck "$root\.ref\Assembly-CSharp.dll" @rckDlls 2>&1
    $out | Where-Object { $_ -match "^(ERROR|WARN) " } | ForEach-Object { Write-Host "   $_" }
    Write-Host "buttons: $(($out | Select-String '^(VanillaButtons|ERRORs|WARNs):') -join '; ')"
    if ($LASTEXITCODE -ne 0) { Write-Host "button check failed"; exit 1 }
}
if ($full) {
    # Every game name RCK passes to the game (sounds, dialogue, buttons, items, agents...) must exist in the decompile.
    $out = & python "$root\tools\check-game-strings\check_game_strings.py" --repo $root 2>&1
    $code = $LASTEXITCODE
    $out | Where-Object { "$_" -match "^(- |Unknowns:|OK:|FAILED:|[A-Za-z].*not found)" } | ForEach-Object { Write-Host "   $_" }
    if ($code -ne 0) { Write-Host "game-string check failed"; exit 1 }
    # Faction grade precedence and FactionRel matrix parsing, compiled from RCK's pure Social sources.
    dotnet build "$root\tools\FactionTests" -c Release -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FactionTests build failed"; exit 1 }
    $out = dotnet "$root\tools\FactionTests\bin\Release\net9.0\FactionTests.dll" $root 2>&1
    $code = $LASTEXITCODE
    $out | ForEach-Object { Write-Host "   $_" }
    if ($code -ne 0) { Write-Host "faction rule tests failed"; exit 1 }
    # One version source per component: <Version> in the project file. build\BuildInfo.targets generates the plugin's
    # BuildInfo constants from it, so the code must not keep its own copy. The built DLLs must agree: the BepInPlugin
    # version is the numeric X.Y.Z, and the informational version (what the menu and the log show) is the version,
    # plus "+<build metadata>" unless it's a build of the release tag. Every RCK DLL carries the same one.
    [void][Reflection.Assembly]::UnsafeLoadFrom("$root\.ref\static\Mono.Cecil.dll")
    $versions = @(
        @("RCK", "$root\RCK\Directory.Build.props", "$root\RCK\Core\Plugin.cs", "$root\RCK\Core\bin\Release\RCK.dll"),
        @("RogueLibsPlus", "$root\RogueLibsPlus\RogueLibsPlus.csproj", "$root\RogueLibsPlus\Plugin.cs", "$root\RogueLibsPlus\bin\Release\RogueLibsPlus.dll"))
    foreach ($v in $versions) {
        $proj = [regex]::Match([IO.File]::ReadAllText($v[1]), "<Version>([^<]+)</Version>").Groups[1].Value
        if ($proj -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') { Write-Host "$($v[0]) <Version> '$proj' is not X.Y.Z[-pre]"; exit 1 }
        if ([IO.File]::ReadAllText($v[2]) -match 'const string (Version|DisplayVersion|PluginVersion) = "') {
            Write-Host "$($v[0]) keeps a version literal in $(Split-Path $v[2] -Leaf); use BuildInfo (build\BuildInfo.targets)"; exit 1
        }
        $numeric = $proj -replace '-.*$', ''
        $dlls = @($v[3])
        if ($v[0] -eq "RCK") { $dlls += @($Dll | Where-Object { (Split-Path $_ -Leaf) -like "RCK.*.dll" }) }
        $infos = @{}
        foreach ($d in $dlls) {
            $m = [Mono.Cecil.ModuleDefinition]::ReadModule($d)
            try {
                $info = @($m.Assembly.CustomAttributes | Where-Object { $_.AttributeType.Name -eq "AssemblyInformationalVersionAttribute" } |
                    ForEach-Object { $_.ConstructorArguments[0].Value })[0]
                $plugins = @(foreach ($t in $m.Types) { foreach ($a in $t.CustomAttributes) { if ($a.AttributeType.Name -eq "BepInPlugin") { "$($t.Name)=$($a.ConstructorArguments[2].Value)" } } })
            } finally { $m.Dispose() }
            if ($info -ne $proj -and -not "$info".StartsWith("$proj+")) { Write-Host "$(Split-Path $d -Leaf) informational version '$info' is not $proj[+metadata]"; exit 1 }
            foreach ($p in $plugins) { if ($p -notlike "*=$numeric") { Write-Host "$(Split-Path $d -Leaf) BepInPlugin $p, expected $numeric"; exit 1 } }
            $infos[$info] = $true
        }
        if ($infos.Count -ne 1) { Write-Host "$($v[0]) DLLs carry different versions: $($infos.Keys -join ', ')"; exit 1 }
        Write-Host "   $($v[0]) version $proj; $($dlls.Count) DLL(s) built as $(@($infos.Keys)[0])"
    }
    # Clean-room trait descriptions: every trait id has one, the generated table is current, and none reuses CCU wording.
    & python "$root\tools\clean-room\check_complete.py" 2>&1 | ForEach-Object { Write-Host "   $_" }
    if ($LASTEXITCODE -ne 0) { Write-Host "trait-description completeness check failed"; exit 1 }
    $gen = "$root\RCK\Core\Generated\TraitDescriptions.g.cs"
    $before = (Get-FileHash $gen).Hash
    & python "$root\tools\ccu-codegen\gen_descriptions_cs.py" | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash $gen).Hash -ne $before) { Write-Host "TraitDescriptions.g.cs was stale; regenerated, rebuild and rerun"; exit 1 }
    & python "$root\tools\clean-room\check_overlap.py" 2>&1 | ForEach-Object { Write-Host "   $_" }
    if ($LASTEXITCODE -ne 0) { Write-Host "trait-description overlap check failed"; exit 1 }
    # Public docs and the pack's text files must carry the real repo URL, not the <owner>/<repo> placeholders. A git
    # archive copy (freeze, package-release) has no .git of its own, so there the files on disk are walked instead.
    $ownRepo = $false
    $top = git -C $root rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and $top) { $ownRepo = ([IO.Path]::GetFullPath(("$top".Trim() -replace "/", "\")).TrimEnd("\") -ieq [IO.Path]::GetFullPath($root).TrimEnd("\")) }
    if ($ownRepo) {
        $docs = @(git -C $root ls-files "*.md" "*.txt" | Where-Object { $_ -notlike "docs/internal/*" })
        $how = "git ls-files"
    } else {
        $skip = '^(\.git|\.ref|upstream|release|dist|docs\\internal)(\\|$)|(^|\\)(bin|obj)\\'
        $docs = @(Get-ChildItem $root -Recurse -File -Include "*.md", "*.txt" -Force | ForEach-Object { $_.FullName.Substring($root.Length + 1) } |
            Where-Object { $_ -notmatch $skip } | ForEach-Object { $_ -replace "\\", "/" })
        $how = "files on disk (no .git)"
    }
    if ($docs.Count -eq 0) { Write-Host "URL placeholder check scanned 0 public docs ($how)"; exit 1 }
    $left = @($docs | Where-Object { [IO.File]::ReadAllText((Join-Path $root $_)) -match "<owner>|<repo>" })
    if ($left) { Write-Host "URL placeholder <owner> or <repo> left in: $($left -join ', ')"; exit 1 }
    Write-Host "   repo URL placeholders: none in $($docs.Count) public docs ($how)"
}
if ($full -and -not $SkipPrivateAccess) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\check-private-access.ps1"
    if ($LASTEXITCODE -ne 0) { Write-Host "private-access check failed"; exit 1 }
}
