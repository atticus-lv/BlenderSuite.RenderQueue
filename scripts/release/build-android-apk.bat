@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 > nul

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
for %%I in ("%~dp0..\..") do set "REPO_ROOT=%%~fI"
set "PROJECT_FILE=%REPO_ROOT%\experimental\Client\QueueClient.Android\QueueClient.Android.csproj"
set "PUBLISH_DIR=%REPO_ROOT%\Install\Android\publish\android-arm64"
set "OUTPUT_DIR=%REPO_ROOT%\Install\Android\output"
set "CONFIGURATION=Release"
set "TARGET_FRAMEWORK=net10.0-android"
set "RID=android-arm64"
set "APP_VERSION="

if not exist "%PROJECT_FILE%" (
    echo ERROR: Project file not found: %PROJECT_FILE%
    exit /b 1
)

for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Select-Xml -Path '%PROJECT_FILE%' -XPath '//Project/PropertyGroup/ApplicationDisplayVersion').Node.InnerText"`) do set "APP_VERSION=%%I"

if not defined APP_VERSION set "APP_VERSION=1.0"

echo ============================================================
echo Building Android APK
echo Version: %APP_VERSION%
echo RID: %RID%
echo ============================================================

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%PUBLISH_DIR%" > nul
mkdir "%OUTPUT_DIR%" > nul

dotnet publish "%PROJECT_FILE%" ^
  -c %CONFIGURATION% ^
  -f %TARGET_FRAMEWORK% ^
  -o "%PUBLISH_DIR%" ^
  -p:AndroidPackageFormat=apk ^
  -p:AndroidEnableProfiledAot=false ^
  -p:AndroidUseSharedRuntime=false ^
  -p:AndroidLinkMode=SdkOnly ^
  -p:AndroidEnableAssemblyCompression=true ^
  -p:AndroidStoreUncompressedFileExtensions="" ^
  -p:AndroidLinkTool=r8 ^
  -p:AndroidLinkSkip="System.Runtime.Serialization" ^
  -p:AndroidEnableMultiDex=false ^
  -p:RuntimeIdentifiers=%RID%
  -p:RuntimeIdentifier=%RID%
if errorlevel 1 exit /b %errorlevel%

set "APK_PATH="
for /f "delims=" %%I in ('dir /b /a:-d "%PUBLISH_DIR%\*.apk" 2^>nul') do (
    if not defined APK_PATH set "APK_PATH=%PUBLISH_DIR%\%%I"
)

if not defined APK_PATH (
    echo ERROR: No APK file found in %PUBLISH_DIR%
    exit /b 1
)

set "OUTPUT_APK=%OUTPUT_DIR%\QueueClient-android-arm64-%APP_VERSION%.apk"
copy /y "%APK_PATH%" "%OUTPUT_APK%" > nul
if errorlevel 1 exit /b %errorlevel%

echo ============================================================
echo APK created successfully
echo Publish: %PUBLISH_DIR%
echo Output: %OUTPUT_APK%
echo ============================================================

if /I "%OPEN_OUTPUT%"=="true" explorer "%OUTPUT_DIR%"
exit /b 0

:usage
echo Usage: build-android-apk.bat [--no-open]
echo.
echo Builds the Android ARM64 APK and copies the final artifact to Install\Android\output.
exit /b 0

:usage_error
echo Usage: build-android-apk.bat [--no-open]
exit /b 1
