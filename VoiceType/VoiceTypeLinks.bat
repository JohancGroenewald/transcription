@echo off
setlocal

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%VoiceType.exe"

if not exist "%APP_EXE%" (
  echo [VoiceType] VoiceType.exe was not found in:
  echo [VoiceType]   %APP_DIR%
  echo [VoiceType] Place this file next to VoiceType.exe and run it again.
  exit /b 1
)

echo [VoiceType] Creating launcher shortcuts...
"%APP_EXE%" --create-activate-shortcut
set "ACTIVATE_EXIT=%ERRORLEVEL%"

"%APP_EXE%" --create-submit-shortcut
set "SUBMIT_EXIT=%ERRORLEVEL%"

echo [VoiceType] Creating no-prefix listener shortcut...
"%APP_EXE%" --create-listen-ignore-prefix-shortcut
set "LISTEN_NO_PREFIX_EXIT=%ERRORLEVEL%"

if "%ACTIVATE_EXIT%"=="0" if "%SUBMIT_EXIT%"=="0" if "%LISTEN_NO_PREFIX_EXIT%"=="0" (
  echo [VoiceType] Shortcuts created successfully.
  exit /b 0
)

echo [VoiceType] One or more shortcut operations failed.
exit /b 1
