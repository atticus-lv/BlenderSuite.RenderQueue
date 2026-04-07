@echo off
setlocal
call "%~dp0scripts\release\build-android-apk.bat" %*
exit /b %errorlevel%
