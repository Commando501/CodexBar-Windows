@echo off
REM Windows build wrapper for CodexBar engine (CodexBarCore + CodexBarCLI).
REM Loads the Visual Studio MSVC environment and the Swift toolchain, then runs
REM whatever swift command is passed as arguments, e.g.:
REM   Scripts\win-build.cmd build --target CodexBarCore
REM   Scripts\win-build.cmd test
setlocal
set "VSVARS=C:\Program Files\Tools\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
set "SWIFT_BIN=%LOCALAPPDATA%\Programs\Swift\Toolchains\6.3.2+Asserts\usr\bin"
set "SWIFT_RT=%LOCALAPPDATA%\Programs\Swift\Runtimes\6.3.2\usr\bin"
set "SDKROOT=%LOCALAPPDATA%\Programs\Swift\Platforms\6.3.2\Windows.platform\Developer\SDKs\Windows.sdk"
call "%VSVARS%" >nul 2>&1
set "PATH=%SWIFT_BIN%;%SWIFT_RT%;%PATH%"
REM Make the locally built static sqlite3.lib discoverable by the linker
REM (SweetCookieKit + Chromium cookie importers link `sqlite3`). Build it once
REM with Scripts\win-build-sqlite.cmd.
set "LIB=%~dp0..\ThirdParty\sqlite-win;%LIB%"
swift %*
endlocal
