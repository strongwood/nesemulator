using NESEmulator.Core.CPU;
using NESEmulator.Core.PPU;
using NESEmulator.Core.Memory;
using NESEmulator.Core.Cartridge;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace NESEmulator.Core.Testing
{
    public class PpuRomTestRunner
    {
        private NES nes;
        private StringBuilder log;
        private int passedTests;
        private int failedTests;
        private int maxFrames = 1800;
        
        public PpuRomTestRunner()
        {
            nes = new NES();
            log = new StringBuilder();
            passedTests = 0;
            failedTests = 0;
        }
        
        public void RunAllTests()
        {
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "ppu_test_results.txt");
            using var writer = new StreamWriter(outputPath, false);
            
            void Log(string msg)
            {
                // Console.WriteLine(msg);
                writer.WriteLine(msg);
            }
            
            Log("=== PPU ROM Test Suite ===");
            Log($"Started at: {DateTime.Now}");
            Log("");
            
            var testRoms = GetTestRoms();
            
            foreach (var test in testRoms)
            {
                if (File.Exists(test.Path))
                {
                    RunSingleTest(test.Name, test.Path, writer);
                }
                else
                {
                    Log($"[SKIP] {test.Name} - ROM not found: {test.Path}");
                }
            }
            
            Log("");
            Log("=== Test Summary ===");
            Log($"Passed: {passedTests}");
            Log($"Failed: {failedTests}");
            Log($"Total:  {passedTests + failedTests}");
            
            if (failedTests == 0 && passedTests > 0)
            {
                Log("");
                Log("ALL PPU ROM TESTS PASSED!");
            }
            else if (passedTests == 0)
            {
                Log("");
                Log("No tests were run - ROM files not found");
            }
            
            writer.Flush();
            // Console.WriteLine($"Results written to: {outputPath}");
        }
        
        private List<(string Name, string Path)> GetTestRoms()
        {
            var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "nes-test-roms-master");
            
            return new List<(string, string)>
            {
                ("PPU VBlank/NMI - Basics", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "01-vbl_basics.nes")),
                ("PPU VBlank/NMI - Set Time", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "02-vbl_set_time.nes")),
                ("PPU VBlank/NMI - Clear Time", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "03-vbl_clear_time.nes")),
                ("PPU VBlank/NMI - NMI Control", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "04-nmi_control.nes")),
                ("PPU VBlank/NMI - NMI Timing", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "05-nmi_timing.nes")),
                ("PPU VBlank/NMI - Suppression", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "06-suppression.nes")),
                ("PPU VBlank/NMI - NMI On Timing", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "07-nmi_on_timing.nes")),
                ("PPU VBlank/NMI - NMI Off Timing", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "08-nmi_off_timing.nes")),
                ("PPU VBlank/NMI - Even/Odd Frames", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "09-even_odd_frames.nes")),
                ("PPU VBlank/NMI - Even/Odd Timing", Path.Combine(baseDir, "ppu_vbl_nmi", "rom_singles", "10-even_odd_timing.nes")),
                ("PPU Open Bus", Path.Combine(baseDir, "ppu_open_bus", "ppu_open_bus.nes")),
                ("Sprite Hit - Basics", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "01.basics.nes")),
                ("Sprite Hit - Alignment", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "02.alignment.nes")),
                ("Sprite Hit - Corners", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "03.corners.nes")),
                ("Sprite Hit - Flip", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "04.flip.nes")),
                ("Sprite Hit - Left Clip", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "05.left_clip.nes")),
                ("Sprite Hit - Right Edge", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "06.right_edge.nes")),
                ("Sprite Hit - Screen Bottom", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "07.screen_bottom.nes")),
                ("Sprite Hit - Double Height", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "08.double_height.nes")),
                ("Sprite Hit - Timing Basics", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "09.timing_basics.nes")),
                ("Sprite Hit - Timing Order", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "10.timing_order.nes")),
                ("Sprite Hit - Edge Timing", Path.Combine(baseDir, "sprite_hit_tests_2005.10.05", "11.edge_timing.nes")),
                ("Sprite Overflow - Basics", Path.Combine(baseDir, "sprite_overflow_tests", "1.Basics.nes")),
                ("Sprite Overflow - Details", Path.Combine(baseDir, "sprite_overflow_tests", "2.Details.nes")),
                ("Sprite Overflow - Timing", Path.Combine(baseDir, "sprite_overflow_tests", "3.Timing.nes")),
                ("Sprite Overflow - Obscure", Path.Combine(baseDir, "sprite_overflow_tests", "4.Obscure.nes")),
                ("Sprite Overflow - Emulator", Path.Combine(baseDir, "sprite_overflow_tests", "5.Emulator.nes")),
                ("PPU Read Buffer", Path.Combine(baseDir, "ppu_read_buffer", "test_ppu_read_buffer.nes")),
            };
        }
        
        private void RunSingleTest(string testName, string romPath, StreamWriter writer)
        {
            void Log(string msg)
            {
                // Console.WriteLine(msg);
                writer.WriteLine(msg);
            }
            
            Log("");
            Log($"--- Running: {testName} ---");
            
            try
            {
                byte[] romData = File.ReadAllBytes(romPath);
                nes = new NES();
                nes.LoadCartridge(romData);
                nes.Reset();
                nes.Start();
                
                int frameCount = 0;
                string result = "";
                bool testComplete = false;
                
                while (frameCount < maxFrames && !testComplete)
                {
                    nes.RunFrame();
                    frameCount++;
                    
                    result = ParseTestResult();
                    
                    if (result.Contains("Passed") || result.Contains("Failed") || 
                        result.Contains("passed") || result.Contains("failed") ||
                        result.Contains("PASS") || result.Contains("FAIL") ||
                        result.Contains("Err") || result.Contains("error"))
                    {
                        testComplete = true;
                    }
                    
                    if (frameCount % 60 == 0)
                    {
                        Log($"  Frame {frameCount}...");
                    }
                }
                
                if (result.Contains("Passed") || result.Contains("passed") || result.Contains("PASS"))
                {
                    Log($"  [PASS] {testName}");
                    Log($"  Result: {result}");
                    passedTests++;
                }
                else if (result.Contains("Failed") || result.Contains("failed") || result.Contains("FAIL"))
                {
                    Log($"  [FAIL] {testName}");
                    Log($"  Result: {result}");
                    failedTests++;
                }
                else if (result.Contains("Err") || result.Contains("error"))
                {
                    Log($"  [FAIL] {testName} - Error detected");
                    Log($"  Result: {result}");
                    failedTests++;
                }
                else
                {
                    Log($"  [TIMEOUT] {testName} - No result after {maxFrames} frames");
                    Log($"  Last result: {result}");
                    failedTests++;
                }
            }
            catch (Exception ex)
            {
                Log($"  [ERROR] {testName}: {ex.Message}");
                failedTests++;
            }
        }
        
        private string ParseTestResult()
        {
            var memoryBus = nes.GetMemoryBus();
            var ppu = nes.GetPPU();
            
            StringBuilder result = new StringBuilder();
            
            string memoryResult = ParseMemoryResult(memoryBus);
            if (!string.IsNullOrEmpty(memoryResult))
            {
                result.Append(memoryResult);
            }
            
            string screenResult = ParseScreenText(ppu);
            if (!string.IsNullOrEmpty(screenResult))
            {
                if (result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(screenResult);
            }
            
            return result.ToString().Trim();
        }
        
        private string ParseScreenText(PPU2C02 ppu)
        {
            StringBuilder text = new StringBuilder();
            for (int row = 0; row < 30; row++)
            {
                for (int col = 0; col < 32; col++)
                {
                    byte tile = ppu.ReadVRAMDebug((ushort)(0x2000 + row * 32 + col));
                    char ch = tile >= 0x20 && tile <= 0x7E ? (char)tile : ' ';
                    text.Append(ch);
                }
                text.Append('\n');
            }

            string normalized = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
            return normalized;
        }
        
        private string ParseMemoryResult(MemoryBus memoryBus)
        {
            StringBuilder result = new StringBuilder();
            
            // 根据blargg测试规范，测试结果写入$6004开始的位置
            // $6000 = 测试状态 ($80 = 运行中, $00-$7F = 完成并返回结果码)
            // $6001-$6003 = 魔数 $DE $B0 $G1 (标识这是blargg测试)
            // $6004+ = 文本输出
            
            byte testStatus = memoryBus.Read(0x6000);
            byte magic1 = memoryBus.Read(0x6001);
            byte magic2 = memoryBus.Read(0x6002);
            byte magic3 = memoryBus.Read(0x6003);
            
            // 检查是否是blargg测试格式
            if (magic1 == 0xDE && magic2 == 0xB0)
            {
                // 读取$6004开始的文本输出
                for (int addr = 0x6004; addr < 0x8000; addr++)
                {
                    byte value = memoryBus.Read((ushort)addr);
                    if (value == 0) break; // 遇到0终止
                    if (value >= 0x20 && value < 0x7F)
                    {
                        result.Append((char)value);
                    }
                }
                
                // 如果测试完成，添加状态
                if (testStatus < 0x80)
                {
                    if (testStatus == 0)
                    {
                        result.Append(" Passed");
                    }
                    else
                    {
                        result.Append($" Failed #{testStatus}");
                    }
                }
            }
            else
            {
                // 不是blargg格式，尝试读取普通内存
                for (int addr = 0x0000; addr < 0x0800; addr += 0x100)
                {
                    byte value = memoryBus.Read((ushort)addr);
                    if (value != 0)
                    {
                        if (value >= 0x20 && value < 0x7F)
                        {
                            result.Append((char)value);
                        }
                    }
                }
            }
            
            return StripAnsi(result.ToString()).Trim();
        }

        private string StripAnsi(string text)
        {
            return Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", "");
        }
        
        public void RunSpecificTest(string romPath)
        {
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "ppu_specific_test.txt");
            using var writer = new StreamWriter(outputPath, false);
            
            void Log(string msg)
            {
                // Console.WriteLine(msg);
                writer.WriteLine(msg);
            }
            
            Log($"Running specific test: {romPath}");
            
            if (!File.Exists(romPath))
            {
                Log($"ROM not found: {romPath}");
                return;
            }
            
            byte[] romData = File.ReadAllBytes(romPath);
            nes = new NES();
            nes.LoadCartridge(romData);
            nes.Reset();
            nes.Start();
            
            Log("\nDetailed execution log:");
            Log("Frame | PPUSTATUS | PPUCTRL | PPUMASK | Scanline | Cycle | PC");
            Log(new string('-', 70));
            
            int frameCount = 0;
            int maxDetailFrames = 60;
            bool sprite0HitDetected = false;
            int lastTestNum = -1;
            int lastPC = -1;
            int samePCCount = 0;
            
            while (frameCount < maxFrames)
            {
                nes.RunFrame();
                frameCount++;
                
                var ppu = nes.GetPPU();
                var cpu = nes.GetCPU();
                var memoryBus = nes.GetMemoryBus();
                
                // 检测PC变化
                if (cpu.PC == lastPC)
                {
                    samePCCount++;
                }
                else
                {
                    if (samePCCount > 10 && lastPC != -1)
                    {
                        Log($"PC stayed at 0x{lastPC:X4} for {samePCCount} frames");
                    }
                    lastPC = cpu.PC;
                    samePCCount = 1;
                }
                
                // 检测精灵0碰撞
                if ((ppu.PPUSTATUS & 0x40) != 0 && !sprite0HitDetected)
                {
                    sprite0HitDetected = true;
                    Log($"*** Sprite 0 Hit detected at frame {frameCount}, scanline {ppu.Scanline}, cycle {ppu.Cycle} ***");
                }
                
                // 每帧检查测试状态
                byte testStatus = memoryBus.Read(0x6000);
                byte magic1 = memoryBus.Read(0x6001);
                byte magic2 = memoryBus.Read(0x6002);
                
                // 检查测试编号变化
                byte currentTestNum = memoryBus.Read(0x6003);
                if (currentTestNum != lastTestNum && magic1 == 0xDE && magic2 == 0xB0)
                {
                    lastTestNum = currentTestNum;
                    Log($"Test #{currentTestNum} started at frame {frameCount}");
                    
                    // 输出当前名称表状态
                    Log($"  Name table at sprite position (Y=120, X=128):");
                    int ntAddr = 0x2000 + 15 * 32 + 16; // Y=120, X=128
                    byte tile = ppu.ReadVRAMDebug((ushort)ntAddr);
                    Log($"    NT$21F0 = ${tile:X2}");
                    
                    // 检查tile图案数据
                    int patternAddr = tile * 16 + 7; // row 7
                    byte patternLow = ppu.ReadVRAMDebug((ushort)patternAddr);
                    byte patternHigh = ppu.ReadVRAMDebug((ushort)(patternAddr + 8));
                    Log($"    Pattern data (row 7): ${patternLow:X2}/${patternHigh:X2}");
                    
                    // 读取测试输出文本
                    StringBuilder output = new StringBuilder();
                    for (int addr = 0x6004; addr < 0x6100; addr++)
                    {
                        byte value = memoryBus.Read((ushort)addr);
                        if (value == 0) break;
                        if (value >= 0x20 && value < 0x7F)
                            output.Append((char)value);
                    }
                    if (output.Length > 0)
                    {
                        Log($"  Test output: {output.ToString()}");
                    }
                }
                
                // 检查是否有测试结果输出
                if (frameCount % 60 == 0 && magic1 == 0xDE && magic2 == 0xB0)
                {
                    // 读取测试输出文本
                    StringBuilder output = new StringBuilder();
                    for (int addr = 0x6004; addr < 0x6100; addr++)
                    {
                        byte value = memoryBus.Read((ushort)addr);
                        if (value == 0) break;
                        if (value >= 0x20 && value < 0x7F)
                            output.Append((char)value);
                    }
                    if (output.Length > 0)
                    {
                        Log($"  Frame {frameCount} test output: {output.ToString()}");
                    }
                }
                
                // 在测试程序开始等待精灵0碰撞时检查状态
                // 注意：RunFrame() 执行完一整帧后，扫描线已经回到0
                // 所以我们需要在帧结束时检查状态
                if (frameCount == 50 || frameCount == 100 || frameCount == 200)
                {
                    var oam = ppu.GetOAM();
                    Log($"\n=== Frame {frameCount} End State ===");
                    Log($"PC: 0x{cpu.PC:X4}");
                    Log($"OAM Sprite 0: Y={oam[0]}, Tile={oam[1]:X2}, Attr={oam[2]:X2}, X={oam[3]}");
                    Log($"PPUMASK: 0x{ppu.PPUMASK:X2} (BG: {(ppu.PPUMASK & 0x08) != 0}, Sprites: {(ppu.PPUMASK & 0x10) != 0})");
                    Log($"PPUCTRL: 0x{ppu.PPUCTRL:X2}");
                    
                    // 检查名称表
                    Log($"\nName table content around sprite position:");
                    for (int ty = 14; ty <= 16; ty++)
                    {
                        for (int tx = 15; tx <= 17; tx++)
                        {
                            int addr = 0x2000 + ty * 32 + tx;
                            byte t = ppu.ReadVRAMDebug((ushort)addr);
                            Log($"  NT${addr:X4} (X={tx*8}, Y={ty*8}): tile ${t:X2}");
                        }
                    }
                    
                    // 检查精灵图案
                    int sprPatternAddr = ((ppu.PPUCTRL & 0x08) != 0 ? 0x1000 : 0x0000) + oam[1] * 16;
                    Log($"\nSprite pattern (tile ${oam[1]:X2}):");
                    for (int r = 0; r < 8; r++)
                    {
                        byte low = ppu.ReadVRAMDebug((ushort)(sprPatternAddr + r));
                        byte high = ppu.ReadVRAMDebug((ushort)(sprPatternAddr + r + 8));
                        Log($"  Row {r}: ${low:X2}/${high:X2}");
                    }
                    
                    // 检查背景图案
                    int bgTile = ppu.ReadVRAMDebug(0x21F0);
                    int bgPatternAddr = ((ppu.PPUCTRL & 0x10) != 0 ? 0x1000 : 0x0000) + bgTile * 16;
                    Log($"\nBackground pattern (tile ${bgTile:X2}):");
                    for (int r = 0; r < 8; r++)
                    {
                        byte low = ppu.ReadVRAMDebug((ushort)(bgPatternAddr + r));
                        byte high = ppu.ReadVRAMDebug((ushort)(bgPatternAddr + r + 8));
                        Log($"  Row {r}: ${low:X2}/${high:X2}");
                    }
                    
                    // 检查精灵0碰撞状态
                    Log($"\nSprite 0 collision check:");
                    Log($"  Sprite 0 Y={oam[0]}, X={oam[3]}");
                    if (oam[0] < 240)
                    {
                        Log($"  Sprite will be visible on scanlines {oam[0]} to {Math.Min(oam[0]+7, 239)}");
                    }
                    else
                    {
                        Log($"  Sprite Y={oam[0]} is off-screen (>= 240)");
                    }
                    Log($"  Background tile at sprite position: ${bgTile:X2}");
                    
                    // 检查背景图案是否有非零像素
                    bool bgHasPixels = false;
                    for (int r = 0; r < 8; r++)
                    {
                        byte low = ppu.ReadVRAMDebug((ushort)(bgPatternAddr + r));
                        byte high = ppu.ReadVRAMDebug((ushort)(bgPatternAddr + r + 8));
                        if (low != 0 || high != 0)
                        {
                            bgHasPixels = true;
                            break;
                        }
                    }
                    Log($"  Background pattern has non-zero pixels: {bgHasPixels}");
                    
                    // 检查精灵图案是否有非零像素
                    bool sprHasPixels = false;
                    for (int r = 0; r < 8; r++)
                    {
                        byte low = ppu.ReadVRAMDebug((ushort)(sprPatternAddr + r));
                        byte high = ppu.ReadVRAMDebug((ushort)(sprPatternAddr + r + 8));
                        if (low != 0 || high != 0)
                        {
                            sprHasPixels = true;
                            break;
                        }
                    }
                    Log($"  Sprite pattern has non-zero pixels: {sprHasPixels}");
                    
                    // 检查渲染是否启用
                    bool bgEnabled = (ppu.PPUMASK & 0x08) != 0;
                    bool sprEnabled = (ppu.PPUMASK & 0x10) != 0;
                    Log($"  Background rendering enabled: {bgEnabled}");
                    Log($"  Sprite rendering enabled: {sprEnabled}");
                    Log($"  NOTE: Sprite 0 hit should work even if sprite rendering is disabled!");
                    
                    // 检查名称表中是否有任何非$20的tile
                    Log($"\nChecking entire name table for non-$20 tiles:");
                    int non20Count = 0;
                    for (int i = 0; i < 960; i++)
                    {
                        byte t = ppu.ReadVRAMDebug((ushort)(0x2000 + i));
                        if (t != 0x20)
                        {
                            non20Count++;
                            if (non20Count <= 10)
                            {
                                int ty = i / 32;
                                int tx = i % 32;
                                Log($"  NT${0x2000+i:X4} (X={tx*8}, Y={ty*8}): tile ${t:X2}");
                            }
                        }
                    }
                    Log($"  Total non-$20 tiles: {non20Count}");
                    
                    // 检查tile 3的图案数据（应该是solid_tile）
                    Log($"\nTile 3 pattern data (should be solid):");
                    for (int r = 0; r < 8; r++)
                    {
                        byte low = ppu.ReadVRAMDebug((ushort)(3 * 16 + r));
                        byte high = ppu.ReadVRAMDebug((ushort)(3 * 16 + r + 8));
                        Log($"  Row {r}: ${low:X2}/${high:X2}");
                    }
                }
                
                if (frameCount <= maxDetailFrames && frameCount % 10 == 0)
                {
                    Log($"{frameCount,5} | 0x{ppu.PPUSTATUS:X2}      | 0x{ppu.PPUCTRL:X2}    | 0x{ppu.PPUMASK:X2}    | {ppu.Scanline,8} | {ppu.Cycle,5} | 0x{cpu.PC:X4}");
                }
                
                // 检查测试状态 - 使用之前已定义的变量
                if (magic1 == 0xDE && magic2 == 0xB0 && testStatus < 0x80)
                {
                    Log($"\nTest completed at frame {frameCount}");
                    Log($"Test status: {testStatus}");
                    
                    // 读取输出文本
                    StringBuilder output = new StringBuilder();
                    for (int addr = 0x6004; addr < 0x6100; addr++)
                    {
                        byte value = memoryBus.Read((ushort)addr);
                        if (value == 0) break;
                        if (value >= 0x20 && value < 0x7F)
                            output.Append((char)value);
                    }
                    Log($"Output: {output.ToString()}");
                    break;
                }
            }
            
            Log($"\nFinal state after {frameCount} frames:");
            var finalPpu = nes.GetPPU();
            var finalCpu = nes.GetCPU();
            var finalMemoryBus = nes.GetMemoryBus();
            Log($"  PC: 0x{finalCpu.PC:X4}");
            Log($"  PPUSTATUS: 0x{finalPpu.PPUSTATUS:X2} (VBlank: {(finalPpu.PPUSTATUS & 0x80) != 0}, Sprite0Hit: {(finalPpu.PPUSTATUS & 0x40) != 0})");
            Log($"  PPUCTRL: 0x{finalPpu.PPUCTRL:X2}");
            Log($"  PPUMASK: 0x{finalPpu.PPUMASK:X2}");
            Log($"  Sprite 0 hit detected: {sprite0HitDetected}");
            
            // 读取测试结果内存
            Log($"\nMemory at $6000-$600F:");
            for (int addr = 0x6000; addr < 0x6010; addr++)
            {
                byte value = finalMemoryBus.Read((ushort)addr);
                Log($"  ${addr:X4} = ${value:X2}");
            }
            
            // 读取测试输出文本
            StringBuilder finalOutput = new StringBuilder();
            for (int addr = 0x6004; addr < 0x6100; addr++)
            {
                byte value = finalMemoryBus.Read((ushort)addr);
                if (value == 0) break;
                if (value >= 0x20 && value < 0x7F)
                    finalOutput.Append((char)value);
            }
            if (finalOutput.Length > 0)
            {
                Log($"\nTest output text: {finalOutput.ToString()}");
            }
            
            writer.Flush();
            // Console.WriteLine($"Detailed results written to: {outputPath}");
        }
        
        public void DumpFrameBuffer(string outputPath)
        {
            var frameBuffer = nes.GetFrameBuffer();
            if (frameBuffer == null) return;
            
            using (var writer = new StreamWriter(outputPath))
            {
                for (int y = 0; y < 240; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        uint pixel = frameBuffer[y * 256 + x];
                        writer.Write($"{pixel:X8} ");
                    }
                    writer.WriteLine();
                }
            }
            
            // Console.WriteLine($"Frame buffer dumped to: {outputPath}");
        }
    }
}
