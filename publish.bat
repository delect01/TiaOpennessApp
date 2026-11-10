@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem ------------------------------------------------------------------
rem  Genera un paquet executable distribuible a dist\
rem    dist\TiaOpennessApp_<data>\      (carpeta llesta per executar)
rem    dist\TiaOpennessApp_<data>.zip   (mateixa carpeta comprimida)
rem ------------------------------------------------------------------

for /f %%d in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmm"') do set "STAMP=%%d"
set "PKGNAME=TiaOpennessApp_%STAMP%"
set "DIST=%~dp0dist"
set "PKG=%DIST%\%PKGNAME%"
set "OUT=%~dp0bin\Release\net48"

echo [1/4] Compilant Release...
dotnet build TiaOpennessApp.csproj -c Release -nologo -v q
if errorlevel 1 (
    echo ERROR: la compilacio ha fallat.
    pause
    exit /b 1
)

echo [2/4] Copiant binaris a "%PKG%"...
if exist "%PKG%" rmdir /s /q "%PKG%"
mkdir "%PKG%"
robocopy "%OUT%" "%PKG%" /E /NFL /NDL /NJH /NJS /NP /XF *.pdb tia_topology.db >nul
if errorlevel 8 (
    echo ERROR: copia de binaris fallida.
    pause
    exit /b 1
)
if not exist "%PKG%\catalog" mkdir "%PKG%\catalog"
if exist "catalog\*" robocopy "catalog" "%PKG%\catalog" /E /NFL /NDL /NJH /NJS /NP >nul
for %%f in (README.md CLI.md) do if exist "%%f" copy /y "%%f" "%PKG%\" >nul

echo [3/4] Generant scripts d'arrencada...
> "%PKG%\run_app.bat" (
    echo @echo off
    echo setlocal
    echo cd /d "%%~dp0"
    echo set "EXE=%%~dp0TiaOpennessApp.exe"
    echo "%%EXE%%" --check-allowlist ^>nul 2^>^&1
    echo if errorlevel 1 call "%%~dp0fix_allowlist.bat"
    echo echo Arrencant l'app...
    echo "%%EXE%%" %%*
    echo pause
)
> "%PKG%\fix_allowlist.bat" (
    echo @echo off
    echo setlocal
    echo set "EXE=%%~dp0TiaOpennessApp.exe"
    echo "%%EXE%%" --check-allowlist ^>nul 2^>^&1
    echo if not errorlevel 1 ^(
    echo     echo AllowList ja es valida per a aquest binari.
    echo     exit /b 0
    echo ^)
    echo echo Calen permisos d'administrador per escriure a HKLM. Demanant elevacio...
    echo powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%%EXE%%' -ArgumentList '--fix-allowlist' -Verb RunAs -Wait"
    echo "%%EXE%%" --check-allowlist
    echo exit /b %%errorlevel%%
)

echo [4/4] Comprimint ZIP...
if exist "%PKG%.zip" del /q "%PKG%.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%PKG%' -DestinationPath '%PKG%.zip' -Force"
if errorlevel 1 (
    echo ERROR: no s'ha pogut crear el ZIP.
    pause
    exit /b 1
)

echo.
echo Paquet generat:
echo   Carpeta: %PKG%
echo   ZIP:     %PKG%.zip
echo.
echo Requisits a la maquina desti: TIA Portal V21 instal.lat (Openness),
echo usuari al grup "Siemens TIA Openness" i .NET Framework 4.8.
echo Revisa les rutes d'appsettings.json abans d'executar.
endlocal
