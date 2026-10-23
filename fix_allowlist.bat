@echo off
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%bin\Debug\net48\TiaOpennessApp.exe"

if not exist "%EXE%" (
    echo No existeix "%EXE%".
    echo Compila primer amb build.bat.
    pause
    exit /b 1
)

"%EXE%" --check-allowlist >nul 2>&1
if not errorlevel 1 (
    echo AllowList ja es valida per a aquest binari.
    "%EXE%" --check-allowlist
    exit /b 0
)

echo Calen permisos d'administrador per escriure a HKLM. Demanant elevacio...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%EXE%' -ArgumentList '--fix-allowlist' -Verb RunAs -Wait"

"%EXE%" --check-allowlist
exit /b %errorlevel%
