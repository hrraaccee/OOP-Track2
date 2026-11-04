@echo off
cd /d "%~dp0"
dotnet run --project TodoApp.Checks\TodoApp.Checks.csproj --configuration Release
pause
