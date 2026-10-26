@echo off
rem Builds PowerSwitcher.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x.
  exit /b 1
)
pushd "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /out:PowerSwitcher.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll PowerSwitcher.cs
set ERR=%ERRORLEVEL%
popd
if %ERR%==0 (echo Built "%~dp0PowerSwitcher.exe") else (echo Build failed.)
if "%1"=="" pause
exit /b %ERR%
