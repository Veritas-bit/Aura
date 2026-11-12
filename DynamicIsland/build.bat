@echo off
setlocal
pushd "%~dp0"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo [1/2] Building Release...
"%DOTNET%" build -c Release
if errorlevel 1 goto :err

echo [2/2] Publishing self-contained Windows x64 executable...
"%DOTNET%" publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o publish
if errorlevel 1 goto :err

echo.
echo Done: publish\AURA.exe
popd
exit /b 0

:err
echo Build failed. Install the .NET 10 SDK and retry.
popd
exit /b 1
