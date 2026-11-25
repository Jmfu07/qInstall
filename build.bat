@echo off
setlocal

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "%CSC%" (
    echo [ERROR] No se encontro el compilador csc.exe de .NET Framework 4.x.
    exit /b 1
)

if not exist "lib\webview2\Microsoft.Web.WebView2.WinForms.dll" (
    echo [ERROR] Faltan las librerias de WebView2 en lib\webview2\.
    exit /b 1
)

echo Compilando Qinstall.exe (WebView2 + respaldo WinForms)...
"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 ^
    /win32manifest:src\app.manifest ^
    /out:Qinstall.exe ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:System.Web.Extensions.dll ^
    /r:lib\webview2\Microsoft.Web.WebView2.Core.dll ^
    /r:lib\webview2\Microsoft.Web.WebView2.WinForms.dll ^
    src\Program.cs src\Models.cs src\InstallerEngine.cs src\AddAppDialog.cs src\MainForm.cs src\WebViewForm.cs

if errorlevel 1 (
    echo [ERROR] Fallo la compilacion.
    exit /b 1
)

copy /Y "lib\webview2\Microsoft.Web.WebView2.Core.dll" . >nul
copy /Y "lib\webview2\Microsoft.Web.WebView2.WinForms.dll" . >nul
copy /Y "lib\webview2\WebView2Loader.dll" . >nul

echo [OK] Compilado correctamente: Qinstall.exe
endlocal
