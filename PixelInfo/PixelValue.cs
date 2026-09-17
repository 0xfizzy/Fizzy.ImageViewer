namespace Fizzy.ImageViewer.PixelInfo;

/// <summary>
/// 像素值结构体，支持多种像素格式。
/// </summary>
public readonly struct PixelValue
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte Gray;
    public readonly bool IsGrayscale;

    public PixelValue(byte gray)
    {
        Gray = gray;
        IsGrayscale = true;
        R = G = B = 0;
    }

    public PixelValue(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        IsGrayscale = false;
        Gray = 0;
    }

    /// <summary>
    /// 格式化像素值到字符缓冲区，0-GC 实现。
    /// </summary>
    /// <returns>写入的字符数</returns>
    public int FormatTo(Span<char> buffer)
    {
        if (IsGrayscale)
        {
            // "GRAY: 255" = 最多 9 字符
            ReadOnlySpan<char> prefix = "GRAY: ";
            prefix.CopyTo(buffer);
            return prefix.Length + FormatByte(Gray, buffer[prefix.Length..]);
        }
        else
        {
            // "R: 255, G: 255, B: 255" = 最多 22 字符
            int pos = 0;
            ReadOnlySpan<char> rPrefix = "R: ";
            rPrefix.CopyTo(buffer[pos..]);
            pos += rPrefix.Length;
            pos += FormatByte(R, buffer[pos..]);

            ReadOnlySpan<char> gPrefix = ", G: ";
            gPrefix.CopyTo(buffer[pos..]);
            pos += gPrefix.Length;
            pos += FormatByte(G, buffer[pos..]);

            ReadOnlySpan<char> bPrefix = ", B: ";
            bPrefix.CopyTo(buffer[pos..]);
            pos += bPrefix.Length;
            pos += FormatByte(B, buffer[pos..]);

            return pos;
        }
    }

    private static int FormatByte(byte value, Span<char> buffer)
    {
        if (value >= 100)
        {
            buffer[0] = (char)('0' + value / 100);
            buffer[1] = (char)('0' + (value / 10) % 10);
            buffer[2] = (char)('0' + value % 10);
            return 3;
        }
        else if (value >= 10)
        {
            buffer[0] = (char)('0' + value / 10);
            buffer[1] = (char)('0' + value % 10);
            return 2;
        }
        else
        {
            buffer[0] = (char)('0' + value);
            return 1;
        }
    }
}
