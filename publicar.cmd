@echo off
rem publicar.cmd - valida, hace commit y sube los cambios a GitHub.
rem Uso:  publicar.cmd                      (pide el mensaje)
rem       publicar.cmd "docs: update specs" (usa el mensaje dado)
rem Formato del mensaje: tipo: descripcion en ingles, imperativo y en minusculas
rem (feat, fix, chore, docs, test, refactor). No uses comillas dentro del mensaje.
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 goto :norepo

for /f "delims=" %%b in ('git branch --show-current') do set "RAMA=%%b"
echo.
echo Rama actual: %RAMA%

echo.
echo [1/4] Validando specs, planes y enlaces...
dotnet run --project tools\SpecCheck
if errorlevel 1 goto :errspec

echo.
echo [2/4] Cambios detectados:
git status --short
git add -A

rem Aborta si se va a subir algo que parece un secreto.
git diff --cached --name-only | findstr /i /r "\.env$ \.pem$ \.key$ \.pfx$ \.p12$ id_rsa secrets\.json credentials.*\.json" >nul
if not errorlevel 1 goto :secreto

git diff --cached --quiet
if not errorlevel 1 goto :sincambios

echo.
echo [3/4] Commit
set "MSG=%~1"
if defined MSG goto :hacercommit
set /p "MSG=Mensaje (tipo: descripcion en ingles): "
if not defined MSG goto :sinmensaje

:hacercommit
git commit -m "%MSG%"
if errorlevel 1 goto :errcommit
goto :push

:sincambios
echo.
echo [3/4] No hay cambios nuevos para commitear.

:push
echo.
echo [4/4] Push
set "N="
for /f %%n in ('git rev-list --count @{u}..HEAD 2^>nul') do set "N=%%n"
if not defined N goto :sinupstream
if "%N%"=="0" goto :nadaquesubir
echo Commits locales sin subir: %N%
set "OK="
set /p "OK=Subir a origin/%RAMA%? (s/n): "
if /i not "%OK%"=="s" goto :cancelado
git push origin %RAMA%
if errorlevel 1 goto :errpush
goto :fin

:sinupstream
echo La rama %RAMA% aun no existe en origin.
set "OK="
set /p "OK=Crearla y subirla con git push -u? (s/n): "
if /i not "%OK%"=="s" goto :cancelado
git push -u origin %RAMA%
if errorlevel 1 goto :errpush
goto :fin

:nadaquesubir
echo Nada que subir: la rama esta al dia con origin.
goto :fin

:norepo
echo Esta carpeta no es un repositorio Git.
exit /b 1

:errspec
echo.
echo ERROR: SpecCheck fallo. Corrige los errores antes de commitear.
exit /b 1

:secreto
echo.
echo ERROR: hay archivos que parecen secretos en el area de preparacion:
git diff --cached --name-only | findstr /i /r "\.env$ \.pem$ \.key$ \.pfx$ \.p12$ id_rsa secrets\.json credentials.*\.json"
git reset -q
echo Se cancelo. Quita esos archivos o agregalos a .gitignore.
exit /b 1

:sinmensaje
echo.
echo Mensaje vacio. Se cancelo el commit; los cambios quedan preparados.
exit /b 1

:errcommit
echo.
echo ERROR: el commit fallo.
exit /b 1

:errpush
echo.
echo ERROR: el push fallo. Revisa tu conexion o tus credenciales de GitHub.
exit /b 1

:cancelado
echo.
echo Push cancelado. Los commits siguen en local.
exit /b 0

:fin
echo.
echo Listo.
endlocal
