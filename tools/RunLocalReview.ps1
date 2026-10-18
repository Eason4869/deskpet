param([int]$Part=0,[int]$Parts=3)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$buildFirstLine=(Get-Content -LiteralPath (Join-Path $root 'Build.ps1') -First 1)
$matched=[regex]::Match($buildFirstLine,"ValidateSet\((.*?)\)\]")
$excluded=@('App','AudioSmoke','SkinPackExport','OnboardingSmoke','OnboardingModelSmoke','OnboardingUpgradeTests','GuideMenuSmoke')
$targets=@([regex]::Matches($matched.Groups[1].Value,"'([^']+)'")|ForEach-Object{$_.Groups[1].Value}|Where-Object{$_ -notin $excluded})
$results=@()
for($index=$Part;$index -lt $targets.Count;$index+=$Parts){
 $testTarget=$targets[$index]
 try { & (Join-Path $root 'Build.ps1') -Target $testTarget *> (Join-Path $root ('work\audit-'+$testTarget+'.log'));$results+=@{target=$testTarget;passed=$true};Write-Output ('PASS '+$testTarget) }
 catch {$results+=@{target=$testTarget;passed=$false;error=$_.ToString()};Write-Output ('FAIL '+$testTarget)}
 $results|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $root ('work\audit-part-'+$Part+'.json')) -Encoding UTF8
}
Write-Output ('Audit partition '+$Part+' complete: '+$results.Count+' suites')
