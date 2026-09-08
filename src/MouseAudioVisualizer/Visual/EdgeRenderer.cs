using System.Windows.Media.Imaging;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 竖向频谱渲染器：将整屏高度按频带分段，每段高度 = 屏高 / bandCount。
/// 每个频带对应一种颜色（底部低频 → 顶部高频彩虹渐变），能量驱动该段点亮。
/// 写入竖条 WriteableBitmap（宽 barWidth 物理像素，高 屏高物理像素）。
/// </summary>
public sealed class EdgeRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _bandCount;
    private readonly int _segHeight;        // 每段像素高度
    private readonly byte[] _colorCache;     // band → BGR (b,g,r)

    public EdgeRenderer(int widthPx, int heightPx, int bandCount)
    {
        _width = widthPx;
        _height = heightPx;
        _bandCount = bandCount;
        _segHeight = Math.Max(1, heightPx / bandCount);
        _colorCache = new byte[bandCount * 3];
        for (int b = 0; b < bandCount; b++)
        {
            // 色相从低频(蓝/青)到高频(紫/粉)：hue 0→300
            HsvToRgb((float)b / bandCount * 300f, 0.85f, 1f,
                out _colorCache[b * 3 + 2], out _colorCache[b * 3 + 1], out _colorCache[b * 3]);
        }
    }

    /// <summary>将频谱帧绘制到竖条 bitmap（Pbgra32，尺寸与构造一致）。</summary>
    public void Draw(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha, bool flip)
    {
        int stride = _width * 4;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;

            // 逐段：段 b 位于 [height - (b+1)*segHeight, height - b*segHeight)
            for (int b = 0; b < _bandCount; b++)
            {
                int srcBand = flip ? (_bandCount - 1 - b) : b;
                float level = srcBand < levels.Length ? levels[srcBand] : 0f;
                float lv = MathF.Min(1f, level * intensity);
                if (lv <= 0.004f) continue;

                float brightness = 0.3f + 0.7f * lv;
                float a = MathF.Min(1f, lv * 1.2f) * alpha;
                byte r = (byte)(_colorCache[b * 3 + 2] * brightness);
                byte g = (byte)(_colorCache[b * 3 + 1] * brightness);
                byte bl = (byte)(_colorCache[b * 3] * brightness);
                byte aa = (byte)(a * 255);

                int y0 = _height - (b + 1) * _segHeight;
                int y1 = _height - b * _segHeight;
                for (int y = y0; y < y1; y++)
                {
                    if (y < 0 || y >= _height) continue;
                    byte* row = px + y * stride;
                    for (int x = 0; x < _width; x++)
                    {
                        int o = x * 4;
                        row[o] = bl;
                        row[o + 1] = g;
                        row[o + 2] = r;
                        row[o + 3] = aa;
                    }
                }
            }
        }
        bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, _width, _height));
        bitmap.Unlock();
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f % 2f) - 1f));
        float m = v - c;
        float r0, g0, b0;
        if (h < 60) { r0 = c; g0 = x; b0 = 0; }
        else if (h < 120) { r0 = x; g0 = c; b0 = 0; }
        else if (h < 180) { r0 = 0; g0 = c; b0 = x; }
        else if (h < 240) { r0 = 0; g0 = x; b0 = c; }
        else if (h < 300) { r0 = x; g0 = 0; b0 = c; }
        else { r0 = c; g0 = 0; b0 = x; }
        r = (byte)((r0 + m) * 255);
        g = (byte)((g0 + m) * 255);
        b = (byte)((b0 + m) * 255);
    }
}
