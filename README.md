# Mouse Audio Visualizer

捕获 Windows 系统音频，将实时频谱渲染为跟随鼠标指针的环形可视化效果（透明悬浮、点击穿透）。

![状态](https://img.shields.io/badge/状态-可用-brightgreen)

## 特性

- WASAPI Loopback 捕获系统输出音频（NAudio 2.2.1，MIT）
- 实时 FFT 频谱（2048 点 / Hann 窗 / 50% 重叠）→ 48 对数频带，Attack/Decay 平滑 + 自动增益（AGC）
- 极坐标环形频谱 / 波纹两种样式，随音频律动
- 悬浮窗口跟随鼠标，透明置顶、点击穿透（WS_EX_TRANSPARENT + LAYERED + TOOLWINDOW + NOACTIVATE）
- 多显示器 + Per-Monitor V2 DPI 感知
- 托盘菜单：启用/禁用、样式、强度、透明度、开机自启、退出
- 低资源占用：空闲 CPU ≈ 0%（16 核机器实测 ~6% 单核当量以内）

## 使用

```powershell
dotnet build src/MouseAudioVisualizer -c Release
# 或直接运行发布产物
src/MouseAudioVisualizer/bin/Release/net8.0-windows/MouseAudioVisualizer.exe
```

运行后无主窗口，仅托盘图标 + 跟随光标的悬浮频谱。播放任意音频即可看到律动。

## 构建

- .NET 8 SDK（Windows）
- 依赖：NAudio 2.2.1（NuGet）

```powershell
dotnet build src/MouseAudioVisualizer/MouseAudioVisualizer.csproj -c Release
```

## 目录结构

```
src/
├── MouseAudioVisualizer/   # 主程序 (WPF)
│   ├── Program.cs          # 入口 + 应用宿主
│   ├── Audio/              # 音频捕获 / FFT / 平滑 / AGC
│   ├── Visual/             # 悬浮窗口 / 环形渲染 / Win32 封装
│   └── Shell/              # 托盘
└── SpectrumProbe/          # 开发工具：控制台频谱数值验证
    # 用法: SpectrumProbe.exe --auto [--play <wav>] [--secs N]
```

## 已知限制

- 全屏独占游戏：透明悬浮窗不显示在独占全屏之上（系统限制）。
- 个别虚拟声卡 / 蓝牙设备 loopback 行为异常，建议使用默认输出设备。
- 开机自启通过 HKCU Run 注册表键实现。

## 许可与合规

- 核心依赖 NAudio（MIT）。
- 本仓库代码为原创实现；调研中引用的无 LICENSE 项目（CursorTrail、chromascope）仅参考架构思路，未复制任何代码。

## 状态

- [x] M1 音频管道（loopback 捕获 + FFT + 频带 + 平滑）
- [x] M2 环形频谱渲染 + 透明置顶悬浮窗口
- [x] M3 鼠标跟随 + 点击穿透
- [x] M4 DPI / 多屏 / 托盘 / 样式 / 开机自启
- [x] M5 README / 清理 / 发布准备

方案详见 [PLAN.md](PLAN.md)。