param([Parameter(Mandatory=$true)][string]$AppRoot)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$app=[IO.Path]::GetFullPath($AppRoot)
$isolated=Join-Path $project ('work/localization-artifacts-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated | Out-Null
$sha=(Get-FileHash -Algorithm SHA256 (Join-Path $project 'assets/locales/zh_CN.json')).Hash.ToLowerInvariant()
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$checker=Join-Path $isolated 'VerifyLocalizationArtifacts.exe'
& $compiler /nologo /codepage:65001 /target:exe ('/out:'+$checker) (Join-Path $PSScriptRoot 'localization/VerifyArtifacts.cs')
if($LASTEXITCODE){throw 'Localization artifact checker compilation failed'}
foreach($name in @('桌面伙伴.exe','DeskPet.Update.exe')) {Copy-Item -LiteralPath (Join-Path $app $name) -Destination $isolated}
Push-Location $isolated
try {
    & $checker $sha (Join-Path $isolated '桌面伙伴.exe') (Join-Path $isolated 'DeskPet.Update.exe')
    if($LASTEXITCODE){throw 'Standalone WPF/helper localization validation failed'}
} finally {Pop-Location}
