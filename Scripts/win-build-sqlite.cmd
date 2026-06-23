@echo off
REM Builds a static sqlite3.lib from the bundled amalgamation using MSVC.
REM SweetCookieKit (and CodexBar's Chromium cookie importers) link `sqlite3`,
REM which is a system library on macOS/Linux but absent on Windows. Run once;
REM the resulting ThirdParty\sqlite-win\sqlite3.lib is added to the linker path
REM by Scripts\win-build.cmd.
setlocal
set "VSVARS=C:\Program Files\Tools\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
set "SQLDIR=%~dp0..\ThirdParty\sqlite-win"
call "%VSVARS%" >nul 2>&1
pushd "%SQLDIR%"
REM /MD matches the dynamic UCRT the Swift toolchain links against.
cl /nologo /c /O2 /MD /DSQLITE_ENABLE_COLUMN_METADATA /DSQLITE_ENABLE_FTS5 sqlite3.c
if errorlevel 1 ( popd & exit /b 1 )
lib /nologo /OUT:sqlite3.lib sqlite3.obj
popd
endlocal
