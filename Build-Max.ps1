# Rebuild the standalone Windows x64 executable after changing Max's C# source.
$ErrorActionPreference = 'Stop'
$sdkExecutable = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdkExecutable)) {
    $sdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($sdkCommand) { $sdkExecutable = $sdkCommand.Source }
    else { throw 'Run Setup-Max.ps1 first to install the build tools.' }
}
$projectFile = Join-Path $PSScriptRoot 'Max.csproj'
$outputDirectory = Join-Path $PSScriptRoot 'bin\portable'
& $sdkExecutable build $projectFile --configuration Release --nologo --warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Build failed. The previous executable was kept.' }
& $sdkExecutable (Join-Path $PSScriptRoot 'bin\Release\net10.0\Max.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed. The previous executable was kept.' }
& $sdkExecutable publish $projectFile --configuration Release --runtime win-x64 --self-contained true --output $outputDirectory --nologo '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None'
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed. The previous executable was kept.' }
try {
    Copy-Item -LiteralPath (Join-Path $outputDirectory 'Max.exe') -Destination (Join-Path $PSScriptRoot 'max.exe') -Force
}
catch {
    throw 'The new build is ready in bin\portable, but max.exe could not be replaced. Close Max Studio and other Max windows, then run Build-Max.ps1 again. The previous executable was kept.'
}
Write-Host 'Built max.exe with its own runtime. No installation is needed to run it.'
