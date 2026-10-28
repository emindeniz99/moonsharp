@echo off
setlocal

set "ROOT=%~dp0.."

call :sync_cs_tree "%ROOT%\MoonSharp.Interpreter" "%~dp0MoonSharp\Assets\Plugins\MoonSharp\Interpreter" "MoonSharp interpreter sources"
if errorlevel 1 exit /b 1

call :sync_cs_tree "%ROOT%\MoonSharp.VsCodeDebugger" "%~dp0MoonSharp\Assets\Plugins\MoonSharp\Debugger" "MoonSharp debugger sources"
if errorlevel 1 exit /b 1

call :sync_cs_tree "%ROOT%\MoonSharp.Interpreter.Tests" "%~dp0MoonSharp\Assets\Tests" "MoonSharp test sources"
if errorlevel 1 exit /b 1

rem Keep in step with UnityTestBed\sync.sh.
call :sync_cs_tree "%ROOT%\MoonSharp.Interpreter.Tests" "%~dp0UnityTestBed\Assets\MoonSharp.Interpreter.Tests" "UnityTestBed test sources"
if errorlevel 1 exit /b 1

call :sync_testbed_scripts "%ROOT%\MoonSharp.Interpreter.Tests\TestMore" "%~dp0UnityTestBed\Assets\Resources\MoonSharp\Scripts"
if errorlevel 1 exit /b 1

echo Done.
exit /b 0

:sync_cs_tree
set "SOURCE=%~1"
set "DEST=%~2"
set "LABEL=%~3"

if not exist "%SOURCE%" (
    echo %LABEL%: source directory not found: %SOURCE%
    exit /b 1
)

if not exist "%DEST%" mkdir "%DEST%"

rem Remove only mirrored C# files; keep existing Unity .meta GUID files.
for /R "%DEST%" %%F in (*.cs) do (
    del /F /Q "%%F" >nul
)

robocopy "%SOURCE%" "%DEST%" *.cs /E /XD obj /XF *.csproj >nul
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
    echo %LABEL%: robocopy failed with exit code %RC%.
    exit /b 1
)

echo %LABEL%: synced.
exit /b 0

:sync_testbed_scripts
set "SOURCE=%~1"
set "DEST=%~2"

rem Unity finds 000-sanity.t.txt as "000-sanity.t" and More.lua.txt as "More.lua".
if exist "%DEST%" rmdir /S /Q "%DEST%"
mkdir "%DEST%"
for %%F in ("%SOURCE%\*.t" "%SOURCE%\Modules\Test\*.lua") do (
    copy /Y "%%F" "%DEST%\%%~nxF.txt" >nul
)

echo UnityTestBed scripts: synced.
exit /b 0
