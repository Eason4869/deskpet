param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture
$script:FontFamily = [System.Drawing.FontFamily]::new('Microsoft YaHei UI')
$script:ImageCache = @{}
$script:DevelopmentVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src/ReleaseInfo.cs')), 'DisplayVersion="([^"]+)"').Groups[1].Value
if (-not $script:DevelopmentVersion) { throw 'Application version was not found.' }

function Format-Coordinate([double]$Value) {
    return $Value.ToString('0.###', $script:Invariant)
}

function New-Label([string]$Text, [double]$Left, [double]$Top, [double]$Size, [string]$Color, [switch]$Bold) {
    $style = [System.Drawing.FontStyle]::Regular
    if ($Bold) { $style = [System.Drawing.FontStyle]::Bold }
    $outline = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $outline.AddString($Text, $script:FontFamily, [int]$style, [single]$Size, [System.Drawing.PointF]::new($Left, $Top), [System.Drawing.StringFormat]::GenericTypographic)
        $points = $outline.PathPoints
        $types = $outline.PathTypes
        $commands = [System.Collections.Generic.List[string]]::new()
        $pointIndex = 0
        while ($pointIndex -lt $points.Length) {
            $kind = $types[$pointIndex] -band 7
            $point = $points[$pointIndex]
            if ($kind -eq 0) {
                $commands.Add("M$(Format-Coordinate $point.X),$(Format-Coordinate $point.Y)")
            } elseif ($kind -eq 1) {
                $commands.Add("L$(Format-Coordinate $point.X),$(Format-Coordinate $point.Y)")
            } elseif ($kind -eq 3) {
                $middle = $points[$pointIndex + 1]
                $last = $points[$pointIndex + 2]
                $commands.Add("C$(Format-Coordinate $point.X),$(Format-Coordinate $point.Y) $(Format-Coordinate $middle.X),$(Format-Coordinate $middle.Y) $(Format-Coordinate $last.X),$(Format-Coordinate $last.Y)")
                $pointIndex += 2
            }
            if (($types[$pointIndex] -band 128) -ne 0) { $commands.Add('Z') }
            $pointIndex++
        }
        $title = [System.Security.SecurityElement]::Escape($Text)
        return "<g><title>$title</title><path fill='$Color' d='$($commands -join ' ')'/></g>"
    } finally {
        $outline.Dispose()
    }
}

function New-Image([string]$Path, [double]$Left, [double]$Top, [double]$Width, [double]$Height, [string]$ViewBox = '') {
    if (-not $script:ImageCache.ContainsKey($Path)) {
        $script:ImageCache[$Path] = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $RepositoryRoot $Path)))
    }
    $bitmap = [System.Drawing.Image]::FromFile((Join-Path $RepositoryRoot $Path))
    try {
        if (-not $ViewBox) { $ViewBox = "0 0 $($bitmap.Width) $($bitmap.Height)" }
        return "<svg x='$Left' y='$Top' width='$Width' height='$Height' viewBox='$ViewBox' preserveAspectRatio='xMidYMid meet'><image width='$($bitmap.Width)' height='$($bitmap.Height)' href='data:image/png;base64,$($script:ImageCache[$Path])'/></svg>"
    } finally {
        $bitmap.Dispose()
    }
}

function Save-Visual([string]$Name, [int]$Width, [int]$Height, [string]$Title, [string]$Body) {
    $safeTitle = [System.Security.SecurityElement]::Escape($Title)
    $svg = "<svg xmlns='http://www.w3.org/2000/svg' width='$Width' height='$Height' viewBox='0 0 $Width $Height' role='img' aria-labelledby='title'><title id='title'>$safeTitle</title><defs><clipPath id='canvas'><rect width='$Width' height='$Height' rx='36'/></clipPath></defs><g clip-path='url(#canvas)'>$Body</g></svg>"
    [IO.File]::WriteAllText((Join-Path $RepositoryRoot "previews/$Name.svg"), $svg, [Text.UTF8Encoding]::new($false))
    Write-Output "Created previews/$Name.svg ($Width x $Height)"
}

function New-Hero([switch]$Mobile) {
    if ($Mobile) {
        $canvasWidth = 780
        $canvasHeight = 1020
        $body = "<defs><linearGradient id='background' x2='1' y2='1'><stop stop-color='#17234d'/><stop offset='1' stop-color='#514487'/></linearGradient></defs><rect width='780' height='1020' rx='36' fill='url(#background)'/><circle cx='740' cy='800' r='420' fill='#8673c8' opacity='.65'/>"
        $body += New-Label '桌面伙伴 · DeskPet' 48 52 33 '#d9dcff'
        $body += New-Label '让桌面，多一个伙伴。' 48 130 58 '#ffffff' -Bold
        $body += New-Label '摸摸头，聊聊天，一起做点事。' 48 225 32 '#dbdff7'
        $body += New-Label ("Windows $script:DevelopmentVersion  /  macOS 预览") 48 300 27 '#cbd4ff'
        $body += New-Image 'previews/e-bao.png' 52 443 180 420 '146 95 193 412'
        $body += New-Image 'previews/dafeiyu.png' 229 405 190 420 '108 95 267 421'
        $body += New-Image 'previews/marfu.png' 430 427 196 420 '118 110 254 387'
        $body += New-Image 'previews/xiaobei.png' 590 561 160 290 '89 69 301 427'
        $body += New-Label 'E包  ·  大肥鱼  ·  马尔芙  ·  小贝' 48 930 29 '#e7e2ff'
    } else {
        $canvasWidth = 1440
        $canvasHeight = 720
        $body = "<defs><linearGradient id='background' x2='1' y2='1'><stop stop-color='#17234d'/><stop offset='1' stop-color='#514487'/></linearGradient></defs><rect width='1440' height='720' rx='36' fill='url(#background)'/><circle cx='1250' cy='240' r='435' fill='#8673c8' opacity='.65'/><circle cx='1410' cy='720' r='245' fill='#aa91e4' opacity='.7'/>"
        $body += New-Label '桌面伙伴 · DeskPet' 64 62 30 '#d9dcff'
        $body += New-Label '让桌面，' 64 155 72 '#ffffff' -Bold
        $body += New-Label '多一个伙伴。' 64 255 72 '#ffffff' -Bold
        $body += New-Label '摸摸头，聊聊天；' 68 384 30 '#dbdff7'
        $body += New-Label '需要时，也能一起做点事。' 68 432 30 '#dbdff7'
        $body += "<rect x='64' y='528' width='570' height='62' rx='31' fill='#ffffff' fill-opacity='.1' stroke='#aaa9ee'/>"
        $body += New-Label ("Windows $script:DevelopmentVersion  /  macOS 预览") 88 541 25 '#eff0ff'
        $body += New-Label 'E包  ·  大肥鱼  ·  马尔芙  ·  小贝' 68 644 24 '#cbd0ef'
        $body += New-Image 'previews/e-bao.png' 772 175 177 380 '146 95 193 412'
        $body += New-Image 'previews/dafeiyu.png' 963 80 220 370 '108 95 267 421'
        $body += New-Image 'previews/marfu.png' 1151 220 211 400 '118 110 254 387'
        $body += New-Image 'previews/xiaobei.png' 965 418 170 242 '89 69 301 427'
    }
    $suffix = if ($Mobile) { '-mobile' } else { '' }
    Save-Visual "readme-hero$suffix" $canvasWidth $canvasHeight '桌面伙伴 DeskPet：E包、大肥鱼、马尔芙与小贝' $body
}

function New-Workspace([switch]$Mobile) {
    if ($Mobile) {
        $body = "<rect width='780' height='870' rx='36' fill='#eaf0ff'/><rect x='40' y='40' width='300' height='52' rx='26' fill='#4e67d9'/>"
        $body += New-Label '聊天 · 项目 · 工具' 64 47 28 '#ffffff'
        $body += New-Label '一个窗口，' 40 117 56 '#22294f' -Bold
        $body += New-Label '陪你聊天和工作' 40 195 56 '#22294f' -Bold
        $body += New-Label 'Windows 真实界面 · 完整窗口' 42 283 28 '#697799'
        $body += "<rect x='24' y='354' width='732' height='492' rx='22' fill='#ffffff'/>"
        $body += New-Image 'previews/workspace-0.9.0.png' 40 370 700 460
        Save-Visual 'readme-workspace-mobile' 780 870 'Windows 完整主窗口截图：窄屏排版' $body
        return
    }
    $body = "<rect width='1728' height='1370' rx='36' fill='#eaf0ff'/><rect x='64' y='52' width='320' height='52' rx='26' fill='#4e67d9'/>"
    $body += New-Label '聊天 · 项目 · 工具' 88 59 28 '#ffffff'
    $body += New-Label '一个窗口，陪你聊天和工作' 64 136 65 '#22294f' -Bold
    $body += New-Label 'Windows 0.9.0 真实界面 · 保留完整窗口' 68 226 28 '#697799'
    $body += "<rect x='46' y='294' width='1636' height='1030' rx='28' fill='#d3dcf0'/><rect x='32' y='280' width='1664' height='1058' rx='28' fill='#ffffff'/>"
    $body += New-Image 'previews/workspace-0.9.0.png' 56 304 1616 1010
    Save-Visual 'readme-workspace' 1728 1370 'Windows 统一主窗口完整截图：会话侧栏、伙伴欢迎页与输入区' $body
}

function New-Interaction([switch]$Mobile) {
    if ($Mobile) {
        $canvasWidth = 780
        $canvasHeight = 1490
        $body = "<rect width='780' height='1490' rx='36' fill='#f1effc'/>"
        $body += New-Label '不接入 AI，也能先玩起来' 40 48 48 '#252b55' -Bold
        $body += New-Label '拖动、抚摸、投喂，让陪伴触手可及。' 42 125 28 '#737b9c'
        $body += "<rect x='32' y='205' width='716' height='550' rx='30' fill='#ffffff'/>"
        $body += New-Label '随手互动' 64 239 35 '#3d477b' -Bold
        $body += New-Image 'previews/e-bao.png' 148 315 195 395 '146 95 193 412'
        $body += New-Image 'previews/dafeiyu.png' 403 320 250 395 '108 95 267 421'
        $body += "<rect x='32' y='789' width='716' height='654' rx='30' fill='#ffffff'/>"
        $body += New-Label '动作轮盘与快速对话' 64 824 35 '#3d477b' -Bold
        $body += New-Image 'previews/action-palette.png' 58 972 300 330
        $body += New-Image 'previews/quick-chat.png' 383 950 332 392
    } else {
        $canvasWidth = 1440
        $canvasHeight = 880
        $body = "<rect width='1440' height='880' rx='36' fill='#f1effc'/>"
        $body += New-Label '不接入 AI，也能先玩起来' 60 50 60 '#252b55' -Bold
        $body += New-Label '拖动、抚摸、投喂；接入模型后，双击就能快速聊两句。' 64 140 28 '#737b9c'
        $body += "<rect x='40' y='230' width='620' height='602' rx='32' fill='#ffffff'/><rect x='696' y='230' width='704' height='602' rx='32' fill='#ffffff'/>"
        $body += New-Label '随手互动' 76 261 34 '#3d477b' -Bold
        $body += New-Label '动作轮盘与快速对话' 732 261 34 '#3d477b' -Bold
        $body += New-Image 'previews/e-bao.png' 122 356 180 400 '146 95 193 412'
        $body += New-Image 'previews/dafeiyu.png' 360 354 244 400 '108 95 267 421'
        $body += New-Image 'previews/action-palette.png' 716 392 315 346
        $body += New-Image 'previews/quick-chat.png' 1050 365 323 380
    }
    $suffix = if ($Mobile) { '-mobile' } else { '' }
    Save-Visual "readme-interaction$suffix" $canvasWidth $canvasHeight '桌宠原画、动作轮盘与快速对话的真实界面' $body
}

try {
    New-Hero
    New-Hero -Mobile
    New-Workspace
    New-Workspace -Mobile
    New-Interaction
    New-Interaction -Mobile
} finally {
    $script:FontFamily.Dispose()
}
