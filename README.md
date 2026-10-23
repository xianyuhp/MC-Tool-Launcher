# MC Tool Launcher (MCTL)

一款综合性功能的 Minecraft 启动器，使用 **WPF / .NET 10** 编写。
当前版本：**1.33.1**（`AssemblyName = MCTL`）

## 构建

环境要求：Windows 10/11、[.NET SDK 10](https://dotnet.microsoft.com/download)

```powershell
# 编译
dotnet build src\AuroraLauncher\AuroraLauncher.csproj -c Release

# 直接运行
dotnet run --project src\AuroraLauncher\AuroraLauncher.csproj
```

### 发布单文件（自包含）

```powershell
dotnet publish src\AuroraLauncher\AuroraLauncher.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
---

## 说明

- 本项目与 Mojang Studios、Microsoft 无关联；Minecraft 相关名称、皮肤与图标版权归其各自所有者。
- 许可证：[Apache License 2.0](LICENSE)
