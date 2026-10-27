# Works in Windows PowerShell 5.1 and PowerShell 7. Installs only inside this folder.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$sdkVersion = '10.0.401'
$sdkUrl = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip'
$sdkSha512 = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
$toolsDirectory = Join-Path $PSScriptRoot '.tools'
$sdkDirectory = Join-Path $toolsDirectory 'dotnet'
$sdkExecutable = Join-Path $sdkDirectory 'dotnet.exe'
$sdkArchive = Join-Path $toolsDirectory 'dotnet-sdk.zip'

if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    throw 'This setup downloads the Windows x64 SDK. Use the PowerShell 7 source runner on other platforms.'
}
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory "sdk\$sdkVersion"))) {
    if (-not (Test-Path -LiteralPath $sdkArchive) -or
        (Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash -ne $sdkSha512) {
        Write-Host "Downloading Microsoft .NET SDK $sdkVersion..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $sdkUrl -OutFile $sdkArchive
    }
    if ((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash -ne $sdkSha512) {
        throw 'The download does not match Microsoft''s SHA512 checksum. Run setup again to retry.'
    }
    Write-Host 'Unpacking the verified SDK...'
    Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkDirectory -Force
}
& $sdkExecutable --version
if ($LASTEXITCODE -ne 0) { throw 'The local SDK did not start.' }
& (Join-Path $PSScriptRoot 'Build-Max.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
Write-Host 'Ready. Run .\max.exe --strict .\first.max'
