using System.Diagnostics.CodeAnalysis;
using System.Drawing;

/// <summary>
/// 在多个选图窗或同一 SPF 内切换图集时复用已解码的 <see cref="Bitmap"/>，避免重复读盘与 PNG 解码；按 SPF 修改时间与逻辑文件名区分版本。
/// </summary>
internal static class IconAtlasBitmapCache
{
    private sealed class Entry
    {
        public required Bitmap Bitmap;
        public required long SpfWriteTicksUtc;
        /// <summary>源图格边长（像素）；与缩略解码后的位图一致。0 表示旧缓存，调用方回退为 <see cref="LaTaleIconSheet.AtlasCellPixels"/>。</summary>
        public int AtlasCellStridePixels;
        public int RefCount;
        /// <summary>仅当 <see cref="RefCount"/> 为 0 时有效：进入空闲池的时刻，用于在淘汰时选最久未用的条目。</summary>
        public long IdleSinceTick;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    private static int s_maxEntries = 12;

    /// <summary>最多保留的图集条数；仅淘汰 <see cref="RefCount"/> 为 0 的条目。</summary>
    public static int MaxEntries => s_maxEntries;

    public static void ConfigureMaxEntries(int maxEntries)
    {
        var n = Math.Clamp(maxEntries, 1, 96);
        lock (Gate)
        {
            s_maxEntries = n;
            while (Entries.Count > s_maxEntries)
            {
                if (!EvictOneUnused())
                {
                    break;
                }
            }
        }
    }

    private static string MakeKey(string spfPath, string logicalUpper, long writeTicksUtc) =>
        $"{writeTicksUtc:X16}:{spfPath.Trim().ToUpperInvariant()}\u001F{logicalUpper}";

    public static void Clear()
    {
        lock (Gate)
        {
            foreach (var e in Entries.Values)
            {
                e.Bitmap.Dispose();
            }

            Entries.Clear();
        }
    }

    /// <summary>
    /// 若缓存命中则增加引用计数；调用方必须在不再显示该图时调用 <see cref="Release"/> 传入同一 <paramref name="leaseKey"/>。
    /// </summary>
    public static bool TryAddRef(
        string spfPath,
        string logicalUpper,
        long spfWriteTicksUtc,
        [NotNullWhen(true)] out Bitmap? bitmap,
        [NotNullWhen(true)] out string? leaseKey,
        out int atlasCellStridePixels)
    {
        atlasCellStridePixels = 0;
        leaseKey = MakeKey(spfPath, logicalUpper, spfWriteTicksUtc);
        lock (Gate)
        {
            if (!Entries.TryGetValue(leaseKey, out var ent) || ent.SpfWriteTicksUtc != spfWriteTicksUtc)
            {
                bitmap = null;
                leaseKey = null;
                return false;
            }

            ent.RefCount++;
            bitmap = ent.Bitmap;
            atlasCellStridePixels = ent.AtlasCellStridePixels;
            return true;
        }
    }

    /// <summary>
    /// 将新解码的位图放入缓存（引用计数 1）。若同名键已存在则复用旧图并释放传入的 <paramref name="bitmap"/>，
    /// <paramref name="result"/> 为实际使用的实例。
    /// 若缓存已满且无法淘汰空闲项，返回 false，<paramref name="result"/> 仍为传入实例，由调用方自行 Dispose。
    /// </summary>
    public static bool TryInsert(
        string spfPath,
        string logicalUpper,
        long spfWriteTicksUtc,
        Bitmap bitmap,
        int atlasCellStridePixels,
        [NotNullWhen(true)] out Bitmap? result,
        [NotNullWhen(true)] out string? leaseKey)
    {
        var key = MakeKey(spfPath, logicalUpper, spfWriteTicksUtc);
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                bitmap.Dispose();
                result = existing.Bitmap;
                leaseKey = key;
                return true;
            }

            while (Entries.Count >= s_maxEntries)
            {
                if (!EvictOneUnused())
                {
                    result = bitmap;
                    leaseKey = null;
                    return false;
                }
            }

            Entries[key] = new Entry
            {
                Bitmap = bitmap,
                SpfWriteTicksUtc = spfWriteTicksUtc,
                AtlasCellStridePixels = atlasCellStridePixels,
                RefCount = 1,
                IdleSinceTick = 0
            };
            result = bitmap;
            leaseKey = key;
            return true;
        }
    }

    public static void Release(string? leaseKey)
    {
        if (string.IsNullOrEmpty(leaseKey))
        {
            return;
        }

        lock (Gate)
        {
            if (!Entries.TryGetValue(leaseKey, out var ent))
            {
                return;
            }

            ent.RefCount--;
            if (ent.RefCount <= 0)
            {
                ent.RefCount = 0;
                ent.IdleSinceTick = Environment.TickCount64;
            }
        }
    }

    private static bool EvictOneUnused()
    {
        string? victimKey = null;
        var bestIdle = long.MaxValue;
        foreach (var kv in Entries)
        {
            if (kv.Value.RefCount != 0)
            {
                continue;
            }

            if (kv.Value.IdleSinceTick <= bestIdle)
            {
                bestIdle = kv.Value.IdleSinceTick;
                victimKey = kv.Key;
            }
        }

        if (victimKey is null)
        {
            return false;
        }

        Entries[victimKey].Bitmap.Dispose();
        Entries.Remove(victimKey);
        return true;
    }
}
