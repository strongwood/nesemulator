using System.Diagnostics;

namespace NESEmulator.Core.Timing
{
    /// <summary>
    /// 时钟同步管理器
    /// 负责协调CPU、PPU、APU之间的精确时序同步
    /// 基于Fami项目的时钟同步方案
    /// </summary>
    public class ClockSynchronizer
    {
        private readonly HighPrecisionTimer timer;
        private long lastCpuClock;
        private long lastPpuClock;
        private long lastApuClock;
        private long frameStartTime;
        
        // 累积的周期计数器（用于精确的时钟分频）
        private double cpuCycleAccumulator;
        private double ppuCycleAccumulator;
        private double apuCycleAccumulator;
        
        // NES标准时序常量
        private const int CPU_CYCLES_PER_FRAME = 29830; // NTSC: 1789773 / 60.0988
        private const int PPU_CYCLES_PER_FRAME = CPU_CYCLES_PER_FRAME * 3;
        private const int APU_CYCLES_PER_FRAME = CPU_CYCLES_PER_FRAME;
        
        // 性能统计
        private long totalCpuCycles;
        private long totalPpuCycles;
        private long totalApuCycles;
        private int currentFrameCpuCycles;
        private int currentFramePpuCycles;
        private int currentFrameApuCycles;
        
        public ClockSynchronizer(HighPrecisionTimer timer)
        {
            this.timer = timer;
            Reset();
        }
        
        /// <summary>
        /// 重置同步器状态
        /// </summary>
        public void Reset()
        {
            long currentTime = timer.GetCurrentTicks();
            lastCpuClock = currentTime;
            lastPpuClock = currentTime;
            lastApuClock = currentTime;
            frameStartTime = currentTime;
            
            cpuCycleAccumulator = 0;
            ppuCycleAccumulator = 0;
            apuCycleAccumulator = 0;
            
            totalCpuCycles = 0;
            totalPpuCycles = 0;
            totalApuCycles = 0;
            currentFrameCpuCycles = 0;
            currentFramePpuCycles = 0;
            currentFrameApuCycles = 0;
        }
        
        /// <summary>
        /// 开始新的一帧
        /// </summary>
        public void StartFrame()
        {
            frameStartTime = timer.GetCurrentTicks();
            currentFrameCpuCycles = 0;
            currentFramePpuCycles = 0;
            currentFrameApuCycles = 0;
        }
        
        /// <summary>
        /// 检查CPU是否应该执行下一个周期
        /// </summary>
        public bool ShouldExecuteCpuCycle()
        {
            if (currentFrameCpuCycles >= CPU_CYCLES_PER_FRAME)
                return false;
                
            long currentTime = timer.GetCurrentTicks();
            long elapsedTicks = currentTime - frameStartTime;
            double elapsedNs = elapsedTicks * 1_000_000_000.0 / Stopwatch.Frequency;
            
            // 计算应该执行的CPU周期数
            double targetCycles = elapsedNs / timer.GetCpuCycleTimeNs();
            
            return currentFrameCpuCycles < (int)targetCycles;
        }
        
        /// <summary>
        /// 检查PPU是否应该执行下一个周期
        /// </summary>
        public bool ShouldExecutePpuCycle()
        {
            if (currentFramePpuCycles >= PPU_CYCLES_PER_FRAME)
                return false;
                
            long currentTime = timer.GetCurrentTicks();
            long elapsedTicks = currentTime - frameStartTime;
            double elapsedNs = elapsedTicks * 1_000_000_000.0 / Stopwatch.Frequency;
            
            // 计算应该执行的PPU周期数
            double targetCycles = elapsedNs / timer.GetPpuCycleTimeNs();
            
            return currentFramePpuCycles < (int)targetCycles;
        }
        
        /// <summary>
        /// 检查APU是否应该执行下一个周期
        /// </summary>
        public bool ShouldExecuteApuCycle()
        {
            if (currentFrameApuCycles >= APU_CYCLES_PER_FRAME)
                return false;
                
            long currentTime = timer.GetCurrentTicks();
            long elapsedTicks = currentTime - frameStartTime;
            double elapsedNs = elapsedTicks * 1_000_000_000.0 / Stopwatch.Frequency;
            
            // 计算应该执行的APU周期数
            double targetCycles = elapsedNs / timer.GetApuCycleTimeNs();
            
            return currentFrameApuCycles < (int)targetCycles;
        }
        
        /// <summary>
        /// 记录CPU周期执行
        /// </summary>
        public void RecordCpuCycle(int cycles = 1)
        {
            currentFrameCpuCycles += cycles;
            totalCpuCycles += cycles;
            lastCpuClock = timer.GetCurrentTicks();
        }
        
        /// <summary>
        /// 记录PPU周期执行
        /// </summary>
        public void RecordPpuCycle(int cycles = 1)
        {
            currentFramePpuCycles += cycles;
            totalPpuCycles += cycles;
            lastPpuClock = timer.GetCurrentTicks();
        }
        
        /// <summary>
        /// 记录APU周期执行
        /// </summary>
        public void RecordApuCycle(int cycles = 1)
        {
            currentFrameApuCycles += cycles;
            totalApuCycles += cycles;
            lastApuClock = timer.GetCurrentTicks();
        }
        
        /// <summary>
        /// 获取当前帧的CPU周期数
        /// </summary>
        public int GetCurrentFrameCpuCycles()
        {
            return currentFrameCpuCycles;
        }
        
        /// <summary>
        /// 获取当前帧的PPU周期数
        /// </summary>
        public int GetCurrentFramePpuCycles()
        {
            return currentFramePpuCycles;
        }
        
        /// <summary>
        /// 获取当前帧的APU周期数
        /// </summary>
        public int GetCurrentFrameApuCycles()
        {
            return currentFrameApuCycles;
        }
        
        /// <summary>
        /// 检查当前帧是否完成
        /// </summary>
        public bool IsFrameComplete()
        {
            return currentFrameCpuCycles >= CPU_CYCLES_PER_FRAME;
        }
        
        /// <summary>
        /// 获取帧完成进度（0.0 - 1.0）
        /// </summary>
        public double GetFrameProgress()
        {
            return Math.Min(1.0, (double)currentFrameCpuCycles / CPU_CYCLES_PER_FRAME);
        }
        
        /// <summary>
        /// 结束帧同步
        /// </summary>
        public void EndFrame()
        {
            long currentTime = timer.GetCurrentTicks();
            if (frameStartTime > 0)
            {
                long frameTime = currentTime - frameStartTime;
                // 可以在这里添加帧时间统计逻辑
            }
        }
        
        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public PerformanceStats GetPerformanceStats()
        {
            long currentTime = timer.GetCurrentTicks();
            long frameElapsed = currentTime - frameStartTime;
            double frameTimeMs = frameElapsed * 1000.0 / Stopwatch.Frequency;
            
            return new PerformanceStats
            {
                CpuUtilization = (double)currentFrameCpuCycles / CPU_CYCLES_PER_FRAME * 100,
                PpuUtilization = (double)currentFramePpuCycles / PPU_CYCLES_PER_FRAME * 100,
                ApuUtilization = (double)currentFrameApuCycles / APU_CYCLES_PER_FRAME * 100,
                AverageFrameTime = frameTimeMs,
                FrameTimeVariance = 0, // 暂时设为0，后续可以添加统计逻辑
                TotalFrames = totalCpuCycles / CPU_CYCLES_PER_FRAME // 估算总帧数
            };
        }
        
        /// <summary>
        /// 获取同步统计信息
        /// </summary>
        public SyncStats GetSyncStats()
        {
            long currentTime = timer.GetCurrentTicks();
            long frameElapsed = currentTime - frameStartTime;
            double frameProgressTime = frameElapsed * 1_000_000_000.0 / Stopwatch.Frequency;
            
            return new SyncStats
            {
                TotalCpuCycles = totalCpuCycles,
                TotalPpuCycles = totalPpuCycles,
                TotalApuCycles = totalApuCycles,
                CurrentFrameCpuCycles = currentFrameCpuCycles,
                CurrentFramePpuCycles = currentFramePpuCycles,
                CurrentFrameApuCycles = currentFrameApuCycles,
                FrameProgressNs = frameProgressTime,
                FrameProgress = GetFrameProgress(),
                CpuUtilization = (double)currentFrameCpuCycles / CPU_CYCLES_PER_FRAME,
                PpuUtilization = (double)currentFramePpuCycles / PPU_CYCLES_PER_FRAME,
                ApuUtilization = (double)currentFrameApuCycles / APU_CYCLES_PER_FRAME
            };
        }
        
        /// <summary>
        /// 等待帧完成（如果提前完成）
        /// </summary>
        public void WaitForFrameCompletion()
        {
            long currentTime = timer.GetCurrentTicks();
            long frameElapsed = currentTime - frameStartTime;
            long targetFrameTime = (long)(timer.GetTargetFrameTimeNs() * Stopwatch.Frequency / 1_000_000_000.0);
            
            if (frameElapsed < targetFrameTime)
            {
                long waitTime = targetFrameTime - frameElapsed;
                
                // 对于较短的等待时间，使用自旋等待
                if (waitTime < Stopwatch.Frequency / 1000) // 小于1ms
                {
                    long targetTime = currentTime + waitTime;
                    while (timer.GetCurrentTicks() < targetTime)
                    {
                        Thread.SpinWait(1);
                    }
                }
                else
                {
                    // 对于较长的等待时间，使用Thread.Sleep
                    int sleepMs = (int)(waitTime * 1000 / Stopwatch.Frequency);
                    if (sleepMs > 0)
                    {
                        Thread.Sleep(sleepMs);
                    }
                }
            }
        }
    }
    
    /// <summary>
     /// 性能统计信息
     /// </summary>
     public struct PerformanceStats
     {
         public double CpuUtilization { get; set; }
         public double PpuUtilization { get; set; }
         public double ApuUtilization { get; set; }
         public double AverageFrameTime { get; set; }
         public double FrameTimeVariance { get; set; }
         public long TotalFrames { get; set; }
     }
    
    /// <summary>
    /// 同步统计信息
    /// </summary>
    public struct SyncStats
    {
        public long TotalCpuCycles { get; set; }
        public long TotalPpuCycles { get; set; }
        public long TotalApuCycles { get; set; }
        public int CurrentFrameCpuCycles { get; set; }
        public int CurrentFramePpuCycles { get; set; }
        public int CurrentFrameApuCycles { get; set; }
        public double FrameProgressNs { get; set; }
        public double FrameProgress { get; set; }
        public double CpuUtilization { get; set; }
        public double PpuUtilization { get; set; }
        public double ApuUtilization { get; set; }
    }
}