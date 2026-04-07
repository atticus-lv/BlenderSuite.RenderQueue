@echo off
setlocal
call "%~dp0make_windows_aot_installer.bat" %*
exit /b %errorlevel%
