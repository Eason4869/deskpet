param(
    [Parameter(Mandatory=$true)][string]$ProjectRoot,
    [Parameter(Mandatory=$true)][string]$Destination,
    [string[]]$FontFiles=@('deskpet-keepsake-sc.otf','deskpet-keepsake-tc.otf','deskpet-keepsake-jp.otf','deskpet-keepsake-kr.otf','deskpet-keepsake-latin.ttf','deskpet-keepsake-devanagari.ttf')
)
$ErrorActionPreference='Stop'
$allowed=@('deskpet-keepsake-sc.otf','deskpet-keepsake-tc.otf','deskpet-keepsake-jp.otf','deskpet-keepsake-kr.otf','deskpet-keepsake-latin.ttf','deskpet-keepsake-devanagari.ttf')
$directory=Join-Path $ProjectRoot 'assets/fonts/relationships'
$writer=[Resources.ResourceWriter]::new($Destination)
try {
    foreach($name in $FontFiles) {
        if($name -cnotin $allowed){throw "Unexpected title font: $name"}
        $path=Join-Path $directory $name
        $file=Get-Item -LiteralPath $path
        if($file.Length -lt 1 -or $file.Length -gt 1048576){throw "Title font exceeds bounded resource budget: $name"}
        # WPF requires a Resource stream in <actual assembly name>.g.resources.
        # Ordinary /resource:<font> EmbeddedResource is not a usable WPF FontFamily.
        $writer.AddResource(('assets/fonts/relationships/'+$name),[IO.File]::OpenRead($path),$true)
    }
    foreach($name in @('OFL-CJK.txt','OFL-Latin.txt','OFL-Devanagari.txt')) {
        $path=Join-Path $directory $name
        if($FontFiles.Count -eq 1 -and !(Test-Path -LiteralPath $path)){continue}
        $file=Get-Item -LiteralPath $path
        if($file.Length -gt 32768){throw "Unexpected title license size: $name"}
        $writer.AddResource(('assets/fonts/relationships/'+$name.ToLowerInvariant()),[IO.File]::OpenRead($path),$true)
    }
    $writer.Generate()
} finally {$writer.Dispose()}
