@echo off
setlocal

:: Local packaging, mirroring release.yml so a hand-cut build matches CI. Publishes a self-contained
:: single-file binoc.exe, packs it with Velopack, and writes SHA256SUMS.txt over releases\ (the same
:: manifest install.ps1 verifies against). Pass a version, or it reads the csproj <Version>.
::   publish.bat 0.1.0

:: Read version from the Avalonia csproj if not passed as argument
if not "%~1"=="" (
    set VERSION=%~1
) else (
    for /f "tokens=*" %%i in ('powershell -NoProfile -Command "(Select-Xml -Path src\Binoc.App\Binoc.App.csproj -XPath \"//Version\").Node.InnerText"') do set VERSION=%%i
)

if "%VERSION%"=="" (
    echo Error: Could not determine version. Pass as argument: publish.bat 0.1.0
    exit /b 1
)

echo Building binoc v%VERSION%...

dotnet publish src\Binoc.App\Binoc.App.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=embedded ^
    -p:Version=%VERSION% ^
    -o publish\

if %ERRORLEVEL% neq 0 (
    echo Build failed.
    exit /b %ERRORLEVEL%
)

echo Packaging ...

vpk pack --packId Binoc --packTitle "binoc" --packVersion %VERSION% --packDir publish\ --mainExe binoc.exe --outputDir releases\

if %ERRORLEVEL% neq 0 (
    echo Pack failed. Is the vpk CLI installed? Run: dotnet tool install -g vpk
    exit /b %ERRORLEVEL%
)

echo Writing SHA256SUMS.txt ...

:: sha256sum-format manifest (lower-case hex, two spaces, LF), so install.ps1 and a Unix sha256sum -c agree.
powershell -NoProfile -Command ^
    "$out = Join-Path 'releases' 'SHA256SUMS.txt';" ^
    "Get-ChildItem -File 'releases' | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {" ^
    "  ('{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name)" ^
    "} | Set-Content -LiteralPath $out -Encoding ascii -NoNewline:$false"

if %ERRORLEVEL% neq 0 (
    echo Could not write SHA256SUMS.txt.
    exit /b %ERRORLEVEL%
)

echo.
echo Done. Artifacts are in releases\ (installer: Binoc-win-Setup.exe, manifest: SHA256SUMS.txt).
endlocal
