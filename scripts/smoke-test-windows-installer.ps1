param(
    [Parameter(Mandatory)]
    [string] $InstallerPath
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) { throw 'The installer smoke test requires Windows and PowerShell 7.' }
$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.PSIsContainer -or $installer.Length -eq 0) { throw 'Expected a non-empty installer file.' }

$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/SpanDraft'
$executable = Join-Path $installDirectory 'SpanDraft.Desktop.exe'
$projectIcon = Join-Path $installDirectory 'spandraft-project.ico'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceIcon = Join-Path $repoRoot 'src/SpanDraft.Desktop/Assets/ProjectFileIcon/spandraft-project.ico'
$fixture = Get-Item -LiteralPath (Join-Path $repoRoot 'tests/SpanDraft.Tests/Fixtures/V1/rectangle.spandraft')
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) "SpanDraft smoke $([Guid]::NewGuid().ToString('N'))"
$projectPath = Join-Path $testDirectory 'Shell opened project.spandraft'
$associationKeys = @(
    'Registry::HKEY_CURRENT_USER\Software\Classes\.spandraft'
    'Registry::HKEY_CURRENT_USER\Software\Classes\SpanDraft.Project'
)
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs', 'DoNotVerify')) 'SpanDraft.lnk'
$desktopShortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory', 'DoNotVerify')) 'SpanDraft.lnk')
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory', 'DoNotVerify')) 'SpanDraft.lnk')
)

foreach ($path in @($installDirectory, $startMenuShortcut) + $desktopShortcuts) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to touch an existing installation or shortcut: $path" }
}
if (Get-Process -Name 'SpanDraft.Desktop' -ErrorAction SilentlyContinue) { throw 'Refusing to run while SpanDraft is already running.' }
foreach ($name in @('recovery.json', 'setup-recovery.json')) {
    $path = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "SpanDraft/$name"
    if (Test-Path -LiteralPath $path) { throw "Refusing to touch existing recovery data: $path" }
}
# Inno can reuse a previously registered custom directory instead of its default.
foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            foreach ($subkey in @(
                'Software\Microsoft\Windows\CurrentVersion\Uninstall\SpanDraft_is1'
                'Software\Classes\.spandraft'
                'Software\Classes\SpanDraft.Project'
                'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.spandraft\UserChoice'
            )) {
                $key = $registry.OpenSubKey($subkey)
                if ($key) {
                    $key.Dispose()
                    throw "Refusing to touch an existing installation or file association: $hive/$subkey"
                }
            }
        } finally {
            $registry.Dispose()
        }
    }
}

function Assert-AssociationValue {
    param([string] $Path, [string] $Expected)
    $key = Get-Item -LiteralPath $Path
    try {
        $actual = $key.GetValue('')
        if ($actual -cne $Expected) { throw "Unexpected association value at ${Path}: '$actual'; expected '$Expected'." }
    } finally {
        $key.Dispose()
    }
}

function Stop-TestApplication {
    param([System.Diagnostics.Process] $Process)
    if ($null -eq $Process -or $Process.HasExited) { return }
    $null = $Process.CloseMainWindow()
    if (-not $Process.WaitForExit(5000)) {
        $Process.Kill()
        if (-not $Process.WaitForExit(5000)) { throw 'The smoke-test application could not be stopped.' }
    }
}

function Uninstall-TestApplication {
    $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Uninstallation failed (exit code $($process.ExitCode))." }
    # Inno's temporary uninstaller can finish removing files after its launcher exits.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $remaining = @(@($executable, $projectIcon, $startMenuShortcut) + $associationKeys |
            Where-Object { Test-Path -LiteralPath $_ })
        if ($remaining.Count -eq 0) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Installed files, shortcut or associations remain after uninstallation: $($remaining -join ', ')"
}

$application = $null
$installationAttempted = $false
$uninstalled = $false
try {
    $installationAttempted = $true
    $setup = Start-Process -FilePath $installer.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' -PassThru -Wait
    if ($setup.ExitCode -ne 0) { throw "Installation failed (exit code $($setup.ExitCode))." }

    foreach ($relativePath in @('SpanDraft.Desktop.exe', 'spandraft-project.ico', 'THIRD_PARTY_NOTICES.txt', 'LICENSE', 'Help/index.html', 'unins000.exe')) {
        $path = Join-Path $installDirectory $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Missing or empty installed file: $path"
        }
    }
    if ((Get-FileHash -LiteralPath $projectIcon).Hash -cne (Get-FileHash -LiteralPath $sourceIcon).Hash) {
        throw 'The installed project icon differs from the checked-in ICO.'
    }
    Assert-AssociationValue -Path $associationKeys[0] -Expected 'SpanDraft.Project'
    Assert-AssociationValue -Path $associationKeys[1] -Expected 'SpanDraft Project'
    Assert-AssociationValue -Path "$($associationKeys[1])\DefaultIcon" -Expected "`"$projectIcon`",0"
    Assert-AssociationValue -Path "$($associationKeys[1])\shell\open\command" -Expected "`"$executable`" `"%1`""
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) { throw 'The Start menu shortcut is missing.' }
    foreach ($path in $desktopShortcuts) {
        if (Test-Path -LiteralPath $path) { throw "Silent installation unexpectedly created a desktop shortcut: $path" }
    }

    $null = New-Item -ItemType Directory -Path $testDirectory
    Copy-Item -LiteralPath $fixture.FullName -Destination $projectPath
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $projectPath
    $start.WorkingDirectory = $testDirectory
    $start.UseShellExecute = $true
    $start.Verb = 'open'
    $application = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $application) { throw 'The Windows shell did not return a SpanDraft process.' }
    if ($application.WaitForExit(5000)) {
        throw "The installed application exited during startup (exit code $($application.ExitCode))."
    }
    $expectedTitle = "$([IO.Path]::GetFileName($projectPath)) — SpanDraft"
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $application.Refresh()
        if ($application.HasExited) { throw 'SpanDraft exited before loading the shell-opened project.' }
        if ($application.MainWindowTitle -ceq $expectedTitle) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($application.MainModule.FileName -ine $executable -or $application.MainWindowTitle -cne $expectedTitle) {
        throw "Shell-open did not load the expected project in the installed application. Window title: '$($application.MainWindowTitle)'"
    }
    Stop-TestApplication -Process $application
    Uninstall-TestApplication
    $uninstalled = $true
    Write-Host 'Installer smoke test passed: install, verify associations and icon, shell-open project, stop and uninstall.'
} finally {
    try {
        Stop-TestApplication -Process $application
    } catch {
        Write-Warning "Smoke-test process cleanup failed: $($_.Exception.Message)"
    }
    if ($installationAttempted -and -not $uninstalled -and (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        try {
            Uninstall-TestApplication
        } catch {
            Write-Warning "Smoke-test installation cleanup failed: $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory -Recurse -Force }
}
