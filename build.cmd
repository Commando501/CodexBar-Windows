@echo off
REM Convenience wrapper for build.ps1 so you can run the whole Windows build
REM with a single command and no PowerShell ceremony, e.g.:
REM   build                 (debug build of CLI + tray)
REM   build -Run            (build, then launch the tray)
REM   build -Package -Run   (build the portable version, then launch it)
REM   build -Release        (release build of both)
REM   build -Test           (build, then run the Swift test suite)
REM Any arguments are passed straight through to build.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
