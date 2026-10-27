param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Locale,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$navigationExecutable=(Resolve-Path -LiteralPath $Executable).Path
$navigationOutput=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $navigationOutput -Force | Out-Null
$navigationRuns=@()
# Keep every capture/check while releasing each large offscreen native resource graph.
# Live theme transitions remain covered by the separate ThemeTests and task UI suites.
foreach($navigationTheme in @('light','dark')) {
    $navigationThemeOutput=Join-Path $navigationOutput $navigationTheme
    New-Item -ItemType Directory -Path $navigationThemeOutput -Force | Out-Null
    $navigationStarted=Get-Date
    $ErrorActionPreference='Continue'
    & $navigationExecutable $Locale '--layout-boundary' ('--theme='+$navigationTheme) *> (Join-Path $navigationThemeOutput 'process.log')
    $navigationExit=$LASTEXITCODE
    $ErrorActionPreference='Stop'
    $navigationHex=[BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$navigationExit),0).ToString('X8')
    $navigationMetadata=[ordered]@{locale=$Locale;theme=$navigationTheme;exit_code=$navigationExit;exit_hex=$navigationHex;started_utc=$navigationStarted.ToUniversalTime().ToString('O');finished_utc=[DateTime]::UtcNow.ToString('O')}
    $navigationRuns+=$navigationMetadata
    $navigationMetadata | ConvertTo-Json | Out-File -LiteralPath (Join-Path $navigationThemeOutput 'process.json') -Encoding UTF8
    $navigationSummary=[ordered]@{locale=$Locale;exit_code=$navigationExit;exit_hex=$navigationHex;runs=$navigationRuns}
    $navigationSummary | ConvertTo-Json -Depth 5 | Out-File -LiteralPath (Join-Path $navigationOutput 'process.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $navigationThemeOutput 'process.log') | Tee-Object -FilePath (Join-Path $navigationOutput 'process.log') -Append
    if($navigationExit -ne 0) {
        $navigationName=[regex]::Escape([IO.Path]::GetFileName($navigationExecutable))
        Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$navigationStarted} -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in @(1000,1001,1026) -and $_.Message -match $navigationName } |
            Select-Object TimeCreated,Id,Message | ConvertTo-Json -Depth 4 |
            Out-File -LiteralPath (Join-Path $navigationThemeOutput 'native-events.json') -Encoding UTF8
        throw ('WPF feature navigation failed: '+$Locale+' / '+$navigationTheme+'; exit '+$navigationExit+' / 0x'+$navigationHex)
    }
}
