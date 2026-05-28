# SPF 特效文件集成开发计划

## 📋 概述

将 LaTale 游戏的 SPF 特效文件（FX Model Binary 格式）集成到 LDT Editor 工具中，使其能够像加载图标一样加载和预览特效纹理。

---

## 🎯 目标

1. **支持 FX Model Binary 格式的 SPF 文件**（如 HOSHIM.SPF、CRI.SPF）
2. **在 LDT Editor 中预览特效纹理**
3. **关联 ITEM_2.LDT 中的 EffectID 到特效纹理**

---

## 📊 现状分析

### 已支持格式

| 格式 | 示例 | 状态 |
|------|------|------|
| PNG Chain | BANX.SPF | ✅ 已支持 |
| FX Model Binary | HOSHIM.SPF | ❌ 未支持 |

### 两种 SPF 格式对比

```
BANX.SPF (PNG Chain):
┌─────────────────────────────────┐
│ PNG #1 │ PNG #2 │ PNG #3 │ ... │
└─────────────────────────────────┘
直接以 PNG 签名开始

HOSHIM.SPF (FX Model Binary):
┌─────────────────────────────────────────────────────────┐
│ Header (2536 bytes) │ PNG #1 │ PNG #2 │ PNG #3 │ ... │
└─────────────────────────────────────────────────────────┘
包含自定义头部 + PNG 纹理链
```

### FX Model Binary 头部结构

```
偏移量    大小    说明
0x000    80B    文件标识: "FX Model Binary File Ver : [4.000000]"
0x080    128B   创建日期: "Create Date : [Mar 30 2021] [08:59:45]"
0x100    4B     可能是物品数量或ID (如 415)
0x110+   ...    元数据区域（浮点数、整数等）
0x9E8    -      第一个 PNG 开始
```

---

## 🔧 技术方案

### 方案 A：扩展现有 SpfPngArchive 类（推荐）

**优点**：
- 最小化代码修改
- 复用现有 PNG 扫描逻辑
- 保持向后兼容

**实现步骤**：

#### 1. 添加 FX Model 头部检测

```csharp
// SpfPngArchive.cs
private static ReadOnlySpan<byte> FxModelHeader => "FX Model Binary File"u8;

/// <summary>
/// 检测是否为 FX Model Binary 格式
/// </summary>
public static bool IsFxModelFormat(string path)
{
    Span<byte> header = stackalloc byte[256];
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (fs.Read(header) < 256) return false;
    return header.IndexOf(FxModelHeader) >= 0;
}

/// <summary>
/// 查找第一个 PNG 的偏移量
/// </summary>
public static long FindFirstPngOffset(string path)
{
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var pos = 0L;
    Span<byte> sig = stackalloc byte[8];
    
    while (pos <= fs.Length - 8)
    {
        fs.Position = pos;
        if (fs.Read(sig) != 8) break;
        if (sig.SequenceEqual(PngSig)) return pos;
        pos++;
    }
    return -1;
}
```

#### 2. 修改 ScanPngChain 方法

```csharp
/// <summary>
/// 自动检测格式并扫描 PNG 链
/// </summary>
public static List<SpfPngEntry> ScanPngChainAuto(string path, IProgress<string>? status = null)
{
    long startOffset = 0;
    
    // 检测是否为 FX Model 格式
    if (IsFxModelFormat(path))
    {
        startOffset = FindFirstPngOffset(path);
        if (startOffset < 0) return [];
        status?.Report($"检测到 FX Model 格式，PNG 起始偏移: 0x{startOffset:X}");
    }
    
    return ScanPngChain(path, startOffset, status);
}
```

#### 3. 添加特效纹理索引

```csharp
/// <summary>
/// 从 FX Model 头部提取元数据
/// </summary>
public static FxModelMetadata ParseFxModelHeader(string path)
{
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var header = new byte[2536];
    fs.Read(header, 0, header.Length);
    
    return new FxModelMetadata
    {
        Version = "4.000000",
        CreateDate = Encoding.ASCII.GetString(header, 0x40, 40).TrimEnd('\0'),
        ItemId = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x100)),
        // ... 其他元数据
    };
}

public record FxModelMetadata
{
    public string Version { get; init; } = "";
    public string CreateDate { get; init; } = "";
    public int ItemId { get; init; }
    // ... 其他字段
}
```

### 方案 B：创建新的 SpfFxArchive 类

**优点**：
- 职责分离
- 可以添加特效特有的功能

**缺点**：
- 代码重复
- 需要修改更多调用点

---

## 📝 实现计划

### Phase 1：基础支持（1-2天）

| 任务 | 说明 | 验证条件 |
|------|------|----------|
| 1.1 | 添加 FX Model 头部检测方法 | `IsFxModelFormat("HOSHIM.SPF") == true` |
| 1.2 | 添加 PNG 偏移量查找方法 | `FindFirstPngOffset("HOSHIM.SPF") == 0x9E8` |
| 1.3 | 修改 ScanPngChain 支持自动检测 | 能正确扫描 HOSHIM.SPF 中的 PNG |
| 1.4 | 单元测试 | 所有测试通过 |

### Phase 2：UI 集成（2-3天）

| 任务 | 说明 | 验证条件 |
|------|------|----------|
| 2.1 | 添加 SPF 文件类型选择 | 可以选择 BANX.SPF 或 HOSHIM.SPF |
| 2.2 | 修改图标选择器支持特效纹理 | 可以预览 HOSHIM.SPF 中的纹理 |
| 2.3 | 添加特效纹理搜索功能 | 可以按尺寸/名称筛选纹理 |
| 2.4 | 优化加载性能 | 大文件加载 < 5秒 |

### Phase 3：高级功能（3-5天）

| 任务 | 说明 | 验证条件 |
|------|------|----------|
| 3.1 | 解析 FX Model 头部元数据 | 显示 ItemId、CreateDate 等信息 |
| 3.2 | 关联 EffectID 到纹理 | 从 ITEM_2.LDT 的 EffectID 查找对应纹理 |
| 3.3 | 特效预览窗口 | 可以预览特效动画帧 |
| 3.4 | 导出功能 | 可以导出单个或批量 PNG |

---

## 🔍 关键代码修改点

### 1. SpfPngArchive.cs

```csharp
// 添加以下方法:
+ IsFxModelFormat(string path)
+ FindFirstPngOffset(string path)
+ ScanPngChainAuto(string path, IProgress<string>? status)
+ ParseFxModelHeader(string path)
```

### 2. MainForm.cs (Program.cs)

```csharp
// 修改图标选择器初始化
- SpfPngArchive.ScanPngChain(spfPath)
+ SpfPngArchive.ScanPngChainAuto(spfPath)

// 添加特效纹理选择
+ private void ShowFxTexturePicker(string spfPath)
```

### 3. Settings UI

```csharp
// 添加 SPF 文件类型选项
+ ComboBox: SPF 文件类型 (PNG Chain / FX Model)
+ TextBox: 特效 SPF 路径 (如 HOSHIM.SPF)
```

---

## ⚠️ 风险与限制

| 风险 | 说明 | 解决方案 |
|------|------|----------|
| 大文件性能 | HOSHIM.SPF 为 633MB | 延迟加载 + 索引缓存 |
| 头部结构未知 | FX Model 头部格式不完全清楚 | 逆向分析 + 测试验证 |
| 内存占用 | 大量 PNG 索引 | 分页加载 + 虚拟列表 |
| 兼容性 | 需要保持 BANX.SPF 兼容 | 自动检测格式 |

---

## 🧪 测试用例

### 单元测试

```csharp
[Test]
public void TestFxModelDetection()
{
    Assert.IsTrue(SpfPngArchive.IsFxModelFormat("HOSHIM.SPF"));
    Assert.IsFalse(SpfPngArchive.IsFxModelFormat("BANX.SPF"));
}

[Test]
public void TestFindFirstPngOffset()
{
    var offset = SpfPngArchive.FindFirstPngOffset("HOSHIM.SPF");
    Assert.AreEqual(0x9E8, offset);
}

[Test]
public void TestScanPngChainAuto()
{
    var pngs = SpfPngArchive.ScanPngChainAuto("HOSHIM.SPF");
    Assert.IsTrue(pngs.Count > 1000);
}
```

### 集成测试

1. 打开 LDT Editor
2. 配置 HOSHIM.SPF 路径
3. 打开图标选择器
4. 验证可以浏览特效纹理
5. 选择一个纹理并应用

---

## 📚 参考资料

| 文件 | 说明 |
|------|------|
| `SpfPngArchive.cs` | 现有 SPF 解析代码 |
| `ItemPreviewFloatForm.cs` | 图标预览实现 |
| `HOSHIM.SPF` | FX Model 格式样本 |
| `BANX.SPF` | PNG Chain 格式样本 |

---

## 💡 总结

**可行性**：✅ **完全可行**

**核心原理**：
- FX Model Binary 格式 = 自定义头部 + PNG 纹理链
- 只需跳过头部，复用现有 PNG 扫描逻辑
- `ScanPngChain` 已支持 `startOffset` 参数

**工作量**：
- Phase 1（基础支持）：1-2 天
- Phase 2（UI 集成）：2-3 天
- Phase 3（高级功能）：3-5 天
- **总计**：6-10 天

**价值**：
- 可以直接在 LDT Editor 中预览特效纹理
- 可以关联 EffectID 到具体纹理
- 提升游戏资源编辑效率

---

*文档生成时间: 2026-05-28*
