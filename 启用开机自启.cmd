@echo off
setlocal
set "APP=%~dp0DiskPulse.exe"
if not exist "%APP%" (
    echo [ERROR] DiskPulse.exe was not found next to this script.
    pause
    exit /b 1
)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v DiskPulse /t REG_SZ /d "\"%APP%\" --silent" /f >nul
if errorlevel 1 (
    echo [ERROR] Could not write the startup entry. Check security software.
    pause
    exit /b 1
)
echo [OK] DiskPulse will now start silently when you sign in to Windows.
echo Closing in 3 seconds...
timeout /t 3 >nul
