using NESEmulator.Core.CPU;
using NESEmulator.Core.PPU;
using NESEmulator.Core.APU;
using NESEmulator.Core.Memory;
using NESEmulator.Core.Cartridge;
using NESEmulator.Core.Input;
using NESEmulator.Core.Timing;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NESEmulator.Core
{
    public class NES
    {
        private CPU6502 cpu;
        private PPU2C02 ppu;
        private APU2A03 apu;
        private MemoryBus memoryBus;
        private ICartridge? cartridge;
        private Controller controller1;
        private Controller controller2;
        
        private int totalCycles;
        private bool isRunning;
        private HighPrecisionTimer? precisionTimer;
        private ClockSynchronizer? clockSynchronizer;

        #region debug-point infra
        private static readonly HttpClient DebugHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
        private static readonly string DebugEnvPath = Path.Combine(".dbg", "audio-interrupt-fps.env");
        private static readonly string? DebugServerUrl = TryReadDebugEnv("DEBUG_SERVER_URL");
        private static readonly string DebugSessionId = TryReadDebugEnv("DEBUG_SESSION_ID") ?? "audio-interrupt-fps";
        private static int debugFrameBreakdownLogCount;

        private static string? TryReadDebugEnv(string key)
        {
            try
            {
                if (!File.Exists(DebugEnvPath))
                {
                    return null;
                }

                foreach (string line in File.ReadAllLines(DebugEnvPath))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line[(key.Length + 1)..].Trim();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void DebugReport(string runId, string hypothesisId, string location, string msg, object data)
        {
            if (string.IsNullOrWhiteSpace(DebugServerUrl))
            {
                return;
            }

            try
            {
                string payload = JsonSerializer.Serialize(new
                {
                    sessionId = DebugSessionId,
                    runId,
                    hypothesisId,
                    location,
                    msg = "[DEBUG] " + msg,
                    data,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                _ = DebugHttpClient.PostAsync(DebugServerUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch
            {
            }
        }
        #endregion

        public NES()
        {
            // 初始化各个组件
            memoryBus = new MemoryBus();
            cpu = new CPU6502(memoryBus);
            ppu = new PPU2C02(memoryBus);
            apu = new APU2A03();
            controller1 = new Controller();
            controller2 = new Controller();
            
            // 连接组件
            memoryBus.ConnectCPU(cpu);
            memoryBus.ConnectPPU(ppu);
            memoryBus.ConnectAPU(apu);
            apu.ConnectConsole(memoryBus, cpu);
            memoryBus.ConnectController(0, controller1);
            memoryBus.ConnectController(1, controller2);
            
            totalCycles = 0;
            isRunning = false;
        }

        public void LoadCartridge(byte[] romData)
        {
            cartridge = CartridgeFactory.CreateCartridge(romData);
            memoryBus.ConnectCartridge(cartridge);
            ppu.ConnectCartridge(cartridge);
        }

        public void Reset()
        {
            cpu.Reset();
            ppu.Reset();
            apu.Reset();
            memoryBus.Reset();
            totalCycles = 0;
            precisionTimer?.Reset();
            clockSynchronizer?.Reset();
        }

        public void RunFrame()
        {
            if (!isRunning || cartridge == null)
            {
                return;
            }

            int instructionCount = 0;
            int maxInstructions = 100000;
            ppu.ClearFrameComplete();

            // 以PPU真正进入VBlank作为帧结束，避免固定CPU周期导致的帧边界漂移。
            while (!ppu.IsFrameComplete() && instructionCount < maxInstructions)
            {
                int cpuCycles = cpu.ExecuteInstruction();
                instructionCount++;
                for (int i = 0; i < cpuCycles * 3; i++)
                {
                    ppu.Clock();
                    if (ppu.IsNMITriggered())
                    {
                        cpu.TriggerNMI();
                        ppu.ClearNMI();
                    }
                }

                for (int i = 0; i < cpuCycles; i++)
                {
                    apu.Clock();
                }

                totalCycles += cpuCycles;
            }

            while (!ppu.IsFrameComplete() && instructionCount < maxInstructions)
            {
                int cpuCycles = cpu.ExecuteInstruction();
                instructionCount++;
                for (int i = 0; i < cpuCycles * 3; i++)
                {
                    ppu.Clock();
                    if (ppu.IsNMITriggered())
                    {
                        cpu.TriggerNMI();
                        ppu.ClearNMI();
                    }
                }

                for (int i = 0; i < cpuCycles; i++)
                {
                    apu.Clock();
                }

                totalCycles += cpuCycles;
            }

            ppu.ClearFrameComplete();
            apu.FlushAudio();
        }

        public uint[] GetFrameBuffer()
        {
            return ppu.GetFrameBuffer();
        }
        
        public PPU2C02 GetPPU()
        {
            return ppu;
        }

        public CPU6502 GetCPU()
        {
            return cpu;
        }
        
        public MemoryBus GetMemoryBus()
        {
            return memoryBus;
        }
        
        public ICartridge? GetCartridge()
        {
            return cartridge;
        }

        public void SetControllerButton(int controller, ControllerButton button, bool pressed)
        {
            if (controller == 0)
                controller1.SetButton(button, pressed);
            else if (controller == 1)
                controller2.SetButton(button, pressed);

        }

        public void SetMasterVolume(float volume)
        {
            apu.SetMasterVolume(volume);
        }

        public float GetMasterVolume()
        {
            return apu.GetMasterVolume();
        }

        public void Start()
        {
            isRunning = true;
        }
        
        public void Stop()
        {
            isRunning = false;
        }
        
        /// <summary>
        /// 设置高精度定时器（由MainWindow调用）
        /// </summary>
        public void SetPrecisionTimer(HighPrecisionTimer timer, ClockSynchronizer synchronizer)
        {
            precisionTimer = timer;
            clockSynchronizer = synchronizer;
        }
        
        /// <summary>
        /// 基于高精度定时器的帧运行方法
        /// 优化版本：减少不必要的时钟同步检查以提高性能
        /// </summary>
        public void RunFrameWithPrecisionTiming()
        {
            if (precisionTimer == null || clockSynchronizer == null)
            {
                // 回退到标准方法
                RunFrame();
                return;
            }
            
            clockSynchronizer.StartFrame();
            ppu.ClearFrameComplete();
            
            int frameCycles = 0;
            int instructionCount = 0;
            int nmiCount = 0;
            const int maxInstructions = 100000;
            long frameStartTicks = Stopwatch.GetTimestamp();
            long cpuTicks = 0;
            long ppuTicks = 0;
            long apuTicks = 0;
            
            // 以 PPU 真正完成一帧作为唯一正常退出条件，避免固定 CPU 周期上限把一帧切成两段。
            while (!ppu.IsFrameComplete() && instructionCount < maxInstructions)
            {
                instructionCount++;
                
                // 执行CPU指令
                long cpuStartTicks = Stopwatch.GetTimestamp();
                int cpuCycles = cpu.ExecuteInstruction();
                cpuTicks += Stopwatch.GetTimestamp() - cpuStartTicks;
                
                // PPU运行3倍CPU周期数
                long ppuStartTicks = Stopwatch.GetTimestamp();
                for (int i = 0; i < cpuCycles * 3; i++)
                {
                    ppu.Clock();
                    
                    // 检查PPU是否触发NMI中断
                    if (ppu.IsNMITriggered())
                    {
                        cpu.TriggerNMI();
                        ppu.ClearNMI();
                        nmiCount++;
                    }
                }
                ppuTicks += Stopwatch.GetTimestamp() - ppuStartTicks;
                
                // APU运行与CPU相同的周期数
                long apuStartTicks = Stopwatch.GetTimestamp();
                for (int i = 0; i < cpuCycles; i++)
                {
                    apu.Clock();
                }
                apuTicks += Stopwatch.GetTimestamp() - apuStartTicks;
                
                frameCycles += cpuCycles;
                totalCycles += cpuCycles;
            }

            ppu.ClearFrameComplete();
            apu.FlushAudio();
            
            clockSynchronizer.EndFrame();

            #region debug-point E:frame-breakdown
            if (debugFrameBreakdownLogCount < 40)
            {
                debugFrameBreakdownLogCount++;
                long frameElapsedTicks = Stopwatch.GetTimestamp() - frameStartTicks;
                DebugReport("post-fix", "A", "NES.RunFrameWithPrecisionTiming", "frame timing breakdown", new
                {
                    frameCycles,
                    instructionCount,
                    nmiCount,
                    frameMs = frameElapsedTicks * 1000.0 / Stopwatch.Frequency,
                    cpuMs = cpuTicks * 1000.0 / Stopwatch.Frequency,
                    ppuMs = ppuTicks * 1000.0 / Stopwatch.Frequency,
                    apuMs = apuTicks * 1000.0 / Stopwatch.Frequency
                });
            }
            #endregion
            
            // 输出性能统计（每60帧输出一次）
            if (totalCycles % (29780 * 60) == 0)
            {
                var stats = clockSynchronizer.GetPerformanceStats();
                var cpuStats = cpu.GetClockStats();
                var ppuStats = ppu.GetClockStats();
                var apuStats = apu.GetClockStats();
                
                // Console.WriteLine($"Precision Timing Stats - Total: {totalCycles}, Frame: {frameCycles}");
                // Console.WriteLine($"Frame Time: {stats.AverageFrameTime:F2}ms, CPU Utilization: {stats.CpuUtilization:F1}%, PPU Utilization: {stats.PpuUtilization:F1}%");
                // Console.WriteLine($"CPU: {cpuStats}");
                // Console.WriteLine($"PPU: {ppuStats}");
                // Console.WriteLine($"APU: {apuStats}");
            }
        }
        
        public void Pause()
        {
            isRunning = false;
        }
        
        public void Resume()
        {
            isRunning = true;
        }

        public bool IsRunning => isRunning;
        public int TotalCycles => totalCycles;
    }
}
