using System;

namespace NESEmulator.Core.CPU
{
    /// <summary>
    /// CPU时钟统计信息
    /// 用于高精度时序控制和性能监控
    /// </summary>
    public struct CPUClockStats
    {
        /// <summary>
        /// 总指令数
        /// </summary>
        public long TotalInstructions { get; set; }
        
        /// <summary>
        /// 总周期数
        /// </summary>
        public int TotalCycles { get; set; }
        
        /// <summary>
        /// 程序计数器
        /// </summary>
        public ushort ProgramCounter { get; set; }
        
        /// <summary>
        /// 累加器
        /// </summary>
        public byte Accumulator { get; set; }
        
        /// <summary>
        /// X索引寄存器
        /// </summary>
        public byte IndexX { get; set; }
        
        /// <summary>
        /// Y索引寄存器
        /// </summary>
        public byte IndexY { get; set; }
        
        /// <summary>
        /// 堆栈指针
        /// </summary>
        public byte StackPointer { get; set; }
        
        /// <summary>
        /// 状态寄存器
        /// </summary>
        public byte StatusRegister { get; set; }
        
        /// <summary>
        /// 最后一次时钟执行时间（Stopwatch.GetTimestamp()）
        /// </summary>
        public long LastClockTime { get; set; }
        
        /// <summary>
        /// 周期累加器（用于精确时序控制）
        /// </summary>
        public double CycleAccumulator { get; set; }
        
        /// <summary>
        /// NMI中断是否挂起
        /// </summary>
        public bool NMIPending { get; set; }
        
        /// <summary>
        /// IRQ中断是否挂起
        /// </summary>
        public bool IRQPending { get; set; }
        
        /// <summary>
        /// 获取格式化的统计信息字符串
        /// </summary>
        public override string ToString()
        {
            return $"CPU Stats - Instructions: {TotalInstructions}, Cycles: {TotalCycles}, PC: 0x{ProgramCounter:X4}, A: 0x{Accumulator:X2}, X: 0x{IndexX:X2}, Y: 0x{IndexY:X2}";
        }
    }
}