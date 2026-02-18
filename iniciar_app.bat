@echo off
setlocal enableextensions

REM ==========================================
REM Audit History Extractor Pro - Start Script
REM ==========================================

title Audit History Extractor Pro - Running...
color 0A

REM Ir a la carpeta donde está este .bat (raíz del repo)
cd /d "%~dp0"

REM Ruta del proyecto UI (.csproj)
set "PROJECT_PATH=src\AuditHistoryExtractorPro.UI\AuditHistoryExtractorPro.UI.csproj"

REM Validar que dotnet esté disponible
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo [ERROR] .NET SDK no está instalado o no está en el PATH.
    echo         Instala .NET 8 SDK y vuelve a intentar.
    echo.
    pause
    exit /b 1
)

REM Validar que el .csproj exista
if not exist "%PROJECT_PATH%" (
    echo.
    echo [ERROR] No se encontró el proyecto:
    echo         %PROJECT_PATH%
    echo.
    echo Verifica que este .bat esté en la raíz del repositorio.
    echo.
    pause
    exit /b 1
)

echo.
echo [INFO] Iniciando Audit History Extractor Pro...
echo [INFO] Proyecto: %PROJECT_PATH%
echo.

REM Ejecutar aplicación

dotnet run --project "%PROJECT_PATH%"
set "APP_EXIT_CODE=%ERRORLEVEL%"

echo.
if "%APP_EXIT_CODE%"=="0" (
    echo [INFO] La aplicación finalizó correctamente.
) else (
    echo [ERROR] La aplicación terminó con código: %APP_EXIT_CODE%
)

echo.
pause
exit /b %APP_EXIT_CODE%
