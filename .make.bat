git submodule update --init --recursive
cd submodules\luamake
call compile\build.bat
cd ..\..
IF "%~1"=="" (
    call submodules\luamake\luamake.exe rebuild
) ELSE (
    call submodules\luamake\luamake.exe rebuild --platform %1
)
