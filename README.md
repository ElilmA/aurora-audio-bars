# Mouse Audio Visualizer

捕获 Windows 系统音频,将实时频谱渲染为跟随鼠标指针的环形可视化效果(透明悬浮、点击穿透)。

## 特性

- WASAPI Loopback 捕获系统输出音频(NAudio)
- 实时 FFT 频谱 → 极坐标环形频谱,随音频律动
- 悬浮窗口跟随鼠标,透明置顶、点击穿透
- 多显示器 + DPI 感知
- 托盘菜单:开关、强度、样式、透明度

## 技术栈

- C# / .NET 8 + WPF
- NAudio(MIT)
- Win32:GetCursorPos / SetWindowPos / WS_EX_TRANSPARENT

## 状态

规划阶段。方案见 [PLAN.md](PLAN.md)。
