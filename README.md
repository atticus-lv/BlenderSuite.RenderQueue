### 标准自包含发布 (推荐)

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

### 单文件发布

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### AOT 发布 (最小体积，但编译时间长)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
```
