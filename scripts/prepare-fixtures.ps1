#Requires -Version 5.1
<#
.SYNOPSIS
  WT8: makes the media fixtures and fetches the corpora the tests read, into .cache.

.DESCRIPTION
  Nothing large is committed. Small media is generated with FFmpeg, so every desk makes the same
  bytes from the same command; the OpenTimelineIO sample corpus is cloned at the version the model
  is measured against (WT11).

    .cache\fixtures\counter-1080p24.mp4   10 s of FFmpeg's testsrc2, whose built-in counter burns
                                          the frame number into each frame, H.264, 24 fps
    .cache\fixtures\bars-tone-1080p25.mov 10 s of SMPTE HD bars with a 1 kHz tone, ProRes 422 HQ
                                          and 48 kHz PCM, the codecs an editorial hand-off uses
    .cache\fixtures\tone-1k-48k.wav       10 s of 1 kHz at -20 dBFS, stereo, 24-bit
    .cache\otio\                          OpenTimelineIO v0.18.1, for tests/sample_data

  A fixture that is already there is kept; -Force makes them again.

.PARAMETER Force
  Make every fixture again even when it exists.
#>
[CmdletBinding()]
param([switch] $Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $root '.cache'
$fixtures = Join-Path $cache 'fixtures'
New-Item -ItemType Directory -Force $fixtures | Out-Null

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg is not on PATH; install it (winget install Gyan.FFmpeg) and run this again.'
}

function Make([string] $name, [string[]] $arguments) {
    $target = Join-Path $fixtures $name
    if ((Test-Path $target) -and -not $Force) {
        Write-Host "  kept  $name"
        return
    }

    & ffmpeg -hide_banner -loglevel error -y @arguments $target
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed making $name" }
    Write-Host "  made  $name"
}

Write-Host 'Media fixtures in .cache\fixtures'
Make 'counter-1080p24.mp4' @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=24:duration=10',
    '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-g', '24', '-bf', '0')
Make 'bars-tone-1080p25.mov' @('-f', 'lavfi', '-i', 'smptehdbars=size=1920x1080:rate=25:duration=10',
    '-f', 'lavfi', '-i', 'sine=frequency=1000:sample_rate=48000:duration=10',
    '-c:v', 'prores_ks', '-profile:v', '3', '-c:a', 'pcm_s24le', '-shortest')
Make 'tone-1k-48k.wav' @('-f', 'lavfi', '-i', 'sine=frequency=1000:sample_rate=48000:duration=10',
    '-af', 'volume=-20dB,pan=stereo|c0=c0|c1=c0', '-c:a', 'pcm_s24le')

Write-Host 'OpenTimelineIO corpus in .cache\otio'
$otio = Join-Path $cache 'otio'
if ((Test-Path (Join-Path $otio 'tests\sample_data')) -and -not $Force) {
    Write-Host '  kept  otio v0.18.1'
}
else {
    if (Test-Path $otio) { Remove-Item -Recurse -Force $otio }
    git clone --quiet --depth 1 --branch v0.18.1 https://github.com/AcademySoftwareFoundation/OpenTimelineIO.git $otio
    if ($LASTEXITCODE -ne 0) { throw 'cloning OpenTimelineIO failed' }
    Write-Host '  made  otio v0.18.1'
}
