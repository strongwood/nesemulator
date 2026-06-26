using System;

namespace NESEmulator.Core.PPU
{
    /// <summary>
    /// PPU时钟统计信息
    /// 用于高精度时序控制和性能监控
    /// </summary>
    public struct PPUClockStats
    {
        /// <summary>
        /// 当前扫描线
        /// </summary>
        public int CurrentScanline { get; set; }
        
        /// <summary>
        /// 当前周期
        /// </summary>
        public int CurrentCycle { get; set; }
        
        /// <summary>
        /// 帧是否完成
        /// </summary>
        public bool FrameComplete { get; set; }
        
        /// <summary>
        /// NMI是否触发
        /// </summary>
        public bool NMITriggered { get; set; }
        
        /// <summary>
        /// 最后一次时钟执行时间（Stopwatch.GetTimestamp()）
        /// </summary>
        public long LastClockTime { get; set; }
        
        /// <summary>
        /// 周期累加器（用于精确时序控制）
        /// </summary>
        public double CycleAccumulator { get; set; }
        
        /// <summary>
        /// 获取格式化的统计信息字符串
        /// </summary>
        public override string ToString()
        {
            return $"PPU Stats - Scanline: {CurrentScanline}, Cycle: {CurrentCycle}, Frame Complete: {FrameComplete}, NMI: {NMITriggered}";
        }
    }
}