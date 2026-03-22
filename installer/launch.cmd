@echo off
setlocal

set "APP_DIR=%~dp0"
if "%APP_DIR:~-1%"=="\" set "APP_DIR=%APP_DIR:~0,-1%"
set "RUNTIME_DIR=%APP_DIR%\runtime"
set "ENSURE_DEPS=%APP_DIR%\deps\ensure_deps.ps1"

if not exist "%RUNTIME_DIR%\venv\Scripts\python.exe" (
  if exist "%ENSURE_DEPS%" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ENSURE_DEPS%" -InstallDir "%APP_DIR%"
    if errorlevel 1 exit /b %errorlevel%
  )
)

if exist "%RUNTIME_DIR%\dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%RUNTIME_DIR%\dotnet"
  set "PATH=%DOTNET_ROOT%;%PATH%"
)

if exist "%RUNTIME_DIR%\venv\Scripts\python.exe" (
  set "PATH=%RUNTIME_DIR%\venv\Scripts;%PATH%"
) else if exist "%RUNTIME_DIR%\python\python.exe" (
  set "PATH=%RUNTIME_DIR%\python;%PATH%"
)

start "" /D "%APP_DIR%" "%APP_DIR%\PhotoSelector.App.exe"
