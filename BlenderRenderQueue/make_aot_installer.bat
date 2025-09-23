@echo off
chcp 65001 > nul
echo Building Release Aot...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true -o Install/Publish

echo Creating installer...
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/dUTF8Output=yes" ".\Install\Windows\setup.iss"

echo Opening output folder...
explorer ".\Install\Windows\Output"

echo Creating installer Done!
pause 