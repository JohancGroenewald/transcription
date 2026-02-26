@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
if exist "%WINDIR%\System32\WindowsPowerShell\v1.0\pwsh.exe" (
  set "POWERSHELL=%WINDIR%\System32\WindowsPowerShell\v1.0\pwsh.exe"
) else (
  set "POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
)

"%POWERSHELL%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build-alpha1.ps1" %*
exit /b %ERRORLEVEL%