@echo off
rem WT11: builds the OTIO 0.18.1 C++ core and the otio_shim.dll C ABI over it, into
rem .cache\otio-install and .cache\otio-shim. Needs Visual Studio's C++ workload, and CMake
rem plus Ninja on PATH (pip install cmake ninja). Fetch the source first:
rem   git clone --depth 1 --branch v0.18.1 https://github.com/AcademySoftwareFoundation/OpenTimelineIO.git .cache\otio
rem   git -C .cache\otio submodule update --init --depth 1 src/deps/rapidjson src/deps/Imath
setlocal
set "ROOT=%~dp0..\..\.."
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

cmake -S "%ROOT%\.cache\otio" -B "%ROOT%\.cache\otio\build" -G Ninja -DCMAKE_BUILD_TYPE=Release ^
  -DOTIO_SHARED_LIBS=ON -DOTIO_PYTHON_INSTALL=OFF -DOTIO_AUTOMATIC_SUBMODULES=OFF ^
  -DOTIO_INSTALL_PYTHON_MODULES=OFF -DOTIO_INSTALL_COMMANDLINE_TOOLS=OFF ^
  -DCMAKE_INSTALL_PREFIX="%ROOT%\.cache\otio-install" || exit /b 1
cmake --build "%ROOT%\.cache\otio\build" || exit /b 1
cmake --install "%ROOT%\.cache\otio\build" || exit /b 1

set "OUT=%ROOT%\.cache\otio-shim"
if not exist "%OUT%" mkdir "%OUT%"
cl /nologo /O2 /EHsc /std:c++17 /MD /LD /I"%ROOT%\.cache\otio-install\include" "%~dp0otio_shim.cpp" ^
  /Fo"%OUT%\\" /Fe"%OUT%\otio_shim.dll" ^
  /link "%ROOT%\.cache\otio-install\lib\opentimelineio.lib" "%ROOT%\.cache\otio-install\lib\opentime.lib" || exit /b 1
copy /y "%ROOT%\.cache\otio-install\lib\opentimelineio.dll" "%OUT%" >nul
copy /y "%ROOT%\.cache\otio-install\lib\opentime.dll" "%OUT%" >nul
echo otio_shim.dll is in %OUT%
