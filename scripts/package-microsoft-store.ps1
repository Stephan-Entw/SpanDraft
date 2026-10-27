param(
    [switch] $Development
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $repoRoot 'tools/MicrosoftStore/SpanDraft.MicrosoftStore.csproj'
$project = Join-Path $repoRoot 'src/SpanDraft.Desktop/SpanDraft.Desktop.csproj'

function Get-ProductVersion {
    $output = dotnet msbuild $project -nologo -getProperty:Version -property:Configuration=Release -property:RuntimeIdentifier=win-x64 -property:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw "MSBuild version query failed (exit code $LASTEXITCODE)." }
    return ($output -join "`n").Trim()
}

function Invoke-StoreTool {
    param([string[]] $Arguments)
    $output = dotnet run --project $helper --configuration Release --no-build -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Microsoft Store validation failed (exit code $LASTEXITCODE)." }
    return ($output -join "`n") | ConvertFrom-Json
}

Push-Location -LiteralPath $repoRoot
try {
    if (-not $IsWindows -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'Microsoft Store packaging requires Windows and PowerShell 7.' }
    foreach ($command in @('dotnet', 'git')) {
        if (-not (Get-Command $command -CommandType Application -ErrorAction SilentlyContinue)) { throw "Required command not found: $command. Install prerequisites outside this script." }
    }
    $sdk = dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') { throw 'The repository requires an installed .NET 10 SDK.' }
    $localHelp = Join-Path $repoRoot 'help/.venv/Scripts/zensical.exe'
    if (-not (Test-Path -LiteralPath $localHelp -PathType Leaf) -and -not (Get-Command zensical.exe -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Offline help requires Python 3.11+ and help/requirements.txt installed in help/.venv or on PATH. See help/README.md; this script installs nothing.'
    }
    $makeAppx = Get-Command MakeAppx.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if (-not $makeAppx) {
        $candidates = foreach ($programDirectory in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ([string]::IsNullOrWhiteSpace($programDirectory)) { continue }
            $sdkBin = Join-Path $programDirectory 'Windows Kits/10/bin'
            if (Test-Path -LiteralPath $sdkBin -PathType Container) {
                Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object Name -Match '^10\.0\.\d+\.\d+$' |
                    Sort-Object { [version] $_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' }
            }
        }
        $makeAppx = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (-not $makeAppx) { throw 'MakeAppx.exe was not found on PATH or in Windows Kits/10/bin/<version>/x64. Install a Windows 10/11 SDK outside this script.' }
    $makePri = Join-Path (Split-Path -Parent $makeAppx) 'makepri.exe'
    if (-not (Test-Path -LiteralPath $makePri -PathType Leaf)) {
        $makePri = Get-Command MakePri.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    }
    if (-not $makePri) { throw 'MakePri.exe was not found beside MakeAppx.exe or on PATH. Install the Windows SDK resource tools outside this script.' }

    dotnet build $helper --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Packaging helper build failed (exit code $LASTEXITCODE)." }
    $mode = if ($Development) { 'Development' } else { 'Submission' }
    $version = Get-ProductVersion
    $source = Invoke-StoreTool -Arguments @('validate-source', $repoRoot, $version, $mode)
    $baseDirectory = Join-Path $repoRoot 'artifacts/microsoft-store'
    $finalDirectory = Join-Path $baseDirectory ($mode.ToLowerInvariant())
    $work = Join-Path $baseDirectory ('.work-' + $mode.ToLowerInvariant())
    foreach ($directory in @($work, $finalDirectory)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    }
    $publish = Join-Path $work 'publish'
    $staging = Join-Path $work 'staging'
    $result = Join-Path $work 'result'
    New-Item -ItemType Directory -Path $result -Force | Out-Null

    dotnet run --file scripts/generate-third-party-notices.cs -- --check
    if ($LASTEXITCODE -ne 0) { throw "Third-party notices check failed (exit code $LASTEXITCODE)." }
    dotnet run --file scripts/build-help.cs -- --no-regenerate-assets
    if ($LASTEXITCODE -ne 0) { throw "Offline help build failed (exit code $LASTEXITCODE)." }
    dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $publish
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed (exit code $LASTEXITCODE)." }
    $null = Invoke-StoreTool -Arguments @('stage', $repoRoot, $publish, $staging, $version)
    $priConfig = Join-Path $work 'priconfig.xml'
    & $makePri createconfig /cf $priConfig /dq en-US /o
    if ($LASTEXITCODE -ne 0) { throw "MakePri configuration failed (exit code $LASTEXITCODE)." }
    # Keep every scale/theme in this MSIX instead of creating separate resource packages.
    [xml] $priSettings = Get-Content -LiteralPath $priConfig -Raw
    $null = $priSettings.resources.RemoveChild($priSettings.resources.packaging)
    $priSettings.Save($priConfig)
    & $makePri new /pr $staging /cf $priConfig /mn (Join-Path $staging 'AppxManifest.xml') /of (Join-Path $staging 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { throw "MakePri indexing failed (exit code $LASTEXITCODE)." }
    & $makePri dump /if (Join-Path $staging 'resources.pri') /of (Join-Path $work 'resources-dump.xml') /dt detailed /o
    if ($LASTEXITCODE -ne 0) { throw "MakePri index verification failed (exit code $LASTEXITCODE)." }

    $suffix = if ($Development) { '-development' } else { '' }
    $packageName = "SpanDraft-$($source.MsixVersion)-x64$suffix.msix"
    $packagePath = Join-Path $result $packageName
    & $makeAppx pack /d $staging /p $packagePath /h SHA256 /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed (exit code $LASTEXITCODE)." }
    $package = Invoke-StoreTool -Arguments @('verify', $packagePath, $version, $staging)
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (Join-Path $staging 'AppxManifest.xml') -Destination $result
    "$hash  $packageName" | Set-Content -LiteralPath (Join-Path $result "$packageName.sha256") -Encoding utf8
    # Re-query instead of overriding Version: the central source and effective build must still agree.
    $finalSource = Invoke-StoreTool -Arguments @('validate-source', $repoRoot, (Get-ProductVersion), $mode, $source.Commit)
    if ($finalSource.ProductVersion -cne $source.ProductVersion) { throw 'Product version changed during packaging.' }
    [ordered]@{ Source = $source; FinalSource = $finalSource; Package = $package; Sha256 = $hash; Signed = $false; MakeAppx = $makeAppx; MakePri = $makePri } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $result 'build-info.json') -Encoding utf8
    Move-Item -LiteralPath $result -Destination $finalDirectory
    Write-Host "Mode: $mode"
    Write-Host "Source: $($source.Commit)"
    Write-Host "Unsigned MSIX: $(Join-Path $finalDirectory $packageName)"
} catch {
    [Console]::Error.WriteLine("Microsoft Store packaging failed: $($_.Exception.Message)")
    exit 1
} finally {
    Pop-Location
}
