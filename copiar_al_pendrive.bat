@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

echo ========================================================
echo    qInstall — Exportador Limpio a Pendrive USB
echo ========================================================
echo.

set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"

if not exist "%SRC%\Qinstall.exe" (
    echo [X] No se encontro Qinstall.exe en %SRC%.
    echo     Ejecuta primero build.bat para compilar la ultima version.
    pause
    exit /b 1
)

echo Unidades USB / extraibles detectadas:
powershell -NoProfile -Command "Get-PSDrive -PSProvider FileSystem | ForEach-Object { try { $di = New-Object System.IO.DriveInfo($_.Root); if ($di.DriveType -eq 'Removable' -and $di.IsReady) { $free = [Math]::Round($di.AvailableFreeSpace / 1GB, 1); Write-Host ('  [' + $di.Name + '] ' + $di.VolumeLabel + ' (' + $free + ' GB libres)') -ForegroundColor Green } } catch {} }"
echo.

set /p "TARGET_DRV=Escribe la letra del pendrive o ruta destino (ej. E o E:\Qinstall): "
if "%TARGET_DRV%"=="" (
    echo [!] Cancelado por el usuario.
    pause
    exit /b 0
)

set "DEST=%TARGET_DRV%"
if "%DEST:~1,1%"=="" set "DEST=%DEST%:\Qinstall"
if "%DEST:~-1%"==":" set "DEST=%DEST%\Qinstall"

echo.
echo [*] Copiando paquete portable limpio a: "%DEST%"
echo     (Se omiten src\, .git\, .agents\ y archivos de desarrollo)
echo.

if not exist "%DEST%" mkdir "%DEST%"

copy /Y "%SRC%\Qinstall.exe" "%DEST%\" >nul
copy /Y "%SRC%\apps.json" "%DEST%\" >nul
if exist "%SRC%\profiles.json" copy /Y "%SRC%\profiles.json" "%DEST%\" >nul

if exist "%SRC%\Microsoft.Web.WebView2.Core.dll" copy /Y "%SRC%\Microsoft.Web.WebView2.Core.dll" "%DEST%\" >nul
if exist "%SRC%\Microsoft.Web.WebView2.WinForms.dll" copy /Y "%SRC%\Microsoft.Web.WebView2.WinForms.dll" "%DEST%\" >nul
if exist "%SRC%\WebView2Loader.dll" copy /Y "%SRC%\WebView2Loader.dll" "%DEST%\" >nul

if not exist "%DEST%\Microsoft.Web.WebView2.Core.dll" (
    if exist "%SRC%\lib\webview2\Microsoft.Web.WebView2.Core.dll" copy /Y "%SRC%\lib\webview2\*.dll" "%DEST%\" >nul
)

robocopy "%SRC%\ui" "%DEST%\ui" /MIR /NFL /NDL /NJH /NJS /NC /NS >nul
robocopy "%SRC%\office" "%DEST%\office" /E /NFL /NDL /NJH /NJS /NC /NS >nul
robocopy "%SRC%\local" "%DEST%\local" /E /NFL /NDL /NJH /NJS /NC /NS >nul

echo [OK] Exportacion portable completada en: "%DEST%"
echo      Incluye: Qinstall.exe, WebView2 DLLs, apps.json, profiles.json, ui\ (con iconos offline), office\ y local\.
echo.
pause
