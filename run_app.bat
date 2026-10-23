@echo off
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%bin\Debug\net48\TiaOpennessApp.exe"

if not exist "%EXE%" (
    echo No existeix "%EXE%". Compila primer amb build.bat.
    pause
    exit /b 1
)

"%EXE%" --check-allowlist >nul 2>&1
if errorlevel 1 call "%ROOT%fix_allowlist.bat"

echo Arrencant l'app...
"%EXE%"
pause
