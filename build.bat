@echo off
setlocal
cd /d "%~dp0"
dotnet build TiaOpennessApp.csproj -c Debug
if errorlevel 1 (
    pause
    exit /b 1
)
echo.
echo Actualitzant l'AllowList d'Openness per al nou binari...
call "%~dp0fix_allowlist.bat"
pause
