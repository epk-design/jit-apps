@echo off
rem Builds a self-contained TrayKanban.exe (no .NET install needed to run it).
rem Building needs the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10
pushd "%~dp0"
dotnet publish -c Release -o publish
set ERR=%ERRORLEVEL%
popd
if %ERR%==0 (echo Built "%~dp0publish\TrayKanban.exe") else (echo Build failed.)
if "%1"=="" pause
exit /b %ERR%
