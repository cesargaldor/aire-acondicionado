@echo off
rem Compila y comprueba Aire para Windows. Requiere el SDK de .NET 8:
rem   winget install Microsoft.DotNet.SDK.8
setlocal
cd /d "%~dp0"

where dotnet >/dev/null 2>nul
if errorlevel 1 (
  echo Falta el SDK de .NET 8. Instalalo con:
  echo   winget install Microsoft.DotNet.SDK.8
  echo y vuelve a ejecutar build.bat en una ventana nueva.
  pause
  exit /b 1
)

echo 1/3  Compilando la herramienta de prueba...
dotnet build AireCli -c Release -o build\cli --nologo -v q
if errorlevel 1 goto error

echo 2/3  Comprobando el protocolo contra msmart-ng...
build\cli\aire-cli.exe selftest ..\mac\Tests\vectors.json
if errorlevel 1 goto error

echo 3/3  Compilando Aire.exe...
dotnet publish Aire -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o build\Aire --nologo -v q
if errorlevel 1 goto error

echo.
echo Listo: %CD%\build\Aire\Aire.exe
echo Es un solo archivo: puedes copiarlo donde quieras (o a otro PC) y abrirlo.
start "" explorer "%CD%\build\Aire"
pause
exit /b 0

:error
echo.
echo Algo ha fallado. Copia lo que sale arriba y pasaselo a Claude.
pause
exit /b 1
