param(
    [Parameter(Mandatory)]
    [string] $PackagePath,
    [string] $PreviousPackagePath
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'The MSIX smoke test requires Windows and PowerShell 7.' }
Import-Module Appx -UseWindowsPowerShell -ErrorAction Stop
$repoRoot = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $repoRoot 'tools/MicrosoftStore/SpanDraft.MicrosoftStore.csproj'
dotnet build $helper --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Packaging helper build failed (exit code $LASTEXITCODE)." }

function Read-TestPackage {
    param([string] $Path)
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Expected a non-empty MSIX: $Path" }
    $output = dotnet run --project $helper --configuration Release --no-build -- verify $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "MSIX validation failed: $Path" }
    $info = ($output -join "`n") | ConvertFrom-Json
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -cne $info.Publisher) {
        throw "A valid, already trusted local test signature matching the manifest Publisher is required: $Path. See docs/RELEASING.md."
    }
    return [pscustomobject]@{ Path = $file.FullName; Info = $info }
}

$current = Read-TestPackage -Path $PackagePath
$previous = if ($PreviousPackagePath) { Read-TestPackage -Path $PreviousPackagePath } else { $null }
if ($previous -and ($previous.Info.Name -cne $current.Info.Name -or $previous.Info.Publisher -cne $current.Info.Publisher -or
        $previous.Info.Architecture -cne $current.Info.Architecture -or [version]$previous.Info.Version -ge [version]$current.Info.Version)) {
    throw 'Update test requires the same Identity/Publisher/architecture and a strictly newer package version.'
}
$identity = $current.Info.Name
if (Get-AppxPackage -Name $identity) { throw 'Refusing to replace an existing SpanDraft MSIX. Use a disposable test account or VM.' }
if (Get-Process -Name 'SpanDraft.Desktop' -ErrorAction SilentlyContinue) { throw 'Refusing to run while SpanDraft is already running.' }
foreach ($path in @('Programs/SpanDraft', 'SpanDraft')) {
    if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA $path)) { throw "Existing SpanDraft installation or user data found: $path. Use a disposable test account or VM." }
}
foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine)) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
        try {
            $key = $registry.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\SpanDraft_is1')
            if ($key) { $key.Dispose(); throw 'Existing Inno installation found. Use a disposable test account or VM.' }
        } finally { $registry.Dispose() }
    }
}

function Get-TestInstallation {
    param($Expected)
    $installed = Get-AppxPackage -Name $identity
    if (-not $installed -or @($installed).Count -ne 1 -or $installed.Publisher -cne $Expected.Info.Publisher -or
        $installed.Version.ToString() -cne $Expected.Info.Version -or $installed.Architecture.ToString() -ine 'X64') {
        throw 'Installed package Identity, version or architecture does not match the tested MSIX.'
    }
    foreach ($relative in @('SpanDraft.Desktop.exe', 'coreclr.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.txt', 'Help/index.html', 'Assets/StoreLogo.png')) {
        $file = Get-Item -LiteralPath (Join-Path $installed.InstallLocation $relative)
        if ($file.PSIsContainer -or $file.Length -eq 0) { throw "Missing or empty installed content: $relative" }
    }
    return $installed
}

$application = $null
function Start-TestApplication {
    param($Installed)
    $executable = Join-Path $Installed.InstallLocation 'SpanDraft.Desktop.exe'
    $started = [DateTime]::UtcNow
    Start-Process -FilePath explorer.exe -ArgumentList "shell:AppsFolder\$($Installed.PackageFamilyName)!$($current.Info.ApplicationId)" | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $process = Get-Process -Name 'SpanDraft.Desktop' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $executable -and $_.StartTime.ToUniversalTime() -ge $started } | Select-Object -First 1
        if ($process) {
            $script:application = $process
            if ($process.WaitForExit(5000)) { throw "Packaged SpanDraft exited during startup (exit code $($process.ExitCode))." }
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'No fresh SpanDraft process started from the registered package location.'
}

function Stop-TestApplication {
    if ($null -eq $script:application -or $script:application.HasExited) { return }
    $null = $script:application.CloseMainWindow()
    if (-not $script:application.WaitForExit(5000)) {
        $script:application.Kill()
        if (-not $script:application.WaitForExit(5000)) { throw 'The smoke-test process could not be stopped.' }
    }
}

$installationAttempted = $false
$ownedPackageFamily = $null
try {
    $first = if ($previous) { $previous } else { $current }
    $installationAttempted = $true
    Add-AppxPackage -Path $first.Path -ErrorAction Stop
    $installed = Get-TestInstallation -Expected $first
    $ownedPackageFamily = $installed.PackageFamilyName
    Start-TestApplication -Installed $installed
    Stop-TestApplication
    if ($previous) {
        $stateDirectory = Join-Path $env:LOCALAPPDATA "Packages/$ownedPackageFamily/LocalState"
        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
        $marker = Join-Path $stateDirectory "DEV-23-smoke-$([Guid]::NewGuid().ToString('N')).txt"
        $markerValue = [Guid]::NewGuid().ToString('N')
        Set-Content -LiteralPath $marker -Value $markerValue -Encoding utf8
        Add-AppxPackage -Path $current.Path -ErrorAction Stop
        $installed = Get-TestInstallation -Expected $current
        if ($installed.PackageFamilyName -cne $ownedPackageFamily -or (Get-Content -LiteralPath $marker -Raw).Trim() -cne $markerValue) {
            throw 'Package family or smoke-test package data changed during update.'
        }
        Start-TestApplication -Installed $installed
        Stop-TestApplication
        Remove-Item -LiteralPath $marker
    }
    Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop
    if (Get-AppxPackage -Name $identity) { throw 'Package registration remains after uninstallation.' }
    $installationAttempted = $false
    Write-Host 'MSIX smoke test passed: package validation, signature, installation, registered launch, process path, optional update/data marker, uninstallation.'
    Write-Host 'File association, interactive project/persistence/recovery/PDF behavior, real application data across updates and printing still require the manual checks in docs/RELEASING.md.'
} finally {
    try { Stop-TestApplication } finally {
        if ($installationAttempted) {
            $remaining = Get-AppxPackage -Name $identity
            if ($remaining -and $remaining.Publisher -ceq $current.Info.Publisher -and
                ($null -eq $ownedPackageFamily -or $remaining.PackageFamilyName -ceq $ownedPackageFamily)) {
                Remove-AppxPackage -Package $remaining.PackageFullName -ErrorAction Stop
            }
        }
    }
}
