# MC Tool Launcher (MCTL)

一款综合性功能的 Minecraft 启动器，使用 **WPF / .NET 10** 编写。集成正版登录、3D 皮肤预览、版本与实例管理、
模组 / 整合包 / 资源包下载、指令生成、开服工具等常用功能。

当前版本：**1.30**（`AssemblyName = MCTL`）

---

## 功能

### 账号与皮肤
- 微软正版登录（OAuth 设备码流程），自动读取正版皮肤与经典 / 纤细（Slim）形态，登录状态可自动续期
- 主页账号区显示 **3D 头部头像**，右侧显示可拖拽旋转、滚轮缩放的 **3D 小人**（行走动画）
- 3D 渲染基于原生 WPF `Viewport3D`，逐像素最近邻放大贴图，像素边缘清晰，不依赖 WebView2
- 默认皮肤内置离线贴图 + 磁盘缓存，启动首帧即为正确皮肤；默认皮肤 UUID 已混淆处理

### 版本与实例
- 多游戏目录管理、版本隔离、按版本筛选
- 原版 / Fabric / Forge / NeoForge 安装，NeoForge、Forge 官方 Maven 不通时自动切换镜像并重试
- Java 自动检测；缺少 Java 可一键下载安装 Temurin（带进度与取消）
- 崩溃日志查看

### 下载
- Modrinth + CurseForge 搜索与下载：模组、整合包、资源包、光影
- 模组文件名可选中文前缀，便于识别；自动检测并补齐前置模组
- 下载源镜像优先、失败自动换源；下载失败时明确提示缺失文件与官方下载页
- **整合包下载后直接解析安装为游戏列表里的一个客户端**（自动补齐所需原版 / 加载器），也支持拖入 `.mrpack` / `.zip` 导入与导出

### 指令生成
- `/give`、`/summon` 可视化生成，按游戏版本自动适配 NBT / 数据组件格式
- 物品与生物列表附带中文名，支持中文搜索
- 全部原版指令速查与生成

### 开服工具
- 服务端核心下载（原版 / Fabric / Forge / NeoForge / Paper 等）、插件下载
- `server.properties` 可视化配置、后台启动与服务器控制台

### 界面
- 白天 / 黑夜主题、玻璃面板（浓度可调）、背景图与高斯模糊
- 卡片 / 侧栏 / 标题栏 / 内容区不透明度、自定义主题色、彩虹标题、背景音乐播放

### 简洁版
- `tools/LiteBoot` 生成的轻量引导器：本机没有 .NET 10 桌面运行时时自动下载安装，再释放并启动主程序

---

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

### 发布简洁版（依赖本机 .NET 10）

```powershell
dotnet publish src\AuroraLauncher\AuroraLauncher.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish-lite

# 用 .NET Framework 的 csc 打包成带内置载荷的引导器
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ `
  /out:dist\MCTL-lite.exe /win32icon:src\AuroraLauncher\app.ico `
  /resource:publish-lite\MCTL.exe,MCTL.exe `
  /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll tools\LiteBoot\Program.cs
```

> 应用图标由 `tools/make-icon.ps1` 生成（透明底「MCT」像素字，多尺寸 PNG 帧 ICO）。

---

## 项目结构

```
src/AuroraLauncher/      启动器主工程
  Core/                  设置、日志、皮肤、下载（Mc / Mods / Net）、音乐等
  Controls/Skin3DView.cs 原生 WPF 3D 皮肤渲染控件
  Views/                 各页面与弹窗（XAML + code-behind）
tools/UiSmoke/           自测台：页面构造、3D 皮肤、图标、整合包导入等检查
tools/LiteBoot/          简洁版引导器（.NET Framework 4.8）
tools/HeadViewer/        独立 3D 头部小工具（WebView2）
tools/make-icon.ps1      应用图标生成脚本
```

---

## 自测

```powershell
dotnet run --project tools\UiSmoke\UiSmoke.csproj -c Release
```

常用子命令：`skin3d`（3D 皮肤 UV 与清晰度）、`skincache`（皮肤缓存一致性）、
`cmdnames`（指令中文名完整性）、`importpack <包> <游戏目录>`（整合包导入 / 导出）、
`renderpage home|settings|... <输出.png>`（页面离屏渲染）。

---

## 说明

- 本项目与 Mojang Studios、Microsoft 无关联；Minecraft 相关名称、皮肤与图标版权归其各自所有者。
- 许可证：[Apache License 2.0](LICENSE)
