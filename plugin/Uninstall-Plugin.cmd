@echo off
<<<<<<< HEAD
rem Double-click to remove the Style Guide plugin for Paratext 9.
=======
rem Double-click to remove the Send Notes to Project plugin for Paratext 9.
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
rem Close Paratext first. Windows asks for administrator permission.
rem The quotes keep folder names with spaces working; -ExecutionPolicy Bypass lets the
rem script run even where PowerShell scripts are blocked by default. PowerShell is
rem called by its full path, so a powershell.exe in this folder is never used.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Plugin.ps1" -Uninstall -Pause
