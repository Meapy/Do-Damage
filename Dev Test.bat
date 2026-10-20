@echo off
rem Local development: rebuilds and restarts an isolated test stack (slot 1: Redis 6381,
rem account 8082, world 2053), then builds and opens the Rust client against it; or builds the
rem website and its wiki and opens them in your browser. Nothing here touches the live game.
rem
rem   Dev Test.bat          a menu of the four below (what a double-click shows)
rem   Dev Test.bat all      servers and client
rem   Dev Test.bat server   servers only
rem   Dev Test.bat client   client only
rem   Dev Test.bat site     the website and wiki, at http://127.0.0.1:8000/wiki/
rem
rem Close your test client window first. The world server is stopped without saving, and the
rem stack's data (the accounts and characters you made on it) is kept. On a fresh clone the
rem stack starts empty: register an account in the client.

setlocal
cd /d "%~dp0"
set SLOT=1
set ACCOUNT=http://127.0.0.1:8082
set SITE_PORT=8000
set MODE=%~1

rem The .NET SDK and Cargo, when a shell doesn't have them on its PATH.
where dotnet >nul 2>nul || if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"
where cargo >nul 2>nul || if exist "%USERPROFILE%\.cargo\bin\cargo.exe" set "PATH=%USERPROFILE%\.cargo\bin;%PATH%"

if "%MODE%"=="" (
    echo Do Damage local development
    echo.
    echo   1  Test servers and the client
    echo   2  Test servers only ^(restart them^)
    echo   3  The client only
    echo   4  The website and wiki, in your browser
    echo.
    choice /c 1234 /n /m "Pick 1-4: "
    if errorlevel 4 (set "MODE=site") else if errorlevel 3 (set "MODE=client") else if errorlevel 2 (set "MODE=server") else (set "MODE=all")
)

if /i "%MODE%"=="client" goto client
if /i "%MODE%"=="site" goto site

echo === Stopping test servers (slot %SLOT%)
python tools\testclient\iso_stack.py --slot %SLOT% stop

echo === Building and starting test servers
python tools\testclient\iso_stack.py --slot %SLOT% start
if errorlevel 1 (
    echo.
    echo Server build or start failed, see above.
    pause
    exit /b 1
)

rem The world server takes a while to start and register; a client that signs in before then
rem gets "the account lists no servers".
echo === Waiting for the world server to come up
set TRIES=0
:wait
curl -s -m 3 -X POST %ACCOUNT%/app/serverList 2>nul | findstr /c:"<Server>" >nul && goto ready
set /a TRIES+=1
if %TRIES% geq 120 (
    echo The world server didn't register within 2 minutes; see build\iso%SLOT%\ws\ws.out.
    pause
    exit /b 1
)
ping -n 2 127.0.0.1 >nul
goto wait
:ready
echo World server is up.

if /i "%MODE%"=="server" (
    echo.
    echo Servers are up. Sign in again in your open client.
    pause
    exit /b 0
)

:client
echo === Building and opening the client (%ACCOUNT%)
cd client-rs
cargo run -p bsk-client -- --account %ACCOUNT%
if errorlevel 1 (
    echo.
    echo Client build failed or the client exited with an error, see above.
    pause
    exit /b 1
)
exit /b 0

:site
rem The website as dodmg.com serves it (tools\build_site.py, wiki included), from build\site.
rem Run it again after changing website\ or the game data to see the change; Ctrl+C stops it.
echo === Building the website and wiki
python tools\build_site.py
if errorlevel 1 (
    echo.
    echo The site build failed, see above.
    pause
    exit /b 1
)
echo === Serving it at http://127.0.0.1:%SITE_PORT%/  (Ctrl+C to stop)
start "" http://127.0.0.1:%SITE_PORT%/wiki/
python -m http.server %SITE_PORT% --bind 127.0.0.1 --directory build\site
