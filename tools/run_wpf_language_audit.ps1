param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Locale,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskExecutable=(Resolve-Path -LiteralPath $Executable).Path
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskStarted=Get-Date
# Windows PowerShell must still record the real native exit code if stderr is nonempty.
$ErrorActionPreference='Continue'
& $taskExecutable $Locale $taskOutput *> (Join-Path $taskOutput 'process.log')
$taskExit=$LASTEXITCODE
$ErrorActionPreference='Stop'
$taskHex=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$taskExit),0).ToString('X8')
$taskMetadata=[ordered]@{locale=$Locale;exit_code=$taskExit;exit_hex=$taskHex;started_utc=$taskStarted.ToUniversalTime().ToString('O');finished_utc=[DateTime]::UtcNow.ToString('O')}
$taskMetadata | ConvertTo-Json | Out-File -LiteralPath (Join-Path $taskOutput 'process.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $taskOutput 'process.log')
if($taskExit -ne 0) {
    $taskName=[regex]::Escape([IO.Path]::GetFileName($taskExecutable))
    Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$taskStarted} -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -in @(1000,1001,1026) -and $_.Message -match $taskName } |
        Select-Object TimeCreated,Id,Message | ConvertTo-Json -Depth 4 |
        Out-File -LiteralPath (Join-Path $taskOutput 'native-events.json') -Encoding UTF8
    throw ('WPF language audit failed: '+$Locale+'; exit '+$taskExit+' / 0x'+$taskHex)
}
