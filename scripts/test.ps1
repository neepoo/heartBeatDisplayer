#requires -Version 7.0
param([string]$RuntimeIdentifier = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli'
$info = & (Join-Path $PSScriptRoot 'get-package-info.ps1') -RuntimeIdentifier $RuntimeIdentifier
Push-Location $projectRoot
try {
    & ./scripts/test-package-info.ps1
    dotnet run --project tests/HeartBeat.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet run --project tests/HeartBeat.Desktop.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed.' }
    dotnet run --project tests/HeartBeat.Desktop.Tests -c Release -- "artifacts/startup-warning-$([Guid]::NewGuid().ToString('N'))" --corrupt-settings
    if ($LASTEXITCODE -ne 0) { throw 'Corrupt settings startup regression failed.' }
    dotnet run --project tests/HeartBeat.Mac.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'macOS notification lifecycle tests failed.' }
    if ($info.RuntimeIdentifier -eq 'win-x64') {
        $directory = "artifacts/windows-native-$([Guid]::NewGuid().ToString('N'))"
        dotnet run --project tests/HeartBeat.App.Tests -c Release -- --demo --settings-dir $directory
        if ($LASTEXITCODE -ne 0) { throw 'Windows native checks failed.' }
    }
    if ($info.RuntimeIdentifier -eq 'linux-x64') {
        dotnet run --project tests/HeartBeat.Linux.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Linux adapter tests failed.' }
    }
    dotnet build $info.ProjectPath -c Release -r $info.RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) { throw 'Platform build failed.' }
}
finally { Pop-Location }
