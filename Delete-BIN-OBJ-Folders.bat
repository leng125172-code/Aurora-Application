@ECHO off
cls

ECHO Deleting all BIN and OBJ folders...
ECHO.

REM Use PowerShell for fast recursive search with excluded folders
powershell -Command "& {$ErrorActionPreference='SilentlyContinue'; $excluded = @('node_modules', '.venv', 'venv', '.virtualenv', 'env', '.env', '.conda', 'conda', 'miniconda', 'miniconda3', 'anaconda', 'anaconda3'); function Find-BinObjFolders($path) { Get-ChildItem -Path $path -Directory -ErrorAction SilentlyContinue | ForEach-Object { if ($excluded -contains $_.Name) { Write-Host \"Skipping: $($_.FullName)\"; return } $fullPath = $_.FullName; $isBinOrObj = $_.Name -eq 'bin' -or $_.Name -eq 'obj'; if ($isBinOrObj) { Write-Host \"Deleting: $fullPath\"; Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue } if (-not $isBinOrObj) { Find-BinObjFolders -path $fullPath } } }; Find-BinObjFolders -path '.' }"

ECHO.
ECHO.BIN and OBJ folders have been successfully deleted. Press any key to exit.
pause > nul
