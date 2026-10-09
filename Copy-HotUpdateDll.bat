@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
set "SOURCE_DIR=%PROJECT_ROOT%HybridCLRData\HotUpdateDlls\WebGL"
set "HOT_UPDATE_TARGET_DIR=%PROJECT_ROOT%Assets\GameAssets\Hotfix\Dll"
set "DEPEND_TARGET_DIR=%PROJECT_ROOT%Assets\GameAssets\Hotfix\DependDll"

if not exist "%SOURCE_DIR%\HotUpdate.dll" (
    echo [ERROR] Source file not found: "%SOURCE_DIR%\HotUpdate.dll"
    exit /b 1
)

if not exist "%SOURCE_DIR%\Shared.dll" (
    echo [ERROR] Source file not found: "%SOURCE_DIR%\Shared.dll"
    exit /b 1
)

if not exist "%HOT_UPDATE_TARGET_DIR%" mkdir "%HOT_UPDATE_TARGET_DIR%"
if errorlevel 1 (
    echo [ERROR] Failed to create target directory: "%HOT_UPDATE_TARGET_DIR%"
    exit /b 1
)

if not exist "%DEPEND_TARGET_DIR%" mkdir "%DEPEND_TARGET_DIR%"
if errorlevel 1 (
    echo [ERROR] Failed to create target directory: "%DEPEND_TARGET_DIR%"
    exit /b 1
)

copy /Y "%SOURCE_DIR%\HotUpdate.dll" "%HOT_UPDATE_TARGET_DIR%\HotUpdate.dll.bytes" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy HotUpdate.dll
    exit /b 1
)

copy /Y "%SOURCE_DIR%\Shared.dll" "%DEPEND_TARGET_DIR%\Shared.dll.bytes" >nul
if errorlevel 1 (
    echo [ERROR] Failed to copy Shared.dll
    exit /b 1
)

echo HotUpdate.dll -^> Assets\GameAssets\Hotfix\Dll\HotUpdate.dll.bytes
echo Shared.dll -^> Assets\GameAssets\Hotfix\DependDll\Shared.dll.bytes
echo DLL copy completed.

endlocal
exit /b 0
