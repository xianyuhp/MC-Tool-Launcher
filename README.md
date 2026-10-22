# MC Tool Launcher (MCTL)

一款综合性功能的 Minecraft 启动器，使用 **WPF / .NET 10** 编写。
当前版本：**1.31**（`AssemblyName = MCTL`）

---

## 开源版本：请自备 API 凭据

公开仓库**不内置**任何官方 API 凭据，自己构建得到的是「开源版本」，启动时会弹窗提示填写：

| 功能 | 需要填写 | 申请地址 |
| --- | --- | --- |
| 微软正版登录 | 应用注册的 Application (client) ID（账户类型选「个人 Microsoft 账户」，并允许公共客户端流） | <https://portal.azure.com> → 应用注册 |
| CurseForge 官方接口 | CurseForge API Key（不填时只走 MCIM 社区镜像，仍可搜索下载） | <https://console.curseforge.com/> |

填写方式（任选一种）：

1. 启动后按提示在「开源版本 · API 凭据」窗口里填写，点「保存并应用」（立即生效）；
2. 设置 → 游戏与 Java → 点「配置微软 / CurseForge 凭据（开源版本）」；
3. 直接编辑 `config/api-keys.json`（首次打开窗口会自动生成模板）：

```json
{
  "MicrosoftClientId": "你的 Application (client) ID",
  "CurseForgeApiKey": "你的 CurseForge API Key"
}
```

4. 或使用环境变量 `MCTL_MS_CLIENT_ID`、`MCTL_CURSEFORGE_KEY`。

> `config/api-keys.json` 已在 `.gitignore` 中忽略，填写后不会被提交到仓库。
> 未配置微软客户端 ID 时，正版登录会给出明确提示；未配置 CurseForge Key 时只使用社区镜像。

---

## 功能

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
