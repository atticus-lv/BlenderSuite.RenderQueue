@echo off
setlocal
call "%~dp0scripts\release\build-windows-aot-installer.bat" %*
exit /b %errorlevel%
