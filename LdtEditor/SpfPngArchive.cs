using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;

internal readonly record struct SpfPngEntry(long FileOffset, int ByteLength, string? NameHint);

internal static class SpfPngArchive
{
    private static readonly byte[] PngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Embedded <c>ITEMETC_ICON**</c> names live in early PNG chunks; cap avoids huge buffers per atlas.</summary>
    private static volatile int s_maxNameScanBytes = 512 * 1024;

    public static void ConfigureNameScanMaxBytes(int maxBytes) =>
        s_maxNameScanBytes = Math.Clamp(maxBytes, 16 * 1024, 8 * 1024 * 1024);

    private static ReadOnlySpan<byte> ItemEtcIconPrefix => "ITEMETC_ICON"u8;
    private static ReadOnlySpan<byte> ItemEtcIconSuffix => ".PNG"u8;

    /// <summary>
    /// Walks the file from <paramref name="startOffset"/> as back-to-back PNGs (BANX.SPF layout).
    /// </summary>
    public static List<SpfPngEntry> ScanPngChain(string path, long startOffset = 0, IProgress<string>? status = null)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        if (fs.Length < 24)
        {
            return [];
        }

        var list = new List<SpfPngEntry>(8192);
        var pos = startOffset;
        var index = 0;
        Span<byte> lenBuf = stackalloc byte[4];
        Span<byte> typBuf = stackalloc byte[4];
        Span<byte> sig = stackalloc byte[8];
        while (pos <= fs.Length - 8)
        {
            fs.Position = pos;
            if (fs.Read(sig) != 8)
            {
                break;
            }

            if (!sig.SequenceEqual(PngSig))
            {
                if (list.Count == 0)
                {
                    pos++;
                    continue;
                }

                break;
            }

            var pngStart = pos;
            var cursor = pngStart + 8;
            var ended = false;
            while (cursor <= fs.Length - 12)
            {
                fs.Position = cursor;
                if (fs.Read(lenBuf) != 4)
                {
                    goto abortChain;
                }

                var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
                if (fs.Read(typBuf) != 4)
                {
                    goto abortChain;
                }

                var type = Encoding.ASCII.GetString(typBuf);
                if (chunkLen > fs.Length - cursor - 8)
                {
                    goto abortChain;
                }

                cursor += 4 + 4 + (long)chunkLen + 4;
                if (string.Equals(type, "IEND", StringComparison.Ordinal))
                {
                    ended = true;
                    break;
                }
            }

            if (!ended)
            {
                break;
            }

            var pngLen = checked((int)(cursor - pngStart));
            if (pngLen < 24)
            {
                break;
            }

            list.Add(new SpfPngEntry(pngStart, pngLen, null));
            pos = cursor;
            index++;
            if ((index & 0x3FF) == 0)
            {
                status?.Report($"已扫描 PNG：{index} …");
            }
        }

    abortChain:
        status?.Report($"PNG 条目：{list.Count}");
        return list;
    }

    public static Dictionary<string, SpfPngEntry> BuildLogicalNameIndex(
        string path,
        IReadOnlyList<SpfPngEntry> entries,
        IProgress<string>? status = null)
    {
        var map = new Dictionary<string, SpfPngEntry>(StringComparer.OrdinalIgnoreCase);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var pool = ArrayPool<byte>.Shared;
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.ByteLength <= 0)
            {
                continue;
            }

            var scanLen = Math.Min(e.ByteLength, s_maxNameScanBytes);
            var buf = pool.Rent(scanLen);
            try
            {
                fs.Position = e.FileOffset;
                if (fs.Read(buf, 0, scanLen) != scanLen)
                {
                    continue;
                }

                var span = buf.AsSpan(0, scanLen);
                if (TryFindItemEtcLogicalName(span, out var logicalUpper)
                    || TryFindEmbeddedIconAtlasLogicalName(span, out logicalUpper)
                    || TryFindItemEtcIconChinaLogicalName(span, out logicalUpper)
                    || TryFind542042520Literal(span, out logicalUpper)
                    || TryFindLogicalNameFromPngChunks(span, out logicalUpper))
                {
                    map.TryAdd(logicalUpper, e);
                }
            }
            finally
            {
                pool.Return(buf, clearArray: false);
            }

            if ((i & 0x1FF) == 0)
            {
                status?.Report($"解析名称：{i + 1}/{entries.Count}");
            }
        }

        return map;
    }

    private static ReadOnlySpan<byte> Literal542042520Png => "542042520.PNG"u8;

    /// <summary>Walks PNG chunks (tEXt / zTXt / iTXt) when a raw sub-byte scan misses embedded paths.</summary>
    private static bool TryFindLogicalNameFromPngChunks(ReadOnlySpan<byte> pngBytes, out string logicalUpper)
    {
        logicalUpper = "";
        if (pngBytes.Length < 24 || !pngBytes.StartsWith(PngSig))
        {
            return false;
        }

        var pos = 8;
        while (pos + 12 <= pngBytes.Length)
        {
            var chunkLen = (int)BinaryPrimitives.ReadUInt32BigEndian(pngBytes.Slice(pos, 4));
            if (chunkLen < 0 || pos + 12L + chunkLen > pngBytes.Length)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(pngBytes.Slice(pos + 4, 4));
            var payload = pngBytes.Slice(pos + 8, chunkLen);
            if (string.Equals(type, "tEXt", StringComparison.Ordinal))
            {
                if (TryScanForLogicalNameInTextPayload(payload, out logicalUpper))
                {
                    return true;
                }
            }
            else if (string.Equals(type, "zTXt", StringComparison.Ordinal))
            {
                if (TryGetZtxtDecompressed(payload, out var decompressed)
                    && TryScanForLogicalNameInTextPayload(decompressed, out logicalUpper))
                {
                    return true;
                }
            }
            else if (string.Equals(type, "iTXt", StringComparison.Ordinal))
            {
                if (TryGetItxtTextUtf8(payload, out var textUtf8, out var isCompressed))
                {
                    if (!isCompressed)
                    {
                        if (TryScanForLogicalNameInTextPayload(textUtf8, out logicalUpper))
                        {
                            return true;
                        }
                    }
                    else if (TryInflateZlib(textUtf8, s_maxNameScanBytes, out var inflated)
                             && TryScanForLogicalNameInTextPayload(inflated, out logicalUpper))
                    {
                        return true;
                    }
                }
            }

            if (string.Equals(type, "IEND", StringComparison.Ordinal))
            {
                break;
            }

            pos += 12 + chunkLen;
        }

        return false;
    }

    private static bool TryScanForLogicalNameInTextPayload(ReadOnlySpan<byte> data, out string logicalUpper) =>
        TryFindItemEtcLogicalName(data, out logicalUpper)
        || TryFindEmbeddedIconAtlasLogicalName(data, out logicalUpper)
        || TryFindItemEtcIconChinaLogicalName(data, out logicalUpper)
        || TryFind542042520Literal(data, out logicalUpper);

    /// <summary>zTXt: keyword\0 compression_method (0) + zlib stream.</summary>
    private static bool TryGetZtxtDecompressed(ReadOnlySpan<byte> payload, [NotNullWhen(true)] out byte[]? decompressed)
    {
        decompressed = null;
        var z = payload.IndexOf((byte)0);
        if (z <= 0 || z > 79 || z + 2 > payload.Length)
        {
            return false;
        }

        if (payload[z + 1] != 0)
        {
            return false;
        }

        var zlibBody = payload.Slice(z + 2);
        return TryInflateZlib(zlibBody, s_maxNameScanBytes, out decompressed);
    }

    /// <summary>iTXt: keyword\0 flag method language\0 translated\0 text (UTF-8; zlib if flag=1).</summary>
    private static bool TryGetItxtTextUtf8(ReadOnlySpan<byte> p, out ReadOnlySpan<byte> textUtf8, out bool compressed)
    {
        textUtf8 = default;
        compressed = false;
        var kz = p.IndexOf((byte)0);
        if (kz <= 0 || kz > 79 || kz + 3 >= p.Length)
        {
            return false;
        }

        var flag = p[kz + 1];
        var method = p[kz + 2];
        if (flag > 1)
        {
            return false;
        }

        compressed = flag == 1;
        if (compressed && method != 0)
        {
            return false;
        }

        var afterMethod = kz + 3;
        var langEndRel = p.Slice(afterMethod).IndexOf((byte)0);
        if (langEndRel < 0)
        {
            return false;
        }

        var transStart = afterMethod + langEndRel + 1;
        if (transStart >= p.Length)
        {
            return false;
        }

        var transEndRel = p.Slice(transStart).IndexOf((byte)0);
        if (transEndRel < 0)
        {
            return false;
        }

        textUtf8 = p.Slice(transStart + transEndRel + 1);
        return !textUtf8.IsEmpty;
    }

    private static bool TryInflateZlib(ReadOnlySpan<byte> src, int maxOut, [NotNullWhen(true)] out byte[]? dst)
    {
        dst = null;
        if (src.IsEmpty)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(src.Length);
            input.Write(src);
            input.Position = 0;
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Min(maxOut, src.Length * 4));
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var n = z.Read(buffer, 0, buffer.Length);
                if (n == 0)
                {
                    break;
                }

                total += n;
                if (total > maxOut)
                {
                    return false;
                }

                output.Write(buffer, 0, n);
            }

            dst = output.ToArray();
            return dst.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Case-insensitive ASCII search for <c>542042520.PNG</c> (200013xx atlas).</summary>
    private static bool TryFind542042520Literal(ReadOnlySpan<byte> data, out string logicalUpper)
    {
        logicalUpper = "";
        var needle = Literal542042520Png;
        if (data.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            if (!RegionEqualsAsciiIgnoreCase(data.Slice(i, needle.Length), needle))
            {
                continue;
            }

            logicalUpper = "542042520.PNG";
            return true;
        }

        return false;
    }

    private static bool RegionEqualsAsciiIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (AsciiToUpper(a[i]) != AsciiToUpper(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<byte> PngSuffixSpan => ".PNG"u8;
    private static ReadOnlySpan<byte> IconTokenSpan => "_ICON"u8;
    private static ReadOnlySpan<byte> ChinaUnderscoreTokenSpan => "_CHINA_"u8;

    /// <summary>First embedded <c>*_ICON##.PNG</c> (ITEMBATTLE_ICON, MOB_ICON, SKILL_ICON, …).</summary>
    private static bool TryFindEmbeddedIconAtlasLogicalName(ReadOnlySpan<byte> data, out string logicalUpper)
    {
        logicalUpper = "";
        var iconTok = IconTokenSpan;
        var suffix = PngSuffixSpan;
        var need = iconTok.Length + 2 + suffix.Length;
        if (data.Length < need)
        {
            return false;
        }

        for (var i = 0; i <= data.Length - need; i++)
        {
            if (!AsciiRegionEqualsIgnoreCase(data.Slice(i, iconTok.Length), iconTok))
            {
                continue;
            }

            var d0 = data[i + iconTok.Length];
            var d1 = data[i + iconTok.Length + 1];
            if (!IsAsciiDigit(d0) || !IsAsciiDigit(d1))
            {
                continue;
            }

            if (!AsciiRegionEqualsIgnoreCase(data.Slice(i + iconTok.Length + 2, suffix.Length), suffix))
            {
                continue;
            }

            var start = i;
            while (start > 0 && IsLogicalFileNameByte(data[start - 1]))
            {
                start--;
            }

            if (i - start < 1)
            {
                continue;
            }

            logicalUpper = AsciiSliceToUpperString(data.Slice(start, i + need - start));
            return true;
        }

        return false;
    }

    /// <summary>First embedded <c>ITEMETC_ICON CHINA_##.PNG</c>.</summary>
    private static bool TryFindItemEtcIconChinaLogicalName(ReadOnlySpan<byte> data, out string logicalUpper)
    {
        logicalUpper = "";
        var chinaTok = ChinaUnderscoreTokenSpan;
        var suffix = PngSuffixSpan;
        var need = chinaTok.Length + 2 + suffix.Length;
        if (data.Length < need)
        {
            return false;
        }

        for (var i = 0; i <= data.Length - need; i++)
        {
            if (!AsciiRegionEqualsIgnoreCase(data.Slice(i, chinaTok.Length), chinaTok))
            {
                continue;
            }

            var d0 = data[i + chinaTok.Length];
            var d1 = data[i + chinaTok.Length + 1];
            if (!IsAsciiDigit(d0) || !IsAsciiDigit(d1))
            {
                continue;
            }

            if (!AsciiRegionEqualsIgnoreCase(data.Slice(i + chinaTok.Length + 2, suffix.Length), suffix))
            {
                continue;
            }

            var start = i;
            while (start > 0 && IsLogicalFileNameByte(data[start - 1]))
            {
                start--;
            }

            if (i - start < 1)
            {
                continue;
            }

            logicalUpper = AsciiSliceToUpperString(data.Slice(start, i + need - start));
            return true;
        }

        return false;
    }

    private static bool IsLogicalFileNameByte(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'a' and <= (byte)'z'
        or >= (byte)'0' and <= (byte)'9'
        or (byte)'_'
        or (byte)' ';

    private static string AsciiSliceToUpperString(ReadOnlySpan<byte> slice)
    {
        Span<char> chars = stackalloc char[slice.Length];
        for (var k = 0; k < slice.Length; k++)
        {
            chars[k] = (char)AsciiToUpper(slice[k]);
        }

        return new string(chars);
    }

    /// <summary>First case-insensitive <c>ITEMETC_ICON##.PNG</c> in PNG bytes (same rule as former regex).</summary>
    private static bool TryFindItemEtcLogicalName(ReadOnlySpan<byte> data, out string logicalUpper)
    {
        logicalUpper = "";
        var prefix = ItemEtcIconPrefix;
        var suffix = ItemEtcIconSuffix;
        var need = prefix.Length + 2 + suffix.Length;
        if (data.Length < need)
        {
            return false;
        }

        Span<char> stackName = stackalloc char[need];
        for (var i = 0; i <= data.Length - need; i++)
        {
            if (!AsciiRegionEqualsIgnoreCase(data.Slice(i, prefix.Length), prefix))
            {
                continue;
            }

            var d0 = data[i + prefix.Length];
            var d1 = data[i + prefix.Length + 1];
            if (!IsAsciiDigit(d0) || !IsAsciiDigit(d1))
            {
                continue;
            }

            var afterDigits = data.Slice(i + prefix.Length + 2, suffix.Length);
            if (!AsciiRegionEqualsIgnoreCase(afterDigits, suffix))
            {
                continue;
            }

            for (var k = 0; k < prefix.Length; k++)
            {
                stackName[k] = (char)AsciiToUpper(data[i + k]);
            }

            stackName[prefix.Length] = (char)d0;
            stackName[prefix.Length + 1] = (char)d1;
            for (var k = 0; k < suffix.Length; k++)
            {
                stackName[prefix.Length + 2 + k] = (char)AsciiToUpper(data[i + prefix.Length + 2 + k]);
            }

            logicalUpper = new string(stackName);
            return true;
        }

        return false;
    }

    private static bool IsAsciiDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static byte AsciiToUpper(byte b) =>
        b is >= (byte)'a' and <= (byte)'z' ? (byte)(b - 32) : b;

    private static bool AsciiRegionEqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (AsciiToUpper(a[i]) != b[i])
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryGetPngBytes(string path, SpfPngEntry entry, out byte[] pngBytes)
    {
        pngBytes = [];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            if (entry.FileOffset < 0 || entry.ByteLength <= 0 || entry.FileOffset + entry.ByteLength > fs.Length)
            {
                return false;
            }

            pngBytes = new byte[entry.ByteLength];
            fs.Position = entry.FileOffset;
            return fs.Read(pngBytes, 0, entry.ByteLength) == entry.ByteLength;
        }
        catch
        {
            pngBytes = [];
            return false;
        }
    }

}
