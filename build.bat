@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo  GitHub Traffic Tray - Build
echo ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FEHLER] "dotnet" wurde nicht gefunden.
    echo Bitte das .NET 10 SDK installieren: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

dotnet --list-sdks | findstr /b "10." >nul
if errorlevel 1 (
    echo [FEHLER] Kein .NET 10 SDK gefunden. Eine reine Runtime reicht zum Kompilieren nicht.
    echo Installierte SDKs:
    dotnet --list-sdks
    pause
    exit /b 1
)

echo Erstelle Release-Build ...
echo.
dotnet publish "GitHubTrafficTray.csproj" -c Release -o "publish"
if errorlevel 1 (
    echo.
    echo [FEHLER] Build fehlgeschlagen. Ausgabe oben pruefen.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  Fertig: %~dp0publish\GitHubTrafficTray.exe
echo ============================================
pause
