param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Report)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Executable).Path)
$skinType=$assembly.GetType('Dafeiyu.SkinDefinition',$true)
$skin=[Activator]::CreateInstance($skinType,[object[]]@('e-bao','E','','',$true,'fixture','fixture'))
$cache=[Activator]::CreateInstance($assembly.GetType('Dafeiyu.AdditionalInteractions',$true))
$get=$cache.GetType().GetMethod('Get')
$actions=@('lifted','landing','feeding','happy','sleeping','special','special2','stumble','lifted-original','special2-original')
$watch=[Diagnostics.Stopwatch]::StartNew()
foreach($action in $actions){$null=$get.Invoke($cache,[object[]]@($skin,$action))}
$watch.Stop()
# Collection is limited to this isolated measurement process; never trim the app.
[GC]::Collect();[GC]::WaitForPendingFinalizers();[GC]::Collect()
$clips=$cache.GetType().GetField('clips',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($cache)
$pixels=[long]0
foreach($clip in $clips.Values){$pixels+=[long]$clip.Width*$clip.Height*$clip.Count}
$process=[Diagnostics.Process]::GetCurrentProcess();$process.Refresh()
[ordered]@{
 executable=$Executable
 sha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
 actions=$actions.Count
 retainedClips=$clips.Count
 decodedPixelAndMaskMB=[Math]::Round($pixels*5/1MB,2)
 privateMB=[Math]::Round($process.PrivateMemorySize64/1MB,2)
 workingSetMB=[Math]::Round($process.WorkingSet64/1MB,2)
 loadMilliseconds=$watch.ElapsedMilliseconds
}|ConvertTo-Json|Set-Content -LiteralPath $Report -Encoding UTF8
Get-Content -LiteralPath $Report
