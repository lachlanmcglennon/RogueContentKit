# Build identity from git, shared by RCK\deploy.ps1, tools\build-ref.ps1 (freeze and package-release) and
# tools\new-release.ps1. Dot-source it:  . "$PSScriptRoot\build-id.ps1"
#   Get-BuildId -Repo <repo> [-Ref <ref>]    a commit, read from git (archive builds)
#   Get-BuildId -Repo <repo> -WorkingTree    the working tree (deploy builds), with .dirty for uncommitted changes
# Metadata is "release" for the commit the release tag v<RCK version> points at (with -WorkingTree, only when the tree is
# clean too). Anything else is "N.g<sha7>" from git describe --tags --long: N commits after the last v* tag (or after
# the first commit when there is no tag), plus ".dirty". build\BuildInfo.targets turns that into the display version:
# X.Y.Z[-pre] for "release", else X.Y.Z[-pre]+<metadata>.

function Get-ProjectVersion([string]$Text, [string]$What) {
    $v = [regex]::Match($Text, "<Version>([^<]+)</Version>").Groups[1].Value
    if (-not $v) { throw "No <Version> in $What" }
    $v
}

function Get-BuildId {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [string]$Ref = "HEAD",
        [switch]$WorkingTree
    )
    # Windows PowerShell turns a native command's stderr into terminating errors under Stop.
    $ErrorActionPreference = "Continue"
    if ($WorkingTree) { $Ref = "HEAD" }
    $commit = git -C $Repo rev-parse --verify --quiet "$Ref^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $commit) { throw "Unknown ref '$Ref' in $Repo" }
    $commit = "$commit".Trim()
    $sha7 = $commit.Substring(0, 7)
    if ($WorkingTree) {
        $rckProps = [IO.File]::ReadAllText((Join-Path $Repo "RCK\Directory.Build.props"))
        $rlpProj = [IO.File]::ReadAllText((Join-Path $Repo "RogueLibsPlus\RogueLibsPlus.csproj"))
    } else {
        $rckProps = (git -C $Repo show "${commit}:RCK/Directory.Build.props") -join "`n"
        $rlpProj = (git -C $Repo show "${commit}:RogueLibsPlus/RogueLibsPlus.csproj") -join "`n"
    }
    $version = Get-ProjectVersion $rckProps "RCK/Directory.Build.props at $Ref"
    $rlpVersion = Get-ProjectVersion $rlpProj "RogueLibsPlus/RogueLibsPlus.csproj at $Ref"

    $describe = git -C $Repo describe --tags --long --match "v[0-9]*" $commit 2>$null
    $tag = $null
    if ($LASTEXITCODE -eq 0 -and "$describe" -match '^(?<tag>.+)-(?<n>\d+)-g[0-9a-f]+$') {
        $tag = $Matches.tag
        $distance = [int]$Matches.n
    } else {
        $distance = [int](git -C $Repo rev-list --count $commit)
    }
    $dirty = $false
    if ($WorkingTree) { $dirty = [bool](git -C $Repo status --porcelain --untracked-files=no) }
    if ($distance -eq 0 -and $tag -eq "v$version" -and -not $dirty) { $meta = "release" }
    else { $meta = "$distance.g$sha7" + $(if ($dirty) { ".dirty" } else { "" }) }
    $suffix = $(if ($meta -eq "release") { "" } else { "+$meta" })
    [pscustomobject]@{
        Commit        = $commit
        Short         = $sha7
        Tag           = $tag
        Distance      = $distance
        Dirty         = $dirty
        Metadata      = $meta
        Version       = $version
        Display       = "$version$suffix"
        RlPlusVersion = $rlpVersion
        RlPlusDisplay = "$rlpVersion$suffix"
    }
}

# True when $Dir is the top of its own git repository, not a folder inside some other one (an archive extracted under
# %TEMP% sits inside the user profile, which can be a repository).
function Test-OwnRepository([string]$Dir) {
    $ErrorActionPreference = "Continue"
    $top = git -C $Dir rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $top) { return $false }
    $a = [IO.Path]::GetFullPath(("$top".Trim() -replace "/", "\")).TrimEnd("\")
    $b = [IO.Path]::GetFullPath($Dir).TrimEnd("\")
    return $a -ieq $b
}
