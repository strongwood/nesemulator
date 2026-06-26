using NESEmulator.Core.Cartridge;

namespace NESEmulator.Core.Cartridge
{
    /// <summary>
    /// Mapper 002 (UxROM) - 支持PRG银行切换的映射器
    /// 支持的游戏：魂斗罗、洛克人、恶魔城等
    /// </summary>
    public class Mapper002 : ICartridge
    {
        private readonly INESHeader header;
        private readonly byte[] prgROM;
        private readonly byte[] chrROM;
        private readonly byte[] sram;
        
        private byte prgBankSelect = 0;
        
        public int MapperNumber => 2;
        public MirrorMode MirrorMode { get; private set; }
        public bool HasBatteryBackedSRAM { get; private set; }
        public int PRGROMSize { get; private set; }
        public int CHRROMSize { get; private set; }

        public Mapper002(INESHeader header, byte[] romData)
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
            if (address >= 0x8000 && address < 0xC000)
            {
                // $8000-$BFFF: 可切换的16KB PRG ROM银行
                int bankOffset = prgBankSelect * 16384;
                int prgAddress = bankOffset + (address - 0x8000);
                
                if (prgAddress < prgROM.Length)
                    return prgROM[prgAddress];
            }
            else if (address >= 0xC000)
            {
                // $C000-$FFFF: 固定为最后一个16KB PRG ROM银行
                int lastBankOffset = prgROM.Length - 16384;
                int prgAddress = lastBankOffset + (address - 0xC000);
                
                if (prgAddress < prgROM.Length)
                    return prgROM[prgAddress];
            }
            
            return 0;
        }

        public void WritePRG(ushort address, byte value)
        {
            if (address >= 0x8000)
            {
                // PRG银行选择
                prgBankSelect = (byte)(value & 0x0F); // 只使用低4位
            }
        }

        public byte ReadCHR(ushort address)
        {
            if (address < 0x2000 && address < chrROM.Length)
                return chrROM[address];
            return 0;
        }

        public void WriteCHR(ushort address, byte value)
        {
            if (address < 0x2000 && header.CHRROMSize == 0 && address < chrROM.Length)
            {
                // CHR RAM写入
                chrROM[address] = value;
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