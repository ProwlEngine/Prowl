@echo off
dotnet build "%~dp0MobaGame.csproj" -c Release
if %errorlevel% neq 0 (
    echo Build failed.
    pause
    exit /b 1
)
echo.
echo Starting server...
"%~dp0bin\Release\net8.0\MobaGame.exe" --server %*
pause
