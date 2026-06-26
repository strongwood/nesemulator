using NESEmulator.Core.PPU;
using NESEmulator.Core.APU;
using NESEmulator.Core.Cartridge;
using NESEmulator.Core.CPU;
using NESEmulator.Core.Input;

namespace NESEmulator.Core.Memory
{
    public class MemoryBus
    {
        // 内部RAM (2KB)
        private byte[] ram = new byte[0x800];
        
        // 连接的组件
        private PPU2C02? ppu;
        private APU2A03? apu;
        private CPU6502? cpu;
        private ICartridge? cartridge;
        private Controller? controller1;
        private Controller? controller2;

        public MemoryBus()
        {
            Array.Clear(ram, 0, ram.Length);
        }

        public void ConnectPPU(PPU2C02 ppu)
        {
            this.ppu = ppu;
        }

        public void ConnectAPU(APU2A03 apu)
        {
            this.apu = apu;
        }

        public void ConnectCPU(CPU6502 cpu)
        {
            this.cpu = cpu;
        }

        public void ConnectCartridge(ICartridge cartridge)
        {
            this.cartridge = cartridge;
        }

        public void ConnectController(int port, Controller controller)
        {
            if (port == 0)
                controller1 = controller;
            else if (port == 1)
                controller2 = controller;
        }

        public byte Read(ushort address)
        {
            switch (address)
            {
                case < 0x2000:
                    // 内部RAM (0x0000-0x07FF) 及其镜像 (0x0800-0x1FFF)
                    return ram[address & 0x07FF];
                case < 0x4000:
                    // PPU寄存器 (0x2000-0x2007) 及其镜像 (0x2008-0x3FFF)
                    return ppu?.ReadRegister((ushort)(0x2000 + (address & 0x0007))) ?? 0;
                case < 0x4020:
                    // APU和I/O寄存器 (0x4000-0x401F)
                    switch (address)
                    {
                        case 0x4016: // 控制器1
                            return controller1?.Read() ?? 0;
                        case 0x4017: // 控制器2
                            return controller2?.Read() ?? 0;
                        default:
                            // APU寄存器
                            return apu?.ReadRegister(address) ?? 0;
                    }
                case < 0x6000:
                    // 扩展ROM区域 (0x4020-0x5FFF)
                    return cartridge?.ReadPRG(address) ?? 0;
                case < 0x8000:
                    // SRAM区域 (0x6000-0x7FFF)
                    return cartridge?.ReadSRAM((ushort)(address - 0x6000)) ?? 0;
                default:
                    // PRG ROM区域 (0x8000-0xFFFF)
                    return cartridge?.ReadPRG(address) ?? 0;
            }
        }

        public void Write(ushort address, byte value)
        {
            if (address < 0x2000)
            {
                // 内部RAM (0x0000-0x07FF) 及其镜像 (0x0800-0x1FFF)
                ram[address & 0x07FF] = value;
            }
            else if (address < 0x4000)
            {
                // PPU寄存器 (0x2000-0x2007) 及其镜像 (0x2008-0x3FFF)
                ppu?.WriteRegister((ushort)(0x2000 + (address & 0x0007)), value);
            }
            else if (address < 0x4020)
            {
                // APU和I/O寄存器 (0x4000-0x401F)
                switch (address)
                {
                    case 0x4014: // OAM DMA
                        PerformOAMDMA(value);
                        break;
                    case 0x4016: // 控制器选通
                        controller1?.Write(value);
                        controller2?.Write(value);
                        break;
                    default:
                        // APU寄存器
                        apu?.WriteRegister(address, value);
                        break;
                }
            }
            else if (address < 0x6000)
            {
                // 扩展ROM区域 (0x4020-0x5FFF)
                cartridge?.WritePRG(address, value);
            }
            else if (address < 0x8000)
            {
                // SRAM区域 (0x6000-0x7FFF)
                cartridge?.WriteSRAM((ushort)(address - 0x6000), value);
            }
            else
            {
                // PRG ROM区域 (0x8000-0xFFFF) - 通常只读，但某些映射器可能允许写入
                cartridge?.WritePRG(address, value);
            }
        }

        private void PerformOAMDMA(byte page)
        {
            // OAM DMA - 将256字节从CPU内存复制到PPU的OAM
            ushort sourceAddress = (ushort)(page << 8);
            
            for (int i = 0; i < 256; i++)
            {
                byte data = Read((ushort)(sourceAddress + i));
                ppu?.WriteRegister(0x2004, data); // OAMDATA寄存器
            }

            if (cpu != null)
            {
                int stallCycles = 513;
                if ((cpu.TotalCycles & 1) != 0)
                {
                    stallCycles++;
                }

                cpu.AddStallCycles(stallCycles);
            }
        }

        public void Reset()
        {
            Array.Clear(ram, 0, ram.Length);
        }

        // 调试用方法
        public byte[] GetRAM()
        {
            return (byte[])ram.Clone();
        }

        public void SetRAM(ushort address, byte value)
        {
            if (address < ram.Length)
            {
                ram[address] = value;
            }
        }
    }
}
