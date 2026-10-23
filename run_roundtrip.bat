@echo off
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%bin\Debug\net48\TiaOpennessApp.exe"
set "LOG=%ROOT%roundtrip_log.txt"
echo RUNNING %date% %time% > "%LOG%"

if not exist "%EXE%" (
    echo No existeix "%EXE%". Compila primer amb build.bat. >> "%LOG%"
    exit /b 1
)

"%EXE%" --check-allowlist >nul 2>&1
if errorlevel 1 call "%ROOT%fix_allowlist.bat" >> "%LOG%" 2>&1
"%EXE%" --check-allowlist >nul 2>&1
if errorlevel 1 (
    echo AllowList encara no valida per a aquest binari ^(cal administrador^). >> "%LOG%"
    exit /b 5
)

"%EXE%" --roundtrip-test "C:\Users\DPTO_ELECT\Documents\Automation\TEST_TIAO\TEST_TIAO.ap21" "%ROOT%exports\roundtrip" "C:\Users\DPTO_ELECT\Documents\Automation" "result" "%ROOT%catalog\Catalog.csv" >> "%LOG%" 2>&1
echo EXITCODE: %errorlevel% > "%ROOT%roundtrip_log.txt.exit"
pause
