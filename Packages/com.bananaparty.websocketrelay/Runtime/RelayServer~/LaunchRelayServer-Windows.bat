@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "SSL_CERT=%SCRIPT_DIR%..\ssl.crt"
set "SSL_KEY=%SCRIPT_DIR%..\ssl.key"

if exist "%SSL_CERT%" if exist "%SSL_KEY%" goto :start_ssl
set "RELAY_PORT=80"
set "RELAY_TLS_CERT="
set "RELAY_TLS_KEY="
echo No SSL certificates found. Starting relay server on port 80 with WS.
goto :launch

:start_ssl
set "RELAY_PORT=443"
set "RELAY_TLS_CERT=%SSL_CERT%"
set "RELAY_TLS_KEY=%SSL_KEY%"
echo SSL certificates found. Starting relay server on port 443 with WSS.

:launch
cd /d "%~dp0"
set "BUN_PATH=%~dp0Bun\bun-windows-x64\bun.exe"

rem Bun's crash reporter would keep the port open after a crash, so the server could not start again.
set "BUN_ENABLE_CRASH_REPORTING=0"

rem Started again whenever it exits, because clients reconnect on their own and a crash should only cost them that.
:run
"%BUN_PATH%" Source\index.ts -relay-server
echo Relay server exited with code %ERRORLEVEL%. Starting it again in 1 second, press Ctrl+C to stop.
rem Unlike timeout, ping also waits when there is no console input, like when running as a service.
ping -n 2 127.0.0.1 >nul
goto :run
