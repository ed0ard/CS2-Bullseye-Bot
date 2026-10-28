param([string]$Dotnet = 'dotnet', [string]$Generator = '', [string]$GeneratorInstance = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $windows = $env:OS -eq 'Windows_NT'
    $rid = if ($windows) { 'win-x64' } else { 'linux-x64' }
    $build = "build/native-$rid"
    $library = if ($windows) { "$build/Release/BullseyeGeometry.dll" } else { "$build/libBullseyeGeometry.so" }
    $cmakeArgs = @('-S', 'native', '-B', $build)
    if ($windows) { $cmakeArgs += @('-A', 'x64') }
    else { $cmakeArgs += '-DCMAKE_BUILD_TYPE=Release' }
    if ($Generator) { $cmakeArgs += @('-G', $Generator) }
    if ($GeneratorInstance) { $cmakeArgs += "-DCMAKE_GENERATOR_INSTANCE=$GeneratorInstance" }
    & cmake @cmakeArgs
    if ($LASTEXITCODE -ne 0) { throw 'Native configure failed' }
    & cmake --build $build --config Release
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    & ctest --test-dir $build -C Release --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
    & $Dotnet run --project tests/AimSelection.Tests.csproj -c Release -- $library
    if ($LASTEXITCODE -ne 0) { throw 'Selection tests failed' }
    # Use a new output directory so an old binary cannot leak into the package.
    $stage = Join-Path 'artifacts' ([guid]::NewGuid().ToString('N'))
    $plugin = Join-Path $stage 'addons/counterstrikesharp/plugins/BotAimImprover'
    & $Dotnet build BotAimImprover.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    New-Item -ItemType Directory -Force "$plugin/native/$rid", "$plugin/gamedata" | Out-Null
    foreach ($file in 'BotAimImprover.dll', 'BotAimImprover.pdb', 'BotAimImprover.deps.json') {
        Copy-Item -LiteralPath "bin/Release/net10.0/$file" -Destination $plugin
    }
    Copy-Item -Path 'gamedata/deadeye*.json' -Destination "$plugin/gamedata"
    Copy-Item -LiteralPath $library -Destination "$plugin/native/$rid"
    Copy-Item -LiteralPath 'LICENSE', 'README.md' -Destination $plugin
    $archive = "$stage.zip"
    Compress-Archive -Path "$stage/addons" -DestinationPath $archive
    Write-Output "Package: $([IO.Path]::GetFullPath($archive))"
} finally { Pop-Location }
