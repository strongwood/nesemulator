using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NESEmulator.Core.PPU;
using NESEmulator.Core.Cartridge;

namespace NESEmulator
{
    public partial class PPUDebugWindow : Window
    {
        private PPU2C02? ppu;
        private ICartridge? cartridge;
        private DispatcherTimer? refreshTimer;
        
        // Pattern Table位图
        private WriteableBitmap? patternTable0Bitmap;
        private WriteableBitmap? patternTable1Bitmap;
        
        // NES调色板
        private static readonly uint[] NesPalette = {
            0xFF545454, 0xFF001E74, 0xFF081090, 0xFF300088, 0xFF440064, 0xFF5C0030, 0xFF540400, 0xFF3C1800,
            0xFF202A00, 0xFF083A00, 0xFF004000, 0xFF003C00, 0xFF00323C, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFF989698, 0xFF084CC4, 0xFF3032EC, 0xFF5C1EE4, 0xFF8814B0, 0xFFA01464, 0xFF982220, 0xFF783C00,
            0xFF545A00, 0xFF287200, 0xFF087C00, 0xFF007628, 0xFF006678, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFFECEEEC, 0xFF4C9AEC, 0xFF787CEC, 0xFFB062EC, 0xFFE454EC, 0xFFEC58B4, 0xFFEC6A64, 0xFFD48820,
            0xFFA0AA00, 0xFF74C400, 0xFF4CD020, 0xFF38CC6C, 0xFF38B4CC, 0xFF3C3C3C, 0xFF000000, 0xFF000000,
            0xFFECEEEC, 0xFFA8CCEC, 0xFFBCBCEC, 0xFFD4B2EC, 0xFFECAEEC, 0xFFECAED4, 0xFFECB4B0, 0xFFE4C490,
            0xFFCCD278, 0xFFB4DE78, 0xFFA8E290, 0xFF98E2B4, 0xFF98D8D8, 0xFFA0A2A0, 0xFF000000, 0xFF000000
        };
        
        public PPUDebugWindow()
        {
            InitializeComponent();
            InitializeBitmaps();
            SetupRefreshTimer();
        }
        
        public void SetPPU(PPU2C02 ppu, ICartridge cartridge)
        {
            this.ppu = ppu;
            this.cartridge = cartridge;
            RefreshAll();
        }
        
        private void InitializeBitmaps()
        {
            // 创建Pattern Table位图 (128x128 tiles, 每个tile 8x8像素)
            patternTable0Bitmap = new WriteableBitmap(128, 128, 96, 96, PixelFormats.Bgra32, null);
            patternTable1Bitmap = new WriteableBitmap(128, 128, 96, 96, PixelFormats.Bgra32, null);
            
            PatternTable0Image.Source = patternTable0Bitmap;
            PatternTable1Image.Source = patternTable1Bitmap;
        }
        
        private void SetupRefreshTimer()
        {
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(100); // 10 FPS刷新
            refreshTimer.Tick += (s, e) => {
                if (AutoRefreshCheckBox.IsChecked == true)
                {
                    RefreshAll();
                }
            };
            refreshTimer.Start();
        }
        
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }
        
        private void RefreshAll()
        {
            if (ppu == null) return;
            
            RefreshPatternTables();
            RefreshPalettes();
            RefreshPPUStatus();
        }
        
        private void RefreshPatternTables()
        {
            if (ppu == null || cartridge == null || patternTable0Bitmap == null || patternTable1Bitmap == null)
                return;
                
            // 渲染Pattern Table 0 ($0000-$0FFF) - 使用背景调色板0
            RenderPatternTable(patternTable0Bitmap, 0x0000, 0x3F00);
            
            // 渲染Pattern Table 1 ($1000-$1FFF) - 使用精灵调色板0
            RenderPatternTable(patternTable1Bitmap, 0x1000, 0x3F10);
        }
        
        private void RenderPatternTable(WriteableBitmap bitmap, ushort baseAddress, ushort paletteBaseAddress)
        {
            if (cartridge == null) return;
            
            bitmap.Lock();
            
            unsafe
            {
                uint* pixels = (uint*)bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride / 4;
                
                // 16x16 tiles grid
                for (int tileY = 0; tileY < 16; tileY++)
                {
                    for (int tileX = 0; tileX < 16; tileX++)
                    {
                        int tileIndex = tileY * 16 + tileX;
                        ushort tileAddress = (ushort)(baseAddress + tileIndex * 16);
                        
                        // 渲染8x8 tile
                        for (int pixelY = 0; pixelY < 8; pixelY++)
                        {
                            byte lowByte = cartridge.ReadCHR((ushort)(tileAddress + pixelY));
                            byte highByte = cartridge.ReadCHR((ushort)(tileAddress + pixelY + 8));
                            
                            for (int pixelX = 0; pixelX < 8; pixelX++)
                            {
                                int bit = 7 - pixelX;
                                int colorIndex = ((highByte >> bit) & 1) << 1 | ((lowByte >> bit) & 1);
                                
                                // 使用指定调色板的颜色显示Pattern Table
                                uint color;
                                if (ppu != null)
                                {
                                    byte paletteColorIndex = ppu.GetPaletteColor(paletteBaseAddress + colorIndex);
                                    color = NesPalette[paletteColorIndex & 0x3F];
                                    
                                    // 如果调色板颜色为0（透明色），使用灰度显示以便调试
                                    if (colorIndex > 0 && paletteColorIndex == 0)
                                    {
                                        color = colorIndex switch
                                        {
                                            1 => 0xFF555555, // 深灰
                                            2 => 0xFFAAAAAA, // 浅灰
                                            3 => 0xFFFFFFFF, // 白色
                                            _ => 0xFF000000
                                        };
                                    }
                                }
                                else
                                {
                                    // 备用灰度调色板
                                    color = colorIndex switch
                                    {
                                        0 => 0xFF000000, // 黑色
                                        1 => 0xFF555555, // 深灰
                                        2 => 0xFFAAAAAA, // 浅灰
                                        3 => 0xFFFFFFFF, // 白色
                                        _ => 0xFF000000
                                    };
                                }
                                
                                int screenX = tileX * 8 + pixelX;
                                int screenY = tileY * 8 + pixelY;
                                
                                if (screenX < 128 && screenY < 128)
                                {
                                    pixels[screenY * stride + screenX] = color;
                                }
                            }
                        }
                    }
                }
            }
            
            bitmap.AddDirtyRect(new Int32Rect(0, 0, 128, 128));
            bitmap.Unlock();
        }
        
        private void RefreshPalettes()
        {
            if (ppu == null) return;
            
            // 清除现有的调色板显示
            BackgroundPalettesPanel.Children.Clear();
            SpritePalettesPanel.Children.Clear();
            
            // 显示背景调色板 (0x3F00-0x3F0F)
            for (int paletteIndex = 0; paletteIndex < 4; paletteIndex++)
            {
                var palettePanel = CreatePalettePanel($"背景调色板 {paletteIndex}", 0x3F00 + paletteIndex * 4);
                BackgroundPalettesPanel.Children.Add(palettePanel);
            }
            
            // 显示精灵调色板 (0x3F10-0x3F1F)
            for (int paletteIndex = 0; paletteIndex < 4; paletteIndex++)
            {
                var palettePanel = CreatePalettePanel($"精灵调色板 {paletteIndex}", 0x3F10 + paletteIndex * 4);
                SpritePalettesPanel.Children.Add(palettePanel);
            }
        }
        
        private StackPanel CreatePalettePanel(string title, int baseAddress)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 10, 0) };
            
            // 标题
            var titleBlock = new TextBlock 
            { 
                Text = title, 
                FontWeight = FontWeights.Bold, 
                Margin = new Thickness(0, 0, 0, 5),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(titleBlock);
            
            // 颜色块容器
            var colorPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            for (int i = 0; i < 4; i++)
            {
                byte colorIndex = GetPaletteColor(baseAddress + i);
                uint nesColor = NesPalette[colorIndex & 0x3F];
                
                var colorBlock = new Border
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(1),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(
                        (byte)((nesColor >> 24) & 0xFF),
                        (byte)((nesColor >> 16) & 0xFF),
                        (byte)((nesColor >> 8) & 0xFF),
                        (byte)(nesColor & 0xFF)))
                };
                
                // 添加颜色索引标签
                var indexLabel = new TextBlock
                {
                    Text = $"${colorIndex:X2}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 8,
                    Foreground = Brushes.White
                };
                
                colorBlock.Child = indexLabel;
                colorPanel.Children.Add(colorBlock);
            }
            
            panel.Children.Add(colorPanel);
            return panel;
        }
        
        private byte GetPaletteColor(int address)
        {
            if (ppu == null) return 0;
            
            // 使用PPU的公共方法获取调色板数据
            return ppu.GetPaletteColor(address);
        }
        
        private void RefreshPPUStatus()
        {
            if (ppu == null) return;
            
            PPUCTRLText.Text = $"${ppu.PPUCTRL:X2}";
            PPUMASKText.Text = $"${ppu.PPUMASK:X2}";
            PPUSTATUSText.Text = $"${ppu.PPUSTATUS:X2}";
            ScanlineText.Text = ppu.Scanline.ToString();
            CycleText.Text = ppu.Cycle.ToString();
            VRAMAddrText.Text = $"${ppu.GetVRAMAddress():X4}";
            
            // 添加寄存器描述
            PPUCTRLDesc.Text = GetPPUCTRLDescription(ppu.PPUCTRL);
            PPUMASKDesc.Text = GetPPUMASKDescription(ppu.PPUMASK);
            PPUSTATUSDesc.Text = GetPPUSTATUSDescription(ppu.PPUSTATUS);
        }
        
        private string GetPPUCTRLDescription(byte value)
        {
            var desc = new System.Text.StringBuilder();
            if ((value & 0x80) != 0) desc.Append("NMI启用 ");
            if ((value & 0x20) != 0) desc.Append("精灵高度16 ");
            if ((value & 0x10) != 0) desc.Append("背景PT1 ");
            if ((value & 0x08) != 0) desc.Append("精灵PT1 ");
            if ((value & 0x04) != 0) desc.Append("VRAM+32 ");
            desc.Append($"名称表{value & 0x03}");
            return desc.ToString();
        }
        
        private string GetPPUMASKDescription(byte value)
        {
            var desc = new System.Text.StringBuilder();
            if ((value & 0x20) != 0) desc.Append("强调红色 ");
            if ((value & 0x40) != 0) desc.Append("强调绿色 ");
            if ((value & 0x80) != 0) desc.Append("强调蓝色 ");
            if ((value & 0x10) != 0) desc.Append("显示精灵 ");
            if ((value & 0x08) != 0) desc.Append("显示背景 ");
            if ((value & 0x04) != 0) desc.Append("左侧精灵 ");
            if ((value & 0x02) != 0) desc.Append("左侧背景 ");
            if ((value & 0x01) != 0) desc.Append("灰度模式 ");
            return desc.ToString();
        }
        
        private string GetPPUSTATUSDescription(byte value)
        {
            var desc = new System.Text.StringBuilder();
            if ((value & 0x80) != 0) desc.Append("VBlank ");
            if ((value & 0x40) != 0) desc.Append("精灵0命中 ");
            if ((value & 0x20) != 0) desc.Append("精灵溢出 ");
            return desc.ToString();
        }
        
        protected override void OnClosed(EventArgs e)
        {
            refreshTimer?.Stop();
            base.OnClosed(e);
        }
    }
}