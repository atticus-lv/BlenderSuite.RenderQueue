# Blender Suite: Render Queue 开发文档

英文版本见 [README.md](README.md)。

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 安装包打包额外需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)
- macOS `.dmg` 打包需要系统自带的 `hdiutil`、`sips`、`iconutil`

## 本地开发

```bash
git clone <repository-url>
cd BlenderSuite.RenderQueue
```

构建桌面应用和测试项目：

```bash
dotnet build BlenderSuite.RenderQueue.sln
```

运行测试：

```bash
dotnet test BlenderSuite.RenderQueue.sln
```

运行桌面应用：

```bash
dotnet run --project src/BlenderSuite.RenderQueue/BlenderSuite.RenderQueue.csproj
```

## Windows 打包

```bat
scripts\release\build-windows-aot-installer.bat
```

参数：

- `--no-open`：打包完成后不打开输出目录

安装包输出到 `install/Windows/output`。

## macOS 打包

```bash
./scripts/release/build-macos-aot-dmg.sh
./scripts/release/build-macos-dmg.sh
```

参数：

- `--no-open`：打包完成后不打开输出目录
- `--install`：安装生成的 macOS app bundle
- `osx-arm64` / `osx-x64`：指定 macOS Runtime Identifier

`.dmg` 文件输出到 `install/macOS/output`。

## 平台说明

- Windows 打包使用 Inno Setup。
- macOS 打包生成 `.dmg` 文件；签名与公证按需要单独处理。
- Linux 桌面打包流程暂未整理。

## 许可证文件

安装包内的中英文许可说明：

- [中文许可说明](../Install/license_zh.txt)
- [English License Notice](../Install/license_en.txt)
