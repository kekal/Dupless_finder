@echo off
setlocal

set RESULTS_DIR=%~dp0TestResults
set REPORT_DIR=%RESULTS_DIR%\CoverageReport

if exist "%RESULTS_DIR%" rmdir /s /q "%RESULTS_DIR%"

dotnet test "%~dp0DuplessFinder.Web.Tests\DuplessFinder.Web.Tests.csproj" --settings "%~dp0coverage.runsettings" --collect:"XPlat Code Coverage" --results-directory "%RESULTS_DIR%" --nologo
if %ERRORLEVEL% neq 0 (
    echo Tests failed.
    exit /b %ERRORLEVEL%
)

reportgenerator -reports:"%RESULTS_DIR%\**\coverage.cobertura.xml" -targetdir:"%REPORT_DIR%" -reporttypes:HtmlInline
if %ERRORLEVEL% neq 0 (
    echo reportgenerator not found. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool
    exit /b 1
)

start "" "%REPORT_DIR%\index.html"
