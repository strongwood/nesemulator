using System;
using System.IO;
using System.Text;
using System.Windows;
using NESEmulator.Core.Testing;

namespace NESEmulator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            try
            {
                InitializeLogging();
                
                if (e.Args.Length > 0)
                {
                    if (e.Args[0] == "--test" || e.Args[0] == "-t")
                    {
                        RunTestMode(e.Args);
                        Shutdown();
                        return;
                    }
                    
                    if (e.Args[0] == "--ppu-test")
                    {
                        RunPpuTestMode();
                        Shutdown();
                        return;
                    }
                    
                    if (e.Args[0] == "--ppu-rom-test")
                    {
                        RunPpuRomTestMode(e.Args.Length > 1 ? e.Args[1] : null);
                        Shutdown();
                        return;
                    }

                    if (e.Args[0] == "--cpu-rom-test")
                    {
                        RunCpuRomTestMode(e.Args.Length > 1 ? e.Args[1] : null);
                        Shutdown();
                        return;
                    }
                    
                    string romPath = e.Args[0];
                    if (!File.Exists(romPath))
                    {
                        MessageBox.Show($"ROM file does not exist: {romPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }
                    
                    var mainWindow = new MainWindow(romPath);
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false;
                }
                else
                {
                    var mainWindow = new MainWindow(null);
                    mainWindow.Show();
                    mainWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
        
        private void RunTestMode(string[] args)
        {
            // Console.WriteLine("=== NES Emulator Test Mode ===");
            
            string testRom = "nestest.nes";
            if (args.Length > 1)
            {
                testRom = args[1];
            }
            
            if (!File.Exists(testRom))
            {
                // Console.WriteLine($"Error: Test ROM not found: {testRom}");
                // Console.WriteLine("Please ensure nestest.nes is in the current directory.");
                return;
            }
            
            // Console.WriteLine($"Loading test ROM: {testRom}");
            
            try
            {
                byte[] romData = File.ReadAllBytes(testRom);
                var runner = new NesTestRunner();
                runner.LoadCartridge(romData);
                runner.RunNesTest();
                
                string logFile = Path.ChangeExtension(testRom, ".log");
                runner.SaveLog(logFile);
            }
            catch (Exception)
            {
                // Console.WriteLine($"Test failed with error: {ex.Message}");
                // Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void RunPpuTestMode()
        {
            // Console.WriteLine("=== NES Emulator PPU Test Mode ===\n");
            
            try
            {
                var runner = new PpuTestRunner();
                runner.RunAllTests();
            }
            catch (Exception)
            {
                // Console.WriteLine($"PPU Test failed with error: {ex.Message}");
                // Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void RunPpuRomTestMode(string? specificRom)
        {
            // Console.WriteLine("=== NES Emulator PPU ROM Test Mode ===\n");
            
            try
            {
                var runner = new PpuRomTestRunner();
                
                if (!string.IsNullOrEmpty(specificRom))
                {
                    runner.RunSpecificTest(specificRom);
                }
                else
                {
                    runner.RunAllTests();
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"PPU ROM Test failed with error: {ex.Message}");
                // Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void RunCpuRomTestMode(string? romOrDirectory)
        {
            // Console.WriteLine("=== NES Emulator CPU ROM Test Mode ===\n");

            try
            {
                var runner = new CpuRomTestRunner();

                if (string.IsNullOrWhiteSpace(romOrDirectory))
                {
                    runner.RunDefaultTests();
                }
                else if (Directory.Exists(romOrDirectory))
                {
                    runner.RunDirectory(romOrDirectory);
                }
                else
                {
                    runner.RunSpecificTest(romOrDirectory);
                }
            }
            catch (Exception)
            {
                // Console.WriteLine($"CPU ROM Test failed with error: {ex.Message}");
                // Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void InitializeLogging()
        {
            // 重定向控制台输出到日志文件，但保留控制台输出
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
            var fileStream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            var streamWriter = new StreamWriter(fileStream) { AutoFlush = true };
            
            // 创建一个同时写入文件和控制台的TextWriter
            var multiWriter = new MultiTextWriter(Console.Out, streamWriter);
            Console.SetOut(multiWriter);
            Console.SetError(multiWriter);
            
            // Console.WriteLine($"=== NES Emulator Debug Log - {DateTime.Now} ===");
        }
        
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Console.WriteLine($"UI thread unhandled exception: {e.Exception.Message}");
            // Console.WriteLine($"Stack trace: {e.Exception.StackTrace}");
            MessageBox.Show($"An error occurred: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown();
        }
        
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            // Console.WriteLine($"Application domain unhandled exception: {ex?.Message}");
            // Console.WriteLine($"Stack trace: {ex?.StackTrace}");
            MessageBox.Show($"A critical error occurred: {ex?.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    // 辅助类：同时写入多个TextWriter
    public class MultiTextWriter : TextWriter
    {
        private readonly TextWriter[] writers;
        
        public MultiTextWriter(params TextWriter[] writers)
        {
            this.writers = writers;
        }
        
        public override Encoding Encoding => writers[0].Encoding;
        
        public override void Write(char value)
        {
            foreach (var writer in writers)
            {
                writer.Write(value);
            }
        }
        
        public override void WriteLine(string? value)
        {
            foreach (var writer in writers)
            {
                writer.WriteLine(value);
            }
        }
        
        public override void Flush()
        {
            foreach (var writer in writers)
            {
                writer.Flush();
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var writer in writers)
                {
                    writer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
