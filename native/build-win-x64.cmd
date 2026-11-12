@echo off
setlocal
if /I not "%VSCMD_ARG_TGT_ARCH%"=="x64" (
    echo Open the x64 Native Tools Command Prompt for VS 2022 and try again.
    exit /b 1
)
pushd "%~dp0"
if not exist "..\runtimes\win-x64\native" mkdir "..\runtimes\win-x64\native"
if not exist "obj" mkdir "obj"
cl /nologo /LD /MT /O2 /W4 /WX /TC /Fo"obj\bookdesk_fee.obj" bookdesk_fee.c /link /MACHINE:X64 /OUT:"..\runtimes\win-x64\native\bookdesk_fee.dll" /IMPLIB:"obj\bookdesk_fee.lib"
set "BOOKDESK_BUILD_STATUS=%ERRORLEVEL%"
popd
exit /b %BOOKDESK_BUILD_STATUS%
