param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path $RepositoryRoot).Path
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $repository 'src/ReleaseInfo.cs')), 'DisplayVersion="([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Application version was not found.' }
$output = Join-Path $repository 'work/readme-capture/app'
& (Join-Path $repository 'Build.ps1') -Target App -OutputDirectory $output
if (-not $?) { throw 'Application build failed.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$executable = Join-Path $output 'CaptureReadmeWorkspace.exe'
& (Join-Path $framework 'csc.exe') /nologo /codepage:65001 /utf8output /target:exe ("/out:$executable") @references ("/reference:" + (Join-Path $output '桌面伙伴.exe')) (Join-Path $PSScriptRoot 'CaptureReadmeWorkspace.cs')
if ($LASTEXITCODE -ne 0) { throw 'Capture tool compilation failed.' }
& $executable (Join-Path $repository "previews/workspace-$version.png")
if ($LASTEXITCODE -ne 0) { throw 'Workspace capture failed.' }
