@echo off
rem Puts the install files on the clipboard. Paste into C:\Program Files (x86)\GRF Editor
powershell -STA -NoProfile -ExecutionPolicy Bypass -File "%~dp0copy-install.ps1"
pause
