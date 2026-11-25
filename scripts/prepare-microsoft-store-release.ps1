<#
.SYNOPSIS
Creates everything for a Microsoft Store draft of a release tag and optionally uploads it.

.DESCRIPTION
Run on Windows from your normal checkout; its tooling is used and it is never switched.
The application comes from a temporary Git worktree of the tag (a sibling of the checkout):
  - from the tag: native screenshots (capture-screenshots.ps1) and the submission MSIX
    (package-microsoft-store.ps1), so nothing of the current development state gets in;
  - from this checkout: hero image, "What's new" export (of the tag's release notes), assembly
    and upload.
The result is one folder artifacts/store-release/<version>_<UTC timestamp>/ (git-ignored):
  screenshots/<culture>/01-hero.png ... 06-start.png   (Store order)
  whats-new/<culture>.txt
  SpanDraft-X.Y.Z.0-x64.msix
  release.json                                          (version, commits, hash, order)
The capture run is kept under artifacts/screenshots/<version>_<timestamp>/.

With -Upload the folder is then uploaded as a Partner Center draft (never submitted).
The worktree is removed after success and kept for diagnosis after a failure.

.EXAMPLE
pwsh ./scripts/prepare-microsoft-store-release.ps1 -Tag v1.2.0 -Upload
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string] $Tag,
    [switch] $Upload,
    [switch] $KeepWorktree
)
$ErrorActionPreference = 'Stop'

function Invoke-Checked([string] $What, [scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$worktree = Join-Path (Split-Path -Parent $repoRoot) "$(Split-Path -Leaf $repoRoot)-release-$Tag"
$originalPath = $env:PATH
$worktreeCreated = $false
$staging = $null
Push-Location -LiteralPath $repoRoot
try {
    # Everything that can fail is checked before the long capture and build steps.
    if (-not $IsWindows -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'Store release preparation requires Windows and PowerShell 7.' }
    foreach ($command in @('git', 'dotnet', 'node', 'npm', 'npx', 'pwsh')) {
        if (-not (Get-Command $command -CommandType Application -ErrorAction SilentlyContinue)) { throw "Required command not found: $command." }
    }
    $python = Join-Path $repoRoot 'help/.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
        $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    }
    if (-not $python) { throw 'Python 3 was not found (help/.venv or PATH).' }
    if ($Upload) { Invoke-Checked 'Store credential check' { & $python scripts/microsoft_store_draft.py --check-credentials } }
    # The worktree has no help/.venv of its own; packaging accepts Zensical from PATH.
    $helpScripts = Join-Path $repoRoot 'help/.venv/Scripts'
    if (Test-Path -LiteralPath (Join-Path $helpScripts 'zensical.exe') -PathType Leaf) {
        $env:PATH = $helpScripts + [IO.Path]::PathSeparator + $env:PATH
    } elseif (-not (Get-Command zensical.exe -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Offline help requires help/requirements.txt installed in help/.venv or on PATH. See help/README.md.'
    }
    if ($worktree -match '\\Users\\') { throw "The screenshot capture rejects personal paths. Use a checkout outside the user profile, e.g. C:\dev\SpanDraft (worktree would be $worktree)." }

    $commit = git rev-parse --verify --quiet "refs/tags/$Tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or -not $commit) { throw "Tag $Tag does not exist locally. Run: git fetch --tags" }
    $commit = $commit.Trim()
    $version = $Tag.Substring(1)

    Invoke-Checked 'git worktree prune' { git worktree prune }
    if (Test-Path -LiteralPath $worktree) {
        throw "A worktree from an earlier run exists. Remove it first: git worktree remove --force `"$worktree`""
    }
    Invoke-Checked 'git worktree add' { git worktree add --detach $worktree $commit }
    $worktreeCreated = $true

    [xml] $props = Get-Content -LiteralPath (Join-Path $worktree 'Directory.Build.props') -Raw
    $tagVersion = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1).Trim()
    if ($tagVersion -cne $version) { throw "Tag $Tag contains version $tagVersion." }
    Write-Host "Preparing Microsoft Store release $version from $Tag ($commit) in $worktree."

    $started = [DateTimeOffset]::UtcNow
    $name = $version + '_' + $started.UtcDateTime.ToString("yyyy-MM-dd_HH-mm-ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $releaseRoot = Join-Path $repoRoot 'artifacts/store-release'
    $release = Join-Path $releaseRoot $name
    if (Test-Path -LiteralPath $release) { throw "Release folder already exists: $release" }
    $staging = Join-Path $releaseRoot ".staging-$name"
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    # 1. "What's new" from the tag's release notes; fails here if they do not fit the Store.
    Push-Location -LiteralPath $worktree
    try { Invoke-Checked 'Release notes export' { & $python (Join-Path $repoRoot 'scripts/release_notes.py') export-store $version (Join-Path $staging 'whats-new') } }
    finally { Pop-Location }

    # 2. Native screenshots of the tagged application.
    # The run lives in the temporary worktree, so the tool must not open it in Explorer.
    # Tags up to v1.2.0 predate this switch and still open it.
    $previousNoOpen = $env:SPANDRAFT_SCREENSHOTS_NO_OPEN
    try {
        $env:SPANDRAFT_SCREENSHOTS_NO_OPEN = '1'
        Invoke-Checked 'Screenshot capture' { pwsh -NoProfile -File (Join-Path $worktree 'tools/Screenshots/capture-screenshots.ps1') }
    } finally { $env:SPANDRAFT_SCREENSHOTS_NO_OPEN = $previousNoOpen }
    # Older tags name runs without the version, so the run is identified by being the only one in the new worktree.
    $runs = @(Get-ChildItem -LiteralPath (Join-Path $worktree 'artifacts/screenshots') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'result.json') -PathType Leaf })
    if ($runs.Count -ne 1) { throw "Expected exactly one capture run in the worktree, found $($runs.Count)." }
    $manifest = Get-Content -LiteralPath (Join-Path $runs[0].FullName 'result.json') -Raw | ConvertFrom-Json
    if (-not $manifest.success) { throw "Screenshot run $($runs[0].Name) was not successful." }
    if ($manifest.PSObject.Properties['productVersion'] -and $manifest.productVersion -cne $version) {
        throw "Screenshot run $($runs[0].Name) captured version $($manifest.productVersion)."
    }
    $runName = if ($runs[0].Name.StartsWith("${version}_", [StringComparison]::Ordinal)) { $runs[0].Name } else { "${version}_$($runs[0].Name)" }
    $screenshotRoot = Join-Path $repoRoot 'artifacts/screenshots'
    $run = Join-Path $screenshotRoot $runName
    if (Test-Path -LiteralPath $run) { throw "Screenshot run already exists: $run" }
    New-Item -ItemType Directory -Path $screenshotRoot -Force | Out-Null
    Copy-Item -LiteralPath $runs[0].FullName -Destination $run -Recurse

    # 3. Hero image with this checkout's template.
    Push-Location -LiteralPath (Join-Path $repoRoot 'tools/Screenshots/Hero')
    try {
        if (-not (Test-Path 'node_modules')) { Invoke-Checked 'npm ci' { npm ci } }
        Invoke-Checked 'Chromium installation' { npx --no-install playwright install chromium }
        Invoke-Checked 'Hero rendering' { node render-hero.mjs --shots (Join-Path $run 'penpot') }
    } finally { Pop-Location }

    # 4. Submission MSIX; the tag's packaging script checks tag, clean tree and version itself.
    Invoke-Checked 'MSIX packaging' { pwsh -NoProfile -File (Join-Path $worktree 'scripts/package-microsoft-store.ps1') }
    $package = "SpanDraft-$version.0-x64.msix"
    $msix = Join-Path $worktree "artifacts/microsoft-store/submission/$package"
    if (-not (Test-Path -LiteralPath $msix -PathType Leaf)) { throw "Expected package not found: $msix" }
    if ((git -C $worktree rev-parse HEAD).Trim() -cne $commit) { throw 'The worktree HEAD changed during preparation.' }
    Copy-Item -LiteralPath $msix -Destination (Join-Path $staging $package)

    # 5. Assemble. Store order (as published): hero first, then the five Store motifs in capture numbering.
    $cultures = [ordered]@{ 'de-DE' = 'DE'; 'en-US' = 'EN' }
    $screenshots = [ordered]@{}
    foreach ($culture in $cultures.Keys) {
        $target = Join-Path $staging "screenshots/$culture"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $store = @(Get-ChildItem -LiteralPath (Join-Path $run "store/$($cultures[$culture])") -Filter "*-$culture.png" | Sort-Object Name)
        if ($store.Count -ne 5) { throw "Expected 5 Store screenshots for $culture, found $($store.Count)." }
        $sources = @(Join-Path $run "hero/hero-$culture.png") + @($store.FullName)
        $files = @()
        for ($i = 0; $i -lt $sources.Count; $i++) {
            if (-not (Test-Path -LiteralPath $sources[$i] -PathType Leaf)) { throw "Missing screenshot: $($sources[$i])" }
            $motif = if ($i -eq 0) { 'hero' } else { [IO.Path]::GetFileNameWithoutExtension($sources[$i]) -replace "^\d+-|-$culture$", '' }
            $file = '{0:D2}-{1}.png' -f ($i + 1), $motif
            Copy-Item -LiteralPath $sources[$i] -Destination (Join-Path $target $file)
            $files += "screenshots/$culture/$file"
        }
        $screenshots[$culture] = $files
    }
    $hash = (Get-FileHash -LiteralPath (Join-Path $staging $package) -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        version = $version; tag = $Tag; commit = $commit
        toolingCommit = (git rev-parse HEAD).Trim(); toolingDirty = [bool] (git status --porcelain)
        createdUtc = $started.ToString('O'); finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        screenshotRun = "artifacts/screenshots/$runName"
        package = [ordered]@{ file = $package; sha256 = $hash }
        whatsNew = [ordered]@{ 'de-DE' = 'whats-new/de-DE.txt'; 'en-US' = 'whats-new/en-US.txt' }
        screenshots = $screenshots
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'release.json') -Encoding utf8NoBOM
    Invoke-Checked 'Release folder validation' { & $python scripts/microsoft_store_draft.py $staging --check }
    Move-Item -LiteralPath $staging -Destination $release
    $staging = $null
    Write-Host "Store release folder: $release"

    if ($KeepWorktree) {
        Write-Host "Worktree kept: $worktree"
    } else {
        # Build servers can keep files of the worktree open.
        dotnet build-server shutdown | Out-Null
        git worktree remove --force $worktree
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove the worktree. Remove it later: git worktree remove --force `"$worktree`"" }
    }
    $worktreeCreated = $false

    if ($Upload) {
        & $python scripts/microsoft_store_draft.py $release
        if ($LASTEXITCODE -ne 0) { throw "Upload failed. Retry without rebuilding: python scripts/microsoft_store_draft.py `"$release`"" }
    } else {
        Write-Host "Upload as draft: python scripts/microsoft_store_draft.py `"$release`""
    }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    if ($worktreeCreated) { [Console]::Error.WriteLine("Worktree kept for diagnosis. Remove it with: git worktree remove --force `"$worktree`"") }
    exit 1
} finally {
    if ($staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force }
    $env:PATH = $originalPath
    Pop-Location
}
