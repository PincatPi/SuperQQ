@echo off
chcp 65001 >nul
setlocal

rem ====== 一键生成所有 .proto 的 C# 代码 ======
rem 用法：双击运行，或在工程根目录执行 Tools\gen_proto.bat

set "ROOT=%~dp0.."
for %%i in ("%ROOT%") do set "ROOT=%%~fi"
set "PROTOC=%ROOT%\Tools\protoc\bin\protoc.exe"
set "PROTO_DIR=%ROOT%\Protos"
set "OUT_DIR=%ROOT%\Assets\Scripts\Network\Proto"

if not exist "%PROTOC%" (
    echo [错误] 未找到 protoc: %PROTOC%
    pause
    exit /b 1
)

if not exist "%PROTO_DIR%" (
    echo [错误] 未找到协议目录: %PROTO_DIR%
    pause
    exit /b 1
)

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo 正在生成 Protobuf C# 代码...
echo   协议目录: %PROTO_DIR%
echo   输出目录: %OUT_DIR%
echo.

rem 递归收集所有 .proto 文件（支持 account/v1/xxx.proto 这类子目录结构）
setlocal enabledelayedexpansion
set "FILES="
for /r "%PROTO_DIR%" %%f in (*.proto) do (
    set "FILES=!FILES! "%%f""
)
"%PROTOC%" --csharp_out="%OUT_DIR%" --proto_path="%PROTO_DIR%" !FILES!
set "GEN_ERR=%errorlevel%"
endlocal & set "GEN_ERR=%GEN_ERR%"

if %GEN_ERR% neq 0 (
    echo.
    echo [失败] protoc 生成出错，请检查 .proto 语法
    pause
    exit /b %GEN_ERR%
)

echo.
echo [完成] 所有协议代码已更新，回 Unity 等待自动编译即可。
pause
