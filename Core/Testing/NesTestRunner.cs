using NESEmulator.Core.CPU;
using NESEmulator.Core.PPU;
using NESEmulator.Core.APU;
using NESEmulator.Core.Memory;
using NESEmulator.Core.Cartridge;
using NESEmulator.Core.Input;
using System.Text;
using System.IO;

namespace NESEmulator.Core.Testing
{
    public class NesTestRunner
    {
        private CPU6502 cpu;
        private PPU2C02 ppu;
        private APU2A03 apu;
        private MemoryBus memoryBus;
        private ICartridge? cartridge;
        private StringBuilder log;
        private int totalCycles;
        private int instructionCount;
        private int ppuCycles;

        public NesTestRunner()
        {
            memoryBus = new MemoryBus();
            cpu = new CPU6502(memoryBus);
            ppu = new PPU2C02(memoryBus);
            apu = new APU2A03();
            apu.ConnectConsole(memoryBus, cpu);
            log = new StringBuilder();
            totalCycles = 0;
            instructionCount = 0;
            ppuCycles = 0;
        }

        public void LoadCartridge(byte[] romData)
        {
            cartridge = CartridgeFactory.CreateCartridge(romData);
            memoryBus.ConnectCartridge(cartridge);
            ppu.ConnectCartridge(cartridge);
        }

        public void RunNesTest()
        {
            if (cartridge == null)
            {
                // Console.WriteLine("Error: No cartridge loaded");
                return;
            }

            // Console.WriteLine("Starting nestest.nes execution from $C000...");
            // Console.WriteLine("Format: PC OPCODE A X Y P SP CYC SL");
            // Console.WriteLine("----------------------------------------");

            cpu.Reset();
            ppu.Reset();
            
            cpu.SetPC(0xC000);
            
            totalCycles = 0;
            instructionCount = 0;
            ppuCycles = 0;

            int maxInstructions = 10000;
            int maxCycles = 1000000;

            while (instructionCount < maxInstructions && totalCycles < maxCycles)
            {
                ushort pc = cpu.PC;
                byte opcode = memoryBus.Read(pc);
                
                string disassembly = DisassembleInstruction(pc);
                
                int cyclesBefore = totalCycles;
                int cpuCycles = cpu.ExecuteInstruction();
                
                for (int i = 0; i < cpuCycles * 3; i++)
                {
                    ppu.Clock();
                    ppuCycles++;
                }
                
                totalCycles += cpuCycles;
                instructionCount++;

                int scanline = ppu.Scanline;
                int ppuCycle = ppu.Cycle;
                
                string logLine = $"{pc:X4}  {opcode:X2}       A:{cpu.A:X2} X:{cpu.X:X2} Y:{cpu.Y:X2} P:{cpu.P:X2} SP:{cpu.SP:X2} CYC:{cyclesBefore,3} SL:{scanline,3}";
                log.AppendLine(logLine);
                
                if (instructionCount <= 100 || instructionCount % 1000 == 0)
                {
                    // Console.WriteLine(logLine);
                }

                if (opcode == 0x00)
                {
                    // Console.WriteLine($"\nBRK instruction encountered at ${pc:X4}");
                    // Console.WriteLine($"Total instructions: {instructionCount}");
                    // Console.WriteLine($"Total cycles: {totalCycles}");
                    break;
                }
                
                if (cpu.PC < 0x0300)
                {
                    break;
                }
            }

            // Console.WriteLine($"\nExecution completed:");
            // Console.WriteLine($"  Total instructions: {instructionCount}");
            // Console.WriteLine($"  Total CPU cycles: {totalCycles}");
            // Console.WriteLine($"  Total PPU cycles: {ppuCycles}");
            
            byte result = memoryBus.Read(0x0000);
            byte result2 = memoryBus.Read(0x0001);
            // Console.WriteLine($"  Test result at $0000: ${result:X2}");
            // Console.WriteLine($"  Test result at $0001: ${result2:X2}");
            
            if (result == 0x00)
            {
                // Console.WriteLine("  ALL TESTS PASSED!");
            }
            else
            {
                // Console.WriteLine($"  Test #{result} FAILED!");
            }
        }

        private string DisassembleInstruction(ushort pc)
        {
            byte opcode = memoryBus.Read(pc);
            byte b1 = memoryBus.Read((ushort)(pc + 1));
            byte b2 = memoryBus.Read((ushort)(pc + 2));
            
            return $"{opcode:X2} {b1:X2} {b2:X2}";
        }

        public string GetLog()
        {
            return log.ToString();
        }

        public void SaveLog(string filename)
        {
            File.WriteAllText(filename, log.ToString());
            // Console.WriteLine($"Log saved to {filename}");
        }
    }
}
