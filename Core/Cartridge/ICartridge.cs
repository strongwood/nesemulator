namespace NESEmulator.Core.Cartridge
{
    public interface ICartridge
    {
        /// <summary>
        /// 读取PRG ROM数据
        /// </summary>
        /// <param name="address">地址</param>
        /// <returns>数据</returns>
        byte ReadPRG(ushort address);
        
        /// <summary>
        /// 写入PRG ROM数据（某些映射器支持）
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">数据</param>
        void WritePRG(ushort address, byte value);
        
        /// <summary>
        /// 读取CHR ROM/RAM数据
        /// </summary>
        /// <param name="address">地址</param>
        /// <returns>数据</returns>
        byte ReadCHR(ushort address);
        
        /// <summary>
        /// 写入CHR ROM/RAM数据
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">数据</param>
        void WriteCHR(ushort address, byte value);
        
        /// <summary>
        /// 读取SRAM数据
        /// </summary>
        /// <param name="address">地址</param>
        /// <returns>数据</returns>
        byte ReadSRAM(ushort address);
        
        /// <summary>
        /// 写入SRAM数据
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">数据</param>
        void WriteSRAM(ushort address, byte value);
        
        /// <summary>
        /// 获取映射器编号
        /// </summary>
        int MapperNumber { get; }
        
        /// <summary>
        /// 获取镜像模式
        /// </summary>
        MirrorMode MirrorMode { get; }
        
        /// <summary>
        /// 是否有电池备份的SRAM
        /// </summary>
        bool HasBatteryBackedSRAM { get; }
        
        /// <summary>
        /// PRG ROM大小（16KB单位）
        /// </summary>
        int PRGROMSize { get; }
        
        /// <summary>
        /// CHR ROM大小（8KB单位）
        /// </summary>
        int CHRROMSize { get; }
    }
    
    public enum MirrorMode
    {
        Horizontal,
        Vertical,
        SingleScreenLower,
        SingleScreenUpper,
        FourScreen
    }
}