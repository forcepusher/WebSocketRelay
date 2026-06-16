@echo off
set BUN_PATH=%~dp0Bun\bun-windows-x64\bun.exe
"%BUN_PATH%" --cwd "%~dp0..\..\." com.bananaparty.websocketrelay\Server\Source\index.ts
