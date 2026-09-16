using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace NexusApp.Services;

public class OcrService : IDisposable
{
    private OcrEngine? _engine;
    private bool _available;

    public bool IsAvailable => _available;
    public bool LastScanHadRegion { get; private set; }

    private int  _regX, _regY, _regW, _regH;
    private bool _hasRegion;

    public void SetRegion(int x, int y, int w, int h)
    {
        _regX = x; _regY = y; _regW = w; _regH = h;
        _hasRegion = w > 0 && h > 0;
    }

    public OcrService()
    {
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                   ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            _available = _engine != null;
        }
        catch { _available = false; }
    }

    public async Task<(IReadOnlyList<int> Values, bool PinFound)> ScanFullScreenAsync()
    {
        if (!_available || _engine == null) return (Array.Empty<int>(), false);

        byte[]? raw;
        int bw, bh;

        if (_hasRegion)
        {
            // Use stored coordinates — capture only the known scan region.
            raw = CaptureRegion(_regX, _regY, _regW, _regH);
            if (raw == null) return (Array.Empty<int>(), false);
            LastScanHadRegion = true;
            bw = _regW; bh = _regH;
        }
        else
        {
            // Fallback: find the magenta scan-box border on the full screen.
            var fw = GetSystemMetrics(SM_CXSCREEN);
            var fh = GetSystemMetrics(SM_CYSCREEN);
            if (fw <= 0 || fh <= 0) return (Array.Empty<int>(), false);

            var full = CaptureRegion(0, 0, fw, fh);
            if (full == null) return (Array.Empty<int>(), false);

            var box = FindMagentaRegion(full, fw, fh);
            LastScanHadRegion = box != null;
            if (box == null) return (Array.Empty<int>(), false);

            var (bx, by, _bw, _bh) = box.Value;
            raw = ExtractSubRegion(full, fw, bx, by, _bw, _bh);
            bw = _bw; bh = _bh;
        }

        try
        {
            var values = await RecognizeStacked(raw!, bw, bh);
            // A drawn/found region still counts as a pin when OCR reads nothing this tick.
            // Treating a blank read as pin-lost resets confirmation and is what made a
            // three-signature region never lock.
            return (values, true);
        }
        catch { }

        return (Array.Empty<int>(), LastScanHadRegion);
    }

    // Compact HUD pills are ~24-32px tall. 56px bands often cover two rows and skip
    // entirely when the drawn region is shorter than 76px.
    private const int StackBandHeight = 28;
    private const int StackBandOverlap = 10;

    private async Task<IReadOnlyList<int>> RecognizeStacked(byte[] raw, int w, int h)
    {
        var full = await RecognizeRaw(raw, w, h);
        if (h < StackBandHeight + StackBandOverlap)
            return full.Values;

        var bands = new List<int>();
        var seen = new HashSet<int>();
        for (var y = 0; y < h; )
        {
            var bandH = Math.Min(StackBandHeight, h - y);
            if (bandH < 16) break;
            var slice = ExtractSubRegion(raw, w, 0, y, w, bandH);
            foreach (var val in (await RecognizeRaw(slice, w, bandH)).Values)
            {
                if (seen.Add(val))
                    bands.Add(val);
            }
            if (y + bandH >= h) break;
            y += StackBandHeight - StackBandOverlap;
        }

        if (bands.Count > full.Values.Count)
            return bands;
        if (full.Values.Count == 0)
            return bands;
        return full.Values;
    }

    private async Task<(IReadOnlyList<int> Values, string Text)> RecognizeRaw(byte[] raw, int w, int h)
    {
        var processed = Preprocess(raw, w, h, out int pw, out int ph);
        var softBmp = ToSoftwareBitmap(processed, pw, ph);
        var result = await _engine!.RecognizeAsync(softBmp);
        return (ExtractRsValuesFromOcr(result), result.Text);
    }

    // ── Preprocessing ──────────────────────────────────────────────────────────
    // Invert (dark navy → white, white text → black) and scale up 6× so each
    // glyph is large enough for the OCR engine to cleanly distinguish 6/8/9.
    // Anti-aliasing is preserved — Windows OCR reads smooth glyphs better than
    // hard-binarized pixel art.

    private static byte[] Preprocess(byte[] bgra, int w, int h, out int outW, out int outH)
    {
        const int scale   = 6;
        const int padding = 24;

        outW = w * scale + padding * 2;
        outH = h * scale + padding * 2;

        var output = new byte[outW * outH * 4];
        Array.Fill(output, (byte)255);

        for (int sy = 0; sy < h; sy++)
            for (int sx = 0; sx < w; sx++)
            {
                int src = (sy * w + sx) * 4;

                // Invert then boost contrast ×1.4 so ambiguous mid-gray pixels
                // (the open gap at the top of "6") are pushed clearly toward
                // white and away from the dark text band.
                byte ib = (byte)Math.Min(255, (255 - bgra[src])     * 14 / 10);
                byte ig = (byte)Math.Min(255, (255 - bgra[src + 1]) * 14 / 10);
                byte ir = (byte)Math.Min(255, (255 - bgra[src + 2]) * 14 / 10);

                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int dstX = sx * scale + dx + padding;
                        int dstY = sy * scale + dy + padding;
                        int dst  = (dstY * outW + dstX) * 4;
                        output[dst]     = ib;
                        output[dst + 1] = ig;
                        output[dst + 2] = ir;
                        output[dst + 3] = 255;
                    }
            }

        return output;
    }

    // ── Value extraction ───────────────────────────────────────────────────────

    // Matches "X XXX" / "XX XXX" / "XXX XXX" (1-3 digits, space, exactly 3 digits) with no
    // surrounding digits. Covers 2,000–200,000 where OCR reads the thousands comma as a space.
    private static readonly Regex _splitThousands = new(@"(?<!\d)(\d{1,3}) (\d{3})(?!\d)", RegexOptions.Compiled);

    internal static int? ExtractRsValue(string text)
    {
        var values = ExtractRsValues(text);
        return values.Count == 0 ? null : values[0];
    }

    /// <summary>
    /// Every valid RS-sized number in reading order. Newlines keep line identity so two
    /// signatures on separate rows do not merge into one digit run.
    /// </summary>
    internal static IReadOnlyList<int> ExtractRsValues(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<int>();

        var found = new List<int>();
        var seen = new HashSet<int>();
        foreach (var line in SplitOcrLines(text))
        {
            foreach (var val in ExtractRsRuns(line))
            {
                if (seen.Add(val))
                    found.Add(val);
            }
        }
        return found;
    }

    private static IReadOnlyList<int> ExtractRsValuesFromOcr(OcrResult result)
    {
        var found = new List<int>();
        var seen = new HashSet<int>();

        if (result.Lines is { Count: > 0 })
        {
            foreach (var line in result.Lines)
            {
                if (line.Words is { Count: > 0 })
                {
                    foreach (var word in line.Words.OrderBy(w => w.BoundingRect.X))
                        AddUnique(found, seen, ExtractRsRuns(word.Text));
                }
                else
                    AddUnique(found, seen, ExtractRsRuns(line.Text));
            }
        }

        if (found.Count == 0)
            AddUnique(found, seen, ExtractRsValues(result.Text));

        return found;
    }

    private static void AddUnique(List<int> found, HashSet<int> seen, IEnumerable<int> values)
    {
        foreach (var val in values)
        {
            if (seen.Add(val))
                found.Add(val);
        }
    }

    /// <summary>
    /// Split a digit run longer than 6 chars into as many 4-6 digit chunks as possible,
    /// then keep those that fall in the RS range. OCR often concatenates stacked
    /// signatures into one token such as 17200450008000. A 4-digit leftover under
    /// 2000 (1328) is consumed as a chunk so it cannot poison 5291 + 18500.
    /// </summary>
    internal static List<int> SplitLongDigitRun(ReadOnlySpan<char> digits)
    {
        var s = digits.ToString();
        var n = s.Length;
        var best = new List<int>?[n + 1];
        best[n] = [];
        for (var i = n - 1; i >= 0; i--)
        {
            List<int>? chosen = null;
            for (var len = 4; len <= 6 && i + len <= n; len++)
            {
                if (!TryParseDigitChunk(s.AsSpan(i, len), out var val)) continue;
                var rest = best[i + len];
                if (rest is null) continue;
                var candidate = new List<int>(1 + rest.Count) { val };
                candidate.AddRange(rest);
                if (IsBetterSplit(candidate, chosen))
                    chosen = candidate;
            }
            best[i] = chosen;
        }

        var split = best[0];
        if (split is null || split.Count == 0) return [];
        var inRange = new List<int>(split.Count);
        foreach (var val in split)
        {
            if (val is >= 2000 and <= 200000)
                inRange.Add(val);
        }
        return inRange;
    }

    private static bool IsBetterSplit(List<int> candidate, List<int>? current)
    {
        if (current is null) return true;
        if (candidate.Count != current.Count) return candidate.Count > current.Count;
        for (var i = 0; i < candidate.Count; i++)
        {
            var a = DigitLen(candidate[i]);
            var b = DigitLen(current[i]);
            if (a != b) return a < b;
        }
        return false;
    }

    private static int DigitLen(int value) => value switch
    {
        >= 100000 => 6,
        >= 10000 => 5,
        _ => 4,
    };

    private static bool TryParseDigitChunk(ReadOnlySpan<char> digits, out int value)
    {
        value = 0;
        if (digits.Length is < 4 or > 6 || digits[0] == '0') return false;
        return int.TryParse(digits, out value);
    }

    private static bool TryParseRs(ReadOnlySpan<char> digits, out int value)
        => TryParseDigitChunk(digits, out value) && value is >= 2000 and <= 200000;

    private static string[] SplitOcrLines(string text)
        => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static List<int> ExtractRsRuns(string text)
    {
        // Strip commas/periods sitting between two digit characters ("17,200" → "17200").
        // Also drop a comma/period before a space+digit ("18, 500" → "18 500") so the
        // thousands regex can collapse it.
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ((c == ',' || c == '.') && i > 0 && char.IsDigit(text[i - 1]))
            {
                if (i + 1 < text.Length && char.IsDigit(text[i + 1]))
                    continue;
                if (i + 2 < text.Length && text[i + 1] == ' ' && char.IsDigit(text[i + 2]))
                    continue;
            }
            sb.Append(c);
        }

        // Collapse "X XXX" → "XXXX" for cases where OCR reads the comma as a space
        var normalized = _splitThousands.Replace(sb.ToString(), "$1$2");

        var runs = new List<int>();
        int start = -1;
        for (int i = 0; i <= normalized.Length; i++)
        {
            bool isDigit = i < normalized.Length && char.IsDigit(normalized[i]);
            if (isDigit && start == -1)
            {
                start = i;
            }
            else if (!isDigit && start != -1)
            {
                int len = i - start;
                if (len >= 4 && len <= 6 && TryParseRs(normalized.AsSpan(start, len), out var val))
                    runs.Add(val);
                else if (len > 6)
                    runs.AddRange(SplitLongDigitRun(normalized.AsSpan(start, len)));
                start = -1;
            }
        }
        return runs;
    }

    // ── Capture / convert ──────────────────────────────────────────────────────

    private static unsafe byte[]? CaptureRegion(int x, int y, int w, int h)
    {
        var hdc    = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return null;
        var hdcMem = CreateCompatibleDC(hdc);
        var hBmp   = CreateCompatibleBitmap(hdc, w, h);
        var hOld   = SelectObject(hdcMem, hBmp);
        BitBlt(hdcMem, 0, 0, w, h, hdc, x, y, SRCCOPY);
        var bmpInfo = new BITMAPINFOHEADER
        {
            biSize     = (uint)sizeof(BITMAPINFOHEADER),
            biWidth    = (uint)w, biHeight = -h,
            biPlanes   = 1, biBitCount = 32, biCompression = 0
        };
        var buf = new byte[w * h * 4];
        fixed (byte* p = buf)
            GetDIBits(hdcMem, hBmp, 0, (uint)h, (IntPtr)p, ref bmpInfo, DIB_RGB_COLORS);
        SelectObject(hdcMem, hOld);
        DeleteObject(hBmp);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdc);
        return buf;
    }

    private static SoftwareBitmap ToSoftwareBitmap(byte[] bgra, int w, int h)
    {
        var bmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        bmp.CopyFromBuffer(bgra.AsBuffer());
        return bmp;
    }

    public void Dispose() { }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static byte[] ExtractSubRegion(byte[] src, int srcW, int rx, int ry, int rw, int rh)
    {
        var dst = new byte[rw * rh * 4];
        for (int y = 0; y < rh; y++)
            Array.Copy(src, ((ry + y) * srcW + rx) * 4, dst, y * rw * 4, rw * 4);
        return dst;
    }

    private static (int x, int y, int w, int h)? FindMagentaRegion(byte[] raw, int sw, int sh)
    {
        int minX = sw, maxX = 0, minY = sh, maxY = 0;
        bool found = false;
        for (int y = 0; y < sh; y += 2)
            for (int x = 0; x < sw; x += 2)
            {
                int i = (y * sw + x) * 4;
                if (!IsMagenta(raw[i + 2], raw[i + 1], raw[i])) continue;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                found = true;
            }
        if (!found || maxX - minX < 20 || maxY - minY < 10) return null;
        const int border = 4;
        int ix = minX + border, iy = minY + border;
        int iw = maxX - minX - border * 2, ih = maxY - minY - border * 2;
        if (iw <= 0 || ih <= 0) return null;
        return (ix, iy, iw, ih);
    }

    private static bool IsMagenta(byte r, byte g, byte b) => r > 200 && g < 30 && b > 200;

    // ── P/Invoke ───────────────────────────────────────────────────────────────

    private const int  SRCCOPY        = 0xCC0020;
    private const uint DIB_RGB_COLORS = 0;
    private const int  SM_CXSCREEN    = 0;
    private const int  SM_CYSCREEN    = 1;

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool   DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool   DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool   BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("gdi32.dll")] static extern int    GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int   ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern int   GetSystemMetrics(int n);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize, biWidth; public int biHeight;
        public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }
}
