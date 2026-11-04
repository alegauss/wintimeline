@echo off
setlocal enabledelayedexpansion

rem WT8: builds WinTimeline (Debug) and starts it, to try the app by hand.
rem
rem   .\run.cmd                  a menu of manual scenarios
rem   .\run.cmd [app options]    starts the app straight away with those options, e.g.
rem                              .\run.cmd --adapter warp --log-level debug
rem
rem Call it as .\run.cmd: with NoDefaultCurrentDirectoryInExePath set, a bare "run.cmd" can find
rem another run.cmd on the PATH first. App options: docs\COMMAND-LINE.md.

pushd "%~dp0"

echo Building WinTimeline (Debug)
dotnet build src\WinTimeline.App\WinTimeline.App.csproj --configuration Debug --nologo -v quiet -nodeReuse:false
if errorlevel 1 (
    echo.
    echo BUILD FAILED - the app was not started.
    popd
    exit /b 1
)

set "EXE=%CD%\src\WinTimeline.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\WinTimeline.exe"
if not "%~1"=="" goto :direct

:menu
cls
echo  WinTimeline - manual scenarios
echo  ==============================
echo.
echo    1  Default start, on the high-performance GPU
echo    2  WARP, the software rasteriser a VM has
echo    3  A damaged settings file (a data folder of its own under artifacts\manual)
echo    4  A managed error, answered with a restart offer
echo    5  A native crash, then a start that offers recovery
echo    6  Invalid argument: the app refuses and exits with code 2
echo.
echo    S  stop
echo.
set "PICK="
set /p "PICK=Choose: "
if not defined PICK goto :menu
if /i "!PICK!"=="S" goto :done

set "WINTIMELINE_DATA="
if "!PICK!"=="1" start "WinTimeline" "%EXE%"
if "!PICK!"=="2" start "WinTimeline" "%EXE%" --adapter warp
if "!PICK!"=="3" (
    set "WINTIMELINE_DATA=%CD%\artifacts\manual\damaged-settings"
    if not exist "!WINTIMELINE_DATA!" mkdir "!WINTIMELINE_DATA!"
    copy /y cases\fixtures\settings\settings.json "!WINTIMELINE_DATA!\settings.json" >nul
    start "WinTimeline" "%EXE%"
)
if "!PICK!"=="4" start "WinTimeline" "%EXE%" --crash-test managed
if "!PICK!"=="5" (
    set "WINTIMELINE_DATA=%CD%\artifacts\manual\after-crash"
    "%EXE%" --crash-test native
    start "WinTimeline" "%EXE%"
)
if "!PICK!"=="6" (
    "%EXE%" --adapter radeon-that-is-not-here
    echo  exit code !ERRORLEVEL!
    pause
)
timeout /t 2 >nul 2>&1
goto :menu

:direct
echo Starting %EXE% %*
start "WinTimeline" "%EXE%" %*

:done
popd
endlocal
