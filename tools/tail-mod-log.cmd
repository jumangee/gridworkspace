@echo off
rem ---------------------------------------------------------------------------------------------
rem Prints the VGS mod's lines from the newest Space Engineers log.
rem
rem The cmd twin of tail-mod-log.ps1, for the same reason deploy-mod.cmd exists: a machine whose
rem execution policy forbids .ps1 files still has to be able to answer "what did the mod say".
rem Without it, diagnosing a defect in the game turns into a PowerShell configuration problem,
rem which is the wrong problem to have while looking at a broken screen.
rem
rem Usage:  tail-mod-log.cmd [lines] [/all] [logPath]
rem   lines    how many trailing lines to consider (default 300)
rem   /all     show the whole tail, not only this mod's lines
rem ---------------------------------------------------------------------------------------------
setlocal EnableExtensions EnableDelayedExpansion

set "LINES=300"
set "SHOW_ALL="
set "LOGPATH="

if /i "%~1"=="/all" ( set "SHOW_ALL=1" & shift )
if /i "%~1"=="-all" ( set "SHOW_ALL=1" & shift )
if not "%~1"=="" set "LINES=%~1"
if not "%~2"=="" set "LOGPATH=%~2"

if defined LOGPATH goto havepath

set "LOGDIR=%APPDATA%\SpaceEngineers"
if not exist "%LOGDIR%" (
    echo Space Engineers log folder not found: %LOGDIR%
    exit /b 1
)

for /f "delims=" %%F in ('dir /b /o-d "%LOGDIR%\SpaceEngineers_*.log" 2^>nul') do (
    set "LOGPATH=%LOGDIR%\%%F"
    goto havepath
)

echo No SpaceEngineers_*.log in %LOGDIR% - has the game been launched?
exit /b 1

:havepath
if not exist "%LOGPATH%" (
    echo Log not found: %LOGPATH%
    exit /b 1
)

echo Log: %LOGPATH%
echo.

rem -ExecutionPolicy Bypass applies to this one process, not to the machine. The script it runs
rem is this repository's own.
if defined SHOW_ALL (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -LiteralPath '%LOGPATH%' -Tail %LINES%"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$l = Get-Content -LiteralPath '%LOGPATH%' -Tail %LINES%; $l | Where-Object { $_ -match '\[VGS\]' -or ($_ -match 'Vgs' -and $_ -match 'Exception') }"
)

exit /b 0
