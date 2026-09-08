param(
    [string]$Tag,
    [ValidateSet('', 'win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')]
    [string]$RuntimeIdentifier = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][\w.-]+)?$') { throw 'Invalid application version.' }
if ($Tag -and $Tag -cne "v$version") { throw "Tag '$Tag' must match application version 'v$version'." }
if (-not $RuntimeIdentifier) {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } elseif ($IsLinux) { 'linux' } else { throw 'Unsupported operating system.' }
    $RuntimeIdentifier = "$platform-$architecture"
}
$projectName = switch ($RuntimeIdentifier) {
    'win-x64' { 'HeartBeat.App' }
    'osx-arm64' { 'HeartBeat.Mac' }
    'osx-x64' { 'HeartBeat.Mac' }
    'linux-x64' { 'HeartBeat.Linux' }
    default { throw "Unsupported runtime: $RuntimeIdentifier" }
}
$packageName = "HeartBeat-$version-$RuntimeIdentifier"
$extension = if ($RuntimeIdentifier -eq 'linux-x64') { 'tar.gz' } else { 'zip' }
[pscustomobject]@{
    Version = $version
    RuntimeIdentifier = $RuntimeIdentifier
    ProjectPath = "src/$projectName/$projectName.csproj"
    PackageName = $packageName
    ArchiveName = "$packageName.$extension"
    IsPrerelease = $version.Contains('-')
}
