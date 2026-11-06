[CmdletBinding()]
param(
    [ValidateSet('All', 'de-DE', 'en-US')][string]$Language = 'All',
    [ValidateSet('All', 'Start', 'Project', 'Section', 'Report', 'Materials')][string]$Scene = 'All',
    [ValidateSet('All', 'Store', 'Penpot')][string]$Target = 'All',
    [ValidateRange(1, [int]::MaxValue)][int]$ScalePercent = 250,
    [ValidateRange(1, [int]::MaxValue)][int]$PenpotScalePercent = 200
)
$ErrorActionPreference = 'Stop'
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Native screenshots require Windows.' }
    if ($Target -ne 'Store' -and $Scene -notin @('All', 'Project')) {
        throw 'Penpot supports only Project. Use -Target Store for other individual scenes.'
    }
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $winapp = Get-Command winapp.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $project = Join-Path $PSScriptRoot 'SpanDraft.Screenshots.csproj'
    $buildRoot = Join-Path $repoRoot 'artifacts/screenshots/build'
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
        '--language', $Language, '--scene', $Scene, '--target', $Target,
        '--scale-percent', $ScalePercent.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--penpot-scale-percent', $PenpotScalePercent.ToString([Globalization.CultureInfo]::InvariantCulture),
        '--build-finished-utc', $buildFinished)
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw "Screenshot run failed (exit code $LASTEXITCODE). See the run-specific result path printed above, if available." }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
