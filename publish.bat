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

echo [1/5] Compilant Release...
dotnet build TiaOpennessApp.csproj -c Release -nologo -v q
if errorlevel 1 (
    echo ERROR: la compilacio ha fallat.
    pause
    exit /b 1
)

echo [2/5] Copiant binaris a "%PKG%"...
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
rem appsettings sense rutes d'usuari: l'app usa Documents\TIAO\{imports,exports,catalog}
copy /y "installer\appsettings.installed.json" "%PKG%\appsettings.json" >nul

echo [3/5] Generant scripts d'arrencada...
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

echo [4/5] Comprimint ZIP...
if exist "%PKG%.zip" del /q "%PKG%.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%PKG%' -DestinationPath '%PKG%.zip' -Force"
if errorlevel 1 (
    echo ERROR: no s'ha pogut crear el ZIP.
    pause
    exit /b 1
)

echo [5/5] Generant instal.lador MSI...
rem Versio MSI: 1.<any>.<dia de l'any*100+hora> (creix a cada publicacio -> actualitza la versio anterior)
for /f %%v in ('powershell -NoProfile -Command "$d=Get-Date; '1.{0}.{1}' -f ($d.Year-2000), ($d.DayOfYear*100+$d.Hour)"') do set "MSIVER=%%v"
set "MSI=%DIST%\%PKGNAME%.msi"
where wix >nul 2>&1
if errorlevel 1 (
    echo ERROR: no es troba 'wix'. Instal.la'l amb: dotnet tool install -g wix --version 6.0.2
    echo        i les extensions: wix extension add -g WixToolset.UI.wixext/6.0.2 WixToolset.Util.wixext/6.0.2
    pause
    exit /b 1
)
wix build "installer\TiaOpennessApp.wxs" -arch x64 ^
    -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext ^
    -d "SourceDir=%PKG%" -d "Version=%MSIVER%" -culture ca-ES -culture en-US ^
    -o "%MSI%"
if errorlevel 1 (
    echo ERROR: no s'ha pogut generar el MSI.
    pause
    exit /b 1
)
if exist "%DIST%\%PKGNAME%.wixpdb" del /q "%DIST%\%PKGNAME%.wixpdb"

echo.
echo Paquet generat (versio %MSIVER%):
echo   Instal.lador: %MSI%
echo   Carpeta:      %PKG%
echo   ZIP:          %PKG%.zip
echo.
echo Instal.lacio per defecte a "C:\Program Files (x86)\TiaOpennessApp".
echo Silenciosa: msiexec /i "%MSI%" INSTALLFOLDER="D:\Altra\Carpeta" /qn
echo Dades d'usuari: %%USERPROFILE%%\Documents\TIAO\{imports,exports,catalog}
echo.
echo Requisits a la maquina desti: TIA Portal V21 instal.lat (Openness),
echo usuari al grup "Siemens TIA Openness" i .NET Framework 4.8.
endlocal
