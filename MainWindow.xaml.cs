using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NESEmulator.Core;
using NESEmulator.Core.Input;
using NESEmulator.Core.CPU;
using NESEmulator.Core.Timing;

namespace NESEmulator
{
    public partial class MainWindow : Window
    {
        private NES? nes;
        private HighPrecisionTimer? precisionTimer;
        private ClockSynchronizer? clockSynchronizer;
        private WriteableBitmap? framebuffer;
        private bool isRunning = false;
        private int frameCount = 0;
        private long lastFPSUpdate;
        private Thread? gameLoopThread;
        private CancellationTokenSource? cancellationTokenSource;
        private KeyboardMapping player1KeyboardMapping = KeyboardMapping.CreateDefault(0);
        private KeyboardMapping player2KeyboardMapping = KeyboardMapping.CreateDefault(1);
        private GamepadMapping player1GamepadMapping = GamepadMapping.CreateDefault(0);
        private GamepadMapping player2GamepadMapping = GamepadMapping.CreateDefault(1);
        private readonly HashSet<ControllerButton> player1KeyboardPressedButtons = new HashSet<ControllerButton>();
        private readonly HashSet<ControllerButton> player2KeyboardPressedButtons = new HashSet<ControllerButton>();
        private readonly HashSet<TurboButton> player1KeyboardPressedTurboButtons = new HashSet<TurboButton>();
        private readonly HashSet<TurboButton> player2KeyboardPressedTurboButtons = new HashSet<TurboButton>();
        private readonly HashSet<GamepadButton> player1GamepadPressedButtons = new HashSet<GamepadButton>();
        private readonly HashSet<GamepadButton> player2GamepadPressedButtons = new HashSet<GamepadButton>();
        private DispatcherTimer? gamepadPollingTimer;
        private readonly Stopwatch turboStopwatch = Stopwatch.StartNew();
        private bool turboPulseActive = true;
        private float masterVolume = 2.0f;
        private double emulationSpeed = 1.0;
        private bool isUpdatingSpeedControls = false;
        private int displayUpdatePending = 0;
        private int skippedDisplayUpdates = 0;
        private static int debugDisplayLogCount;
        private const int TurboPulseIntervalMs = 50;

        #region debug-point infra
        private static readonly HttpClient DebugHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
        private static readonly string DebugEnvPath = Path.Combine(".dbg", "audio-interrupt-fps.env");
        private static readonly string? DebugServerUrl = TryReadDebugEnv("DEBUG_SERVER_URL");
        private static readonly string DebugSessionId = TryReadDebugEnv("DEBUG_SESSION_ID") ?? "audio-interrupt-fps";
        private static int debugFrameLoopLogCount;
        private static int debugUiSkipLogCount;

        private static string? TryReadDebugEnv(string key)
        {
            try
            {
                if (!File.Exists(DebugEnvPath))
                {
                    return null;
                }

                foreach (string line in File.ReadAllLines(DebugEnvPath))
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line[(key.Length + 1)..].Trim();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void DebugReport(string runId, string hypothesisId, string location, string msg, object data)
        {
            if (string.IsNullOrWhiteSpace(DebugServerUrl))
            {
                return;
            }

            try
            {
                string payload = JsonSerializer.Serialize(new
                {
                    sessionId = DebugSessionId,
                    runId,
                    hypothesisId,
                    location,
                    msg = "[DEBUG] " + msg,
                    data,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                _ = DebugHttpClient.PostAsync(DebugServerUrl, new StringContent(payload, Encoding.UTF8, "application/json"));
            }
            catch
            {
            }
        }
        #endregion

        public MainWindow() : this(null)
        {
        }
        
        public MainWindow(string? romPath)
        {
            try
            {
                // Console.WriteLine("MainWindow constructor started");
                InitializeComponent();
                UpdateSpeedControls();
                // Console.WriteLine("InitializeComponent completed");
                InitializeEmulator();
                // Console.WriteLine("InitializeEmulator completed");
                SetupInput();
                // Console.WriteLine("SetupInput completed");
                
                // 如果提供了ROM路径，自动加载
                if (!string.IsNullOrEmpty(romPath))
                {
                    // Console.WriteLine($"Auto-loading ROM: {romPath}");
                    LoadROMFile(romPath);
                    // 延迟自动开始运行，确保窗口完全初始化
                    Dispatcher.BeginInvoke(new Action(() => {
                        // Console.WriteLine("Delayed start of emulator");
                        Start_Click(this, new RoutedEventArgs());
                    }), DispatcherPriority.Loaded);
                }
                // Console.WriteLine("MainWindow constructor completed");
                
                // 确保窗口保持打开
                this.Closing += (s, e) => {
                    // Console.WriteLine("Window is closing");
                };
            }
            catch (Exception)
            {
                // Console.WriteLine($"MainWindow constructor exception: {ex.Message}");
                // Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeEmulator()
        {
            framebuffer = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgr32, null);
            player1KeyboardMapping = KeyboardMapping.Load(GetKeyboardMappingPath(0), 0);
            player2KeyboardMapping = KeyboardMapping.Load(GetKeyboardMappingPath(1), 1);
            player1GamepadMapping = GamepadMapping.Load(GetGamepadMappingPath(0), 0);
            player2GamepadMapping = GamepadMapping.Load(GetGamepadMappingPath(1), 1);
            
            precisionTimer = new HighPrecisionTimer();
            precisionTimer.SetSpeedMultiplier(emulationSpeed);
            clockSynchronizer = new ClockSynchronizer(precisionTimer);
            lastFPSUpdate = precisionTimer.GetCurrentTicks();
            
            ClearDisplay();
            
            // Console.WriteLine($"高精度定时器初始化完成:");
            var timerInfo = precisionTimer.GetTimerInfo();
            // Console.WriteLine($"  - 高分辨率支持: {timerInfo.IsHighResolution}");
            // Console.WriteLine($"  - 定时器频率: {timerInfo.Frequency:N0} Hz");
            // Console.WriteLine($"  - 分辨率: {timerInfo.ResolutionNs:F2} ns");
        }
        
        private void ClearDisplay()
        {
            if (framebuffer != null)
            {
                framebuffer.Lock();
                unsafe
                {
                    var backBuffer = (uint*)framebuffer.BackBuffer;
                    for (int i = 0; i < 256 * 240; i++)
                    {
                        backBuffer[i] = 0xFF000000;
                    }
                }
                framebuffer.AddDirtyRect(new Int32Rect(0, 0, 256, 240));
                framebuffer.Unlock();
                GameCanvas.Source = framebuffer;
            }
            StatusText.Text = "请加载ROM文件";
        }

        private void SetupInput()
        {
            // 使用预览事件，保证窗口内其他控件获得焦点时也能响应游戏输入
            this.PreviewKeyDown += MainWindow_KeyDown;
            this.PreviewKeyUp += MainWindow_KeyUp;
            this.Focusable = true;
            SetupGamepadPolling();
        }

        private void SetupGamepadPolling()
        {
            if (gamepadPollingTimer != null)
            {
                return;
            }

            gamepadPollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            gamepadPollingTimer.Tick += GamepadPollingTimer_Tick;
            gamepadPollingTimer.Start();
        }

        private void GamepadPollingTimer_Tick(object? sender, EventArgs e)
        {
            UpdateGamepadInputs(0, player1GamepadMapping, player1GamepadPressedButtons);
            UpdateGamepadInputs(1, player2GamepadMapping, player2GamepadPressedButtons);

            bool nextTurboPulseActive = GetTurboPulseActive();
            if (nextTurboPulseActive != turboPulseActive)
            {
                turboPulseActive = nextTurboPulseActive;

                if (IsAnyTurboHeld())
                {
                    ApplyTurboButtons();
                }
            }
        }

        private void UpdateGamepadInputs(int playerIndex, GamepadMapping mapping, HashSet<GamepadButton> pressedButtons)
        {
            var nextPressedButtons = new HashSet<GamepadButton>();
            if (XInputGamepad.TryGetState(mapping.DeviceIndex, out XInputGamepad.Snapshot snapshot))
            {
                foreach (GamepadButton button in XInputGamepad.GetPressedButtons(snapshot))
                {
                    nextPressedButtons.Add(button);
                }
            }

            if (pressedButtons.SetEquals(nextPressedButtons))
            {
                return;
            }

            pressedButtons.Clear();
            foreach (GamepadButton button in nextPressedButtons)
            {
                pressedButtons.Add(button);
            }

            ApplyAllControllerButtonsForPlayer(playerIndex);
        }

        private void LoadROM_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "NES ROM文件 (*.nes)|*.nes|所有文件 (*.*)|*.*";
            openFileDialog.Title = "选择NES ROM文件";

            if (openFileDialog.ShowDialog() == true)
            {
                LoadROMFile(openFileDialog.FileName);
            }
        }
        
        private void LoadROMFile(string filePath)
        {
            try
            {
                // Console.WriteLine($"Starting to load ROM file: {filePath}");
                
                // 如果当前有游戏在运行，先停止它
                if (isRunning)
                {
                    // Console.WriteLine("Stopping current game before loading new ROM");
                    StopGameLoop();
                }
                
                // 加载新ROM
                byte[] romData = File.ReadAllBytes(filePath);
                nes = new NES();
                nes.SetMasterVolume(masterVolume);
                nes.LoadCartridge(romData);
                nes.Reset();
                ApplyAllControllerButtons();
                
                StatusText.Text = $"Loaded: {Path.GetFileName(filePath)} - 点击开始运行";
                // Console.WriteLine($"ROM file loaded successfully: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to load ROM: {ex.Message}";
                // Console.WriteLine(errorMsg);
                MessageBox.Show(errorMsg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null && !isRunning)
            {
                // Console.WriteLine("开始运行模拟器");
                StartGameLoop();
                StatusText.Text = "运行中...";
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                // Console.WriteLine("暂停模拟器");
                StopGameLoop();
                StatusText.Text = "已暂停";
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                bool wasRunning = isRunning;
                if (isRunning)
                {
                    StopGameLoop();
                }
                
                nes.Reset();
                precisionTimer?.Reset();
                clockSynchronizer?.Reset();
                
                if (wasRunning)
                {
                    StartGameLoop();
                }
                
                StatusText.Text = "已重置";
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            StopGameLoop();
            this.Close();
        }
        
        private void OpenDisassemblyWindow_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                DisassemblyWindow disassemblyWindow = new DisassemblyWindow(nes);
                disassemblyWindow.Owner = this;
                disassemblyWindow.Show();
            }
            else
            {
                MessageBox.Show("请先加载ROM文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void OpenPPUDebugWindow_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                var ppu = nes.GetPPU();
                var cartridge = nes.GetCartridge();
                if (ppu != null && cartridge != null)
                {
                    PPUDebugWindow ppuDebugWindow = new PPUDebugWindow();
                    ppuDebugWindow.SetPPU(ppu, cartridge);
                    ppuDebugWindow.Owner = this;
                    ppuDebugWindow.Show();
                }
                else
                {
                    MessageBox.Show("PPU或卡带未正确初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("请先加载ROM文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenNameTableDebugWindow_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                var ppu = nes.GetPPU();
                var cartridge = nes.GetCartridge();
                if (ppu != null && cartridge != null)
                {
                    NameTableDebugWindow nameTableDebugWindow = new NameTableDebugWindow();
                    nameTableDebugWindow.SetPPU(ppu, cartridge);
                    nameTableDebugWindow.Owner = this;
                    nameTableDebugWindow.Show();
                }
                else
                {
                    MessageBox.Show("PPU或卡带未正确初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("请先加载ROM文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenPlayer1KeyBindingWindow_Click(object sender, RoutedEventArgs e)
        {
            OpenKeyBindingWindowForPlayer(0);
        }

        private void OpenPlayer2KeyBindingWindow_Click(object sender, RoutedEventArgs e)
        {
            OpenKeyBindingWindowForPlayer(1);
        }

        private void OpenKeyBindingWindowForPlayer(int playerIndex)
        {
            KeyboardMapping currentMapping = playerIndex == 0 ? player1KeyboardMapping : player2KeyboardMapping;
            GamepadMapping currentGamepadMapping = playerIndex == 0 ? player1GamepadMapping : player2GamepadMapping;
            KeyBindingWindow keyBindingWindow = new KeyBindingWindow(currentMapping, currentGamepadMapping, playerIndex)
            {
                Owner = this
            };

            if (keyBindingWindow.ShowDialog() == true)
            {
                if (playerIndex == 0)
                {
                    player1KeyboardMapping = keyBindingWindow.ResultKeyboardMapping;
                    player1GamepadMapping = keyBindingWindow.ResultGamepadMapping;
                    player1KeyboardMapping.Save(GetKeyboardMappingPath(0));
                    player1GamepadMapping.Save(GetGamepadMappingPath(0));
                }
                else
                {
                    player2KeyboardMapping = keyBindingWindow.ResultKeyboardMapping;
                    player2GamepadMapping = keyBindingWindow.ResultGamepadMapping;
                    player2KeyboardMapping.Save(GetKeyboardMappingPath(1));
                    player2GamepadMapping.Save(GetGamepadMappingPath(1));
                }

                ApplyAllControllerButtons();
                StatusText.Text = $"输入已更新: {GetControlSummary()}";
            }
        }

        private void SetVolume50_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(0.5f);
        private void SetVolume100_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(1.0f);
        private void SetVolume150_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(1.5f);
        private void SetVolume200_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(2.0f);
        private void SetVolume250_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(2.5f);
        private void SetVolume300_Click(object sender, RoutedEventArgs e) => ApplyMasterVolume(3.0f);
        private void SetSpeed85_Click(object sender, RoutedEventArgs e) => ApplyEmulationSpeed(0.85);
        private void SetSpeed925_Click(object sender, RoutedEventArgs e) => ApplyEmulationSpeed(0.925);
        private void SetSpeed100_Click(object sender, RoutedEventArgs e) => ApplyEmulationSpeed(1.0);
        private void SetSpeed1075_Click(object sender, RoutedEventArgs e) => ApplyEmulationSpeed(1.075);
        private void SetSpeed115_Click(object sender, RoutedEventArgs e) => ApplyEmulationSpeed(1.15);

        private void ApplyMasterVolume(float volume)
        {
            masterVolume = volume;
            nes?.SetMasterVolume(masterVolume);
            StatusText.Text = $"音量已设置为 {(int)(masterVolume * 100)}%";
        }

        private void ApplyEmulationSpeed(double speedMultiplier)
        {
            emulationSpeed = Math.Clamp(speedMultiplier, 0.85, 1.15);
            precisionTimer?.SetSpeedMultiplier(emulationSpeed);
            UpdateSpeedControls();
            StatusText.Text = $"运行速度已设置为 {emulationSpeed * 100:0.#}%";
        }

        private void EmulationSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isUpdatingSpeedControls)
            {
                return;
            }

            ApplyEmulationSpeed(e.NewValue / 100.0);
        }

        private void UpdateSpeedControls()
        {
            isUpdatingSpeedControls = true;
            try
            {
                if (EmulationSpeedSlider != null)
                {
                    EmulationSpeedSlider.Value = emulationSpeed * 100.0;
                }

                if (SpeedText != null)
                {
                    SpeedText.Text = $"{emulationSpeed * 100:0.0}%";
                }
            }
            finally
            {
                isUpdatingSpeedControls = false;
            }
        }

        private void StartGameLoop()
        {
            if (isRunning || nes == null || precisionTimer == null || clockSynchronizer == null)
                return;
                
            isRunning = true;
            nes.Start();
            Interlocked.Exchange(ref displayUpdatePending, 0);
            
            cancellationTokenSource = new CancellationTokenSource();
            gameLoopThread = new Thread(() => GameLoopThread(cancellationTokenSource.Token))
            {
                IsBackground = true,
                Name = "NES-GameLoop",
                Priority = ThreadPriority.AboveNormal
            };
            gameLoopThread.Start();
        }
        
        private void StopGameLoop()
        {
            if (!isRunning)
                return;
                
            isRunning = false;
            nes?.Stop();
            Interlocked.Exchange(ref displayUpdatePending, 0);
            
            cancellationTokenSource?.Cancel();
            
            try
            {
                gameLoopThread?.Join(1000); // 等待最多1秒
            }
            catch (ThreadStateException)
            {
                // 线程已经结束
            }
            
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            gameLoopThread = null;
        }
        
        private void GameLoopThread(CancellationToken cancellationToken)
        {
            if (nes == null || precisionTimer == null || clockSynchronizer == null)
                return;
                
            // Console.WriteLine("高精度游戏循环线程启动");
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && isRunning)
                {
                    try
                    {
                        long frameLoopStartTicks = precisionTimer.GetCurrentTicks();
                        long emulateStartTicks = frameLoopStartTicks;

                        // 运行一帧（使用高精度定时器）
                        nes.RunFrameWithPrecisionTiming();
                        long afterEmulateTicks = precisionTimer.GetCurrentTicks();
                        
                        // 等待帧完成时间
                        precisionTimer.WaitForNextFrame();
                        long afterWaitTicks = precisionTimer.GetCurrentTicks();

                        #region debug-point C:game-loop-frame
                        if (debugFrameLoopLogCount < 50)
                        {
                            debugFrameLoopLogCount++;
                            double emulateMs = (afterEmulateTicks - emulateStartTicks) * 1000.0 / Stopwatch.Frequency;
                            double waitMs = (afterWaitTicks - afterEmulateTicks) * 1000.0 / Stopwatch.Frequency;
                            double frameLoopMs = (afterWaitTicks - frameLoopStartTicks) * 1000.0 / Stopwatch.Frequency;
                            double actualFps = precisionTimer.GetTimerInfo().ActualFPS;
                            DebugReport("post-fix", "A", "MainWindow.GameLoopThread", "completed emulator frame loop", new
                            {
                                frameLoopMs,
                                emulateMs,
                                waitMs,
                                actualFps,
                                skippedDisplayUpdates,
                                displayUpdatePending
                            });
                        }
                        #endregion
                        
                        // 如果 UI 线程已堆积待渲染任务，就不要继续排队，避免显示拖慢整体节奏。
                        if (Interlocked.CompareExchange(ref displayUpdatePending, 1, 0) == 0)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    if (isRunning)
                                    {
                                        UpdateDisplay();
                                        UpdateFPS();
                                    }
                                }
                                finally
                                {
                                    Interlocked.Exchange(ref displayUpdatePending, 0);
                                }
                            }), DispatcherPriority.Render);
                        }
                        else
                        {
                            skippedDisplayUpdates++;

                            #region debug-point D:ui-frame-skip
                            if (debugUiSkipLogCount < 30)
                            {
                                debugUiSkipLogCount++;
                                DebugReport("post-fix", "C", "MainWindow.GameLoopThread", "skipped scheduling UI frame update because render task is pending", new
                                {
                                    skippedDisplayUpdates,
                                    displayUpdatePending
                                });
                            }
                            #endregion
                        }
                    }
                    catch (Exception ex)
                    {
                        // 如果出现错误，暂停模拟
                        // Console.WriteLine($"游戏循环错误: {ex.Message}");
                        
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            isRunning = false;
                            nes?.Stop();
                            string errorMsg = $"运行时错误: {ex.Message}\n堆栈跟踪: {ex.StackTrace}";
                            // Console.WriteLine(errorMsg);
                            StatusText.Text = $"运行时错误: {ex.Message}";
                            MessageBox.Show(errorMsg, "运行时错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }));
                        
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需要处理
            }
            
            // Console.WriteLine("高精度游戏循环线程结束");
        }

        private void UpdateDisplay()
        {
            if (nes != null && framebuffer != null)
            {
                try
                {
                    long displayStartTicks = Stopwatch.GetTimestamp();
                    var frameBuffer = nes.GetFrameBuffer();
                    
                    if (frameBuffer == null)
                    {
                        StatusText.Text = "错误：帧缓冲区为空";
                        return;
                    }
                    
                    // 移除调试输出以提高性能
                    
                    // 将帧缓冲区数据复制到WPF位图
                    framebuffer.Lock();
                    unsafe
                    {
                        var backBuffer = (uint*)framebuffer.BackBuffer;
                        
                        // 复制PPU帧缓冲区数据
                        for (int i = 0; i < 256 * 240; i++)
                        {
                            if (i < frameBuffer.Length)
                            {
                                backBuffer[i] = frameBuffer[i];
                            }
                            else
                            {
                                backBuffer[i] = 0xFF000000; // 黑色
                            }
                        }
                    }
                    framebuffer.AddDirtyRect(new Int32Rect(0, 0, 256, 240));
                    framebuffer.Unlock();

                    #region debug-point F:update-display
                    if (debugDisplayLogCount < 30)
                    {
                        debugDisplayLogCount++;
                        DebugReport("post-fix", "C", "MainWindow.UpdateDisplay", "completed framebuffer upload", new
                        {
                            displayMs = (Stopwatch.GetTimestamp() - displayStartTicks) * 1000.0 / Stopwatch.Frequency,
                            skippedDisplayUpdates,
                            displayUpdatePending
                        });
                    }
                    #endregion
                    
                    // 显示PPU状态信息
                    // string runningStatus = nes.IsRunning ? "运行中" : "已停止";
                    // var ppu = nes.GetPPU();
                    // var cpu = nes.GetCPU();
                    // if (ppu != null)
                    // {
                    //     StatusText.Text = $"状态: {runningStatus} | CPU: PC=0x{cpu.PC:X4} | PPU: CTRL=0x{ppu.PPUCTRL:X2}, MASK=0x{ppu.PPUMASK:X2}, 扫描线={ppu.Scanline}, 周期={ppu.Cycle}";
                    // }
                    // else
                    // {
                    //     StatusText.Text = $"状态: {runningStatus}";
                    // }
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"显示更新错误: {ex.Message}";
                }
            }
            else if (framebuffer != null)
            {
                ClearDisplay();
            }
        }

        private void UpdateFPS()
        {
            if (precisionTimer == null)
                return;
                
            frameCount++;
            long currentTime = precisionTimer.GetCurrentTicks();
            long elapsedTicks = currentTime - lastFPSUpdate;
            double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
            
            if (elapsedSeconds >= 1.0)
            {
                double fps = frameCount / elapsedSeconds;
                var timerInfo = precisionTimer.GetTimerInfo();
                var syncStats = clockSynchronizer?.GetSyncStats();
                
                FPSText.Text = $"FPS: {fps:F1} | 实际: {timerInfo.ActualFPS:F1} | 方差: {timerInfo.FrameTimeVariance:F2}";
                
                if (syncStats.HasValue)
                {
                    var stats = syncStats.Value;
                    // Console.WriteLine($"性能统计 - FPS: {fps:F1}, CPU利用率: {stats.CpuUtilization:P1}, PPU利用率: {stats.PpuUtilization:P1}");
                }
                
                frameCount = 0;
                lastFPSUpdate = currentTime;
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (nes != null)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                bool handled = false;
                if (player1KeyboardMapping.TryGetButton(key, out ControllerButton player1Button))
                {
                    SetKeyboardButtonState(0, player1Button, true);
                    handled = true;
                }

                if (player1KeyboardMapping.TryGetTurboButton(key, out TurboButton player1TurboButton))
                {
                    SetKeyboardTurboButtonState(0, player1TurboButton, true);
                    handled = true;
                }

                if (player2KeyboardMapping.TryGetButton(key, out ControllerButton player2Button))
                {
                    SetKeyboardButtonState(1, player2Button, true);
                    handled = true;
                }

                if (player2KeyboardMapping.TryGetTurboButton(key, out TurboButton player2TurboButton))
                {
                    SetKeyboardTurboButtonState(1, player2TurboButton, true);
                    handled = true;
                }

                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (nes != null)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                bool handled = false;
                if (player1KeyboardMapping.TryGetButton(key, out ControllerButton player1Button))
                {
                    SetKeyboardButtonState(0, player1Button, false);
                    handled = true;
                }

                if (player1KeyboardMapping.TryGetTurboButton(key, out TurboButton player1TurboButton))
                {
                    SetKeyboardTurboButtonState(0, player1TurboButton, false);
                    handled = true;
                }

                if (player2KeyboardMapping.TryGetButton(key, out ControllerButton player2Button))
                {
                    SetKeyboardButtonState(1, player2Button, false);
                    handled = true;
                }

                if (player2KeyboardMapping.TryGetTurboButton(key, out TurboButton player2TurboButton))
                {
                    SetKeyboardTurboButtonState(1, player2TurboButton, false);
                    handled = true;
                }

                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        private void SetKeyboardButtonState(int playerIndex, ControllerButton button, bool pressed)
        {
            HashSet<ControllerButton> pressedButtons = GetKeyboardPressedButtons(playerIndex);
            if (pressed)
            {
                pressedButtons.Add(button);
            }
            else
            {
                pressedButtons.Remove(button);
            }

            ApplyControllerButton(playerIndex, button);
        }

        private void SetKeyboardTurboButtonState(int playerIndex, TurboButton button, bool pressed)
        {
            HashSet<TurboButton> pressedButtons = GetKeyboardPressedTurboButtons(playerIndex);
            if (pressed)
            {
                pressedButtons.Add(button);
            }
            else
            {
                pressedButtons.Remove(button);
            }

            ApplyTurboButton(playerIndex, button);
        }

        private void ApplyAllControllerButtons()
        {
            ApplyAllControllerButtonsForPlayer(0);
            ApplyAllControllerButtonsForPlayer(1);
        }

        private void ApplyAllControllerButtonsForPlayer(int playerIndex)
        {
            foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
            {
                ApplyControllerButton(playerIndex, button);
            }
        }

        private void ApplyTurboButtons()
        {
            ApplyTurboButtonsForPlayer(0);
            ApplyTurboButtonsForPlayer(1);
        }

        private void ApplyTurboButtonsForPlayer(int playerIndex)
        {
            ApplyTurboButton(playerIndex, TurboButton.A);
            ApplyTurboButton(playerIndex, TurboButton.B);
        }

        private void ApplyTurboButton(int playerIndex, TurboButton button)
        {
            ApplyControllerButton(playerIndex, button == TurboButton.A ? ControllerButton.A : ControllerButton.B);
        }

        private void ApplyControllerButton(int playerIndex, ControllerButton button)
        {
            if (nes == null)
            {
                return;
            }

            bool isPressed = GetKeyboardPressedButtons(playerIndex).Contains(button) ||
                             IsPressedByGamepad(playerIndex, button) ||
                             IsTurboApplied(playerIndex, button);
            nes.SetControllerButton(playerIndex, button, isPressed);
        }

        private HashSet<ControllerButton> GetKeyboardPressedButtons(int playerIndex)
        {
            return playerIndex == 0 ? player1KeyboardPressedButtons : player2KeyboardPressedButtons;
        }

        private HashSet<TurboButton> GetKeyboardPressedTurboButtons(int playerIndex)
        {
            return playerIndex == 0 ? player1KeyboardPressedTurboButtons : player2KeyboardPressedTurboButtons;
        }

        private HashSet<GamepadButton> GetPressedGamepadButtons(int playerIndex)
        {
            return playerIndex == 0 ? player1GamepadPressedButtons : player2GamepadPressedButtons;
        }

        private GamepadMapping GetGamepadMapping(int playerIndex)
        {
            return playerIndex == 0 ? player1GamepadMapping : player2GamepadMapping;
        }

        private bool IsPressedByGamepad(int playerIndex, ControllerButton button)
        {
            GamepadButton inputButton = GetGamepadMapping(playerIndex).GetButton(button);
            return inputButton != GamepadButton.None && GetPressedGamepadButtons(playerIndex).Contains(inputButton);
        }

        private bool IsTurboApplied(int playerIndex, ControllerButton button)
        {
            TurboButton? turboButton = button switch
            {
                ControllerButton.A => TurboButton.A,
                ControllerButton.B => TurboButton.B,
                _ => null
            };

            if (turboButton == null)
            {
                return false;
            }

            return turboPulseActive && IsTurboHeld(playerIndex, turboButton.Value);
        }

        private bool IsTurboHeld(int playerIndex, TurboButton button)
        {
            if (GetKeyboardPressedTurboButtons(playerIndex).Contains(button))
            {
                return true;
            }

            GamepadButton inputButton = GetGamepadMapping(playerIndex).GetTurboButton(button);
            return inputButton != GamepadButton.None && GetPressedGamepadButtons(playerIndex).Contains(inputButton);
        }

        private bool IsAnyTurboHeld()
        {
            return IsTurboHeld(0, TurboButton.A) ||
                   IsTurboHeld(0, TurboButton.B) ||
                   IsTurboHeld(1, TurboButton.A) ||
                   IsTurboHeld(1, TurboButton.B);
        }

        private bool GetTurboPulseActive()
        {
            long phase = turboStopwatch.ElapsedMilliseconds / TurboPulseIntervalMs;
            return phase % 2 == 0;
        }

        private static string GetKeyboardMappingPath(int playerIndex)
        {
            return Path.Combine(AppContext.BaseDirectory, "config", $"keyboard-mapping-player{playerIndex + 1}.json");
        }

        private static string GetGamepadMappingPath(int playerIndex)
        {
            return Path.Combine(AppContext.BaseDirectory, "config", $"gamepad-mapping-player{playerIndex + 1}.json");
        }

        private string GetControlSummary()
        {
            return $"P1 键盘 A {player1KeyboardMapping.GetKey(ControllerButton.A)} B {player1KeyboardMapping.GetKey(ControllerButton.B)} 连A {player1KeyboardMapping.GetTurboKey(TurboButton.A)} 连B {player1KeyboardMapping.GetTurboKey(TurboButton.B)} | 手柄 {player1GamepadMapping.DeviceIndex + 1} A {player1GamepadMapping.GetButton(ControllerButton.A).GetDisplayName()} B {player1GamepadMapping.GetButton(ControllerButton.B).GetDisplayName()} 连A {player1GamepadMapping.GetTurboButton(TurboButton.A).GetDisplayName()} 连B {player1GamepadMapping.GetTurboButton(TurboButton.B).GetDisplayName()} | P2 键盘 A {player2KeyboardMapping.GetKey(ControllerButton.A)} B {player2KeyboardMapping.GetKey(ControllerButton.B)} 连A {player2KeyboardMapping.GetTurboKey(TurboButton.A)} 连B {player2KeyboardMapping.GetTurboKey(TurboButton.B)} | 手柄 {player2GamepadMapping.DeviceIndex + 1} A {player2GamepadMapping.GetButton(ControllerButton.A).GetDisplayName()} B {player2GamepadMapping.GetButton(ControllerButton.B).GetDisplayName()} 连A {player2GamepadMapping.GetTurboButton(TurboButton.A).GetDisplayName()} 连B {player2GamepadMapping.GetTurboButton(TurboButton.B).GetDisplayName()}";
        }
    }
}
