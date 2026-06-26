using NESEmulator.Core.Cartridge;

namespace NESEmulator.Core.Cartridge
{
    /// <summary>
    /// Mapper 023 (VRC4e) - Konami VRC4映射器
    /// 支持的游戏：Contra Force, Gradius II等
    /// </summary>
    public class Mapper023 : ICartridge
    {
        private readonly INESHeader header;
        private readonly byte[] prgROM;
        private readonly byte[] chrROM;
        private readonly byte[] sram;
        private readonly byte[] prgRAM;
        
        // VRC4寄存器
        private int[] prgBanks = new int[3]; // PRG银行选择
        private int[] chrBanks = new int[8]; // CHR银行选择
        private MirrorMode currentMirrorMode;
        private bool irqEnabled;
        private int irqCounter;
        private int irqLatch;
        private bool irqMode; // false = scanline mode, true = cycle mode
        
        public int MapperNumber => 23;
        public MirrorMode MirrorMode => currentMirrorMode;
        public bool HasBatteryBackedSRAM { get; private set; }
        public int PRGROMSize { get; private set; }
        public int CHRROMSize { get; private set; }

        public Mapper023(INESHeader header, byte[] romData)
        {
            this.header = header;
            this.currentMirrorMode = header.MirrorMode;
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
            prgRAM = new byte[8192]; // 8KB PRG RAM
            
            // 初始化银行
            InitializeBanks();
        }
        
        private void InitializeBanks()
        {
            // 初始PRG银行设置
            prgBanks[0] = 0; // $8000-$9FFF
            prgBanks[1] = 1; // $A000-$BFFF  
            prgBanks[2] = (prgROM.Length / 8192) - 1; // $E000-$FFFF (最后一个银行)
            
            // 初始CHR银行设置
            for (int i = 0; i < 8; i++)
            {
                chrBanks[i] = i;
            }
        }

        public byte ReadPRG(ushort address)
        {
            if (address >= 0x6000 && address < 0x8000)
            {
                // PRG RAM区域
                return prgRAM[address - 0x6000];
            }
            else if (address >= 0x8000 && address < 0xA000)
            {
                // $8000-$9FFF: 可切换银行0
                int bank = prgBanks[0] % (prgROM.Length / 8192);
                int bankOffset = bank * 8192;
                return prgROM[bankOffset + (address - 0x8000)];
            }
            else if (address >= 0xA000 && address < 0xC000)
            {
                // $A000-$BFFF: 可切换银行1
                int bank = prgBanks[1] % (prgROM.Length / 8192);
                int bankOffset = bank * 8192;
                return prgROM[bankOffset + (address - 0xA000)];
            }
            else if (address >= 0xC000 && address < 0xE000)
            {
                // $C000-$DFFF: 固定为倒数第二个银行
                int bankOffset = ((prgROM.Length / 8192) - 2) * 8192;
                if (bankOffset < 0) bankOffset = 0;
                return prgROM[bankOffset + (address - 0xC000)];
            }
            else if (address >= 0xE000)
            {
                // $E000-$FFFF: 固定为最后一个银行
                int bank = prgBanks[2] % (prgROM.Length / 8192);
                int bankOffset = bank * 8192;
                return prgROM[bankOffset + (address - 0xE000)];
            }
            
            return 0;
        }

        public void WritePRG(ushort address, byte value)
        {
            if (address >= 0x6000 && address < 0x8000)
            {
                // PRG RAM区域
                prgRAM[address - 0x6000] = value;
                return;
            }
            
            // VRC4寄存器写入
            // VRC4使用A0和A1位来选择寄存器
            int reg = ((address >> 1) & 0x01) | ((address << 1) & 0x02);
            
            switch (address & 0xF000)
            {
                case 0x8000:
                    // PRG银行选择
                    prgBanks[0] = value & 0x1F;
                    break;
                    
                case 0x9000:
                    if (reg == 0)
                    {
                        // 镜像控制
                        switch (value & 0x03)
                        {
                            case 0: currentMirrorMode = MirrorMode.Vertical; break;
                            case 1: currentMirrorMode = MirrorMode.Horizontal; break;
                            case 2: currentMirrorMode = MirrorMode.SingleScreenLower; break;
                            case 3: currentMirrorMode = MirrorMode.SingleScreenUpper; break;
                        }
                    }
                    break;
                    
                case 0xA000:
                    // PRG银行选择
                    prgBanks[1] = value & 0x1F;
                    break;
                    
                case 0xB000:
                case 0xC000:
                case 0xD000:
                case 0xE000:
                    // CHR银行选择
                    int chrBank = ((address - 0xB000) >> 12) * 2 + reg;
                    if (chrBank < 8)
                    {
                        chrBanks[chrBank] = value;
                    }
                    break;
                    
                case 0xF000:
                    if (reg == 0)
                    {
                        // IRQ Latch
                        irqLatch = value;
                    }
                    else if (reg == 1)
                    {
                        // IRQ Control
                        irqEnabled = (value & 0x02) != 0;
                        irqMode = (value & 0x04) != 0;
                        if (irqEnabled)
                        {
                            irqCounter = irqLatch;
                        }
                    }
                    break;
            }
        }

        public byte ReadCHR(ushort address)
        {
            if (chrROM.Length == 0) return 0;
            
            // CHR银行切换 (1KB银行)
            int bank = address / 1024;
            int offset = address % 1024;
            
            if (bank < 8)
            {
                int bankOffset = chrBanks[bank] * 1024;
                if (bankOffset + offset < chrROM.Length)
                {
                    return chrROM[bankOffset + offset];
                }
            }
            
            return 0;
        }

        public void WriteCHR(ushort address, byte value)
        {
            if (header.CHRROMSize == 0) // CHR RAM
            {
                int bank = address / 1024;
                int offset = address % 1024;
                
                if (bank < 8)
                {
                    int bankOffset = chrBanks[bank] * 1024;
                    if (bankOffset + offset < chrROM.Length)
                    {
                        chrROM[bankOffset + offset] = value;
                    }
                }
            }
        }

        public void Reset()
        {
            InitializeBanks();
            irqEnabled = false;
            irqCounter = 0;
            irqLatch = 0;
            irqMode = false;
        }

        public bool IRQActive()
        {
            return false; // 简化实现，暂不支持IRQ
        }

        public void IRQClear()
        {
            // IRQ清除
        }

        public void ScanlineIRQ()
        {
            // 扫描线IRQ处理（简化实现）
            if (irqEnabled && !irqMode)
            {
                if (irqCounter == 0)
                {
                    irqCounter = irqLatch;
                }
                else
                {
                    irqCounter--;
                }
            }
        }

        public byte[] GetSRAM()
        {
            return HasBatteryBackedSRAM ? sram : null;
        }

        public void LoadSRAM(byte[] data)
        {
            if (HasBatteryBackedSRAM && data != null && data.Length == sram.Length)
            {
                Array.Copy(data, sram, sram.Length);
            }
        }

        public byte ReadSRAM(ushort address)
        {
            if (address < sram.Length)
            {
                return sram[address];
            }
            return 0;
        }

        public void WriteSRAM(ushort address, byte value)
        {
            if (address < sram.Length)
            {
                sram[address] = value;
            }
        }
    }
}