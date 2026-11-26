[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string] $Executable)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).ProviderPath
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
    [IO.Path]::GetFileName($exe) -ne 'Broiler.Broadplay.Windows.exe') {
    throw 'Select the installed Broadplay Windows executable.'
}

$extensions = @('.wav','.wave','.mp3','.m4a','.aac','.flac','.ogg','.wma',
    '.mp4','.m4v','.mov','.webm','.mkv','.avi','.wmv','.m3u','.m3u8')
$classes = 'HKCU:\Software\Classes'
$app = Join-Path $classes 'Applications\Broiler.Broadplay.Windows.exe'
if ($PSCmdlet.ShouldProcess($app, 'Register Broadplay as an Open With choice')) {
    $command = Join-Path $app 'shell\open\command'
    New-Item -Path $command -Force | Out-Null
    Set-Item -LiteralPath $command -Value ('"{0}" "%1"' -f $exe)
    New-ItemProperty -LiteralPath $app -Name FriendlyAppName -Value 'Broiler Broadplay' -Force | Out-Null
    $types = Join-Path $app 'SupportedTypes'
    New-Item -Path $types -Force | Out-Null
    foreach ($extension in $extensions) {
        New-ItemProperty -LiteralPath $types -Name $extension -Value '' -Force | Out-Null
    }
}
