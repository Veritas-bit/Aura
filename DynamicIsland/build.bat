@echo off
rem ============================================
rem  AURA - сборка под Windows
rem  Требуется .NET SDK 10: https://dot.net
rem ============================================
setlocal
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo [1/2] Сборка (Release)...
"%DOTNET%" build -c Release || goto :err

echo [2/2] Публикация одиночного EXE (win-x64, автономный)...
"%DOTNET%" publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish || goto :err

echo.
echo Готово! Исполняемый файл: publish\AURA.exe
goto :eof

:err
echo ОШИБКА сборки. Проверьте, что установлен .NET SDK 10.
exit /b 1
