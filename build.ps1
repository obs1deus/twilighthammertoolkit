$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (!(Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$dist = (New-Item -ItemType Directory -Force (Join-Path $root 'dist')).FullName
foreach ($edition in @('guild','auctions','bank')) {
    $stage = (New-Item -ItemType Directory -Force (Join-Path $dist $edition)).FullName
    $addon = (New-Item -ItemType Directory -Force (Join-Path $stage 'addon')).FullName
    Copy-Item (Join-Path $root 'addon\*') $addon -Force
    [IO.File]::WriteAllText((Join-Path $addon 'Edition.lua'), "TwilightHammerSync_Edition='$edition'")
    $toc = Join-Path $addon 'TwilightHammerSync.toc'
    $manifest = [IO.File]::ReadAllText($toc)
    $title = switch ($edition) {
        'guild' { 'Twilight Hammer Guild Sync' }
        'auctions' { 'Twilight Hammer Auction Only' }
        'bank' { 'Twilight Hammer Bank and Guild Sync' }
    }
    $manifest = $manifest.Replace('Title: Twilight Hammer Sync',"Title: $title").Replace('TwilightHammerSync.lua',"Edition.lua`r`nTwilightHammerSync.lua")
    [IO.File]::WriteAllText($toc,$manifest)
    $name = if ($edition -eq 'auctions') { 'THAuctionOnlySetup.exe' } else { 'TwilightHammerSetup.exe' }
    $options = @('/nologo','/target:winexe','/optimize+','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll',"/out:$stage\$name")
    if ($edition -eq 'auctions') { $options += '/define:AUCTION_ONLY' }
    if ($edition -eq 'bank') { $options += '/define:BANK_ENABLED' }
    $options += Get-ChildItem $addon -File | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
    & $csc @options (Join-Path $root 'TwilightSetup.cs')
    if ($LASTEXITCODE) { throw "Build failed for $edition." }
    Write-Output (Join-Path $stage $name)
}
