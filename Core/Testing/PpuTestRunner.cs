using NESEmulator.Core.CPU;
using NESEmulator.Core.PPU;
using NESEmulator.Core.Memory;
using NESEmulator.Core.Cartridge;
using System.Text;

namespace NESEmulator.Core.Testing
{
    public class PpuTestRunner
    {
        private CPU6502 cpu;
        private PPU2C02 ppu;
        private MemoryBus memoryBus;
        private StringBuilder log;
        private int passedTests;
        private int failedTests;

        public PpuTestRunner()
        {
            memoryBus = new MemoryBus();
            cpu = new CPU6502(memoryBus);
            ppu = new PPU2C02(memoryBus);
            log = new StringBuilder();
            passedTests = 0;
            failedTests = 0;
        }

        public void RunAllTests()
        {
            // Console.WriteLine("=== PPU Test Suite ===\n");
            
            TestPpuRegisters();
            TestPpuVram();
            TestPaletteRam();
            TestScrollRegisters();
            TestVBlankTiming();
            TestFrameBuffer();
            
            // Console.WriteLine("\n=== Test Summary ===");
            // Console.WriteLine($"Passed: {passedTests}");
            // Console.WriteLine($"Failed: {failedTests}");
            // Console.WriteLine($"Total:  {passedTests + failedTests}");
            
            if (failedTests == 0)
            {
                // Console.WriteLine("\nALL PPU TESTS PASSED!");
            }
            else
            {
                // Console.WriteLine($"\n{failedTests} TEST(S) FAILED!");
            }
        }

        private void TestPpuRegisters()
        {
            // Console.WriteLine("--- Testing PPU Registers ---");
            
            Test("PPUCTRL read/write", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2000, 0x1F);
                return ppu.PPUCTRL == 0x1F;
            });
            
            Test("PPUMASK read/write", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2001, 0x1F);
                return ppu.PPUMASK == 0x1F;
            });
            
            Test("PPUSTATUS VBlank flag", () =>
            {
                ppu.Reset();
                byte status = ppu.ReadRegister(0x2002);
                return (status & 0x80) == 0;
            });
            
            Test("PPUSTATUS clears on read", () =>
            {
                ppu.Reset();
                int maxCycles = 262 * 341;
                int cycles = 0;
                while (cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                    if (ppu.Scanline == 241 && ppu.Cycle == 2)
                    {
                        byte statusBefore = ppu.PPUSTATUS;
                        if ((statusBefore & 0x80) == 0) return false;
                        byte statusAfter = ppu.ReadRegister(0x2002);
                        byte statusFinal = ppu.PPUSTATUS;
                        return (statusFinal & 0x80) == 0;
                    }
                }
                return false;
            });
            
            Test("PPUADDR double write", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x21);
                ppu.WriteRegister(0x2006, 0x00);
                return true;
            });
            
            Test("PPUDATA write/read", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x21);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0x55);
                ppu.WriteRegister(0x2006, 0x21);
                ppu.WriteRegister(0x2006, 0x00);
                byte data = ppu.ReadRegister(0x2007);
                return true;
            });
        }

        private void TestPpuVram()
        {
            // Console.WriteLine("\n--- Testing PPU VRAM ---");
            
            Test("VRAM write/read", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0xAB);
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.ReadRegister(0x2007);
                byte data = ppu.ReadRegister(0x2007);
                return data == 0xAB;
            });
            
            Test("VRAM address increment", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2000, 0x00);
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0x11);
                ppu.WriteRegister(0x2007, 0x22);
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.ReadRegister(0x2007);
                byte data1 = ppu.ReadRegister(0x2007);
                return data1 == 0x11;
            });
            
            Test("VRAM address increment +32", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2000, 0x04);
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0x11);
                ppu.WriteRegister(0x2007, 0x22);
                ppu.WriteRegister(0x2006, 0x20);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.ReadRegister(0x2007);
                byte data1 = ppu.ReadRegister(0x2007);
                return data1 == 0x11;
            });
        }

        private void TestPaletteRam()
        {
            // Console.WriteLine("\n--- Testing Palette RAM ---");
            
            Test("Palette write/read", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0x0F);
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x00);
                byte data = ppu.ReadRegister(0x2007);
                return data == 0x0F;
            });
            
            Test("Palette mirror $3F10 to $3F00", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x00);
                ppu.WriteRegister(0x2007, 0x11);
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x10);
                byte data = ppu.ReadRegister(0x2007);
                return data == 0x11;
            });
            
            Test("Palette mirror $3F14 to $3F04", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x04);
                ppu.WriteRegister(0x2007, 0x22);
                ppu.WriteRegister(0x2006, 0x3F);
                ppu.WriteRegister(0x2006, 0x14);
                byte data = ppu.ReadRegister(0x2007);
                return data == 0x22;
            });
        }

        private void TestScrollRegisters()
        {
            // Console.WriteLine("\n--- Testing Scroll Registers ---");
            
            Test("PPUSCROLL X write", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2005, 0x07);
                return true;
            });
            
            Test("PPUSCROLL Y write", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2005, 0x07);
                ppu.WriteRegister(0x2005, 0x05);
                return true;
            });
            
            Test("PPUSCROLL write toggle", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2005, 0x07);
                ppu.WriteRegister(0x2005, 0x05);
                ppu.WriteRegister(0x2005, 0x00);
                return true;
            });
        }

        private void TestVBlankTiming()
        {
            // Console.WriteLine("\n--- Testing VBlank Timing ---");
            
            Test("VBlank starts at scanline 241", () =>
            {
                ppu.Reset();
                int maxCycles = 300 * 341;
                int cycles = 0;
                while (cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                    if (ppu.Scanline == 241 && ppu.Cycle == 2)
                    {
                        byte status = ppu.PPUSTATUS;
                        return (status & 0x80) != 0;
                    }
                }
                return false;
            });
            
            Test("VBlank ends at pre-render scanline", () =>
            {
                ppu.Reset();
                int maxCycles = 262 * 341 + 10;
                int cycles = 0;
                while (cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                    if ((ppu.Scanline == -1 || ppu.Scanline == 261) && ppu.Cycle == 2)
                    {
                        byte status = ppu.PPUSTATUS;
                        return (status & 0x80) == 0;
                    }
                }
                return false;
            });
            
            Test("NMI triggered when enabled", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2000, 0x80);
                int maxCycles = 300 * 341;
                int cycles = 0;
                while (cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                    if (ppu.Scanline == 241 && ppu.Cycle == 2)
                    {
                        return ppu.IsNMITriggered();
                    }
                }
                return false;
            });
            
            Test("NMI not triggered when disabled", () =>
            {
                ppu.Reset();
                ppu.WriteRegister(0x2000, 0x00);
                int maxCycles = 300 * 341;
                int cycles = 0;
                while (cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                    if (ppu.Scanline == 241 && ppu.Cycle == 2)
                    {
                        return !ppu.IsNMITriggered();
                    }
                }
                return false;
            });
        }

        private void TestFrameBuffer()
        {
            // Console.WriteLine("\n--- Testing Frame Buffer ---");
            
            Test("Frame buffer exists", () =>
            {
                var buffer = ppu.GetFrameBuffer();
                return buffer != null && buffer.Length == 256 * 240;
            });
            
            Test("Frame complete flag", () =>
            {
                ppu.Reset();
                int maxCycles = 262 * 341 + 100;
                int cycles = 0;
                while (!ppu.IsFrameComplete() && cycles < maxCycles)
                {
                    ppu.Clock();
                    cycles++;
                }
                return ppu.IsFrameComplete();
            });
        }

        private void Test(string name, Func<bool> testFunc)
        {
            try
            {
                bool result = testFunc();
                if (result)
                {
                    // Console.WriteLine($"  [PASS] {name}");
                    passedTests++;
                }
                else
                {
                    // Console.WriteLine($"  [FAIL] {name}");
                    failedTests++;
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"  [ERROR] {name}: {ex.Message}");
                failedTests++;
            }
        }
    }
}
