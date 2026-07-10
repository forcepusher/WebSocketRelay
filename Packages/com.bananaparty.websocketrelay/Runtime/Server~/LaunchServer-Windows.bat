@echo off
set "SCRIPT_DIR=%~dp0"
set "SSL_CERT=%SCRIPT_DIR%..\ssl.crt"
set "SSL_KEY=%SCRIPT_DIR%..\ssl.key"

if exist "%SSL_CERT%" if exist "%SSL_KEY%" (
    set "RELAY_PORT=443"
    set "RELAY_TLS_CERT=%SSL_CERT%"
    set "RELAY_TLS_KEY=%SSL_KEY%"
    echo SSL certificates found. Starting relay server on port 443 (wss://)
) else (
    set "RELAY_PORT=80"
    set "RELAY_TLS_CERT="
    set "RELAY_TLS_KEY="
    echo No SSL certificates found. Starting relay server on port 80 (ws://)
)

set "BUN_PATH=%SCRIPT_DIR%Bun\bun-windows-x64\bun.exe"
"%BUN_PATH%" --cwd "%SCRIPT_DIR%..\..\.." com.bananaparty.websocketrelay\Runtime\Server~\Source\index.ts -relay-server
