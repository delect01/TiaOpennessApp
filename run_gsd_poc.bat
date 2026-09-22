@echo off
set "EXE=C:\Users\DPTO_ELECT\Documents\TIA Portal Openness\TiaOApp\bin\Debug\net48\TiaOpennessApp.exe"
set "LOG=C:\Users\DPTO_ELECT\Documents\TIA Portal Openness\TiaOApp\gsd_poc_log.txt"
echo RUNNING %date% %time% > "%LOG%"
for /f "usebackq delims=" %%H in (`powershell -NoProfile -Command "(Get-FileHash -LiteralPath '%EXE%' -Algorithm SHA1).Hash"`) do set "FILEHASH=%%H"
echo Hash SHA1 detectat: %FILEHASH% >> "%LOG%"
reg add "HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaOpennessApp.exe\Entry" /v "Path" /t REG_SZ /d "%EXE%" /f >> "%LOG%" 2>&1
reg add "HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaOpennessApp.exe\Entry" /v "DateModified" /t REG_SZ /d "22/09/2026" /f >> "%LOG%" 2>&1
reg add "HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList\TiaOpennessApp.exe\Entry" /v "FileHash" /t REG_SZ /d "%FILEHASH%" /f >> "%LOG%" 2>&1
"%EXE%" --gsd-cax-poc "C:\Users\DPTO_ELECT\Documents\Automation\TEST_TIAO\TEST_TIAO.ap21" "C:\Users\DPTO_ELECT\Documents\Automation" "GSD_POC" %* >> "%LOG%" 2>&1
echo EXITCODE: %errorlevel% > "C:\Users\DPTO_ELECT\Documents\TIA Portal Openness\TiaOApp\gsd_poc_log.txt.exit"