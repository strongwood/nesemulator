namespace NESEmulator.Core.Cartridge
{
    /// <summary>
    /// Mapper 000 (NROM) - 最简单的映射器
    /// 支持的游戏：魂斗罗、超级玛丽、冰雪兄弟等
    /// </summary>
    public class Mapper000 : ICartridge
    {
        private readonly INESHeader header;
        private readonly byte[] prgROM;
        private readonly byte[] chrROM;
        private readonly byte[] sram;
        
        public int MapperNumber => 0;
        public MirrorMode MirrorMode { get; private set; }
        public bool HasBatteryBackedSRAM { get; private set; }
        public int PRGROMSize { get; private set; }
        public int CHRROMSize { get; private set; }

        public Mapper000(INESHeader header, byte[] romData)
        {
            this.header = header;
            this.MirrorMode = header.MirrorMode;
            this.HasBatteryBackedSRAM = header.HasBatteryBackedSRAM;
            this.PRGROMSize = header.PRGROMSize;
            this.CHRROMSize = header.CHRROMSize;
            
            // 计算数据偏移
            int offset = 16; // iNES头部大小
            
            if (header.HasTrainer)
                offset += 512; // 训练器大小
            
            // 加载PRG ROM
            int prgSize = header.PRGROMSize * 16384; // 16KB单位
            prgROM = new byte[prgSize];
            Array.Copy(romData, offset, prgROM, 0, prgSize);
            offset += prgSize;
            
            // 加载CHR ROM
            if (header.CHRROMSize > 0)
            {
                int chrSize = header.CHRROMSize * 8192; // 8KB单位
                chrROM = new byte[chrSize];
                Array.Copy(romData, offset, chrROM, 0, chrSize);
            }
            else
            {
                // CHR RAM
                chrROM = new byte[8192]; // 8KB CHR RAM
            }
            
            // 初始化SRAM
            sram = new byte[8192]; // 8KB SRAM
        }

        public byte ReadPRG(ushort address)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                // PRG ROM区域
                int prgAddress = address - 0x8000;
                
                if (prgROM.Length == 16384) // 16KB PRG ROM
                {
                    // 镜像到32KB空间
                    prgAddress %= 16384;
                }
                else if (prgROM.Length == 32768) // 32KB PRG ROM
                {
                    // 直接映射
                    prgAddress %= 32768;
                }
                
                if (prgAddress < prgROM.Length)
                    return prgROM[prgAddress];
            }
            
            return 0;
        }

        public void WritePRG(ushort address, byte value)
        {
            // NROM映射器的PRG ROM是只读的
            // 某些游戏可能会写入，但会被忽略
        }

        public byte ReadCHR(ushort address)
        {
            if (address < 0x2000)
            {
                // CHR ROM/RAM区域
                if (address < chrROM.Length)
                    return chrROM[address];
            }
            
            return 0;
        }

        public void WriteCHR(ushort address, byte value)
        {
            if (address < 0x2000)
            {
                // 如果是CHR RAM，允许写入
                if (header.CHRROMSize == 0 && address < chrROM.Length)
                {
                    chrROM[address] = value;
                }
                // CHR ROM是只读的
            }
        }

        public byte ReadSRAM(ushort address)
        {
            if (address < sram.Length)
                return sram[address];
            
            return 0;
        }

        public void WriteSRAM(ushort address, byte value)
        {
            if (address < sram.Length)
                sram[address] = value;
        }
    }
}