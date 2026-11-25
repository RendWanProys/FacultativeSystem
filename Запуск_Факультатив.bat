@echo off
chcp 65001 >nul
echo Запуск ИС «Факультатив»...
start "ИС Факультатив — Web API" "%~dp0FacultativeSystem.Api\bin\Release\net9.0\FacultativeSystem.Api.exe"
timeout /t 2 /nobreak >nul
start "" "%~dp0FacultativeSystem\bin\Release\net9.0-windows\FacultativeSystem.exe" --public
