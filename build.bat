@echo off
setlocal
cd /d "%~dp0"

echo === Building HyenaQuestCheat (net472 / BepInEx 5) ===
dotnet build -c Release 2>&1
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see errors above.
  goto :end
)

set SRC=bin\Release\HyenaQuestCheat.dll
if exist "%SRC%" (
  copy /Y "%SRC%" "..\BepInEx\plugins\HyenaQuestCheat.dll" >nul
  echo.
  echo SUCCESS - copied to ..\BepInEx\plugins\HyenaQuestCheat.dll
  echo Launch the game. Press INS for the menu.
) else (
  echo.
  echo Output not found: %SRC%
)

:end
echo.
pause
