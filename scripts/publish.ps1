param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$packageInfo = & (Join-Path $PSScriptRoot 'get-package-info.ps1')
$packageName = $packageInfo.PackageName
$output = Join-Path $projectRoot "artifacts\$packageName"
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
    $zipPath = Join-Path $projectRoot "artifacts\$packageName.zip"
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -Force
    $hashValue = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$hashValue  $packageName.zip"
    Write-Output "Published: $output"
}
finally { Pop-Location }
