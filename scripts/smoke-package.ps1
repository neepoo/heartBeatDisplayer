#requires -Version 7.0
param(
    [string]$RuntimeIdentifier = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$info = & (Join-Path $PSScriptRoot 'get-package-info.ps1') -RuntimeIdentifier $RuntimeIdentifier
$smokeDirectory = Join-Path $projectRoot "artifacts/smoke-$($info.RuntimeIdentifier)-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $smokeDirectory -Force | Out-Null
$archive = Join-Path $projectRoot "artifacts/$($info.ArchiveName)"
$expected = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Package checksum mismatch.' }
$PublishDirectory = Join-Path $smokeDirectory 'unpacked'
New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
if ($IsMacOS) {
    & /usr/bin/ditto -x -k $archive $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'macOS archive extraction failed.' }
}
elseif ($IsLinux) {
    tar -xzf $archive -C $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive extraction failed.' }
}
else { Expand-Archive -LiteralPath $archive -DestinationPath $PublishDirectory }
$start = [Diagnostics.ProcessStartInfo]::new()
$start.UseShellExecute = $false
if ($IsMacOS) {
    $bundles = @(Get-ChildItem -LiteralPath $PublishDirectory -Directory -Filter '*.app')
    if ($bundles.Count -ne 1) { throw 'Expected one macOS app bundle.' }
    $start.FileName = '/usr/bin/open'
    foreach ($argument in @('-n', '-W', $bundles[0].FullName, '--args')) { $start.ArgumentList.Add($argument) }
    # Run the bundle executable first so native startup failures reach CI stderr.
    # LaunchServices does not forward application output or its exit status.
    $directDirectory = Join-Path $smokeDirectory 'direct-exit-check'
    $directStart = [Diagnostics.ProcessStartInfo]::new()
    $directStart.UseShellExecute = $false
    $directStart.FileName = Join-Path $bundles[0].FullName 'Contents/MacOS/HeartBeat'
    foreach ($argument in @('--demo', '--smoke-test', '--settings-dir', $directDirectory)) { $directStart.ArgumentList.Add($argument) }
    $directProcess = [Diagnostics.Process]::Start($directStart)
    try {
        if (-not $directProcess.WaitForExit(120000)) { $directProcess.Kill($true); throw 'macOS bundle executable timed out.' }
        if ($directProcess.ExitCode -ne 0) { throw "macOS application exited with $($directProcess.ExitCode)" }
        $directReport = Get-Content -LiteralPath (Join-Path $directDirectory 'smoke-test.txt') -Raw
        if ($directReport -match '(?m)^FAIL' -or $directReport -notmatch 'SMOKE COMPLETE') { throw 'macOS direct executable checks failed.' }
        Write-Output 'PASS macOS bundle executable exit code 0'
    }
    finally { $directProcess.Dispose() }
}
else {
    $start.FileName = Join-Path $PublishDirectory $(if ($IsWindows) { 'HeartBeat.exe' } else { 'HeartBeat' })
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
}
foreach ($argument in @('--demo', '--smoke-test', '--settings-dir', $smokeDirectory)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(120000)) {
        $process.Kill($true)
        throw 'Packaged application smoke test timed out.'
    }
    if ($process.ExitCode -ne 0) { throw "Package launcher exited with $($process.ExitCode)" }
    $report = Get-Content -LiteralPath (Join-Path $smokeDirectory 'smoke-test.txt') -Raw
    if ($report -match '(?m)^FAIL' -or $report -notmatch '(?m)^PASS ' -or $report -notmatch 'SMOKE COMPLETE') {
        throw "Packaged smoke did not complete successfully: $report"
    }
    Write-Output $report
    if ($IsMacOS) {
        $evidence = 'PASS macOS LaunchServices startup and bundle executable exit code 0'
        Add-Content -LiteralPath (Join-Path $smokeDirectory 'smoke-test.txt') -Value $evidence
        Write-Output $evidence
    }
}
finally {
    $process.Dispose()
    if ($env:GITHUB_OUTPUT) { "smoke-directory=$smokeDirectory" >> $env:GITHUB_OUTPUT }
}
