[CmdletBinding()]
param(
    [ValidateSet('All', 'de-DE', 'en-US')][string]$Language = 'All',
    [ValidateSet('All', 'Start', 'Project', 'Section', 'Report', 'Materials')][string]$Scene = 'All',
    [double]$WidthDip,
    [double]$HeightDip
)
$ErrorActionPreference = 'Stop'
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Native Store screenshots require Windows.' }
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $winapp = Get-Command winapp.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $project = Join-Path $PSScriptRoot 'SpanDraft.StoreScreenshots.csproj'
    $buildRoot = Join-Path $repoRoot 'artifacts/store-screenshots/build'
    $previousTelemetry = $env:AVALONIA_TELEMETRY_OPTOUT
    try {
        $env:AVALONIA_TELEMETRY_OPTOUT = '1'
        dotnet build $project --configuration Release --artifacts-path $buildRoot --nologo
    } finally { $env:AVALONIA_TELEMETRY_OPTOUT = $previousTelemetry }
    if ($LASTEXITCODE -ne 0) { throw "Screenshot harness build failed (exit code $LASTEXITCODE)." }
    $buildFinished = [DateTimeOffset]::UtcNow.ToString('O')
    $target = dotnet msbuild $project -nologo -getProperty:TargetPath -property:Configuration=Release "-property:ArtifactsPath=$buildRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the newly built harness executable.' }
    $executable = [IO.Path]::ChangeExtension([IO.Path]::GetFullPath(($target -join "`n").Trim()), '.exe')
    $arguments = @('run', '--repo', $repoRoot, '--winapp', $winapp.Source,
        '--language', $Language, '--scene', $Scene, '--build-finished-utc', $buildFinished)
    foreach ($parameter in @('WidthDip', 'HeightDip')) {
        if ($PSBoundParameters.ContainsKey($parameter)) {
            $flag = if ($parameter -eq 'WidthDip') { '--width-dip' } else { '--height-dip' }
            $arguments += @($flag, $PSBoundParameters[$parameter].ToString([Globalization.CultureInfo]::InvariantCulture))
        }
    }
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw "Store screenshot run failed (exit code $LASTEXITCODE). See artifacts/store-screenshots/result.json." }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
