namespace NESEmulator.Core.Cartridge
{
    public static class CartridgeFactory
    {
        public static ICartridge CreateCartridge(byte[] romData)
        {
            if (romData.Length < 16)
                throw new ArgumentException("ROM文件太小，无法包含有效的iNES头部");
            
            // 检查iNES头部标识
            if (romData[0] != 0x4E || romData[1] != 0x45 || romData[2] != 0x53 || romData[3] != 0x1A)
                throw new ArgumentException("无效的iNES ROM文件格式");
            
            // 解析iNES头部
            var header = ParseINESHeader(romData);
            
            // 根据映射器编号创建相应的卡带
            return header.MapperNumber switch
            {
                0 => new Mapper000(header, romData),
                1 => new Mapper001(header, romData),
                2 => new Mapper002(header, romData),
                3 => new Mapper003(header, romData),
                23 => new Mapper023(header, romData),
                _ => throw new NotSupportedException($"不支持的映射器: {header.MapperNumber}")
            };
        }
        
        private static INESHeader ParseINESHeader(byte[] romData)
        {
            var header = new INESHeader
            {
                PRGROMSize = romData[4], // 16KB单位
                CHRROMSize = romData[5] // 8KB单位
            };

            byte flags6 = romData[6];
            byte flags7 = romData[7];
            
            // 镜像模式
            if ((flags6 & 0x08) != 0)
                header.MirrorMode = MirrorMode.FourScreen;
            else if ((flags6 & 0x01) != 0)
                header.MirrorMode = MirrorMode.Vertical;
            else
                header.MirrorMode = MirrorMode.Horizontal;
            
            // 电池备份SRAM
            header.HasBatteryBackedSRAM = (flags6 & 0x02) != 0;
            
            // 训练器
            header.HasTrainer = (flags6 & 0x04) != 0;
            
            // 映射器编号
            header.MapperNumber = (flags6 >> 4) | (flags7 & 0xF0);
            
            // iNES 2.0格式检查
            if ((flags7 & 0x0C) == 0x08)
            {
                // iNES 2.0格式
                header.IsNES2Format = true;
            }
            
            return header;
        }
    }
    
    public class INESHeader
    {
        public int PRGROMSize { get; set; }
        public int CHRROMSize { get; set; }
        public MirrorMode MirrorMode { get; set; }
        public bool HasBatteryBackedSRAM { get; set; }
        public bool HasTrainer { get; set; }
        public int MapperNumber { get; set; }
        public bool IsNES2Format { get; set; }
    }
}