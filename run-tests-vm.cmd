@echo off
rem WT8: the gate, as modelado and Revelei run theirs. Carries this working tree (uncommitted files
rem included) to the VMware Workstation guest through winwright's runner, runs run-tests.cmd there
rem on a desk of its own, and brings the verdict back, so the UI cases never take over the desk you
rem are working at.
rem
rem The guest's credentials are winwright's: d:\tmp\winwright-vm.env (winwright's
rem tools\run-tests-vm.ps1 says what goes in it). Every argument goes to that runner whole, for
rem example -Screenshot to bring back a picture of the guest's desk. WINTIMELINE_CONFIG=Release
rem tests the Release build.
rem
rem Usage:  run-tests-vm.cmd [runner options]
setlocal

set "WINWRIGHT=%WINWRIGHT_HOME%"
if "%WINWRIGHT%"=="" set "WINWRIGHT=D:\Git\alegauss\winwright"
if not exist "%WINWRIGHT%\run-tests-vm.cmd" (
    echo winwright's run-tests-vm.cmd was not found in %WINWRIGHT%; set WINWRIGHT_HOME to its checkout.
    exit /b 3
)

set "CONFIG=%WINTIMELINE_CONFIG%"
if "%CONFIG%"=="" set "CONFIG=Debug"

call "%WINWRIGHT%\run-tests-vm.cmd" -Tree "%~dp0." -Run "run-tests.cmd %CONFIG%" -ResultsIn "TestResults\reports" %*
exit /b %ERRORLEVEL%
