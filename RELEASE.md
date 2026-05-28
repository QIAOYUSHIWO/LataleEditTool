# LDT Editor — 发布说明（构建与运行时）

版本号以 `LdtEditor/LdtEditor.csproj` 中的 `<Version>` 为准；打包目录名、ZIP 文件名建议包含同一版本号，与程序内 **帮助 → 关于** 一致。

## 分发形态与渠道（L4-1）

| 项目 | 结论 |
|------|------|
| 首发形态 | **ZIP 便携目录**（将整个 `publish` 输出目录打包）。默认 **`dotnet publish` 为单文件**：目录内通常为 **`LdtEditor.exe` + `README.md` + `LICENSE` 共 3 个文件**（无大量 DLL；`publish-release.ps1` 已按此配置）。 |
| 安装包 | **未**随仓库提供 MSIX / WiX；若需要可在 L6 另起任务。 |
| 下载渠道 | 由维护者在 **`README.md` →「获取与安装」** 填写固定下载入口（如 GitHub Releases、网盘）；避免仅口头传递路径。 |
| 构建入口 | 下文「手动命令」或根目录 **`publish-release.ps1`**。 |

## 代码签名（L4-2）

| 项目 | 首版（小圈） |
|------|----------------|
| Authenticode | **暂缓**：无证书时不对 `LdtEditor.exe` 做数字签名。 |
| 用户侧现象 | Windows SmartScreen 可能提示「未知发布者」；需「仍要运行」或通过已信任路径分发。 |
| 公开广撒 | 若改为对陌生用户大规模分发，建议补办代码签名以降低误报与信任成本。 |

## `.ldt` 文件关联（L4-3）

便携包**不经**安装程序写注册表时，由用户在应用内操作：**工具 → 将 .LDT 关联到本程序…**（写入当前用户 `HKCU\Software\Classes`，详见程序内提示）。与「安装包级关联」等价目标，仅缺机器级安装器。

## 运行时二选一

| 方式 | 说明 | 适用 |
|------|------|------|
| **Framework-dependent（FDD）** | 单文件 `LdtEditor.exe` 体积约 **1 MB 级**（仍依赖本机已装 **.NET Desktop Runtime**，与 `TargetFramework` 一致，当前为 `net10.0-windows`）。 | 已装对应 .NET 的编辑者、日常迭代。 |
| **Self-contained** | 单文件 exe 约 **50 MB 级**（含压缩与自解压运行时；首次启动可能略慢）。目标机**无需**单独安装 .NET。 | 小圈分发、无法保证已装运行时。 |

## 产物路径（手动命令）

在仓库根目录 `LataleEditorTools` 下执行（将 `X.Y.Z` 换成 `csproj` 里的 `<Version>`）。

**Framework-dependent（单文件，须显式声明非自包含；否则部分 SDK 下单文件会被推断为 self-contained）：**

```powershell
dotnet publish .\LdtEditor\LdtEditor.csproj -c Release -o ".\dist\LdtEditor-X.Y.Z-fdd" `
  /p:PublishSelfContained=false /p:SelfContained=false `
  /p:EnableCompressionInSingleFile=false `
  /p:DebugType=none /p:DebugSymbols=false
```

（`LdtEditor.csproj` 已含 `PublishSingleFile`；**压缩**仅适用于 self-contained 单文件，FDD 单文件须关闭 `EnableCompressionInSingleFile`。）

**Self-contained（win-x64，单文件 + 压缩）：**

```powershell
dotnet publish .\LdtEditor\LdtEditor.csproj -c Release -r win-x64 --self-contained true -o ".\dist\LdtEditor-X.Y.Z-self-contained-win-x64" `
  /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:DebugType=none /p:DebugSymbols=false
```

默认入口程序为 `LdtEditor.exe`（在 `-o` 指定目录内）。`README.md` 与 **`LICENSE`** 会随项目设置复制到输出目录；**帮助 → 使用说明** 读取 `README.md`；分发 ZIP 时请保留同目录下上述文件。

分发时将**整个输出目录**打 ZIP 即可，例如：

- `LdtEditor-1.0.0-fdd.zip`
- `LdtEditor-1.0.0-self-contained-win-x64.zip`

## 脚本（可选）

```powershell
# 默认：仅 self-contained win-x64 单文件（推荐分发）
.\publish-release.ps1

# 仅 framework-dependent 单文件（需目标机已装对应 .NET Desktop Runtime）
.\publish-release.ps1 -FrameworkDependent
# 同上，简写：
.\publish-release.ps1 -Fdd

# 两种形态各打一份
.\publish-release.ps1 -All
```

脚本会读取 `csproj` 中的 `<Version>`，并把 `publish` 输出写到 `dist\` 下带版本号的子目录（本地生成物，可自行加入 `.gitignore`）。**发布前会删除对应输出目录**，避免旧版「多文件 publish」残留在目录中与单文件结果混在一起。

若**手工**执行 `dotnet publish`，建议在换用单文件参数后**先清空 `-o` 目录**，否则可能看到多余 DLL。

## 变更记录

见同目录 `CHANGELOG.md`。

## 首发前验证（L2）

质量与签收清单、命令行 `ldt-roundtrip` 用法见 **`L2_VERIFICATION.md`**。

## 文档分工（维护者）

| 文件 | 说明 |
|------|------|
| **`README.md`** | **仅面向最终用户**（随 ZIP 分发）：使用说明、**著作权署名**、许可摘要、免责声明、已知问题等；**不含**构建步骤、发版路线、开发进度类说明。 |
| **`RELEASE.md`** | 构建、打包、运行时与分发形态（本文）。 |
| **`L2_VERIFICATION.md`** | 发版前质量核对与 `ldt-roundtrip` 命令行回归。 |
| **`CHANGELOG.md`** | 版本变更记录（可与发版说明对照）。 |
| **`LdtEditor_memory.txt` / `LdtEditor_launch_memory.txt`** | 开发与上线准备路线（仓库内；默认不要求打进用户 ZIP）。 |
