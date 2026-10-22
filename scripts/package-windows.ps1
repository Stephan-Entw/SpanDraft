$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    if (-not $IsWindows) {
        throw 'Windows packaging requires Windows and Inno Setup 6.'
    }

    $compilerPath = $null
    $compiler = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($compiler) {
        $compilerPath = $compiler.Source
    } else {
        foreach ($programDirectory in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ([string]::IsNullOrWhiteSpace($programDirectory)) { continue }
            $candidate = Join-Path $programDirectory 'Inno Setup 6/ISCC.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $compilerPath = $candidate
                break
            }
        }
    }
    if (-not $compilerPath -and -not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidate = Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $compilerPath = $candidate
        }
    }
    if (-not $compilerPath) {
        throw 'ISCC.exe was not found. Install Inno Setup 6 outside this script or add its directory to PATH.'
    }

    $project = 'src/SpanDraft.Desktop/SpanDraft.Desktop.csproj'
    $versionOutput = dotnet msbuild $project -nologo -getProperty:Version -property:Configuration=Release -property:RuntimeIdentifier=win-x64
    if ($LASTEXITCODE -ne 0) { throw "MSBuild version query failed (exit code $LASTEXITCODE)." }
    $version = ($versionOutput -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'MSBuild returned an empty product version.' }

    $publishDirectory = Join-Path $repoRoot 'artifacts/windows/publish'
    $installerDirectory = Join-Path $repoRoot 'artifacts/windows/installer'
    $installerName = "SpanDraft-$version-win-x64-setup"
    $installerPath = Join-Path $installerDirectory "$installerName.exe"
    foreach ($directory in @($publishDirectory, $installerDirectory)) {
        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

    dotnet run --file scripts/generate-third-party-notices.cs -- --check
    if ($LASTEXITCODE -ne 0) { throw "Third-party notices check failed (exit code $LASTEXITCODE)." }

    dotnet run --file scripts/build-help.cs -- --no-regenerate-assets
    if ($LASTEXITCODE -ne 0) { throw "Offline help build failed (exit code $LASTEXITCODE)." }

    dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed (exit code $LASTEXITCODE)." }

    foreach ($requiredFile in @(
        (Join-Path $publishDirectory 'SpanDraft.Desktop.exe')
        (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.txt')
        (Join-Path $publishDirectory 'Help/index.html')
        (Join-Path $repoRoot 'LICENSE')
    )) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf) -or (Get-Item -LiteralPath $requiredFile).Length -eq 0) {
            throw "Missing or empty package input: $requiredFile"
        }
    }

    & $compilerPath "/DAppVersion=$version" "/DPublishDir=$publishDirectory" "/DRepoRoot=$repoRoot" `
        "/O$installerDirectory" "/F$installerName" (Join-Path $PSScriptRoot 'SpanDraft.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf) -or (Get-Item -LiteralPath $installerPath).Length -eq 0) {
        throw "Missing or empty installer: $installerPath"
    }

    Write-Host "Version: $version"
    Write-Host "Installer: $installerPath"
} catch {
    [Console]::Error.WriteLine("Windows packaging failed: $($_.Exception.Message)")
    exit 1
} finally {
    Pop-Location
}
