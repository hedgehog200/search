@echo off
dotnet restore search.sln
dotnet build search.sln -c Release
pause
