@echo off
setlocal EnableExtensions
pushd "%~dp0"
set "BRANCH=feature/companion-capsule-eyes"

where git >nul 2>nul
if errorlevel 1 (
  echo ERROR: Git is required.
  goto :error
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 10 SDK is required.
  goto :error
)

echo [1/5] Fetching current AURA feature branch...
git fetch origin "%BRANCH%"
if errorlevel 1 goto :error

git show-ref --verify --quiet "refs/heads/%BRANCH%"
if errorlevel 1 (
  git switch -c "%BRANCH%" --track "origin/%BRANCH%"
) else (
  git switch "%BRANCH%"
)
if errorlevel 1 (
  echo ERROR: Could not switch branch. Commit or stash local changes first.
  goto :error
)

git pull --ff-only origin "%BRANCH%"
if errorlevel 1 (
  echo ERROR: Branch diverged. Nothing was overwritten.
  goto :error
)
echo Selected branch:
git branch --show-current
git log -1 --format="Commit: %%h %%s"

echo [2/5] Closing an older running AURA...
taskkill /F /IM AURA.exe >nul 2>nul

echo [3/5] Cleaning old publish folder...
if exist "DynamicIsland\publish" rmdir /S /Q "DynamicIsland\publish"

echo [4/5] Building the latest self-contained AURA...
dotnet publish "DynamicIsland\DynamicIsland.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  -o "DynamicIsland\publish"
if errorlevel 1 goto :error

if not exist "DynamicIsland\publish\AURA.exe" (
  echo ERROR: Expected AURA.exe was not produced.
  goto :error
)

echo [5/5] Starting the freshly built executable...
start "" "%CD%\DynamicIsland\publish\AURA.exe"
popd
exit /b 0

:error
echo AURA update/build failed; see error above. Old source files were not discarded.
popd
exit /b 1
