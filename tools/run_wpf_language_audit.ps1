param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Locale,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskExecutable=(Resolve-Path -LiteralPath $Executable).Path
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskRuns=@()
# Keep every capture/check while releasing each large offscreen native resource graph.
# Live theme transitions remain covered by the separate ThemeTests and task UI suites.
foreach($taskTheme in @('light','dark')) {
    $taskThemeOutput=Join-Path $taskOutput $taskTheme
    New-Item -ItemType Directory -Path $taskThemeOutput -Force | Out-Null
    $taskStarted=Get-Date
    $ErrorActionPreference='Continue'
    & $taskExecutable $Locale $taskThemeOutput $taskTheme *> (Join-Path $taskThemeOutput 'process.log')
    $taskExit=$LASTEXITCODE
    $ErrorActionPreference='Stop'
    $taskHex=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$taskExit),0).ToString('X8')
    $taskMetadata=[ordered]@{locale=$Locale;theme=$taskTheme;exit_code=$taskExit;exit_hex=$taskHex;started_utc=$taskStarted.ToUniversalTime().ToString('O');finished_utc=[DateTime]::UtcNow.ToString('O')}
    $taskRuns+=$taskMetadata
    $taskMetadata | ConvertTo-Json | Out-File -LiteralPath (Join-Path $taskThemeOutput 'process.json') -Encoding UTF8
    $taskSummary=[ordered]@{locale=$Locale;exit_code=$taskExit;exit_hex=$taskHex;runs=$taskRuns}
    $taskSummary | ConvertTo-Json -Depth 5 | Out-File -LiteralPath (Join-Path $taskOutput 'process.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $taskThemeOutput 'process.log') | Tee-Object -FilePath (Join-Path $taskOutput 'process.log') -Append
    if($taskExit -ne 0) {
        $taskName=[regex]::Escape([IO.Path]::GetFileName($taskExecutable))
        Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$taskStarted} -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in @(1000,1001,1026) -and $_.Message -match $taskName } |
            Select-Object TimeCreated,Id,Message | ConvertTo-Json -Depth 4 |
            Out-File -LiteralPath (Join-Path $taskThemeOutput 'native-events.json') -Encoding UTF8
        throw ('WPF language audit failed: '+$Locale+' / '+$taskTheme+'; exit '+$taskExit+' / 0x'+$taskHex)
    }
}
