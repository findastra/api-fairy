@echo off
rem Starts API Fairy, building it first if needed. "Start API Fairy.cmd --rebuild" builds again after an update.
setlocal
cd /d "%~dp0"
if /i "%~1"=="--rebuild" if exist "bin\API Fairy.exe" del "bin\API Fairy.exe"
if not exist "bin\API Fairy.exe" (
  echo Building API Fairy. This takes a few seconds...
  call build.cmd
  if errorlevel 1 (
    echo The build failed. See the messages above.
    pause
    exit /b 1
  )
)
start "" "bin\API Fairy.exe"
