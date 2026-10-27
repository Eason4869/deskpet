param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Locale,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$navigationExecutable=(Resolve-Path -LiteralPath $Executable).Path
$navigationOutput=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $navigationOutput -Force | Out-Null
$navigationStarted=Get-Date
$navigationLog=Join-Path $navigationOutput 'process.log'
$ErrorActionPreference='Continue'
& $navigationExecutable $Locale '--layout-boundary' *> $navigationLog
$navigationExit=$LASTEXITCODE
$ErrorActionPreference='Stop'
$navigationHex=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$navigationExit),0).ToString('X8')
[ordered]@{
    locale=$Locale;exit_code=$navigationExit;exit_hex=$navigationHex
    started_utc=$navigationStarted.ToUniversalTime().ToString('O');finished_utc=[DateTime]::UtcNow.ToString('O')
} | ConvertTo-Json | Out-File -LiteralPath (Join-Path $navigationOutput 'process.json') -Encoding UTF8
Get-Content -LiteralPath $navigationLog
if($navigationExit -ne 0) {
    $navigationName=[regex]::Escape([IO.Path]::GetFileName($navigationExecutable))
    $navigationEvents=@(Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$navigationStarted} -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -in @(1000,1001,1026) -and $_.Message -match $navigationName } |
        Select-Object TimeCreated,Id,Message)
    ConvertTo-Json -InputObject $navigationEvents -Depth 4 | Out-File -LiteralPath (Join-Path $navigationOutput 'native-events.json') -Encoding UTF8
    throw ('WPF feature navigation failed: '+$Locale+'; exit '+$navigationExit+' / 0x'+$navigationHex+'; diagnostics '+$navigationOutput)
}
