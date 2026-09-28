param([string]$Dotnet = 'dotnet', [string]$Generator = '', [string]$GeneratorInstance = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $cmakeArgs = @('-S', 'native', '-B', 'build/native', '-A', 'x64')
    if ($Generator) { $cmakeArgs += @('-G', $Generator) }
    if ($GeneratorInstance) { $cmakeArgs += "-DCMAKE_GENERATOR_INSTANCE=$GeneratorInstance" }
    & cmake @cmakeArgs
    if ($LASTEXITCODE -ne 0) { throw 'Native configure failed' }
    & cmake --build build/native --config Release
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & ctest --test-dir build/native -C Release --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
    & $Dotnet run --project tests/AimSelection.Tests.csproj -c Release -- build/native/Release/BullseyeGeometry.dll
    if ($LASTEXITCODE -ne 0) { throw 'Selection tests failed' }
    # Use a new output directory so an old binary cannot leak into the package.
    $stage = Join-Path 'artifacts' ([guid]::NewGuid().ToString('N'))
    $plugin = Join-Path $stage 'addons/counterstrikesharp/plugins/BotAimImprover'
    & $Dotnet build BotAimImprover.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    New-Item -ItemType Directory -Force "$plugin/native/win-x64", "$plugin/gamedata" | Out-Null
    foreach ($file in 'BotAimImprover.dll', 'BotAimImprover.pdb', 'BotAimImprover.deps.json') {
        Copy-Item -LiteralPath "bin/Release/net10.0/$file" -Destination $plugin
    }
    Copy-Item -LiteralPath 'gamedata/deadeye.json' -Destination "$plugin/gamedata"
    Copy-Item -LiteralPath 'build/native/Release/BullseyeGeometry.dll' -Destination "$plugin/native/win-x64"
    Copy-Item -LiteralPath 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md' -Destination $plugin
    Copy-Item -LiteralPath 'docs' -Destination $plugin -Recurse
    $archive = "$stage.zip"
    Compress-Archive -Path "$stage/addons" -DestinationPath $archive
    Write-Output "Package: $([IO.Path]::GetFullPath($archive))"
} finally { Pop-Location }
