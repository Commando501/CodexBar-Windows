@echo off
REM Convenience wrapper to run the Windows-built codexbar CLI with the Swift
REM runtime on PATH. Usage: codexbar config providers   /   codexbar usage --json
setlocal
set "EXE=%~dp0.build\x86_64-unknown-windows-msvc\debug\CodexBarCLI.exe"
set "SWIFT_RT=%LOCALAPPDATA%\Programs\Swift\Runtimes\6.3.2\usr\bin"
set "PATH=%SWIFT_RT%;%PATH%"
"%EXE%" %*
endlocal
