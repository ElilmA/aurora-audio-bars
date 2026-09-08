using System.Windows.Media.Imaging;
using MouseAudioVisualizer.Audio;

namespace MouseAudioVisualizer.Visual;

/// <summary>
/// 纵向能量条渲染器（重写版）：
/// 不是频谱柱。呈现为「从屏幕底部向上生长的连续炫彩发光能量带」。
///
/// - 驱动：全局音量（RMS + Peak 聚合）→ 单一填充高度（0..1）
/// - 高度：attack 快 / release 慢 平滑，防抖动
/// - 形态：底部↔填充高度之间整段连续填充，无分块、无断点
/// - 渐变色：蓝(底) → 紫 → 粉 → 红/橙(顶)，高饱和高亮度模拟霓虹
/// - 顶部：约 24px 光晕淡出，边缘柔和（液态顶部）
/// - 两侧窗口调用同一 renderer 即天然对称。
/// </summary>
public sealed class EdgeRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _bandCount;
    private readonly byte[] _rowColor;   // 行 → 绝对位置的渐变 RGB (r,g,b)，底蓝顶橙

    private float _smoothH;     // 平滑后的填充高度（0..1）

    public EdgeRenderer(int widthPx, int heightPx, int bandCount)
    {
        _width = widthPx;
        _height = heightPx;
        _bandCount = bandCount;
        _rowColor = new byte[heightPx * 3];
        for (int y = 0; y < heightPx; y++)
        {
            float t = (float)(heightPx - 1 - y) / Math.Max(1, heightPx - 1); // 0=底部 1=顶部
            HsvToRgb(240f + t * 150f % 360f, 1f, 1f,   // 蓝240 → 紫270 → 粉300~330 → 红/橙0~30
                out _rowColor[y * 3], out _rowColor[y * 3 + 1], out _rowColor[y * 3 + 2]);
        }
    }

    /// <summary>将频谱帧绘制到竖条 bitmap（Pbgra32，尺寸与构造一致）。</summary>
    public void Draw(WriteableBitmap bitmap, SpectrumFrame frame, float intensity, float alpha)
    {
        int stride = _width * 4;
        bitmap.Lock();
        unsafe
        {
            byte* px = (byte*)bitmap.BackBuffer.ToPointer();
            var levels = frame.Levels;

            // 1) 聚合全局音量（RMS-like + Peak）
            float vol = AggregateVolume(levels, frame.Peak) * intensity;
            vol = MathF.Min(1.25f, vol);

            // 2) 目标高度映射：静音~3%，小音量~15-25%，正常~40-60%，大声~80-100%
            float targetH = MathF.Min(1f, 0.03f + vol * 0.95f);

            // 3) attack / release 平滑：上升快、回落慢 → 防抖动、似液体
            float k = targetH > _smoothH ? 0.55f : 0.12f;
            _smoothH += (targetH - _smoothH) * k;
            if (MathF.Abs(_smoothH) > 1f) _smoothH = 1f;
            if (MathF.Abs(_smoothH) < 0f) _smoothH = 0f;

            float fillPx = _smoothH * _height;
            const float glowPx = 24f; // 顶部光晕过渡区

            // 4) 先清空整张 bitmap：上一帧内容必须完全消失（残影 bug 修复）
            for (int y = 0; y < _height; y++)
            {
                byte* row = px + y * stride;
                for (int x = 0; x < _width; x++)
                {
                    row[x * 4] = 0;
                    row[x * 4 + 1] = 0;
                    row[x * 4 + 2] = 0;
                    row[x * 4 + 3] = 0;
                }
            }

            // 5) 绘制：从底部到 fillPx 连续填充；顶部 glow 柔化淡出
            for (int y = 0; y < _height; y++)
            {
                float rowFromBottom = _height - 1 - y;
                if (rowFromBottom > fillPx) continue;

                // 顶部光晕：接近 fillPx 的 glow 区渐隐，其余全亮
                float glowT = MathF.Min(1f, (fillPx - rowFromBottom) / glowPx); // 0=顶端边缘 1=远离边缘
                float aBoost = 0.35f + 0.65f * glowT;   // 边缘 35% → 实体 100%

                byte r = _rowColor[y * 3];
                byte g = _rowColor[y * 3 + 1];
                byte b = _rowColor[y * 3 + 2];
                byte aa = (byte)(alpha * aBoost * 255);

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

    /// <summary>全局音量：各频带能量平方均值（RMS 近似）+ 峰值加权。</summary>
    private static float AggregateVolume(float[] levels, float peak)
    {
        if (levels.Length == 0) return peak;
        double sumSq = 0;
        foreach (var l in levels) sumSq += (double)l * l;
        float rms = (float)Math.Sqrt(sumSq / levels.Length);
        // 峰值反映瞬态，RMS 反映持续能量；权重让打击感与持续音都可见
        return MathF.Min(1.2f, 0.72f * rms * 2.2f + 0.28f * peak);
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        h %= 360f;
        if (h < 0f) h += 360f;
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