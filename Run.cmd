@echo off
cd /d "%~dp0"
dotnet run --project TodoApp\TodoApp.csproj --launch-profile TodoApp
pause
