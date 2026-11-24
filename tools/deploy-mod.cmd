@echo off
rem ---------------------------------------------------------------------------
rem  Deploys the VGS mod into the Space Engineers local mod folder.
rem
rem  Cmd twin of tools\deploy-mod.ps1, for machines where PowerShell script
rem  execution is blocked by policy. It does exactly the same thing: mirror
rem  src\GridWorkspace.Mod into %APPDATA%\SpaceEngineers\Mods\<folder> and verify that the
rem  result has the layout the game actually compiles (Data\Scripts\**\*.cs).
rem
rem  The game compiles only .cs files under Data\Scripts of the mod folder. A
rem  folder whose sources sit in its root loads as an empty mod and fails world
rem  loading with MOD_CRITICAL_ERROR, which is why the layout is checked here and
rem  again after the copy.
rem
rem  Usage:
rem      tools\deploy-mod.cmd                 deploy into ...\Mods\GridWorkspace
rem      tools\deploy-mod.cmd GridWorkspace-dev deploy into ...\Mods\GridWorkspace-dev
rem      tools\deploy-mod.cmd /force          deploy even if the game is running
rem      tools\deploy-mod.cmd GridWorkspace-dev /force  both
rem
rem  Environment override (mainly for tests):
rem      set VGS_MODS_ROOT=<path>             use another Mods folder
rem
rem  Exit codes: 0 = deployed, 1 = failed (nothing copied or copy unverified).
rem ---------------------------------------------------------------------------

setlocal EnableExtensions

set "MOD_FOLDER=%~1"
if "%MOD_FOLDER%"=="/force" (
    set "MOD_FOLDER="
    set "FORCE=1"
) else if /i "%~2"=="/force" (
    set "FORCE=1"
)
if not defined MOD_FOLDER set "MOD_FOLDER=GridWorkspace"

rem Repo root is the parent of this script's folder.
for %%I in ("%~dp0..") do set "REPO=%%~fI"
set "SRC=%REPO%\src\GridWorkspace.Mod"

if defined VGS_MODS_ROOT (
    set "MODS_ROOT=%VGS_MODS_ROOT%"
) else (
    set "MODS_ROOT=%APPDATA%\SpaceEngineers\Mods"
)
set "TARGET=%MODS_ROOT%\%MOD_FOLDER%"

echo Source : %SRC%
echo Target : %TARGET%

rem --- 1. Preconditions -------------------------------------------------------

if not exist "%SRC%\Data\Scripts\VGS" (
    echo ERROR: mod sources not found: "%SRC%\Data\Scripts\VGS"
    echo        Run this script from the repository: tools\deploy-mod.cmd
    exit /b 1
)
if not exist "%MODS_ROOT%" (
    echo ERROR: mods folder not found: "%MODS_ROOT%"
    echo        Start Space Engineers once, so it creates its profile folders.
    exit /b 1
)

rem The game compiles the sources at world load; copying under a running game
rem gives an unhelpful compile error inside the game instead of here.
if not defined FORCE (
    tasklist /fi "imagename eq SpaceEngineers.exe" 2>nul | find /i "SpaceEngineers.exe" >nul
    if not errorlevel 1 (
        echo ERROR: SpaceEngineers.exe is running. Close the game first.
        echo        Pass /force if you know the game is only at the main menu.
        exit /b 1
    )
)

rem Count sources before touching the target, so a broken source tree fails
rem before the deployed copy is destroyed.
set "SRC_COUNT=0"
for /f %%N in ('dir /s /b "%SRC%\Data\Scripts\VGS\*.cs" 2^>nul ^| find /c /v ""') do set "SRC_COUNT=%%N"
if "%SRC_COUNT%"=="0" (
    echo ERROR: no .cs files under "%SRC%\Data\Scripts\VGS"
    exit /b 1
)
echo Files  : %SRC_COUNT% .cs

rem --- 2. Mirror --------------------------------------------------------------

rem Mirror, not merge: a stale .cs left behind would still be compiled by the
rem game and produce confusing duplicate-type errors.
if exist "%TARGET%" (
    rmdir /s /q "%TARGET%"
    if exist "%TARGET%" (
        echo ERROR: cannot remove the previous copy: "%TARGET%"
        echo        Close the game, or check that no file there is read-only.
        exit /b 1
    )
)
mkdir "%TARGET%"
if errorlevel 1 (
    echo ERROR: cannot create "%TARGET%"
    exit /b 1
)

xcopy "%SRC%" "%TARGET%" /e /i /y /q >nul
if errorlevel 1 (
    echo ERROR: xcopy failed, the deployed mod is incomplete.
    exit /b 1
)

rem --- 3. Verify what actually landed -----------------------------------------

if not exist "%TARGET%\Data\Scripts\VGS" (
    echo ERROR: "%TARGET%\Data\Scripts\VGS" is missing after the copy.
    echo        The game would load this as an empty mod and fail the world load.
    exit /b 1
)

set "DST_COUNT=0"
for /f %%N in ('dir /s /b "%TARGET%\Data\Scripts\VGS\*.cs" 2^>nul ^| find /c /v ""') do set "DST_COUNT=%%N"
if not "%DST_COUNT%"=="%SRC_COUNT%" (
    echo ERROR: deployed %DST_COUNT% of %SRC_COUNT% .cs file^(s^).
    exit /b 1
)

echo.
echo Deployed %DST_COUNT% .cs file^(s^) to "%TARGET%"
if not exist "%TARGET%\metadata.mod" (
    echo WARNING: metadata.mod is missing; the game still loads the mod,
    echo          but bug reports lose the version string.
)

echo.
echo Next: enable "%MOD_FOLDER%" in the world's mod list, load the world, then run:
echo   pwsh -File tools\tail-mod-log.ps1
echo   or: type "%%APPDATA%%\SpaceEngineers\SpaceEngineers.log"

endlocal
exit /b 0
