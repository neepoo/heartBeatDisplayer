$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'get-package-info.ps1'
[xml]$props = Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
foreach ($rid in @('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')) {
    $info = & $script -RuntimeIdentifier $rid
    $extension = if ($rid -eq 'linux-x64') { 'tar.gz' } else { 'zip' }
    if ($info.PackageName -ne "HeartBeat-$version-$rid" -or $info.ArchiveName -ne "$($info.PackageName).$extension" -or $info.IsPrerelease -ne $version.Contains('-')) {
        throw "Incorrect package metadata for $rid"
    }
    if (-not (Test-Path (Join-Path (Split-Path $PSScriptRoot) $info.ProjectPath))) { Write-Output "INFO entry project not yet present: $($info.ProjectPath)" }
    Write-Output "PASS $rid package metadata"
}
$rejected = $false
try { & $script -Tag "v$version-mismatch" | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw 'Mismatched tag was accepted.' }
& $script -Tag "v$version" | Out-Null
Write-Output 'PASS matching tag accepted and mismatched tag rejected'
