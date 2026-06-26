using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shapes;
using NESEmulator.Core.PPU;
using NESEmulator.Core.Cartridge;

namespace NESEmulator
{
    public partial class NameTableDebugWindow : Window
    {
        private PPU2C02? ppu;
        private ICartridge? cartridge;
        private DispatcherTimer? refreshTimer;
        
        // NameTable位图
        private WriteableBitmap? nameTable0Bitmap;
        private WriteableBitmap? nameTable1Bitmap;
        private WriteableBitmap? nameTable2Bitmap;
        private WriteableBitmap? nameTable3Bitmap;
        
        // 滚动线
        private Line? scrollLineX0, scrollLineY0;
        private Line? scrollLineX1, scrollLineY1;
        private Line? scrollLineX2, scrollLineY2;
        private Line? scrollLineX3, scrollLineY3;
        
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
        
        public NameTableDebugWindow()
        {
            InitializeComponent();
            InitializeBitmaps();
            InitializeScrollLines();
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
            // 创建NameTable位图 (32x30 tiles, 每个tile 8x8像素, 放大2倍显示)
            nameTable0Bitmap = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgra32, null);
            nameTable1Bitmap = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgra32, null);
            nameTable2Bitmap = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgra32, null);
            nameTable3Bitmap = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgra32, null);
            
            NameTable0Image.Source = nameTable0Bitmap;
            NameTable1Image.Source = nameTable1Bitmap;
            NameTable2Image.Source = nameTable2Bitmap;
            NameTable3Image.Source = nameTable3Bitmap;
        }
        
        private void InitializeScrollLines()
        {
            // 为每个NameTable创建滚动线
            CreateScrollLinesForCanvas(NameTable0Canvas, out scrollLineX0, out scrollLineY0);
            CreateScrollLinesForCanvas(NameTable1Canvas, out scrollLineX1, out scrollLineY1);
            CreateScrollLinesForCanvas(NameTable2Canvas, out scrollLineX2, out scrollLineY2);
            CreateScrollLinesForCanvas(NameTable3Canvas, out scrollLineX3, out scrollLineY3);
        }
        
        private void CreateScrollLinesForCanvas(Canvas canvas, out Line lineX, out Line lineY)
        {
            // 垂直滚动线 (X位置)
            lineX = new Line
            {
                X1 = 0, Y1 = 0, X2 = 0, Y2 = 480,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            canvas.Children.Add(lineX);
            
            // 水平滚动线 (Y位置)
            lineY = new Line
            {
                X1 = 0, Y1 = 0, X2 = 512, Y2 = 0,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            canvas.Children.Add(lineY);
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
            
            RenderNameTables();
            UpdateScrollInfo();
            UpdateScrollLines();
        }
        
        private void RenderNameTables()
        {
            if (ppu == null || cartridge == null) return;
            
            // 渲染四个NameTable
            RenderNameTable(nameTable0Bitmap!, 0x2000, 0);
            RenderNameTable(nameTable1Bitmap!, 0x2400, 1);
            RenderNameTable(nameTable2Bitmap!, 0x2800, 2);
            RenderNameTable(nameTable3Bitmap!, 0x2C00, 3);
        }
        
        private void RenderNameTable(WriteableBitmap bitmap, ushort baseAddress, int tableIndex)
        {
            if (cartridge == null || ppu == null) return;
            
            bitmap.Lock();
            
            unsafe
            {
                uint* pixels = (uint*)bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride / 4;
                
                // 32x30 tiles grid
                for (int tileY = 0; tileY < 30; tileY++)
                {
                    for (int tileX = 0; tileX < 32; tileX++)
                    {
                        // 获取tile索引
                        ushort nameTableAddr = (ushort)(baseAddress + tileY * 32 + tileX);
                        byte tileIndex = ReadNameTableByte(nameTableAddr);
                        
                        // 获取属性字节
                        ushort attrAddr = (ushort)(baseAddress + 0x3C0 + (tileY / 4) * 8 + (tileX / 4));
                        byte attrByte = ReadNameTableByte(attrAddr);
                        
                        // 计算调色板索引
                        int paletteIndex = GetPaletteIndex(attrByte, tileX, tileY);
                        
                        // 渲染8x8 tile
                        RenderTile(pixels, stride, tileX * 8, tileY * 8, tileIndex, paletteIndex);
                    }
                }
            }
            
            bitmap.AddDirtyRect(new Int32Rect(0, 0, 256, 240));
            bitmap.Unlock();
        }
        
        private byte ReadNameTableByte(ushort address)
        {
            // 使用PPU的公共方法来读取NameTable数据
            if (ppu == null) return 0;
            
            return ppu.GetVRAMByte(address);
        }
        
        private int GetPaletteIndex(byte attrByte, int tileX, int tileY)
        {
            // 计算在2x2 tile块中的位置
            int quadrantX = (tileX % 4) / 2;
            int quadrantY = (tileY % 4) / 2;
            int shift = (quadrantY * 2 + quadrantX) * 2;
            
            return (attrByte >> shift) & 0x03;
        }
        
        private unsafe void RenderTile(uint* pixels, int stride, int startX, int startY, byte tileIndex, int paletteIndex)
        {
            if (cartridge == null || ppu == null) return;
            
            // 确定使用哪个Pattern Table (由PPUCTRL bit 4控制)
            ushort patternTableBase = (ushort)((ppu.PPUCTRL & 0x10) != 0 ? 0x1000 : 0x0000);
            ushort tileAddress = (ushort)(patternTableBase + tileIndex * 16);
            
            // 渲染8x8像素
            for (int pixelY = 0; pixelY < 8; pixelY++)
            {
                byte lowByte = cartridge.ReadCHR((ushort)(tileAddress + pixelY));
                byte highByte = cartridge.ReadCHR((ushort)(tileAddress + pixelY + 8));
                
                for (int pixelX = 0; pixelX < 8; pixelX++)
                {
                    int bit = 7 - pixelX;
                    int colorIndex = ((highByte >> bit) & 1) << 1 | ((lowByte >> bit) & 1);
                    
                    uint color;
                    if (IgnorePalettesCheckBox.IsChecked == true)
                    {
                        // 使用灰度显示
                        color = colorIndex switch
                        {
                            0 => 0xFF000000, // 黑色
                            1 => 0xFF555555, // 深灰
                            2 => 0xFFAAAAAA, // 浅灰
                            3 => 0xFFFFFFFF, // 白色
                            _ => 0xFF000000
                        };
                    }
                    else
                    {
                        // 使用调色板颜色
                        if (colorIndex == 0)
                        {
                            // 透明色，使用通用背景色
                            byte bgColorIndex = ppu.GetPaletteColor(0x3F00);
                            color = NesPalette[bgColorIndex & 0x3F];
                        }
                        else
                        {
                            // 使用背景调色板
                            byte paletteColorIndex = ppu.GetPaletteColor(0x3F00 + paletteIndex * 4 + colorIndex);
                            color = NesPalette[paletteColorIndex & 0x3F];
                        }
                    }
                    
                    int screenX = startX + pixelX;
                    int screenY = startY + pixelY;
                    
                    if (screenX < 256 && screenY < 240)
                    {
                        pixels[screenY * stride + screenX] = color;
                    }
                }
            }
        }
        
        private void UpdateScrollInfo()
        {
            if (ppu == null) return;
            
            ushort vramAddr = ppu.GetVRAMAddress();
            
            VRAMAddressText.Text = $"${vramAddr:X4}";
            CoarseXText.Text = (vramAddr & 0x1F).ToString();
            CoarseYText.Text = ((vramAddr >> 5) & 0x1F).ToString();
            FineYText.Text = ((vramAddr >> 12) & 0x07).ToString();
        }
        
        private void UpdateScrollLines()
        {
            if (ppu == null || ShowScrollLinesCheckBox.IsChecked != true) 
            {
                HideAllScrollLines();
                return;
            }
            
            ushort vramAddr = ppu.GetVRAMAddress();
            int coarseX = vramAddr & 0x1F;
            int coarseY = (vramAddr >> 5) & 0x1F;
            int fineY = (vramAddr >> 12) & 0x07;
            
            // 计算像素位置 (放大2倍)
            double scrollX = coarseX * 16; // 8像素 * 2倍放大
            double scrollY = coarseY * 16 + fineY * 2; // 包含fine Y
            
            // 更新所有NameTable的滚动线
            UpdateScrollLine(scrollLineX0!, scrollLineY0!, scrollX, scrollY);
            UpdateScrollLine(scrollLineX1!, scrollLineY1!, scrollX, scrollY);
            UpdateScrollLine(scrollLineX2!, scrollLineY2!, scrollX, scrollY);
            UpdateScrollLine(scrollLineX3!, scrollLineY3!, scrollX, scrollY);
        }
        
        private void UpdateScrollLine(Line lineX, Line lineY, double scrollX, double scrollY)
        {
            lineX.X1 = lineX.X2 = scrollX;
            lineY.Y1 = lineY.Y2 = scrollY;
            
            lineX.Visibility = Visibility.Visible;
            lineY.Visibility = Visibility.Visible;
        }
        
        private void HideAllScrollLines()
        {
            scrollLineX0!.Visibility = Visibility.Collapsed;
            scrollLineY0!.Visibility = Visibility.Collapsed;
            scrollLineX1!.Visibility = Visibility.Collapsed;
            scrollLineY1!.Visibility = Visibility.Collapsed;
            scrollLineX2!.Visibility = Visibility.Collapsed;
            scrollLineY2!.Visibility = Visibility.Collapsed;
            scrollLineX3!.Visibility = Visibility.Collapsed;
            scrollLineY3!.Visibility = Visibility.Collapsed;
        }
        
        protected override void OnClosed(EventArgs e)
        {
            refreshTimer?.Stop();
            base.OnClosed(e);
        }
    }
}