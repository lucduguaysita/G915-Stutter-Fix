# G915-Stutter-Fix macOS release kit.
# Adds a macOS-only release track to your repo clone (does NOT touch the Windows
# version line), commits, pushes, and optionally tags macos-v1.0.0 to build and
# publish the macOS pre-release. Your Windows v3.3.1 stays the "latest" release,
# so CheckForUpdates never fires for Windows users or you.
#
# Run by double-clicking apply.bat, or:
#   powershell -ExecutionPolicy Bypass -File .\apply.ps1

$ErrorActionPreference = "Stop"
$kit = Join-Path $PSScriptRoot "payload"

function WriteText($path, $text) {
    $enc = New-Object System.Text.UTF8Encoding($false)   # no BOM
    [System.IO.File]::WriteAllText($path, $text, $enc)
}

Write-Host ""
Write-Host "G915-Stutter-Fix macOS release kit" -ForegroundColor Cyan
Write-Host ""

# 1. Find the repo clone (must contain KeyboardRepeatFilter.sln and a macos/ folder).
function Is-Repo($p) {
    return (Test-Path (Join-Path $p "KeyboardRepeatFilter.sln")) -and (Test-Path (Join-Path $p "macos"))
}
$repo = $null
foreach ($cand in @((Get-Location).Path, $PSScriptRoot, (Split-Path $PSScriptRoot -Parent))) {
    if (Is-Repo $cand) { $repo = $cand; break }
}
if (-not $repo) {
    $entered = Read-Host "Full path to your G915-Stutter-Fix repo clone"
    if ($entered -and (Is-Repo $entered)) { $repo = $entered }
}
if (-not $repo) {
    Write-Host "Could not find the repo (expected KeyboardRepeatFilter.sln and a macos/ folder)." -ForegroundColor Red
    Read-Host "Press Enter to close"; exit 1
}
Set-Location $repo
Write-Host "Repo: $repo" -ForegroundColor Green

# 2. The two things only you know: the contributor handle and today's date.
$handle = (Read-Host "GitHub @handle of the macOS contributor (blank to skip)").Trim()
if ($handle -and ($handle[0] -ne '@')) { $handle = '@' + $handle }
if ($handle) { $credit = $handle; $owner = $handle } else { $credit = "a community contributor"; $owner = $null }
$date = (Get-Date -Format "yyyy-MM-dd")
function Fill($text) { return ($text -replace '\{\{CREDIT\}\}', $credit) -replace '\{\{DATE\}\}', $date }

# 3. Workflow (copied verbatim).
New-Item -ItemType Directory -Force -Path (Join-Path $repo ".github\workflows") | Out-Null
Copy-Item (Join-Path $kit ".github\workflows\macos-release.yml") (Join-Path $repo ".github\workflows\macos-release.yml") -Force
Write-Host "wrote .github/workflows/macos-release.yml"

# 4. RELEASE_NOTES_macos.md (used as the macOS release body).
WriteText (Join-Path $repo "RELEASE_NOTES_macos.md") (Fill (Get-Content (Join-Path $kit "RELEASE_NOTES_macos.md") -Raw))
Write-Host "wrote RELEASE_NOTES_macos.md"

# 5. CODEOWNERS: route macos/ to the contributor.
$coPath = Join-Path $repo ".github\CODEOWNERS"
if ($owner) {
    $co = (Get-Content (Join-Path $kit "CODEOWNERS.template") -Raw) -replace '\{\{OWNER\}\}', $owner
} else {
    $co = "# Route the macOS port to its contributor for review.`r`n# TODO: add the contributor handle, e.g.:  /macos/ @their-handle`r`n"
}
if (Test-Path $coPath) { Add-Content $coPath ("`r`n" + $co) } else { WriteText $coPath $co }
Write-Host "wrote .github/CODEOWNERS"

# 6. CHANGELOG.md: insert the macOS entry above the newest existing entry.
$clPath = Join-Path $repo "CHANGELOG.md"
$entry = (Fill (Get-Content (Join-Path $kit "CHANGELOG_macos.md") -Raw)).TrimEnd()
if (Test-Path $clPath) {
    $raw = Get-Content $clPath -Raw
    $m = [regex]::Match($raw, '(?m)^## \[')
    if ($m.Success) {
        $newRaw = $raw.Substring(0, $m.Index) + $entry + "`r`n`r`n" + $raw.Substring($m.Index)
    } else {
        $newRaw = $raw.TrimEnd() + "`r`n`r`n" + $entry + "`r`n"
    }
    WriteText $clPath $newRaw
    Write-Host "updated CHANGELOG.md (added [macOS 1.0.0])"
} else {
    WriteText $clPath ("# Changelog`r`n`r`n" + $entry + "`r`n")
    Write-Host "created CHANGELOG.md"
}

# 7. README note: save it and print it (README edits are left to you to place).
$note = (Fill (Get-Content (Join-Path $kit "README_note.md") -Raw)).Trim()
WriteText (Join-Path $repo "MACOS_README_NOTE.txt") ($note + "`r`n")
Write-Host ""
Write-Host "Paste this into README.md under the opening summary (also saved as MACOS_README_NOTE.txt):" -ForegroundColor Yellow
Write-Host $note -ForegroundColor Gray

# 8. Commit and push (docs + workflow only; no release is created by this push).
git config --global --add safe.directory '*' | Out-Null
if (-not (git config user.email)) { git config user.email "luc.duguay@gmail.com" | Out-Null }
if (-not (git config user.name))  { git config user.name  "Luc Duguay"        | Out-Null }
try { gh auth setup-git 2>$null | Out-Null } catch { }
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
git add -A
git commit -m "Add macOS pre-release track (macos-v*) and docs; Windows version line unchanged" 2>$null | Out-Null
Write-Host ""
Write-Host "Pushing to $branch ..." -ForegroundColor Yellow
git push origin $branch
if ($LASTEXITCODE -ne 0) {
    Write-Host "Push failed (see above). Fix it and re-run." -ForegroundColor Red
    Read-Host "Press Enter to close"; exit 1
}
Write-Host "Pushed." -ForegroundColor Green

# 9. Optional: tag macos-v1.0.0 to build + publish the macOS pre-release.
$tagAns = Read-Host "Create and push tag macos-v1.0.0 now to build and publish the macOS pre-release? (y/N)"
if ($tagAns -match '^(y|Y)') {
    git tag macos-v1.0.0
    git push origin macos-v1.0.0
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Tag pushed. Watch it build here:" -ForegroundColor Green
        Write-Host "  https://github.com/lucduguaysita/G915-Stutter-Fix/actions"
        Write-Host "The macOS pre-release will appear here (your Windows 3.3.1 stays 'Latest'):" -ForegroundColor Green
        Write-Host "  https://github.com/lucduguaysita/G915-Stutter-Fix/releases"
    } else {
        Write-Host "Could not push the tag (it may already exist)." -ForegroundColor Red
    }
} else {
    Write-Host "Skipped tagging. When ready:  git tag macos-v1.0.0 ; git push origin macos-v1.0.0" -ForegroundColor Yellow
}

Write-Host ""
Read-Host "Press Enter to close"
