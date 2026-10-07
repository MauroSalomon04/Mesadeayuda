@echo off
cd /d "%~dp0src\HelpDesk.Api"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo No se encontro .NET. Instala el SDK de .NET 10 desde https://dotnet.microsoft.com/download
  pause
  exit /b 1
)
echo Iniciando la Mesa de Ayuda en http://localhost:5080
echo La primera vez compila y crea la base de datos: puede tardar un minuto.
echo Para detenerla, cerra esta ventana o presiona Ctrl+C.
start "" cmd /c "timeout /t 15 >nul & start http://localhost:5080"
dotnet run
pause
