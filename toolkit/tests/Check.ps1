param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe=Join-Path ([IO.Path]::GetTempPath()) ('ToolkitHistoryCheck-'+[guid]::NewGuid().ToString('N')+'.exe')
try {
    foreach($test in @('HistoryCheck','TweakSafetyCheck','PresenceCheck','OctoPatchCheck','PatchDownloadCheck','ClientModsCheck','SettingsCheck')) {
        & $compiler /nologo "/out:$testExe" (Join-Path $PSScriptRoot ($test+'.cs'))
        if($LASTEXITCODE){throw "$test compilation failed"}
        & $testExe (Resolve-Path -LiteralPath $Executable).Path
        if($LASTEXITCODE){throw "$test failed"}
    }
} finally {
    if(Test-Path -LiteralPath $testExe){Remove-Item -LiteralPath $testExe}
}
