param([Parameter(Mandatory=$true)][string]$AppRoot)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$app=[IO.Path]::GetFullPath($AppRoot)
$work=[IO.Path]::GetFullPath((Join-Path $project 'work'))
$isolated=Join-Path $work ('localization-artifacts-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
Push-Location $isolated
try {
    $catalogs=@('zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES')
    $sha=($catalogs | ForEach-Object { $_+'='+((Get-FileHash -Algorithm SHA256 (Join-Path $project ('assets/locales/'+$_+'.json'))).Hash.ToLowerInvariant()) }) -join ';'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $checker=Join-Path $isolated 'VerifyLocalizationArtifacts.exe'
    $response=Join-Path $isolated 'verify.rsp'
    [IO.File]::WriteAllLines($response,@('/nologo','/codepage:65001','/target:exe',('"/out:'+$checker+'"'),('"'+(Join-Path $PSScriptRoot 'localization/VerifyArtifacts.cs')+'"')),[Text.UTF8Encoding]::new($true))
    & $compiler ('@'+$response)
    if($LASTEXITCODE){throw 'Localization artifact checker compilation failed'}
    foreach($name in @('桌面伙伴.exe','DeskPet.Update.exe')) {
        $source=Join-Path $app $name
        $fixture=Join-Path $isolated $name
        if (!(Get-Item -LiteralPath $source).IsReadOnly) {
            try {
                New-Item -ItemType HardLink -Path $fixture -Target $source -ErrorAction Stop | Out-Null
                continue
            } catch { }
        }
        Copy-Item -LiteralPath $source -Destination $fixture
    }
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
        Write-Output ('PASS thirteen independent startup language lookups: '+$name)
        foreach($fixture in @(@{Culture='zh-TW';Code='zh_TW'},@{Culture='en-GB';Code='en_US'},@{Culture='ar-SA';Code='en_US'},@{Culture='fr-CA';Code='fr_FR'},@{Culture='de-DE';Code='de_DE'},@{Culture='hi-IN';Code='hi_IN'},@{Culture='id-ID';Code='id_ID'},@{Culture='it-IT';Code='it_IT'},@{Culture='pt-PT';Code='pt_BR'},@{Culture='es-ES';Code='es_ES'},@{Culture='es-MX';Code='es_419'})) {
            $actual=& $checker --system-language-lookup $fixture.Culture $fixture.Code (Join-Path $isolated $name)
            if($LASTEXITCODE){throw ('Standalone system language selection failed: '+$name+' '+$fixture.Culture)}
            $json=Get-Content -LiteralPath (Join-Path $project ('assets/locales/'+$fixture.Code+'.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
            $expected=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json.'common.cancel'))
            if($actual -ne $expected){throw ('Standalone system language lookup differs: '+$name+' '+$fixture.Culture)}
        }
        Write-Output ('PASS eleven system UI language startup lookups: '+$name)
    }
} finally {
    Pop-Location
    $fixtureRoot=Get-Item -LiteralPath $isolated
    if($fixtureRoot.Parent.FullName -ne $work -or $fixtureRoot.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)){throw 'Unexpected localization fixture directory'}
    Remove-Item -LiteralPath $fixtureRoot.FullName -Recurse -Force
}
