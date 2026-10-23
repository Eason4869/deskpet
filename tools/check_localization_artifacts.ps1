param([Parameter(Mandatory=$true)][string]$AppRoot)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$app=[IO.Path]::GetFullPath($AppRoot)
$isolated=Join-Path $project ('work/localization-artifacts-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
$catalogs=@('zh_CN','zh_TW','en_US','ja_JP','ko_KR')
$sha=($catalogs | ForEach-Object { $_+'='+((Get-FileHash -Algorithm SHA256 (Join-Path $project ('assets/locales/'+$_+'.json'))).Hash.ToLowerInvariant()) }) -join ';'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$checker=Join-Path $isolated 'VerifyLocalizationArtifacts.exe'
$response=Join-Path $isolated 'verify.rsp'
[IO.File]::WriteAllLines($response,@('/nologo','/codepage:65001','/target:exe',('"/out:'+$checker+'"'),('"'+(Join-Path $PSScriptRoot 'localization/VerifyArtifacts.cs')+'"')),[Text.UTF8Encoding]::new($true))
& $compiler ('@'+$response)
if($LASTEXITCODE){throw 'Localization artifact checker compilation failed'}
foreach($name in @('桌面伙伴.exe','DeskPet.Update.exe')) {Copy-Item -LiteralPath (Join-Path $app $name) -Destination $isolated}
Push-Location $isolated
try {
    & $checker $sha (Join-Path $isolated '桌面伙伴.exe') (Join-Path $isolated 'DeskPet.Update.exe')
    if($LASTEXITCODE){throw 'Standalone WPF/helper localization validation failed'}
    foreach($name in @('桌面伙伴.exe','DeskPet.Update.exe')) {
        foreach($locale in $catalogs) {
            $actual=& $checker --language-lookup $locale (Join-Path $isolated $name)
            if($LASTEXITCODE){throw ('Standalone language selection failed: '+$name+' '+$locale)}
            $json=Get-Content -LiteralPath (Join-Path $project ('assets/locales/'+$locale+'.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
            $expected=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json.'common.cancel'))
            if($actual -ne $expected){throw ('Standalone translated lookup differs: '+$name+' '+$locale)}
        }
        Write-Output ('PASS five independent startup language lookups: '+$name)
        foreach($fixture in @(@{Culture='zh-TW';Code='zh_TW'},@{Culture='en-GB';Code='en_US'},@{Culture='ar-SA';Code='en_US'})) {
            $actual=& $checker --system-language-lookup $fixture.Culture $fixture.Code (Join-Path $isolated $name)
            if($LASTEXITCODE){throw ('Standalone system language selection failed: '+$name+' '+$fixture.Culture)}
            $json=Get-Content -LiteralPath (Join-Path $project ('assets/locales/'+$fixture.Code+'.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
            $expected=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json.'common.cancel'))
            if($actual -ne $expected){throw ('Standalone system language lookup differs: '+$name+' '+$fixture.Culture)}
        }
        Write-Output ('PASS three system UI language startup lookups: '+$name)
    }
} finally {Pop-Location}
