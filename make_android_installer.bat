@echo off
setlocal
call "%~dp0make_android_apk.bat" %*
exit /b %errorlevel%
