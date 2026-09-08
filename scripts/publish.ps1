#requires -Version 7.0
param(
    [switch]$SkipTests,
    [ValidateSet('', 'win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')]
    [string]$RuntimeIdentifier = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$info = & (Join-Path $PSScriptRoot 'get-package-info.ps1') -RuntimeIdentifier $RuntimeIdentifier
if (($info.RuntimeIdentifier.StartsWith('osx-') -and -not $IsMacOS) -or
    ($info.RuntimeIdentifier.StartsWith('win-') -and -not $IsWindows) -or
    ($info.RuntimeIdentifier.StartsWith('linux-') -and -not $IsLinux)) {
    throw 'Publish on the target operating system so its native package and permissions can be verified.'
}
# A fresh directory prevents stale files and never overwrites a running app.
$output = Join-Path $projectRoot "artifacts/$($info.PackageName)/$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $projectRoot "artifacts/$($info.ArchiveName)"
Push-Location $projectRoot
try {
    if (-not $SkipTests) { & ./scripts/test.ps1 -RuntimeIdentifier $info.RuntimeIdentifier }
    $publishArgs = @('publish', $info.ProjectPath, '-c', 'Release', '-r', $info.RuntimeIdentifier,
        '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $output)
    if ($IsMacOS) {
        # The macOS workload's bundle directory is separate from PublishDir (-o).
        $publishArgs += '-p:CodesignKey=-', '-p:CreatePackage=false', "-p:AppBundleDir=$(Join-Path $output 'HeartBeat.app')"
    }
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($IsMacOS) {
        $bundles = @(Get-ChildItem -LiteralPath $output -Directory -Filter '*.app')
        if ($bundles.Count -ne 1) { throw "Expected one .app bundle in $output" }
        & /usr/bin/codesign --verify --deep --strict $bundles[0].FullName
        if ($LASTEXITCODE -ne 0) { throw 'macOS ad hoc signature verification failed.' }
        & /usr/bin/ditto -c -k --sequesterRsrc --keepParent $bundles[0].FullName $archivePath
        if ($LASTEXITCODE -ne 0) { throw 'macOS archive creation failed.' }
    }
    else {
        Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $output '使用说明.md')
        if ($IsWindows) {
            Copy-Item -LiteralPath 'scripts/演示模式.cmd' -Destination $output
            Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archivePath -Force
        }
        else {
            Copy-Item -LiteralPath 'scripts/demo.sh' -Destination $output
            chmod +x (Join-Path $output 'HeartBeat') (Join-Path $output 'demo.sh')
            if ($LASTEXITCODE -ne 0) { throw 'Setting executable permissions failed.' }
            tar -czf $archivePath -C $output .
            if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed.' }
        }
    }
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$hash  $($info.ArchiveName)`n", [Text.UTF8Encoding]::new($false))
    if ($env:GITHUB_OUTPUT) { "publish-directory=$output" >> $env:GITHUB_OUTPUT }
    Write-Output "Published: $output"
    Write-Output "Archive: $archivePath"
}
finally { Pop-Location }
