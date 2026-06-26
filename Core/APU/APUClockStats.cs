using System;

namespace NESEmulator.Core.APU
{
    /// <summary>
    /// APU时钟统计信息
    /// 用于高精度时序控制和性能监控
    /// </summary>
    public struct APUClockStats
    {
        /// <summary>
        /// 总周期数
        /// </summary>
        public long TotalCycles { get; set; }
        
        /// <summary>
        /// 帧时钟计数器
        /// </summary>
        public int FrameClock { get; set; }
        
        /// <summary>
        /// 采样时钟计数器
        /// </summary>
        public int SampleClock { get; set; }
        
        /// <summary>
        /// 音频缓冲区索引
        /// </summary>
        public int BufferIndex { get; set; }
        
        /// <summary>
        /// 最后一次时钟执行时间（Stopwatch.GetTimestamp()）
        /// </summary>
        public long LastClockTime { get; set; }
        
        /// <summary>
        /// 周期累加器（用于精确时序控制）
        /// </summary>
        public double CycleAccumulator { get; set; }
        
        /// <summary>
        /// Pulse1通道是否启用
        /// </summary>
        public bool Pulse1Enabled { get; set; }
        
        /// <summary>
        /// Pulse2通道是否启用
        /// </summary>
        public bool Pulse2Enabled { get; set; }
        
        /// <summary>
        /// Triangle通道是否启用
        /// </summary>
        public bool TriangleEnabled { get; set; }
        
        /// <summary>
        /// Noise通道是否启用
        /// </summary>
        public bool NoiseEnabled { get; set; }
        
        /// <summary>
        /// DMC通道是否启用
        /// </summary>
        public bool DMCEnabled { get; set; }
        
        /// <summary>
        /// 获取格式化的统计信息字符串
        /// </summary>
        public override string ToString()
        {
            return $"APU Stats - Total Cycles: {TotalCycles}, Frame Clock: {FrameClock}, Sample Clock: {SampleClock}, Buffer Index: {BufferIndex}";
        }
    }
}
