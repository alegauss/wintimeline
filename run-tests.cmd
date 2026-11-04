@echo off
setlocal enabledelayedexpansion

rem WT8: the one command whose exit code is the suite's verdict. It builds WinTimeline.slnx, then
rem runs every test application under tests\ - the headless *.Tests and the winwright UI cases in
rem *.Cases - and fails on a red one, a missing one, or none at all.
rem
rem The UI cases take over the desktop while they run: run this in the VMware guest with
rem run-tests-vm.cmd, and on this desk only when nobody is using it.
rem
rem Usage:  run-tests.cmd [Configuration]        default Debug

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

rem How long one test may run before it is taken as hung, ended and named.
if "%WINTIMELINE_HANG%"=="" set "WINTIMELINE_HANG=10m"

pushd "%~dp0"

set "REPORTS=%CD%\TestResults\reports"
if exist "%REPORTS%" rd /s /q "%REPORTS%"
mkdir "%REPORTS%" 2>nul

echo Building WinTimeline.slnx (%CONFIG%)
dotnet build WinTimeline.slnx --configuration %CONFIG% --nologo -v quiet -nodeReuse:false
if errorlevel 1 (
    echo.
    echo BUILD FAILED - no tests were run.
    popd
    exit /b 1
)

set /a RAN=0
set /a FAILED=0
set "BROKEN="

for /d %%P in (tests\*.Tests tests\*.Cases) do (
    set "APP=%%P\bin\%CONFIG%\net10.0-windows10.0.19041.0\%%~nxP.exe"
    echo.
    echo === %%~nxP ===
    if exist "!APP!" (
        "!APP!" --results-directory "%REPORTS%" --hangdump --hangdump-timeout %WINTIMELINE_HANG% --hangdump-type Mini
        if errorlevel 1 (
            set /a FAILED+=1
            set "BROKEN=!BROKEN! %%~nxP"
        )
        set /a RAN+=1
    ) else (
        echo   no test application at !APP!
        set /a FAILED+=1
        set "BROKEN=!BROKEN! %%~nxP^(missing^)"
    )
)

echo.

rem Nothing found is not a pass: a suite that silently shrinks to zero is what this guards against.
if %RAN%==0 (
    echo NO TEST APPLICATIONS FOUND under tests\ for configuration %CONFIG%.
    popd
    exit /b 1
)

if %FAILED%==0 (
    echo All %RAN% test suites passed.
    popd
    exit /b 0
)

echo %FAILED% of %RAN% test suites failed:!BROKEN!
popd
exit /b 1
