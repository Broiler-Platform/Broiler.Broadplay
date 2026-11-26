param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory,
    [string] $Ffmpeg = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Write-Fixture([string] $Name, [string[]] $Arguments) {
    $target = Join-Path $OutputDirectory $Name
    & $Ffmpeg -hide_banner -loglevel error -y @Arguments $target
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg could not create $Name." }
}

$tone = 'aevalsrc=0.2*sin(2*PI*(440+440*gte(t\,2))*t):s=48000:d=4'
$audio = @('-f', 'lavfi', '-i', $tone)
$video = @('-f', 'lavfi', '-i', 'color=c=red:s=320x180:r=24:d=4,drawbox=color=blue:t=fill:enable=gte(t\,2)')
$h264 = @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-g', '24')

Write-Fixture 'tone.wav' ($audio + @('-c:a', 'pcm_s16le'))
Write-Fixture 'tone.mp3' ($audio + @('-c:a', 'libmp3lame', '-b:a', '128k'))
Write-Fixture 'audio-only.mp4' ($audio + @('-c:a', 'aac', '-movflags', '+faststart'))
Write-Fixture 'color-and-tone.mp4' ($video + $audio + $h264 + @('-c:a', 'aac', '-movflags', '+faststart'))
Write-Fixture 'silent-color.mp4' ($video + $h264 + @('-an', '-movflags', '+faststart'))
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'tone.wav') -Destination (Join-Path $OutputDirectory 'extensionless-tone')
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'color-and-tone.mp4') -Destination (Join-Path $OutputDirectory 'misnamed-video.m4a')
Set-Content -LiteralPath (Join-Path $OutputDirectory 'invalid.mp4') -Value 'This is not a media stream.' -Encoding ascii
