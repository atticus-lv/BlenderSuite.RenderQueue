@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "OPEN_OUTPUT=true"

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--no-open" (
    set "OPEN_OUTPUT=false"
    shift
    goto parse_args
)
if /I "%~1"=="-h" goto usage
if /I "%~1"=="--help" goto usage

echo ERROR: Unknown argument: %~1
echo.
goto usage_error

:args_done

if defined RENDERQUEUE_REPO_ROOT (
    for %%I in ("%RENDERQUEUE_REPO_ROOT%") do set "REPO_ROOT=%%~fI"
) else if defined GITHUB_WORKSPACE (
    for %%I in ("%GITHUB_WORKSPACE%") do set "REPO_ROOT=%%~fI"
) else (
    for %%I in ("%~dp0..\..") do set "REPO_ROOT=%%~fI"
)
set "PROJECT_FILE=%REPO_ROOT%\src\BlenderSuite.RenderQueue\BlenderSuite.RenderQueue.csproj"
set "PUBLISH_DIR=%REPO_ROOT%\install\Windows\publish\aot\win-x64"
set "OUTPUT_DIR=%REPO_ROOT%\install\Windows\output"
set "INNO_SCRIPT=%REPO_ROOT%\install\Windows\setup.iss"
set "APP_EXE_NAME=BlenderSuite.RenderQueue.exe"
set "RID=win-x64"
set "CONFIGURATION=Release"

if not exist "%PROJECT_FILE%" (
    echo ERROR: Project file not found: %PROJECT_FILE%
    exit /b 1
)

if not exist "%INNO_SCRIPT%" (
    echo ERROR: Inno Setup script not found: %INNO_SCRIPT%
    exit /b 1
)

for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Select-Xml -Path '%PROJECT_FILE%' -XPath '//Project/PropertyGroup/Version').Node.InnerText"`) do set "APP_VERSION=%%I"

if not defined APP_VERSION (
    echo ERROR: Failed to read project version from %PROJECT_FILE%
    exit /b 1
)

set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if defined INNO_SETUP_COMPILER set "ISCC_PATH=%INNO_SETUP_COMPILER%"

if not exist "!ISCC_PATH!" (
    echo ERROR: Inno Setup compiler not found.
    echo Expected at: !ISCC_PATH!
    echo You can also set INNO_SETUP_COMPILER to the full path of ISCC.exe
    exit /b 1
)

echo ============================================================
echo Building Windows Native AOT installer
echo Version: %APP_VERSION%
echo RID: %RID%
echo ============================================================

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%" > nul

dotnet publish "%PROJECT_FILE%" ^
  -c %CONFIGURATION% ^
  -r %RID% ^
  --self-contained true ^
  -p:PublishAot=true ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b %errorlevel%

"%ISCC_PATH%" ^
  "/dUTF8Output=yes" ^
  "/DMyAppExeName=%APP_EXE_NAME%" ^
  "/DMyAppVersion=%APP_VERSION%" ^
  "/DMyPublishDir=%PUBLISH_DIR%" ^
  "/DMyOutputDir=%OUTPUT_DIR%" ^
  "%INNO_SCRIPT%"
if errorlevel 1 exit /b %errorlevel%

echo ============================================================
echo Installer created successfully
echo Output: %OUTPUT_DIR%
echo ============================================================

if /I "%OPEN_OUTPUT%"=="true" explorer "%OUTPUT_DIR%"
exit /b 0

:usage
echo Usage: build-windows-aot-installer.bat [--no-open]
echo.
echo Builds the Windows Native AOT publish and packages it with Inno Setup.
echo Final installer output is written to install\Windows\output.
exit /b 0

:usage_error
echo Usage: build-windows-aot-installer.bat [--no-open]
exit /b 1
