@echo off
setlocal
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v DiskPulse /f >nul 2>&1
if errorlevel 1 (
    echo [INFO] The DiskPulse startup entry was not found.
) else (
    echo [OK] The DiskPulse startup entry was removed.
)
timeout /t 2 >nul
