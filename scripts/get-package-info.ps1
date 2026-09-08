param([string]$Tag)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/HeartBeat.App/HeartBeat.App.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][\w.-]+)?$') { throw 'Invalid application version.' }
if ($Tag -and $Tag -cne "v$version") { throw "Tag '$Tag' must match application version 'v$version'." }
[pscustomobject]@{
    Version = $version
    PackageName = "HeartBeat-$version-win-x64"
    IsPrerelease = $version.Contains('-')
}
