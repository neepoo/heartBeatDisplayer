param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$output = Join-Path $projectRoot 'artifacts\HeartBeat-win-x64'
Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        dotnet run --project tests/HeartBeat.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    }
    dotnet publish src/HeartBeat.App/HeartBeat.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $output '使用说明.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\演示模式.cmd') -Destination $output
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $projectRoot 'artifacts\HeartBeat-win-x64.zip') -Force
    Write-Output "Published: $output"
}
finally { Pop-Location }
