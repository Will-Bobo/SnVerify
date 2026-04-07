@echo off
REM 供自动化/文档引用的入口，转调 build\build.cmd
call "%~dp0build\build.cmd" %*
