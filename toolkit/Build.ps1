param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\Toolkit'))
$ErrorActionPreference='Stop'
$sync=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$out=(New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$stage=Join-Path $out 'build'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$common=@('/nologo','/target:winexe','/optimize+','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll','/reference:System.Security.dll')
foreach($edition in @('guild','auction','bank')){
    $addon=(New-Item -ItemType Directory -Path (Join-Path $stage $edition) -Force).FullName
    Copy-Item (Join-Path $sync 'addon\*') -Destination $addon -Force
    $mode=if($edition -eq 'auction'){'auctions'}else{$edition}
    [IO.File]::WriteAllText((Join-Path $addon 'Edition.lua'),"TwilightHammerSync_Edition='$mode'")
    $toc=Join-Path $addon 'TwilightHammerSync.toc'
    [IO.File]::WriteAllText($toc,[IO.File]::ReadAllText($toc).Replace('TwilightHammerSync.lua',"Edition.lua`r`nTwilightHammerSync.lua"))
    $options=$common+@("/out:$stage\$edition.exe")
    if($edition -eq 'auction'){$options+='/define:AUCTION_ONLY'}
    if($edition -eq 'bank'){$options+='/define:BANK_ENABLED'}
    $options+=Get-ChildItem $addon -File | ForEach-Object {"/resource:$($_.FullName),$($_.Name)"}
    & $compiler @options (Join-Path $sync 'TwilightSetup.cs')
    if($LASTEXITCODE){throw "Sync build failed for $edition"}
}
$icon=Join-Path $PSScriptRoot 'TwilightHammer.ico'
$sdk=Join-Path $sync 'toolkit-deps\sdk'
if(!(Test-Path (Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    $sdkArchive=Join-Path $stage 'webview2-sdk.zip'
    $sdkVersion='1.0.2903.40'
    $sdkUrl="https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg"
    Invoke-WebRequest -UseBasicParsing -Uri $sdkUrl -OutFile $sdkArchive
    Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdk -Force
}
$options=$common+@('/reference:System.Drawing.dll','/reference:System.IO.Compression.dll',"/win32icon:$icon","/out:$out\TwilightHammerToolkit.exe")
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){
    $file=(Resolve-Path (Join-Path $sdk "lib\net462\$name")).Path
    $options+=@("/reference:$file","/resource:$file,$name")
}
foreach($arch in @('x86','x64','arm64')){
    $file=(Resolve-Path (Join-Path $sdk "runtimes\win-$arch\native\WebView2Loader.dll")).Path
    $options+="/resource:$file,WebView2Loader.$arch.dll"
}
$options+="/resource:$(Join-Path $PSScriptRoot 'Toolkit.html'),Toolkit.html"
foreach($asset in @('LauncherHero.jpg','LauncherHammers.png')){
    $options+="/resource:$(Join-Path $PSScriptRoot $asset),$asset"
}
$options+=@('guild.exe','auction.exe','bank.exe') | ForEach-Object {"/resource:$(Join-Path $stage $_),$_"}
$source=Get-ChildItem $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ToolkitPackages.cs')),'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
if(!$version){throw 'Toolkit version was not found'}
$versionFile=Join-Path $stage 'ToolkitVersion.cs'
[IO.File]::WriteAllText($versionFile,@"
using System.Reflection;
[assembly: AssemblyTitle("Twilight Hammer Toolkit")]
[assembly: AssemblyProduct("Twilight Hammer Toolkit")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
"@)
& $compiler @options @source $versionFile
if($LASTEXITCODE){throw 'Toolkit build failed'}
Copy-Item -LiteralPath (Join-Path $sync 'LICENSE'),(Join-Path $PSScriptRoot 'README.txt') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE.txt') -Destination (Join-Path $out 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdk 'NOTICE.txt') -Destination (Join-Path $out 'WebView2-NOTICE.txt') -Force
Write-Output (Join-Path $out 'TwilightHammerToolkit.exe')
