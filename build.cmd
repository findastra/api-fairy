@echo off
rem Builds API Fairy with the C# compiler that ships with Windows (.NET Framework 4.8).
rem Nothing is downloaded. "build.cmd test" also builds and runs the tests.
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo Couldn't find the C# compiler that comes with Windows ^(.NET Framework 4.8^).
  exit /b 1
)
set "CSC=%FW%\csc.exe"
set "WPF=%FW%\WPF"
set REFS=/r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:System.Xaml.dll /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll
set CORE=src\Providers.cs src\Dotenv.cs src\Scanner.cs src\Store.cs src\Core.cs src\Sprite.cs
if not exist bin mkdir bin

set ICON=
if exist assets\fairy.ico set ICON=/win32icon:assets\fairy.ico
"%CSC%" /nologo /target:winexe /optimize+ /utf8output /out:"bin\API Fairy.exe" %ICON% /win32manifest:src\app.manifest %REFS% src\*.cs
if errorlevel 1 exit /b 1

if /i "%~1"=="test" (
  "%CSC%" /nologo /target:exe /optimize+ /utf8output /out:bin\tests.exe /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.Core.dll %CORE% tests\*.cs
  if errorlevel 1 exit /b 1
  bin\tests.exe "%~dp0."
  if errorlevel 1 exit /b 1
)
exit /b 0
