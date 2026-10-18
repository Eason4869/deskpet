param([string[]]$Only=@())
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$logs=Join-Path $repo 'work/agent-evolution/full'
New-Item -ItemType Directory -Force $logs | Out-Null
$first=Get-Content -LiteralPath (Join-Path $repo 'Build.ps1') -First 1
$definition=[regex]::Match($first,'ValidateSet\((.*?)\)\]')
$later=@('App','AudioSmoke','SkinPackExport','OnboardingSmoke','OnboardingModelSmoke','OnboardingUpgradeTests','GuideMenuSmoke','DetailedGuideTests')
if($Only.Count){$later=@('App','AudioSmoke','SkinPackExport')}
$targets=@([regex]::Matches($definition.Groups[1].Value,"'([^']+)'")|ForEach-Object{$_.Groups[1].Value}|Where-Object{$_ -notin $later})
if($Only.Count){$targets=@($targets|Where-Object{$_ -in $Only})}
$results=@()
if($Only.Count -and (Test-Path -LiteralPath (Join-Path $logs 'results.json'))){$results=@(Get-Content -LiteralPath (Join-Path $logs 'results.json') -Raw|ConvertFrom-Json)}
foreach($target in $targets){
  $results=@($results|Where-Object {$_.target -ne $target})
  $watch=[Diagnostics.Stopwatch]::StartNew()
  try{
    $arguments=if($target -eq 'XiaobeiUiSmoke'){@(Join-Path $repo 'assets/bundled-skins/xiaobei/pet.json')}else{@()}
    & (Join-Path $repo 'Build.ps1') -Target $target -TestArguments $arguments *> (Join-Path $logs ($target+'.log'))
    $results+=@{target=$target;passed=$true;seconds=[math]::Round($watch.Elapsed.TotalSeconds,1)}
  }catch{$results+=@{target=$target;passed=$false;seconds=[math]::Round($watch.Elapsed.TotalSeconds,1);error=$_.Exception.Message}}
  $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $logs 'results.json') -Encoding UTF8
  $result=$results[-1];$prefix=if($result.passed){'PASS '}else{'FAIL '};Write-Output ($prefix+$target)
}
