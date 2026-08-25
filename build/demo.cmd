@echo off
REM One-command demo on Windows: build, test, then run the console walkthrough against the simulator.
setlocal
cd /d "%~dp0.."
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
bash build\check-no-network.sh || exit /b 1
dotnet build ROXDiagnostic.sln -c Release || exit /b 1
dotnet test ROXDiagnostic.sln -c Release || exit /b 1
dotnet run -c Release --project samples\Rox.Demo || exit /b 1
endlocal
