@echo off
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

set REFS=/reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll
set FLAGS=/target:winexe /optimize+ /langversion:Default /nologo

echo Compiling ClipLite...
"%CSC%" %FLAGS% %REFS% /out:ClipLite.exe Program.cs Models.cs Services.cs HistoryForm.cs

if %ERRORLEVEL% equ 0 (
    echo.
    echo Build successful: ClipLite.exe
    dir /-C ClipLite.exe
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)

endlocal
