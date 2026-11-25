@echo off
chcp 65001 >nul
echo Запуск DEV-версии ИС «Факультатив» (быстрый вход для проверки ролей)...
start "ИС Факультатив — Web API (Dev, Swagger)" "%~dp0FacultativeSystem.Api\bin\Debug\net9.0\FacultativeSystem.Api.exe"
timeout /t 2 /nobreak >nul
start "" "%~dp0FacultativeSystem\bin\Debug\net9.0-windows\FacultativeSystem.exe" --dev
