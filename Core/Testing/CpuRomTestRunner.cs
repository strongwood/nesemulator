using NESEmulator.Core.Memory;
using System.IO;
using System.Text;

namespace NESEmulator.Core.Testing
{
    public class CpuRomTestRunner
    {
        private const int MaxFrames = 6000;
        private const int ResetDelayFrames = 10;
        private readonly string outputPath;
        private int passedTests;
        private int failedTests;

        public CpuRomTestRunner()
        {
            outputPath = Path.Combine(Directory.GetCurrentDirectory(), "cpu_test_results.txt");
        }

        public void RunDefaultTests()
        {
            var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "nes-test-roms-master");
            RunSpecificTest(Path.Combine(baseDir, "instr_test-v5", "official_only.nes"));
        }

        public void RunDirectory(string directoryPath)
        {
            using var writer = new StreamWriter(outputPath, false);

            void Log(string message)
            {
                // Console.WriteLine(message);
                writer.WriteLine(message);
            }

            Log("=== CPU ROM Test Suite ===");
            Log($"Started at: {DateTime.Now}");
            Log($"Directory: {directoryPath}");
            Log("");

            var roms = Directory.EnumerateFiles(directoryPath, "*.nes", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roms.Count == 0)
            {
                Log("No .nes files found.");
                return;
            }

            foreach (string rom in roms)
            {
                RunSingleTest(rom, Log);
            }

            LogSummary(Log);
        }

        public void RunSpecificTest(string romPath)
        {
            using var writer = new StreamWriter(outputPath, false);

            void Log(string message)
            {
                // Console.WriteLine(message);
                writer.WriteLine(message);
            }

            Log("=== CPU ROM Test Suite ===");
            Log($"Started at: {DateTime.Now}");
            Log("");

            RunSingleTest(romPath, Log);
            LogSummary(Log);
        }

        private void RunSingleTest(string romPath, Action<string> log)
        {
            log($"--- Running: {romPath} ---");

            if (!File.Exists(romPath))
            {
                log($"[ERROR] ROM not found: {romPath}");
                failedTests++;
                return;
            }

            TextWriter originalOut = Console.Out;
            Console.SetOut(new NonFlushingTextWriter(originalOut));

            try
            {
                byte[] romData = File.ReadAllBytes(romPath);
                var nes = new NES();
                nes.LoadCartridge(romData);
                nes.Reset();
                nes.Start();

                bool sawBlarggOutput = false;
                int resetWaitFrames = 0;
                int resetCount = 0;
                string lastOutput = "";

                for (int frame = 1; frame <= MaxFrames; frame++)
                {
                    nes.RunFrame();

                    MemoryBus memoryBus = nes.GetMemoryBus();
                    byte status = memoryBus.Read(0x6000);
                    bool hasMagic = HasBlarggMagic(memoryBus);

                    if (hasMagic)
                    {
                        sawBlarggOutput = true;
                        lastOutput = ReadBlarggText(memoryBus);

                        if (status == 0x81)
                        {
                            resetWaitFrames++;
                            if (resetWaitFrames >= ResetDelayFrames)
                            {
                                resetCount++;
                                log($"  Reset requested by ROM; reset #{resetCount} at frame {frame}");
                                nes.Reset();
                                nes.Start();
                                resetWaitFrames = 0;
                            }

                            continue;
                        }

                        resetWaitFrames = 0;

                        if (status < 0x80)
                        {
                            if (status == 0)
                            {
                                log($"[PASS] {Path.GetFileName(romPath)} completed in {frame} frames");
                                LogOutput(log, lastOutput);
                                passedTests++;
                            }
                            else
                            {
                                log($"[FAIL] {Path.GetFileName(romPath)} returned code {status} in {frame} frames");
                                LogOutput(log, lastOutput);
                                failedTests++;
                            }

                            return;
                        }
                    }

                    if (frame % 300 == 0)
                    {
                        string progress = sawBlarggOutput ? Shorten(lastOutput) : "waiting for $6000 magic";
                        log($"  Frame {frame}: {progress}");
                    }
                }

                log($"[TIMEOUT] {Path.GetFileName(romPath)} did not finish after {MaxFrames} frames");
                if (sawBlarggOutput)
                {
                    LogOutput(log, lastOutput);
                }
                else
                {
                    log("  No blargg $6000 output was detected.");
                }
                failedTests++;
            }
            catch (Exception ex)
            {
                log($"[ERROR] {Path.GetFileName(romPath)}: {ex.Message}");
                failedTests++;
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        private static bool HasBlarggMagic(MemoryBus memoryBus)
        {
            return memoryBus.Read(0x6001) == 0xDE && memoryBus.Read(0x6002) == 0xB0;
        }

        private static string ReadBlarggText(MemoryBus memoryBus)
        {
            var output = new StringBuilder();

            for (int address = 0x6004; address < 0x8000; address++)
            {
                byte value = memoryBus.Read((ushort)address);
                if (value == 0)
                {
                    break;
                }

                if (value == '\n' || value == '\r' || value == '\t')
                {
                    output.Append((char)value);
                }
                else if (value >= 0x20 && value < 0x7F)
                {
                    output.Append((char)value);
                }
            }

            return output.ToString().Trim();
        }

        private static void LogOutput(Action<string> log, string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                log("  Output: <empty>");
                return;
            }

            log("  Output:");
            foreach (string line in output.Replace("\r\n", "\n").Split('\n'))
            {
                log($"    {line}");
            }
        }

        private static string Shorten(string text)
        {
            string compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= 120 ? compact : compact[..117] + "...";
        }

        private void LogSummary(Action<string> log)
        {
            log("");
            log("=== CPU Test Summary ===");
            log($"Passed: {passedTests}");
            log($"Failed: {failedTests}");
            log($"Total:  {passedTests + failedTests}");
            log($"Results written to: {outputPath}");
        }

        private sealed class NonFlushingTextWriter : TextWriter
        {
            private readonly TextWriter inner;

            public NonFlushingTextWriter(TextWriter inner)
            {
                this.inner = inner;
            }

            public override Encoding Encoding => inner.Encoding;

            public override void Write(char value) => inner.Write(value);
            public override void Write(string? value) => inner.Write(value);
            public override void WriteLine() => inner.WriteLine();
            public override void WriteLine(string? value) => inner.WriteLine(value);

            public override void Flush()
            {
            }
        }
    }
}
