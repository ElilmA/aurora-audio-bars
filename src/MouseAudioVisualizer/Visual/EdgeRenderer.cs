using System.Windows.Media.Imaging;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 竖向频谱渲染器（连续版）：
/// 整条高度逐行渲染，每行对应一个“连续频带位置”（对数谱插值，无分块感），
/// 亮度 = 相邻频带线性插值能量 + 波浪前沿（能量变化率）亮线，
/// 呈现“从底部向上推动的波形”效果。底部低频、顶部高频，彩虹连续渐变。
/// </summary>
public sealed class EdgeRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _bandCount;
    private readonly float[] _rowBandPos;   // 行 → 连续频带位置 [0..bandCount-1]
    private readonly byte[] _rowColor;      // 行 → 基础色 RGB (r,g,b)

    public EdgeRenderer(int widthPx, int heightPx, int bandCount)
    {
        _width = widthPx;
        _height = heightPx;
        _bandCount = bandCount;
        _rowBandPos = new float[heightPx];
        _rowColor = new byte[heightPx * 3];
        for (int y = 0; y < heightPx; y++)
        {
            float t = (float)(heightPx - 1 - y) / Math.Max(1, heightPx - 1); // 0=顶部(高频) 1=底部(低频)
            float pos = t * (bandCount - 1);
            _rowBandPos[y] = pos;
            HsvToRgb(pos / bandCount * 300f, 0.95f, 1f,
                out _rowColor[y * 3], out _rowColor[y * 3 + 1], out _rowColor[y * 3 + 2]);
        }
    }

    /// <summary>将频谱帧绘制到竖条 bitmap（Pbgra32，尺寸与构造一致）。</summary>
    public void Draw(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha)
    {
        int stride = _width * 4;
        int bandMax = _bandCount - 1;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;

            // 先清透明（逐行）
            for (int y = 0; y < _height; y++)
            {
                byte* row = px + y * stride;
                for (int x = 0; x < _width; x++)
                {
                    row[x * 4 + 3] = 0;
                }
            }

            for (int y = 0; y < _height; y++)
            {
                float pos = _rowBandPos[y];

                // 相邻频带线性插值能量（保证纵向连续渐变）
                int i0 = (int)pos; if (i0 > bandMax) i0 = bandMax;
                int i1 = Math.Min(i0 + 1, bandMax);
                float frac = pos - i0;
                float e0 = levels.Length > i0 ? levels[i0] : 0f;
                float e1 = levels.Length > i1 ? levels[i1] : 0f;
                float e = e0 + (e1 - e0) * frac;
                float lv = MathF.Min(1f, e * intensity);
                if (lv <= 0.01f) continue;

                // 下一行能量变化率 → 波浪前沿亮线（“推动”感）
                float eE = 0f;
                if (y > 0)
                {
                    int yi0 = (int)_rowBandPos[y - 1]; if (yi0 > bandMax) yi0 = bandMax;
                    int yi1 = Math.Min(yi0 + 1, bandMax);
                    float yf = _rowBandPos[y - 1] - yi0;
                    float ee0 = levels.Length > yi0 ? levels[yi0] : 0f;
                    float ee1 = levels.Length > yi1 ? levels[yi1] : 0f;
                    eE = ee0 + (ee1 - ee0) * yf;
                }
                float edge = MathF.Min(1f, MathF.Abs(e - eE) * 10f);

                float brightness = 0.5f + 0.5f * lv;
                float a = alpha * MathF.Min(1f, 0.6f + 0.4f * lv + edge * 0.6f);

                byte r = (byte)(_rowColor[y * 3] * brightness);
                byte g = (byte)(_rowColor[y * 3 + 1] * brightness);
                byte b = (byte)(_rowColor[y * 3 + 2] * brightness);
                byte aa = (byte)(a * 255);

                byte* row = px + y * stride;
                for (int x = 0; x < _width; x++)
                {
                    int o = x * 4;
                    row[o] = b;
                    row[o + 1] = g;
                    row[o + 2] = r;
                    row[o + 3] = aa;
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