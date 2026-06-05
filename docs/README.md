# Blender Suite: Render Queue Developer Docs

中文版本见 [README.zh-CN.md](README.zh-CN.md)。

## Environment

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) for Windows installer packaging
- macOS system tools `hdiutil`, `sips`, and `iconutil` for `.dmg` packaging

## Local Development

```bash
git clone <repository-url>
cd BlenderSuite.RenderQueue
```

Build the desktop app and test project:

```bash
dotnet build BlenderSuite.RenderQueue.sln
```

Run tests:

```bash
dotnet test BlenderSuite.RenderQueue.sln
```

Run the desktop app:

```bash
dotnet run --project src/BlenderSuite.RenderQueue/BlenderSuite.RenderQueue.csproj
```

## Windows Packaging

```bat
scripts\release\build-windows-aot-installer.bat
```

Options:

- `--no-open`: do not open the output folder after packaging

Generated installers are written to `install/Windows/output`.

## macOS Packaging

```bash
./scripts/release/build-macos-aot-dmg.sh
./scripts/release/build-macos-dmg.sh
```

Options:

- `--no-open`: do not open the output folder after packaging
- `--install`: install the generated macOS app bundle
- `osx-arm64` / `osx-x64`: choose a macOS runtime identifier

Generated `.dmg` files are written to `install/macOS/output`.

## Platform Notes

- Windows packaging uses Inno Setup.
- macOS packaging creates `.dmg` files. Signing and notarization are handled separately when needed.
- Linux desktop packaging is not documented yet.

## License Files

Installer license notices:

- [中文许可说明](../Install/license_zh.txt)
- [English License Notice](../Install/license_en.txt)
