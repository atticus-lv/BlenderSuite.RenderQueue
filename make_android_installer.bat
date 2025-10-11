@echo off
chcp 65001 > nul
echo Building Optimized Android APK...

echo Cleaning previous build...
dotnet clean Client/QueueClient.Android -c Release

echo Restoring packages...
dotnet restore Client/QueueClient.Android

echo Building Release APK with maximum optimizations (ARM64 only)...
dotnet publish Client/QueueClient.Android -c Release -f net9.0-android ^
    -p:AndroidPackageFormat=apk ^
    -p:AndroidEnableProfiledAot=false ^
    -p:AndroidUseSharedRuntime=false ^
    -p:AndroidLinkMode=SdkOnly ^
    -p:AndroidEnableAssemblyCompression=true ^
    -p:AndroidStoreUncompressedFileExtensions="" ^
    -p:AndroidLinkTool=r8 ^
    -p:AndroidLinkSkip="System.Runtime.Serialization" ^
    -p:AndroidEnableMultiDex=false ^
    -p:RuntimeIdentifiers=android-arm64

echo Copying ARM64 APK files to Install/Android...
if exist "Client\QueueClient.Android\bin\Release\net9.0-android\*-arm64*.apk" (
    copy "Client\QueueClient.Android\bin\Release\net9.0-android\*-arm64*.apk" "Install\Android\"
    echo ARM64 APK files copied successfully!
) else (
    echo Warning: No ARM64 APK files found in Release build directory.
    echo Checking Debug build directory...
    if exist "Client\QueueClient.Android\bin\Debug\net9.0-android\*.apk" (
        copy "Client\QueueClient.Android\bin\Debug\net9.0-android\*.apk" "Install\Android\"
        echo APK files copied from Debug build!
    ) else (
        echo Error: No APK files found in either Release or Debug directories.
    )
)

echo Opening Android output folder...
explorer "Install\Android"

echo Optimized Android build Done!
pause
