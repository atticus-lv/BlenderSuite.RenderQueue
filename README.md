# Blender Render Queue

Blender Render Queue 是一个面向 Blender 工作流的队列渲染工具。当前仓库主线聚焦桌面应用、测试，以及对应平台的发布与安装包脚本。

## 当前状态

### 已完成

- 队列渲染
- 场景/帧范围覆写
- 暂停/恢复渲染
- 视频合成
- 中英文界面切换
- 深色/浅色主题切换
- Windows 安装包构建
- macOS `.dmg` 打包

### 进行中

- Linux 桌面支持
- macOS 签名与公证流程
- CI/CD 多平台发布
- 官网与授权服务

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 安装包打包额外需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)
- macOS `.dmg` 打包需要系统自带的 `hdiutil`、`sips`、`iconutil`

## 本地开发

```bash
git clone <repository-url>
cd BlenderSuite.RenderQueue
```

调试构建：

```bash
dotnet build BlenderSuite.RenderQueue.sln
```

测试：

```bash
dotnet test BlenderSuite.RenderQueue.sln
```

## 统一发布脚本

所有实际发布逻辑统一放在 `scripts/release/` 下。

### Windows

构建 Native AOT 安装包：

```bat
scripts\release\build-windows-aot-installer.bat
```

可选参数：

```bat
scripts\release\build-windows-aot-installer.bat --no-open
```

产物目录：

- 发布目录：`Install/Windows/publish/aot/win-x64`
- 安装包目录：`Install/Windows/output`

### macOS

构建 Native AOT `.dmg`：

```bash
chmod +x scripts/release/build-macos-aot-dmg.sh
./scripts/release/build-macos-aot-dmg.sh
```

构建非 AOT `.dmg`：

```bash
chmod +x scripts/release/build-macos-dmg.sh
./scripts/release/build-macos-dmg.sh
```

常用参数：

```bash
./scripts/release/build-macos-aot-dmg.sh osx-arm64
./scripts/release/build-macos-aot-dmg.sh osx-x64
./scripts/release/build-macos-aot-dmg.sh --install
./scripts/release/build-macos-aot-dmg.sh --no-open
./scripts/release/build-macos-dmg.sh osx-arm64
./scripts/release/build-macos-dmg.sh osx-x64
./scripts/release/build-macos-dmg.sh --install
./scripts/release/build-macos-dmg.sh --no-open
```

产物目录：

- 发布目录：`Install/macOS/publish/{aot|non-aot}/<RID>`
- 符号目录：`Install/macOS/symbols/{aot|non-aot}/<RID>`
- 安装包目录：`Install/macOS/output`

## 直接调用底层脚本

如果你需要在 CI 或自定义流程中直接调用脚本，请使用：

- `scripts/release/build-windows-aot-installer.bat`
- `scripts/release/build-macos-dmg.sh`
- `scripts/release/build-macos-aot-dmg.sh`
- `scripts/release/install-macos-app.sh`

其中：

- `build-macos-dmg.sh` 现在支持 `--aot` 参数
- `build-macos-aot-dmg.sh` 是对 `build-macos-dmg.sh --aot` 的兼容包装

## 手动发布命令

### Windows Native AOT

```bash
dotnet publish BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o Install/Windows/publish/aot/win-x64
```

### macOS Native AOT

```bash
dotnet publish BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true -o Install/macOS/publish/aot/osx-arm64
```

### macOS 非 AOT

```bash
dotnet publish BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=false -o Install/macOS/publish/non-aot/osx-arm64
```

### Windows Inno Setup

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyPublishDir="%CD%\Install\Windows\publish\aot\win-x64" /DMyOutputDir="%CD%\Install\Windows\output" .\Install\Windows\setup.iss
```

## 平台支持

- Windows：完整支持，包含 Inno Setup 安装程序
- macOS：完整支持 `.dmg` 打包，签名与公证待补充
- Linux：待支持

## 归档实验

`Client/` 下保留了一组历史远程监控客户端实验代码，包括 Desktop、Browser、Android 三个目标。它们当前不属于主线产品范围，后续远程查看能力会优先迁移到统一的 Web Dashboard/官网体系。

如需继续研究这部分代码，请先阅读：

- [QueueClient 归档说明](docs/queue-client-archive.md)

## 许可证

本项目采用自定义许可协议，详见：

- [中文许可协议](Install/license_zh.txt)
- [English License Agreement](Install/license_en.txt)
