# Builds a commit the way a release is built, for tools\freeze.ps1 and tools\package-release.ps1, so both give the same
# DLL bytes for the same commit. Dot-source it, then:
#   $b = Invoke-RefBuild -Repo <repo> -Ref <ref> -Dir <empty build folder> [-RequireCcu]
# It extracts `git archive <ref>` into Dir (uncommitted changes are not included), copies .ref (the game DLLs the build
# compiles against), builds RogueLibsPlus and RCK with the commit's build identity from tools\build-id.ps1 (the archive
# has no .git), then runs verify-ccu and the private-access check on the result. It returns the Get-BuildId object plus
# Dir. build\BuildInfo.targets makes the build deterministic and maps Dir to /_/, so the folder doesn't change the bytes.
. (Join-Path $PSScriptRoot "build-id.ps1")

function Invoke-RefBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Ref,
        [Parameter(Mandatory = $true)][string]$Dir,
        # A release needs the clean-room description check (no CCU wording in RCK), which needs the CCU clone.
        [switch]$RequireCcu
    )
    $ErrorActionPreference = "Stop"
    $id = Get-BuildId -Repo $Repo -Ref $Ref
    New-Item -ItemType Directory $Dir -Force | Out-Null
    $tarball = "$($Dir.TrimEnd('\')).tar"
    git -C $Repo archive --format=tar -o $tarball $id.Commit
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
    # Expand-Archive takes minutes on this tree; bsdtar takes seconds.
    & "$env:SystemRoot\System32\tar.exe" -xf $tarball -C $Dir
    if ($LASTEXITCODE -ne 0) { throw "tar extract failed" }
    Remove-Item $tarball
    Copy-Item (Join-Path $Repo ".ref") (Join-Path $Dir ".ref") -Recurse

    # The checks in the build folder can't find the sorcampaigns checkout or the CCU clone beside this repository.
    $scData = Join-Path (Split-Path $Repo -Parent) "sorcampaigns\data"
    if (-not $env:SORCAMPAIGNS_DATA -and (Test-Path $scData)) { $env:SORCAMPAIGNS_DATA = $scData }
    $ccuClone = Join-Path $Repo "upstream\CCU"
    if (Test-Path $ccuClone) { $env:RCK_CCU_UPSTREAM = $ccuClone }
    elseif ($RequireCcu) { throw "upstream\CCU is missing; clone https://github.com/Freiling87/CCU there so the clean-room description check can run" }

    # deploy -BuildOnly re-checks the SHA256 of Dzhake's rc.3 DLLs in .ref\roguelibs (downloading them if the copy lacks
    # them), then builds RogueLibsPlus and RCK.
    Write-Host "Building $($id.Short) as RCK $($id.Display), RL+ $($id.RlPlusDisplay)"
    # Their output goes to the host: only the build id object is returned.
    & (Join-Path $Dir "RCK\deploy.ps1") -BuildOnly -BuildMetadata $id.Metadata | Out-Host
    $modules = @(Get-ChildItem (Join-Path $Dir "RCK\Systems") -Recurse -Filter "RCK.*.csproj" | Where-Object { $_.Directory.Name -ne "_Template" })
    $missing = @($modules | Where-Object { -not (Test-Path (Join-Path $_.DirectoryName "bin\Release\$($_.BaseName).dll")) } | ForEach-Object BaseName)
    if ($missing) { throw "modules failed to build: $($missing -join ', ')" }

    # Checks RogueLibsCore, RogueLibsPlus and every RCK DLL.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Dir "tools\verify-ccu.ps1") -SkipPrivateAccess | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "verify-ccu failed on the build of $($id.Short) (see above)" }
    # Code compiled against the publicized .ref can still touch private members, which Mono rejects at runtime.
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Dir "tools\check-private-access.ps1") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "private member access found (see above); the build would fail in game" }

    $id | Add-Member -NotePropertyName Dir -NotePropertyValue $Dir -PassThru |
        Add-Member -NotePropertyName Modules -NotePropertyValue $modules -PassThru
}
