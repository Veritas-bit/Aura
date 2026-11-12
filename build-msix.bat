@echo off
setlocal
pushd "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET SDK 10 is not installed.
  goto :err
)
where winapp >nul 2>nul
if errorlevel 1 (
  echo ERROR: WinApp CLI is not installed.
  echo Install it with: winget install Microsoft.winappcli
  goto :err
)

if not exist "devcert.pfx" (
  echo [1/4] Creating a local development signing certificate...
  winapp cert generate --manifest "Package.appxmanifest" --if-exists skip --export-cer
  if errorlevel 1 goto :err
) else (
  echo [1/4] Using existing local development certificate.
)

echo [2/4] Publishing self-contained Windows x64 app...
dotnet publish "DynamicIsland\DynamicIsland.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false ^
  -o "publish-msix"
if errorlevel 1 goto :err

echo [3/4] Creating signed MSIX package...
winapp pack "publish-msix" --manifest "Package.appxmanifest" --cert "devcert.pfx"
if errorlevel 1 goto :err

echo [4/4] Package created. Install dev certificate once with:
echo   winapp cert install devcert.pfx
echo Then install the generated .msix package.
popd
exit /b 0

:err
echo.
echo MSIX packaging failed. See the error above.
popd
exit /b 1
