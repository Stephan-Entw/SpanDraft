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
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs', 'DoNotVerify')) 'SpanDraft.lnk'
$desktopShortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory', 'DoNotVerify')) 'SpanDraft.lnk')
    (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory', 'DoNotVerify')) 'SpanDraft.lnk')
)

foreach ($path in @($installDirectory, $startMenuShortcut) + $desktopShortcuts) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to touch an existing installation or shortcut: $path" }
}
# Inno can reuse a previously registered custom directory instead of its default.
foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            $key = $registry.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\SpanDraft_is1')
            if ($key) {
                $key.Dispose()
                throw 'Refusing to touch an existing registered SpanDraft installation.'
            }
        } finally {
            $registry.Dispose()
        }
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
        if (-not (Test-Path -LiteralPath $executable) -and -not (Test-Path -LiteralPath $startMenuShortcut)) { return }
        Start-Sleep -Milliseconds 500
    }
    throw 'The executable or Start menu shortcut remains after uninstallation.'
}

$application = $null
$installationAttempted = $false
$uninstalled = $false
try {
    $installationAttempted = $true
    $setup = Start-Process -FilePath $installer.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' -PassThru -Wait
    if ($setup.ExitCode -ne 0) { throw "Installation failed (exit code $($setup.ExitCode))." }

    foreach ($relativePath in @('SpanDraft.Desktop.exe', 'THIRD_PARTY_NOTICES.txt', 'LICENSE', 'Help/index.html', 'unins000.exe')) {
        $path = Join-Path $installDirectory $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Missing or empty installed file: $path"
        }
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) { throw 'The Start menu shortcut is missing.' }
    foreach ($path in $desktopShortcuts) {
        if (Test-Path -LiteralPath $path) { throw "Silent installation unexpectedly created a desktop shortcut: $path" }
    }

    $application = Start-Process -FilePath $executable -WorkingDirectory $installDirectory -PassThru
    if ($application.WaitForExit(5000)) {
        throw "The installed application exited during startup (exit code $($application.ExitCode))."
    }
    Stop-TestApplication -Process $application
    Uninstall-TestApplication
    $uninstalled = $true
    Write-Host 'Installer smoke test passed: install, verify, launch, stop and uninstall.'
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
}
