using MouseAudioVisualizer.Audio;
using NAudio.Wave;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("== SpectrumProbe: WASAPI Loopback -> FFT 验证 ==");
Console.WriteLine("用法: --auto [--play <wav>] [--secs N]");

var cliArgs = Environment.GetCommandLineArgs();
bool auto = cliArgs.Contains("--auto");
if (!auto)
{
    Console.ReadKey();
}

using var capture = new AudioCapture();
var engine = new SpectrumEngine(capture.SampleRate);
Console.WriteLine($"设备: {capture.DeviceName}  采样率: {capture.SampleRate} Hz  声道: {capture.Channels}");
Console.WriteLine($"FFT: {SpectrumEngine.FftSize} 点, 频带: {SpectrumEngine.BandCount}, Attack={engine.Attack:F2}, Decay={engine.Decay:F2}");

var playIdx = Array.IndexOf(cliArgs, "--play");
WaveOutEvent? player = null;
if (playIdx >= 0 && playIdx + 1 < cliArgs.Length)
{
    var reader = new WaveFileReader(cliArgs[playIdx + 1]);
    player = new WaveOutEvent();
    player.Init(reader);
    player.Play();
    Console.WriteLine($"播放: {cliArgs[playIdx + 1]} ({reader.WaveFormat})");
}

var secsIdx = Array.IndexOf(cliArgs, "--secs");
int totalSecs = secsIdx >= 0 && secsIdx + 1 < cliArgs.Length ? int.Parse(cliArgs[secsIdx + 1]) : 10;

var frame = new float[SpectrumEngine.FftSize];
capture.Error += ex => Console.WriteLine($"[ERROR] {ex.Message}");

capture.Start();
var sw = System.Diagnostics.Stopwatch.StartNew();
var lastPrint = 0L;
int frames = 0;
float maxRms = 0f;

while (true)
{
    int got = capture.Read(frame, SpectrumEngine.FftSize);
    if (got < SpectrumEngine.FftSize)
    {
        Thread.Sleep(5);
        continue;
    }

    var spec = engine.Analyze(frame);
    frames++;
    float rms = 0f;
    for (int i = 0; i < got; i++) rms += frame[i] * frame[i];
    rms = MathF.Sqrt(rms / got);
    if (rms > maxRms) maxRms = rms;

    if (sw.ElapsedMilliseconds - lastPrint >= 1000)
    {
        lastPrint = sw.ElapsedMilliseconds;
        Console.Write($"t={sw.Elapsed.TotalSeconds,5:F1}s peak={spec.Peak,6:F3} rms={rms,6:F4}  ");
        for (int b = 0; b < spec.Levels.Length; b++)
        {
            int v = (int)(spec.Levels[b] * 8);
            char c = v switch { 0 => ' ', 1 => '.', 2 => ':', 3 => '|', 4 => '!', _ => '#' };
            Console.Write(c);
        }
        Console.WriteLine();
    }

    if (sw.Elapsed.TotalSeconds >= totalSecs)
    {
        break;
    }
    Thread.Sleep(20);
}

capture.Stop();
player?.Stop();
player?.Dispose();
Console.WriteLine($"完成。共处理 {frames} 帧, 最大 RMS={maxRms:F4}。");
