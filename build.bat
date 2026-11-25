@echo off
setlocal
cd /d "%~dp0"
rem Compila o Beat usando o compilador que ja vem no Windows (.NET Framework 4).
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:Beat.exe /win32icon:Beat.ico ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Core.dll /r:Microsoft.CSharp.dll ^
  Beat.cs

if errorlevel 1 (
  echo.
  echo ERRO ao compilar. Mande a mensagem acima para o Claude.
  pause
  exit /b 1
)
echo.
echo Pronto! Abra o Beat.exe (na primeira vez ele baixa o yt-dlp sozinho).
echo Dica: se voce ja tem o yt-dlp.exe da versao anterior, copie para esta pasta.
pause
