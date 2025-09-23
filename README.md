# Blender Render Queue

一个用于Blender的队列渲染的工具

## 开发环境配置

### 1. 安装必要工具

1. 下载并安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. 下载并安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)
3.
下载 [Inno Setup 简体中文语言包](https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/Unofficial/ChineseSimplified.isl)
    - （Windows）将下载的 `ChineseSimplified.isl` 复制到 `C:\Program Files (x86)\Inno Setup 6\Languages` 目录

### 2. 克隆项目

```bash
git clone ...
cd BlenderSuite.RenderQueue
```

### 4. 构建项目

#### 调试构建

```bash
dotnet build
```

#### 发布构建

运行以下脚本来创建安装程序：

```bash
cd src/BlenderDisplaceSuite
step1_publish_release.bat
```

### 单文件发布

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### AOT 发布 (最小体积，但编译时间长)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
```
