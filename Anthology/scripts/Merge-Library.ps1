<#
.SYNOPSIS
  Folds an existing Prowl library repo into the Anthology monorepo under its own
  subfolder, preserving full commit history (rewritten so `git log -- <Name>/`
  shows the library's entire past). The source repo is never modified.

.PARAMETER Name
  The subfolder the library will live under in the monorepo, e.g. "Vector".

.PARAMETER Source
  Path (or URL) of the source repo, e.g. "../Prowl.Vector".

.PARAMETER Branch
  Branch to import. Auto-detected from the source's default branch if omitted
  (needed because, e.g., Scribe is on 'master' and Slang is on 'Managed').

.EXAMPLE
  ./scripts/Merge-Library.ps1 -Name Vector -Source ../Prowl.Vector
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Name,
  [Parameter(Mandatory)] [string] $Source,
  [string] $Branch
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path $PSScriptRoot -Parent
$filterRepo = Join-Path $PSScriptRoot 'git-filter-repo'
$solution   = Join-Path $repoRoot 'Anthology.slnx'

# --- sanity checks ---
if (-not (Test-Path (Join-Path $repoRoot '.git'))) { throw "Not inside the Anthology repo: $repoRoot" }
if (Test-Path (Join-Path $repoRoot $Name))         { throw "Folder '$Name' already exists in the monorepo." }
if (-not (Test-Path $filterRepo))                  { throw "Missing scripts/git-filter-repo" }

$src = (Resolve-Path $Source).Path
if (-not (Test-Path (Join-Path $src '.git')))      { throw "Source is not a git repo: $src" }
if (git -C $repoRoot status --porcelain)           { throw "Anthology working tree is dirty. Commit or stash first." }

# --- fresh, fully detached copy of the source (no hardlinks: original is never touched) ---
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("anthology_" + $Name + "_" + [System.IO.Path]::GetRandomFileName())
Write-Host "Cloning $src -> $tmp" -ForegroundColor Cyan
git clone --no-hardlinks --quiet "$src" "$tmp"

if (-not $Branch) { $Branch = (git -C $tmp rev-parse --abbrev-ref HEAD).Trim() }
Write-Host "Importing branch '$Branch' into '$Name/'" -ForegroundColor Cyan

# --- rewrite history so every file moves under <Name>/ ---
Push-Location $tmp
try { python "$filterRepo" --force --to-subdirectory-filter "$Name" }
finally { Pop-Location }

# --- merge the rewritten history into Anthology ---
$remote = "import_$Name"
git -C $repoRoot remote add $remote "$tmp"
git -C $repoRoot fetch --quiet $remote
git -C $repoRoot merge --allow-unrelated-histories --no-edit -m "Fold in $Name with full history" "$remote/$Branch"
git -C $repoRoot remote remove $remote
Remove-Item -Recurse -Force $tmp

Write-Host ""
Write-Host "Done. '$Name/' folded in (merge committed)." -ForegroundColor Green
Write-Host "Verify history:  git log --oneline -- $Name/"
