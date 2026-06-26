using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NESEmulator.Core;
using NESEmulator.Core.CPU;
using NESEmulator.Core.Memory;

namespace NESEmulator
{
    /// <summary>
    /// DisassemblyWindow.xaml 的交互逻辑
    /// </summary>
    public partial class DisassemblyWindow : Window
    {
        private NES nes;
        private CPU6502 cpu;
        private Disassembler disassembler;
        private ObservableCollection<DisassembledInstruction> disassemblyList;
        private DispatcherTimer updateTimer;
        private bool isRunning = false;
        private bool isSingleStepping = false;
        
        // 当前显示的反汇编起始地址
        private ushort currentStartAddress = 0x8000; // 默认从PRG ROM起始地址开始
        
        public DisassemblyWindow(NES nes)
        {
            InitializeComponent();
            
            this.nes = nes;
            this.cpu = nes.GetCPU(); // 假设NES类提供了获取CPU的方法
            this.disassembler = new Disassembler(nes.GetMemoryBus()); // 假设NES类提供了获取MemoryBus的方法
            
            // 初始化反汇编列表
            disassemblyList = new ObservableCollection<DisassembledInstruction>();
            DisassemblyListView.ItemsSource = disassemblyList;
            
            // 设置更新定时器
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(100); // 100ms更新一次
            updateTimer.Tick += UpdateTimer_Tick;
            
            // 初始化反汇编显示
            RefreshDisassembly();
            UpdateCPUState();
            
            // 窗口关闭时停止定时器
            this.Closed += (s, e) => {
                updateTimer.Stop();
                if (isSingleStepping)
                {
                    nes.Resume(); // 恢复模拟器运行
                }
            };
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (isRunning && !isSingleStepping)
            {
                // 更新CPU状态
                UpdateCPUState();
                
                // 如果当前PC不在视图中，更新反汇编视图
                if (!IsAddressVisible(cpu.PC))
                {
                    JumpToPC();
                }
                else
                {
                    // 高亮当前执行的指令
                    HighlightCurrentInstruction();
                }
            }
        }

        private void RefreshDisassembly()
        {
            disassemblyList.Clear();
            
            // 反汇编从当前地址开始的一段代码
            List<DisassembledInstruction> instructions = disassembler.DisassembleRange(currentStartAddress, (ushort)(currentStartAddress + 100));
            foreach (var instruction in instructions)
            {
                disassemblyList.Add(instruction);
            }
            
            // 高亮当前执行的指令
            HighlightCurrentInstruction();
        }

        private void UpdateCPUState()
        {
            // 更新寄存器显示
            RegisterA.Text = $"${cpu.A:X2}";
            RegisterX.Text = $"${cpu.X:X2}";
            RegisterY.Text = $"${cpu.Y:X2}";
            RegisterSP.Text = $"${cpu.SP:X2}";
            RegisterPC.Text = $"${cpu.PC:X4}";
            RegisterP.Text = $"${cpu.P:X2} ({GetFlagsString()})";
            
            // 更新标志位复选框
            FlagN.IsChecked = (cpu.P & 0x80) != 0; // 负数标志
            FlagV.IsChecked = (cpu.P & 0x40) != 0; // 溢出标志
            FlagB.IsChecked = (cpu.P & 0x10) != 0; // 中断标志
            FlagD.IsChecked = (cpu.P & 0x08) != 0; // 十进制模式标志
            FlagI.IsChecked = (cpu.P & 0x04) != 0; // 中断禁止标志
            FlagZ.IsChecked = (cpu.P & 0x02) != 0; // 零标志
            FlagC.IsChecked = (cpu.P & 0x01) != 0; // 进位标志
        }

        private string GetFlagsString()
        {
            char[] flags = new char[8];
            flags[0] = (cpu.P & 0x80) != 0 ? 'N' : '-'; // 负数标志
            flags[1] = (cpu.P & 0x40) != 0 ? 'V' : '-'; // 溢出标志
            flags[2] = '-'; // 未使用位
            flags[3] = (cpu.P & 0x10) != 0 ? 'B' : '-'; // 中断标志
            flags[4] = (cpu.P & 0x08) != 0 ? 'D' : '-'; // 十进制模式标志
            flags[5] = (cpu.P & 0x04) != 0 ? 'I' : '-'; // 中断禁止标志
            flags[6] = (cpu.P & 0x02) != 0 ? 'Z' : '-'; // 零标志
            flags[7] = (cpu.P & 0x01) != 0 ? 'C' : '-'; // 进位标志
            return new string(flags);
        }

        private void HighlightCurrentInstruction()
        {
            // 查找当前PC对应的指令
            for (int i = 0; i < disassemblyList.Count; i++)
            {
                if (disassemblyList[i].Address == cpu.PC)
                {
                    DisassemblyListView.SelectedIndex = i;
                    DisassemblyListView.ScrollIntoView(disassemblyList[i]);
                    break;
                }
            }
        }

        private bool IsAddressVisible(ushort address)
        {
            foreach (var instruction in disassemblyList)
            {
                if (instruction.Address == address)
                {
                    return true;
                }
            }
            return false;
        }

        private void JumpToPC()
        {
            currentStartAddress = cpu.PC;
            RefreshDisassembly();
        }

        private void StepButton_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                // 暂停模拟器主循环
                if (!isSingleStepping)
                {
                    nes.Pause();
                    isSingleStepping = true;
                    updateTimer.Start();
                    StatusText.Text = "Step mode";
                }
                
                // 执行一条指令
                cpu.ExecuteInstruction();
                
                // 更新显示
                UpdateCPUState();
                
                // 如果当前PC不在视图中，更新反汇编视图
                if (!IsAddressVisible(cpu.PC))
                {
                    JumpToPC();
                }
                else
                {
                    // 高亮当前执行的指令
                    HighlightCurrentInstruction();
                }
            }
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null)
            {
                if (isSingleStepping)
                {
                    // 从单步模式恢复正常运行
                    nes.Resume();
                    isSingleStepping = false;
                }
                
                isRunning = true;
                updateTimer.Start();
                StatusText.Text = "Running";
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (nes != null && isRunning)
            {
                isRunning = false;
                updateTimer.Stop();
                
                if (!isSingleStepping)
                {
                    nes.Pause();
                }
                
                StatusText.Text = "Paused";
                
                // 更新显示
                UpdateCPUState();
                if (!IsAddressVisible(cpu.PC))
                {
                    JumpToPC();
                }
                else
                {
                    HighlightCurrentInstruction();
                }
            }
        }

        private void JumpToPCButton_Click(object sender, RoutedEventArgs e)
        {
            JumpToPC();
        }

        private void JumpToAddressButton_Click(object sender, RoutedEventArgs e)
        {
            if (ushort.TryParse(AddressTextBox.Text.Replace("$", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                currentStartAddress = address;
                RefreshDisassembly();
                StatusText.Text = $"Jumped to ${address:X4}";
            }
            else
            {
                MessageBox.Show("Please enter a valid hexadecimal address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (ushort.TryParse(MemoryAddressTextBox.Text.Replace("$", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort address))
            {
                // 显示内存内容
                DisplayMemory(address);
            }
            else
            {
                MessageBox.Show("Please enter a valid hexadecimal address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisplayMemory(ushort startAddress)
        {
            var memoryBus = nes.GetMemoryBus();
            StringBuilder sb = new StringBuilder();
            
            // 显示16行，每行16字节
            for (int row = 0; row < 16; row++)
            {
                ushort rowAddress = (ushort)(startAddress + row * 16);
                sb.AppendFormat("${0:X4}: ", rowAddress);
                
                // 显示十六进制值
                for (int col = 0; col < 16; col++)
                {
                    ushort address = (ushort)(rowAddress + col);
                    byte value = memoryBus.Read(address);
                    sb.AppendFormat("{0:X2} ", value);
                    
                    // 在8字节处添加额外空格
                    if (col == 7)
                        sb.Append(" ");
                }
                
                sb.Append(" | ");
                
                // 显示ASCII值
                for (int col = 0; col < 16; col++)
                {
                    ushort address = (ushort)(rowAddress + col);
                    byte value = memoryBus.Read(address);
                    char c = (value >= 32 && value < 127) ? (char)value : '.';
                    sb.Append(c);
                }
                
                sb.AppendLine();
            }
            
            MemoryViewerTextBox.Text = sb.ToString();
        }
    }
}
