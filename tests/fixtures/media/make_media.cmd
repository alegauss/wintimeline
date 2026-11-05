@echo off
rem WT68: small real media for dropping on the welcome page and for probing, made with ffmpeg.
rem   bars-3s-25p.mp4  3 s of test pattern at 25 fps, 320x180 H.264 baseline, with a 440 Hz AAC tone
rem   tone-2s.wav      2 s of a 220 Hz tone, 48 kHz stereo PCM
rem   clicks-120bpm.wav  WT121: 4 s of 3 ms clicks of a 2 kHz tone at 120 BPM from 0.25 s
rem                      (beats at 0.25, 0.75, ... 3.75 s), 44.1 kHz mono PCM
pushd "%~dp0"
ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=320x180:rate=25:duration=3" -f lavfi -i "sine=frequency=440:duration=3" -c:v libx264 -pix_fmt yuv420p -profile:v baseline -c:a aac -b:a 64k -shortest -movflags +faststart bars-3s-25p.mp4
ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=220:duration=2" -ac 2 -ar 48000 tone-2s.wav
ffmpeg -hide_banner -loglevel error -y -f lavfi -i "aevalsrc='if(lt(mod(t+9.75\,0.5)\,0.003)\,0.8*sin(2*PI*2000*t)\,0)':s=44100:d=4" -ac 1 -c:a pcm_s16le clicks-120bpm.wav
popd
