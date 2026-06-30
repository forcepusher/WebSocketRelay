@echo off
setlocal

if /i "%~1"=="stop" goto stop
goto start

:stop
wmic process where "name='bun.exe' and CommandLine like '%%-relay-server%%'" call terminate >nul 2>&1
if /i "%~1"=="stop" exit /b 0

:start
call :stop

if exist "%~dp0..\..\package.json" (
  set "RELAY_CWD=%~dp0..\..\.."
  set "RELAY_ENTRY=com.bananaparty.websocketrelay\Runtime\Server\Source\index.ts"
) else (
  set "RELAY_CWD=%~dp0"
  set "RELAY_ENTRY=Source\index.ts"
)

set "BUN_PATH=%~dp0Bun\bun-windows-x64\bun.exe"
"%BUN_PATH%" --cwd "%RELAY_CWD%" %RELAY_ENTRY% -relay-server
