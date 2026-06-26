using System.Diagnostics;

namespace NESEmulator.Core.Timing
{
    /// <summary>
    /// 高精度定时器类，基于System.Diagnostics.Stopwatch实现
    /// 参考Fami项目的定时器方案，提供纳秒级精度的时钟控制
    /// </summary>
    public class HighPrecisionTimer
    {
        private readonly Stopwatch stopwatch;
        private long lastFrameTime;
        private long targetFrameTime;
        private double frameTimeNs;
        private double cpuCycleTimeNs;
        private double ppuCycleTimeNs;
        private double apuCycleTimeNs;
        private double speedMultiplier;
        
        // NES标准时序常量
        private const double NES_CPU_FREQUENCY = 1789773.0; // Hz
        private const double NES_PPU_FREQUENCY = NES_CPU_FREQUENCY * 3.0; // PPU运行在CPU频率的3倍
        private const double NES_APU_FREQUENCY = NES_CPU_FREQUENCY; // APU与CPU同频率
        private const double TARGET_FPS = 60.0988; // NTSC标准帧率
        
        // 时钟精度统计
        private long totalFrames;
        private double averageFrameTime;
        private double frameTimeVariance;
        
        public HighPrecisionTimer()
        {
            stopwatch = Stopwatch.StartNew();
            speedMultiplier = 1.0;
            RecalculateTimingConstants();
            lastFrameTime = stopwatch.ElapsedTicks;
            
            totalFrames = 0;
            averageFrameTime = 0;
            frameTimeVariance = 0;
        }

        public void SetSpeedMultiplier(double multiplier)
        {
            speedMultiplier = Math.Clamp(multiplier, 0.85, 1.15);
            RecalculateTimingConstants();
        }

        public double GetSpeedMultiplier()
        {
            return speedMultiplier;
        }
        
        /// <summary>
        /// 获取当前高精度时间戳（纳秒）
        /// </summary>
        public long GetCurrentTimeNs()
        {
            return (long)(stopwatch.ElapsedTicks * 1_000_000_000.0 / Stopwatch.Frequency);
        }
        
        /// <summary>
        /// 获取当前高精度时间戳（Stopwatch Ticks）
        /// </summary>
        public long GetCurrentTicks()
        {
            return stopwatch.ElapsedTicks;
        }
        
        /// <summary>
        /// 等待到下一帧时间
        /// 使用高精度自旋等待确保准确的帧率控制
        /// </summary>
        public void WaitForNextFrame()
        {
            long currentTime = stopwatch.ElapsedTicks;
            long nextFrameTime = lastFrameTime + targetFrameTime;
            
            // 如果还没到下一帧时间，进行精确等待
            if (currentTime < nextFrameTime)
            {
                // Windows 上 Thread.Sleep 很容易过度睡眠到下一个调度片，导致 60Hz 节拍抖动成 16ms/30ms。
                // 这里改用 yield + 短自旋的混合等待，优先保证帧节奏稳定。
                while (true)
                {
                    currentTime = stopwatch.ElapsedTicks;
                    long remainingTicks = nextFrameTime - currentTime;
                    if (remainingTicks <= 0)
                    {
                        break;
                    }

                    if (remainingTicks > Stopwatch.Frequency / 250) // > 4ms
                    {
                        Thread.Sleep(0);
                    }
                    else if (remainingTicks > Stopwatch.Frequency / 2000) // > 0.5ms
                    {
                        Thread.Yield();
                    }
                    else
                    {
                        Thread.SpinWait(64);
                    }
                }
            }
            
            // 更新帧时间统计
            long actualFrameTime = stopwatch.ElapsedTicks - lastFrameTime;
            UpdateFrameTimeStatistics(actualFrameTime);
            
            lastFrameTime = stopwatch.ElapsedTicks;
        }
        
        /// <summary>
        /// 检查是否应该执行下一帧
        /// </summary>
        public bool ShouldExecuteFrame()
        {
            long currentTime = stopwatch.ElapsedTicks;
            return currentTime >= lastFrameTime + targetFrameTime;
        }
        
        /// <summary>
        /// 计算CPU周期的精确时间间隔
        /// </summary>
        public double GetCpuCycleTimeNs()
        {
            return cpuCycleTimeNs;
        }
        
        /// <summary>
        /// 计算PPU周期的精确时间间隔
        /// </summary>
        public double GetPpuCycleTimeNs()
        {
            return ppuCycleTimeNs;
        }
        
        /// <summary>
        /// 计算APU周期的精确时间间隔
        /// </summary>
        public double GetApuCycleTimeNs()
        {
            return apuCycleTimeNs;
        }
        
        /// <summary>
        /// 获取目标帧时间（纳秒）
        /// </summary>
        public double GetTargetFrameTimeNs()
        {
            return frameTimeNs;
        }
        
        /// <summary>
        /// 获取实际帧率
        /// </summary>
        public double GetActualFPS()
        {
            if (averageFrameTime <= 0) return 0;
            return Stopwatch.Frequency / averageFrameTime;
        }
        
        /// <summary>
        /// 获取帧时间方差（用于评估时钟稳定性）
        /// </summary>
        public double GetFrameTimeVariance()
        {
            return frameTimeVariance;
        }
        
        /// <summary>
        /// 获取定时器精度信息
        /// </summary>
        public TimerInfo GetTimerInfo()
        {
            return new TimerInfo
            {
                IsHighResolution = Stopwatch.IsHighResolution,
                Frequency = Stopwatch.Frequency,
                ResolutionNs = 1_000_000_000.0 / Stopwatch.Frequency,
                ActualFPS = GetActualFPS(),
                FrameTimeVariance = GetFrameTimeVariance(),
                TotalFrames = totalFrames
            };
        }
        
        /// <summary>
        /// 重置定时器
        /// </summary>
        public void Reset()
        {
            stopwatch.Restart();
            lastFrameTime = 0;
            totalFrames = 0;
            averageFrameTime = 0;
            frameTimeVariance = 0;
        }

        private void RecalculateTimingConstants()
        {
            double adjustedFps = TARGET_FPS * speedMultiplier;
            frameTimeNs = 1_000_000_000.0 / adjustedFps;
            cpuCycleTimeNs = 1_000_000_000.0 / NES_CPU_FREQUENCY;
            ppuCycleTimeNs = 1_000_000_000.0 / NES_PPU_FREQUENCY;
            apuCycleTimeNs = 1_000_000_000.0 / NES_APU_FREQUENCY;
            targetFrameTime = (long)(frameTimeNs * Stopwatch.Frequency / 1_000_000_000.0);
        }
        
        private void UpdateFrameTimeStatistics(long frameTime)
        {
            totalFrames++;
            
            // 使用指数移动平均计算平均帧时间
            double alpha = 0.1; // 平滑因子
            if (totalFrames == 1)
            {
                averageFrameTime = frameTime;
            }
            else
            {
                double oldAverage = averageFrameTime;
                averageFrameTime = alpha * frameTime + (1 - alpha) * averageFrameTime;
                
                // 计算方差
                double delta = frameTime - oldAverage;
                frameTimeVariance = (1 - alpha) * (frameTimeVariance + alpha * delta * delta);
            }
        }
    }
    
    /// <summary>
    /// 定时器信息结构
    /// </summary>
    public struct TimerInfo
    {
        public bool IsHighResolution { get; set; }
        public long Frequency { get; set; }
        public double ResolutionNs { get; set; }
        public double ActualFPS { get; set; }
        public double FrameTimeVariance { get; set; }
        public long TotalFrames { get; set; }
    }
}
