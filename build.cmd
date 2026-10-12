@echo off
rem Compila Drevogitech Led Control in .\bin\app (richiede l'SDK .NET 10; usa quello in %USERPROFILE%\.dotnet se presente).
setlocal
cd /d "%~dp0"
set DOTNET=dotnet
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set DOTNET=%USERPROFILE%\.dotnet\dotnet.exe
if exist bin\app rmdir /s /q bin\app
"%DOTNET%" publish DrevogitechLedControl.csproj -c Release -o bin\app --nologo -p:DebugType=none
if errorlevel 1 (echo Compilazione fallita & exit /b 1)
echo Creato bin\app\DrevogitechLedControl.exe
