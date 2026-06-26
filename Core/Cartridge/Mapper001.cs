namespace NESEmulator.Core.Cartridge
{
    /// <summary>
    /// Mapper 001 (MMC1) - 支持银行切换的映射器
    /// 支持的游戏：塞尔达传说、恶魔城2、最终幻想等
    /// </summary>
    public class Mapper001 : ICartridge
    {
        private readonly INESHeader header;
        private readonly byte[] prgROM;
        private readonly byte[] chrROM;
        private readonly byte[] sram;
        
        // MMC1寄存器
        private byte shiftRegister = 0x10;
        private byte control = 0x0C;
        private byte chrBank0 = 0;
        private byte chrBank1 = 0;
        private byte prgBank = 0;
        
        public int MapperNumber => 1;
        public MirrorMode MirrorMode { get; private set; }
        public bool HasBatteryBackedSRAM { get; private set; }
        public int PRGROMSize { get; private set; }
        public int CHRROMSize { get; private set; }

        public Mapper001(INESHeader header, byte[] romData)
        {
            this.header = header;
            this.MirrorMode = header.MirrorMode;
            this.HasBatteryBackedSRAM = header.HasBatteryBackedSRAM;
            this.PRGROMSize = header.PRGROMSize;
            this.CHRROMSize = header.CHRROMSize;
            
            // 计算数据偏移
            int offset = 16;
            if (header.HasTrainer) offset += 512;
            
            // 加载PRG ROM
            int prgSize = header.PRGROMSize * 16384;
            prgROM = new byte[prgSize];
            Array.Copy(romData, offset, prgROM, 0, prgSize);
            offset += prgSize;
            
            // 加载CHR ROM/RAM
            if (header.CHRROMSize > 0)
            {
                int chrSize = header.CHRROMSize * 8192;
                chrROM = new byte[chrSize];
                Array.Copy(romData, offset, chrROM, 0, chrSize);
            }
            else
            {
                chrROM = new byte[8192]; // CHR RAM
            }
            
            sram = new byte[8192];
        }

        public byte ReadPRG(ushort address)
        {
            if (address >= 0x8000 && address <= 0xFFFF)
            {
                int bank;
                int bankOffset = address & 0x3FFF;
                
                if (address < 0xC000)
                {
                    // 低16KB银行
                    if ((control & 0x08) != 0)
                    {
                        // 16KB模式
                        bank = prgBank & 0x0F;
                    }
                    else
                    {
                        // 32KB模式
                        bank = (prgBank & 0x0E);
                        if (address >= 0xC000) bankOffset += 0x4000;
                    }
                }
                else
                {
                    // 高16KB银行
                    if ((control & 0x08) != 0)
                    {
                        if ((control & 0x04) != 0)
                        {
                            // 固定最后一个银行
                            bank = (prgROM.Length / 16384) - 1;
                        }
                        else
                        {
                            // 可切换银行
                            bank = prgBank & 0x0F;
                        }
                    }
                    else
                    {
                        // 32KB模式
                        bank = (prgBank & 0x0E) + 1;
                        bankOffset = address & 0x3FFF;
                    }
                }
                
                int prgAddress = bank * 16384 + bankOffset;
                if (prgAddress < prgROM.Length)
                    return prgROM[prgAddress];
            }
            
            return 0;
        }

        public void WritePRG(ushort address, byte value)
        {
            if (address >= 0x8000)
            {
                if ((value & 0x80) != 0)
                {
                    // 重置移位寄存器
                    shiftRegister = 0x10;
                    control |= 0x0C;
                }
                else
                {
                    // 写入移位寄存器
                    bool complete = (shiftRegister & 1) != 0;
                    shiftRegister >>= 1;
                    shiftRegister |= (byte)((value & 1) << 4);
                    
                    if (complete)
                    {
                        WriteRegister(address, (byte)(shiftRegister & 0x1F));
                        shiftRegister = 0x10;
                    }
                }
            }
        }
        
        private void WriteRegister(ushort address, byte value)
        {
            switch ((address >> 13) & 3)
            {
                case 0: // Control
                    control = value;
                    UpdateMirrorMode();
                    break;
                case 1: // CHR bank 0
                    chrBank0 = value;
                    break;
                case 2: // CHR bank 1
                    chrBank1 = value;
                    break;
                case 3: // PRG bank
                    prgBank = value;
                    break;
            }
        }
        
        private void UpdateMirrorMode()
        {
            switch (control & 3)
            {
                case 0: MirrorMode = MirrorMode.SingleScreenLower; break;
                case 1: MirrorMode = MirrorMode.SingleScreenUpper; break;
                case 2: MirrorMode = MirrorMode.Vertical; break;
                case 3: MirrorMode = MirrorMode.Horizontal; break;
            }
        }

        public byte ReadCHR(ushort address)
        {
            if (address < 0x2000)
            {
                int bank;
                int bankOffset;
                
                if ((control & 0x10) != 0)
                {
                    // 4KB CHR银行模式
                    if (address < 0x1000)
                    {
                        bank = chrBank0;
                        bankOffset = address;
                    }
                    else
                    {
                        bank = chrBank1;
                        bankOffset = address - 0x1000;
                    }
                }
                else
                {
                    // 8KB CHR银行模式
                    bank = chrBank0 & 0xFE;
                    bankOffset = address;
                }
                
                int chrAddress = bank * 4096 + bankOffset;
                if (chrAddress < chrROM.Length)
                    return chrROM[chrAddress];
            }
            
            return 0;
        }

        public void WriteCHR(ushort address, byte value)
        {
            if (address < 0x2000 && header.CHRROMSize == 0)
            {
                // CHR RAM写入
                int bank;
                int bankOffset;
                
                if ((control & 0x10) != 0)
                {
                    if (address < 0x1000)
                    {
                        bank = chrBank0;
                        bankOffset = address;
                    }
                    else
                    {
                        bank = chrBank1;
                        bankOffset = address - 0x1000;
                    }
                }
                else
                {
                    bank = chrBank0 & 0xFE;
                    bankOffset = address;
                }
                
                int chrAddress = bank * 4096 + bankOffset;
                if (chrAddress < chrROM.Length)
                    chrROM[chrAddress] = value;
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