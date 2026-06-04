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

## 目录约定

- `src/`：主线产品源码
- `tests/`：测试项目
- `scripts/`：构建与发布脚本
- `docs/`：项目文档
- `install/`：安装包与打包相关资源

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

- 发布目录：`install/Windows/publish/aot/win-x64`
- 安装包目录：`install/Windows/output`

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

- 发布目录：`install/macOS/publish/{aot|non-aot}/<RID>`
- 符号目录：`install/macOS/symbols/{aot|non-aot}/<RID>`
- 安装包目录：`install/macOS/output`

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
dotnet publish src/BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o install/Windows/publish/aot/win-x64
```

### macOS Native AOT

```bash
dotnet publish src/BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=true -o install/macOS/publish/aot/osx-arm64
```

### macOS 非 AOT

```bash
dotnet publish src/BlenderRenderQueue/BlenderRenderQueue.csproj -c Release -r osx-arm64 --self-contained true -p:PublishAot=false -o install/macOS/publish/non-aot/osx-arm64
```

### Windows Inno Setup

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyPublishDir="%CD%\install\Windows\publish\aot\win-x64" /DMyOutputDir="%CD%\install\Windows\output" .\install\Windows\setup.iss
```

## 平台支持

- Windows：完整支持，包含 Inno Setup 安装程序
- macOS：完整支持 `.dmg` 打包，签名与公证待补充
- Linux：待支持

## 许可证

本项目采用双授权模式：

- 公共版本采用 [GNU Affero General Public License v3.0 only](LICENSE)（`AGPL-3.0-only`）
- 闭源分发、专有产品集成、OEM、SaaS/托管服务或其他不兼容 AGPLv3 的商业用途，需要单独商业授权，详见 [COMMERCIAL_LICENSE.md](COMMERCIAL_LICENSE.md)
- 提交贡献需同意 [CONTRIBUTOR_LICENSE_AGREEMENT.md](CONTRIBUTOR_LICENSE_AGREEMENT.md)

安装包内的中英文许可说明：

- [中文许可说明](Install/license_zh.txt)
- [English License Notice](Install/license_en.txt)
