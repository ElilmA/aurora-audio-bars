# 项目记忆 - Mouse Audio Visualizer

> 更新:2026-09-08

## 项目目标

捕获 Windows 系统音频,将实时频谱渲染为跟随鼠标指针的悬浮效果(环形频谱/波纹),透明置顶、点击穿透。方案见 PLAN.md。

## 关键决策

- **技术栈**:C# .NET 8 + WPF + NAudio 2.2.1(MIT),WinForms 仅用于托盘(NotifyIcon)。
- **渲染方案:WPF 自绘(WriteableBitmap)** 已选定并实测通过:256x256 Pbgra32,unsafe 指针直接写 BackBuffer,预计算像素查找表(距离/频带索引),每帧只遍历环+核心区(~2.2万像素)。**未用 SkiaSharp**——当前方案足够,CPU 实测很低。
- **FFT**:NAudio `FastFourierTransform`(2048 点,Hann 窗,50% 重叠,~47fps)。注意 NAudio FFT 输出已按 N 归一化,单位幅度正弦波峰值约 0.5,**不能再除以 half**。
- **归一化/增益**:AGC(自动增益)跟踪慢速峰值,把整体电平映射到目标 0.75,增益限幅 [0.5, 40]。默认 Gain=1.0。
- **平滑参数**:Attack=0.45 / Decay=0.12(实测合理,默认值)。AGC attack=0.05 / decay=0.0015。
- **窗口**:AllowsTransparency + WindowStyle.None + WS_EX_TRANSPARENT|TOOLWINDOW|NOACTIVATE|LAYERED;Per-Monitor V2 DPI(manifest 声明,非运行时调用)。
- **鼠标跟随**:GetCursorPos 轮询(渲染循环 33ms 内),SetWindowPos 物理像素居中,光标位置缓存(仅移动时 SetWindowPos)。**注意 GetDpiForWindow 在 96 DPI 机器返回 96**。
- **入口**:自定义 Program.Main(无 App.xaml,已删除模板 MainWindow),Application.Run 阻塞 + Startup 事件启动 AppHost。

## 重要教训(Bug 记录)

1. **AudioEngine 后台线程必须节流!** 最初循环无 Sleep 节流,数据充足时无限高速 FFT 空转,CPU 吃满单核(98%)。修复:Stopwatch 时间戳节流 ~21ms/帧(与音频块速率匹配),CPU 降至 ~1%。Probe 一直有 Sleep(20) 所以没暴露此问题。
2. 测试环境有**真人移动鼠标**,坐标验证要用"光标静止时立即读窗口"的方式,避免误判。
3. PowerShell 测试环境可能与应用 DPI 感知不同,坐标对比时先确认 GetDpiForWindow 值。

## 验证结果(2026-09-08,本机 16 核 / 双显示器 / 96 DPI)

- M1:probe 捕获 48kHz/2ch,440Hz 落在正确频带,峰值 0.87(AGC 生效)
- M2:环形渲染可见、律动正常
- M3:光标(960,540)/(100,100) 窗口中心精确匹配;样式位 0x080800A8(全部穿透标志)
- M4:CPU 音频播放时 ~1.17% 单核;托盘样式位验证通过;开机自启 HKCU Run 键
- 双屏:副屏(-1920,7) 跟随逻辑正确(SetWindowPos 物理坐标天然支持)

## 文件结构

- src/MouseAudioVisualizer/:Program.cs(入口+宿主)、Audio/(AudioCapture/SpectrumEngine/SpectrumFrame/AudioEngine)、Visual/(CursorOverlay/RingRenderer/Native)、Shell/TrayIcon.cs、app.manifest
- src/SpectrumProbe/:控制台验证工具(--auto [--play wav] [--secs N])

## 风险/待办

- **真实音乐内容**未经全面测试(测试音为合成正弦波);平滑参数可能需按音乐类型微调。
- 蓝牙/虚拟声卡 loopback 异常未测(计划中已列)。
- 全屏独占游戏不显示悬浮层(系统限制,非 bug)。
- CPU 在 4 核机器上的实测未做(本机 16 核)。
- 单文件发布(M5 后续)未做。
- 无 LICENSE 项目(CursorTrail/chromascope)仅参考,未复制代码。