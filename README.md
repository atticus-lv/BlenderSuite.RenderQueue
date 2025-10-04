# Blender Render Queue

一个用于Blender的队列渲染工具，目前支持Windows平台，计划支持多语言界面和跨平台部署。

## 功能特性

### 已实现功能 ✅

- 🎬 **队列渲染** 
  - 拖拽排序，启用/禁用
  - 状态提示
  - 场景/帧范围覆写
  - 暂停渲染/恢复渲染
  - 可选blender
  - 合成视频

- 📦 **Windows安装程序** - 使用Inno Setup创建专业的Windows安装包
- ⚡ **AOT编译** - 支持Ahead-of-Time编译，获得更小的文件体积和更快的启动速度
- 🔧 **现代化UI** - 基于Avalonia UI框架的现代化界面
- 🌍 **多语言支持** - 应用程序界面支持中文和英文切换
- 深色/浅色主题切换

### 计划中功能 🚧

- 🖥️ **跨平台支持** - 支持Windows、macOS和Linux平台

## 开发环境配置

### 1. 安装必要工具

1. **下载并安装 [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)**
2. (Windows) **下载并安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)**

### 2. 克隆项目

```bash
git clone <repository-url>
cd BlenderSuite.RenderQueue
```

### 3. 构建项目

#### 调试构建

```bash
dotnet build
```

#### 发布构建

项目提供了便捷的构建脚本来创建完整的安装程序：

```bash
# 进入项目目录
cd BlenderRenderQueue

# 运行AOT构建和安装程序创建脚本
make_aot_installer.bat
```

这个脚本会自动完成以下步骤：

1. 使用AOT编译发布应用程序
2. 创建Windows安装程序
3. 打开输出文件夹

#### 手动构建选项

**AOT发布 (推荐，最小体积)**

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true -o Install/Publish
```

**标准发布**

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o Install/Publish
```

**单文件发布**

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o Install/Publish
```

**创建安装程序（Windows）**

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ".\Install\Windows\setup.iss"
```

## 安装程序特性

### 多语言安装界面 ✅

- **英语** - 默认语言
- **简体中文** - 完整的中文安装界面支持

### 许可协议 ✅

- 根据用户选择的语言显示对应的许可协议
- 中文许可协议：`Install/license_zh.txt`
- 英文许可协议：`Install/license_en.txt`

### 安装选项 ✅

- 自动检测并卸载旧版本
- 创建桌面快捷方式（可选）
- 创建开始菜单快捷方式
- 管理员权限安装

### 平台支持

- **Windows** ✅ - 完整支持，包含安装程序
- **macOS** 🚧 - 长期计划支持，将提供.dmg安装包
- **Linux** 🚧 - 待定

## 开发说明

### 技术栈

- **.NET 9.0** - 应用程序框架
- **Avalonia UI** - 跨平台UI框架
- **Inno Setup** - Windows安装程序制作工具

### 构建要求

#### 当前支持 (Windows)

- Windows 10/11 (64位)
- .NET 9.0 SDK
- Inno Setup 6

#### 计划支持 (跨平台)

- **macOS**: macOS 10.15+ (Catalina)
- **Linux**: 待定
- **通用要求**: .NET 9.0 SDK, 对应平台的构建工具

## 开发路线图

### 跨平台支持计划 🚧

- [ ] macOS 支持
    - [ ] .dmg 安装包制作
    - [ ] macOS 特定功能适配
- [ ] Linux 支持
- [ ] CI/CD 多平台构建

### 技术改进计划 🚧

- [ ] 插件系统架构

## 许可证

本项目采用自定义许可协议，详见：

- [中文许可协议](BlenderRenderQueue/Install/license_zh.txt)
- [English License Agreement](BlenderRenderQueue/Install/license_en.txt)

