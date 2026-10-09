@echo off
setlocal
cd /d "%~dp0"

rem Builds release\IBSaveConverter.exe and release\iOSBackup.exe.
rem Needs the .NET 9 SDK. Python 3 is only needed for iOSBackup.exe.

rem build.bat notrim   skips trimming if the trimmed exe ever misbehaves (bigger exe).
set "TRIM="
if /i "%~1"=="notrim" set "TRIM=-p:Trim=false"

set "RELEASE=%~dp0release"
set "HELPER_OUT=%~dp0artifacts\ios-helper"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 9 SDK is not installed. Get it from https://dotnet.microsoft.com/download
    goto failed
)

echo.
echo [1/2] Building iOSBackup.exe, the iOS backup reader...
set "PY="
py -3 --version >nul 2>nul && set "PY=py -3"
if not defined PY (
    python --version >nul 2>nul && set "PY=python"
)
if not defined PY (
    echo Python 3 was not found, so iOSBackup.exe is skipped.
    echo The release gets iOSBackup.py instead, which needs Python on the PC that runs it.
    if exist "%HELPER_OUT%\iOSBackup.exe" del /q "%HELPER_OUT%\iOSBackup.exe"
    goto app
)

%PY% -m pip install --disable-pip-version-check --quiet -r tools\ios-backup\requirements.txt pyinstaller
if errorlevel 1 goto failed

rem --icon NONE gives the plain Windows program icon instead of the Python logo.
rem The --exclude-module list drops Python parts the helper never uses. Do not add http, urllib or email: the exe then fails to start.
%PY% -m PyInstaller --noconfirm --clean --onefile --log-level WARN --icon NONE --name iOSBackup ^
    --exclude-module tkinter --exclude-module unittest --exclude-module pydoc --exclude-module doctest ^
    --exclude-module lib2to3 --exclude-module xmlrpc --exclude-module curses --exclude-module readline ^
    --exclude-module ssl --exclude-module _ssl --exclude-module cffi --exclude-module _cffi_backend ^
    --distpath "%HELPER_OUT%" --workpath "%HELPER_OUT%\build" --specpath "%HELPER_OUT%\build" ^
    tools\ios-backup\iOSBackup.py
if errorlevel 1 goto failed

:app
echo.
echo [2/2] Building IBSaveConverter.exe...
if exist "%RELEASE%" rmdir /s /q "%RELEASE%"
dotnet publish src\IBSaveConverter\IBSaveConverter.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:DebugType=none %TRIM% -o "%RELEASE%"
if errorlevel 1 goto failed

echo.
echo Done. The release folder holds:
dir /b "%RELEASE%"
echo.
echo If the app closes on start, look for crash.log next to IBSaveConverter.exe.
pause
exit /b 0

:failed
echo.
echo Build failed. See the messages above.
pause
exit /b 1
