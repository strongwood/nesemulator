using NESEmulator.Core.Cartridge;
using NESEmulator.Core.Memory;
using System.Diagnostics;

namespace NESEmulator.Core.PPU
{
    public class PPU2C02
    {
        private readonly MemoryBus memoryBus;
        private ICartridge? cartridge;

        private byte ppuCtrl;
        private byte ppuMask;
        private byte openBus;
        private readonly int[] openBusDecayCycles = new int[8];
        private byte oamAddr;
        private byte ppuDataBuffer;

        private ushort vramAddr;
        private ushort tempAddr;
        private byte fineX;
        private bool writeToggle;
        private bool oddFrame;
        private bool oddFrameSkipRenderingEnabled;

        private bool nmiOccurred;
        private bool nmiOutput;
        private bool nmiPrevious;
        private byte nmiDelay;
        private bool nmiArmed;
        private bool nmiTriggered;
        private bool frameComplete;
        private bool suppressVBlankThisFrame;

        private byte flagSpriteZeroHit;
        private byte flagSpriteOverflow;

        private byte nameTableByte;
        private byte attributeTableByte;
        private byte lowTileByte;
        private byte highTileByte;
        private ulong tileData;

        private int spriteCount;
        private readonly uint[] spritePatterns = new uint[8];
        private readonly byte[] spritePositions = new byte[8];
        private readonly byte[] spritePriorities = new byte[8];
        private readonly byte[] spriteIndexes = new byte[8];
        private int scheduledSpriteOverflowCycle;

        private readonly byte[] vram = new byte[0x1000];
        private readonly byte[] paletteRam = new byte[0x20];
        private readonly byte[] oam = new byte[0x100];
        private readonly uint[] frameBuffer = new uint[256 * 240];

        private int scanline;
        private int cycle;
        private int frameCount;

        private long lastClockTime;
        private double cycleAccumulator;
        private const int OpenBusDecayPeriod = 3_200_000; // Roughly 600ms of PPU clocks.

        // 已禁用调试插桩与日志上报，以减少运行时开销。
        private const bool EnableDebugLogging = false;

        private static readonly uint[] NesPalette =
        {
            0xFF545454, 0xFF001E74, 0xFF081090, 0xFF300088, 0xFF440064, 0xFF5C0030, 0xFF540400, 0xFF3C1800,
            0xFF202A00, 0xFF083A00, 0xFF004000, 0xFF003C00, 0xFF00323C, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFF989698, 0xFF084CC4, 0xFF3032EC, 0xFF5C1EE4, 0xFF8814B0, 0xFFA01464, 0xFF982220, 0xFF783C00,
            0xFF545A00, 0xFF287200, 0xFF087C00, 0xFF007628, 0xFF006678, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFFECEEEC, 0xFF4C9AEC, 0xFF787CEC, 0xFFB062EC, 0xFFE454EC, 0xFFEC58B4, 0xFFEC6A64, 0xFFD48820,
            0xFFA0AA00, 0xFF74C400, 0xFF4CD020, 0xFF38CC6C, 0xFF38B4CC, 0xFF3C3C3C, 0xFF000000, 0xFF000000,
            0xFFECEEEC, 0xFFA8CCEC, 0xFFBCBCEC, 0xFFD4B2EC, 0xFFECAEEC, 0xFFECAED4, 0xFFECB4B0, 0xFFE4C490,
            0xFFCCD278, 0xFFB4DE78, 0xFFA8E290, 0xFF98E2B4, 0xFF98D8D8, 0xFFA0A2A0, 0xFF000000, 0xFF000000
        };

        public PPU2C02(MemoryBus memoryBus)
        {
            this.memoryBus = memoryBus;
            Reset();
        }

        public void ConnectCartridge(ICartridge cartridge)
        {
            this.cartridge = cartridge;
        }

        public void Reset()
        {
            ppuCtrl = 0;
            ppuMask = 0;
            openBus = 0;
            Array.Clear(openBusDecayCycles, 0, openBusDecayCycles.Length);
            oamAddr = 0;
            ppuDataBuffer = 0;

            vramAddr = 0;
            tempAddr = 0;
            fineX = 0;
            writeToggle = false;
            oddFrame = false;
            oddFrameSkipRenderingEnabled = false;

            nmiOccurred = false;
            nmiOutput = false;
            nmiPrevious = false;
            nmiDelay = 0;
            nmiArmed = false;
            nmiTriggered = false;
            frameComplete = false;
            suppressVBlankThisFrame = false;

            flagSpriteZeroHit = 0;
            flagSpriteOverflow = 0;

            nameTableByte = 0;
            attributeTableByte = 0;
            lowTileByte = 0;
            highTileByte = 0;
            tileData = 0;

            spriteCount = 0;
            scanline = 240;
            cycle = 340;
            frameCount = 0;

            lastClockTime = 0;
            cycleAccumulator = 0;

            Array.Clear(vram, 0, vram.Length);
            Array.Clear(paletteRam, 0, paletteRam.Length);
            Array.Clear(oam, 0, oam.Length);
            Array.Clear(frameBuffer, 0, frameBuffer.Length);
            Array.Clear(spritePatterns, 0, spritePatterns.Length);
            Array.Clear(spritePositions, 0, spritePositions.Length);
            Array.Clear(spritePriorities, 0, spritePriorities.Length);
            Array.Clear(spriteIndexes, 0, spriteIndexes.Length);
            scheduledSpriteOverflowCycle = 0;
        }

        public void Clock()
        {
            if (nmiDelay > 0)
            {
                nmiDelay--;
                if (nmiDelay == 0 && nmiArmed)
                {
                    nmiTriggered = true;
                    nmiArmed = false;
                    // 调试日志已禁用。
                }
            }

            TickOpenBusDecay();

            bool renderingEnabled = IsRenderingEnabled();
            bool preLine = scanline == 261;
            bool visibleLine = scanline >= 0 && scanline <= 239;
            bool renderLine = preLine || visibleLine;
            bool visibleCycle = cycle >= 1 && cycle <= 256;
            bool preFetchCycle = cycle >= 321 && cycle <= 336;
            bool fetchCycle = visibleCycle || preFetchCycle;

            if (visibleLine && visibleCycle)
            {
                RenderPixel();
            }

            if (renderingEnabled)
            {
                if (visibleLine)
                {
                    UpdateSpriteOverflowTiming();
                }

                if (renderLine && fetchCycle)
                {
                    tileData <<= 4;

                    switch (cycle & 7)
                    {
                        case 1:
                            FetchNameTableByte();
                            break;
                        case 3:
                            FetchAttributeTableByte();
                            break;
                        case 5:
                            FetchLowTileByte();
                            break;
                        case 7:
                            FetchHighTileByte();
                            break;
                        case 0:
                            StoreTileData();
                            IncrementScrollX();
                            break;
                    }
                }

                if (renderLine && cycle == 256)
                {
                    IncrementScrollY();
                }

                if (renderLine && cycle == 257)
                {
                    CopyHorizontalScroll();
                    if (visibleLine)
                    {
                        EvaluateSprites();
                    }
                    else
                    {
                        spriteCount = 0;
                    }
                }

                if (preLine && cycle >= 280 && cycle <= 304)
                {
                    CopyVerticalScroll();
                }
            }

            if (scanline == 241 && cycle == 1)
            {
                SetVerticalBlank();
                frameComplete = true;
            }

            if (preLine && cycle == 1)
            {
                ClearVerticalBlank();
                flagSpriteZeroHit = 0;
                flagSpriteOverflow = 0;
                frameComplete = false;
            }

            Tick();
        }

        public void ClockWithPrecisionTiming()
        {
            lastClockTime = Stopwatch.GetTimestamp();
            Clock();
        }

        public PPUClockStats GetClockStats()
        {
            return new PPUClockStats
            {
                CurrentScanline = scanline,
                CurrentCycle = cycle,
                FrameComplete = frameComplete,
                NMITriggered = nmiTriggered,
                LastClockTime = lastClockTime,
                CycleAccumulator = cycleAccumulator
            };
        }

        public byte ReadRegister(ushort address)
        {
            switch (address & 0x7)
            {
                case 0:
                case 1:
                case 3:
                case 5:
                case 6:
                    // 调试日志已禁用。
                    return openBus;
                case 2:
                    return ReadStatus();
                case 4:
                    return ReadOamData();
                case 7:
                    return ReadData();
                default:
                    return 0;
            }
        }

        public void WriteRegister(ushort address, byte value)
        {
            RefreshOpenBus(0xFF, value);

            switch (address & 0x7)
            {
                case 0:
                    WriteControl(value);
                    break;
                case 1:
                    WriteMask(value);
                    break;
                case 3:
                    oamAddr = value;
                    break;
                case 4:
                    oam[oamAddr] = value;
                    oamAddr++;
                    break;
                case 5:
                    WriteScroll(value);
                    break;
                case 6:
                    WriteAddress(value);
                    break;
                case 7:
                    WriteData(value);
                    break;
            }
        }

        private void WriteControl(byte value)
        {
            ppuCtrl = value;
            nmiOutput = (value & 0x80) != 0;
            tempAddr = (ushort)((tempAddr & 0xF3FF) | ((value & 0x03) << 10));
            // 调试日志已禁用。
            UpdateNmiState();
        }

        private void WriteMask(byte value)
        {
            ppuMask = value;
            // 调试日志已禁用。
        }

        private byte ReadStatus()
        {
            byte result = (byte)((openBus & 0x1F) | (flagSpriteOverflow << 5) | (flagSpriteZeroHit << 6));
            if (nmiOccurred)
            {
                result |= 0x80;
            }

            // 调试日志已禁用。

            if (scanline == 241 && cycle == 1)
            {
                suppressVBlankThisFrame = true;
            }

            nmiOccurred = false;
            writeToggle = false;
            UpdateNmiState();
            RefreshOpenBus(0xE0, result);
            return result;
        }

        private byte ReadOamData()
        {
            byte data = oam[oamAddr];
            if ((oamAddr & 0x03) == 0x02)
            {
                data &= 0xE3;
            }
            RefreshOpenBus(0xFF, data);
            return data;
        }

        private void WriteScroll(byte value)
        {
            if (!writeToggle)
            {
                tempAddr = (ushort)((tempAddr & 0xFFE0) | (value >> 3));
                fineX = (byte)(value & 0x07);
                writeToggle = true;
            }
            else
            {
                tempAddr = (ushort)((tempAddr & 0x8FFF) | ((value & 0x07) << 12));
                tempAddr = (ushort)((tempAddr & 0xFC1F) | ((value & 0xF8) << 2));
                writeToggle = false;
            }
        }

        private void WriteAddress(byte value)
        {
            if (!writeToggle)
            {
                tempAddr = (ushort)((tempAddr & 0x80FF) | ((value & 0x3F) << 8));
                writeToggle = true;
            }
            else
            {
                tempAddr = (ushort)((tempAddr & 0xFF00) | value);
                vramAddr = tempAddr;
                writeToggle = false;
            }
        }

        private byte ReadData()
        {
            bool paletteRead = (vramAddr & 0x3FFF) >= 0x3F00;
            byte value = ReadPPU(vramAddr);
            ushort addressBeforeIncrement = vramAddr;
            byte bufferedBeforeRead = ppuDataBuffer;
            if (!paletteRead)
            {
                byte buffered = ppuDataBuffer;
                ppuDataBuffer = value;
                value = buffered;
            }
            else
            {
                ppuDataBuffer = ReadPPU((ushort)(vramAddr - 0x1000));
                value = (byte)((openBus & 0xC0) | (value & 0x3F));
            }

            vramAddr += GetAddressIncrement();
            RefreshOpenBus((byte)(paletteRead ? 0x3F : 0xFF), value);
            // 调试日志已禁用。
            return value;
        }

        private void WriteData(byte value)
        {
            WritePPU(vramAddr, value);
            vramAddr += GetAddressIncrement();
        }

        private byte GetAddressIncrement()
        {
            return (ppuCtrl & 0x04) != 0 ? (byte)32 : (byte)1;
        }

        private void TickOpenBusDecay()
        {
            for (int bit = 0; bit < 8; bit++)
            {
                if (openBusDecayCycles[bit] <= 0)
                {
                    continue;
                }

                openBusDecayCycles[bit]--;
                if (openBusDecayCycles[bit] == 0)
                {
                    openBus = (byte)(openBus & ~(1 << bit));
                }
            }
        }

        private void RefreshOpenBus(byte mask, byte value)
        {
            byte preserved = (byte)(openBus & ~mask);
            openBus = (byte)(preserved | (value & mask));

            for (int bit = 0; bit < 8; bit++)
            {
                int bitMask = 1 << bit;
                if ((mask & bitMask) == 0)
                {
                    continue;
                }

                if ((value & bitMask) != 0)
                {
                    openBusDecayCycles[bit] = OpenBusDecayPeriod;
                }
                else
                {
                    openBusDecayCycles[bit] = 0;
                }
            }
        }

        private void UpdateNmiState()
        {
            bool nmi = nmiOutput && nmiOccurred;
            if (nmi && !nmiPrevious)
            {
                nmiDelay = 14;
                nmiArmed = true;
                // 调试日志已禁用。
            }
            else if (!nmi && nmiPrevious && nmiArmed && nmiDelay > 12)
            {
                nmiDelay = 0;
                nmiArmed = false;
                // 调试日志已禁用。
            }
            nmiPrevious = nmi;
        }

        private void SetVerticalBlank()
        {
            if (suppressVBlankThisFrame)
            {
                suppressVBlankThisFrame = false;
                // 调试日志已禁用。
                return;
            }

            nmiOccurred = true;
            // 调试日志已禁用。
            UpdateNmiState();
        }

        private void ClearVerticalBlank()
        {
            suppressVBlankThisFrame = false;
            nmiOccurred = false;
            // 调试日志已禁用。
            UpdateNmiState();
        }

        private void FetchNameTableByte()
        {
            ushort address = (ushort)(0x2000 | (vramAddr & 0x0FFF));
            nameTableByte = ReadPPU(address);
        }

        private void FetchAttributeTableByte()
        {
            ushort v = vramAddr;
            ushort address = (ushort)(0x23C0 | (v & 0x0C00) | ((v >> 4) & 0x38) | ((v >> 2) & 0x07));
            int shift = ((v >> 4) & 4) | (v & 2);
            attributeTableByte = (byte)(((ReadPPU(address) >> shift) & 0x03) << 2);
        }

        private void FetchLowTileByte()
        {
            ushort fineY = (ushort)((vramAddr >> 12) & 0x07);
            ushort table = (ushort)(((ppuCtrl >> 4) & 0x01) * 0x1000);
            ushort address = (ushort)(table + (nameTableByte * 16) + fineY);
            lowTileByte = ReadPPU(address);
        }

        private void FetchHighTileByte()
        {
            ushort fineY = (ushort)((vramAddr >> 12) & 0x07);
            ushort table = (ushort)(((ppuCtrl >> 4) & 0x01) * 0x1000);
            ushort address = (ushort)(table + (nameTableByte * 16) + fineY + 8);
            highTileByte = ReadPPU(address);
        }

        private void StoreTileData()
        {
            uint data = 0;
            for (int i = 0; i < 8; i++)
            {
                byte a = attributeTableByte;
                byte p1 = (byte)((lowTileByte & 0x80) >> 7);
                byte p2 = (byte)((highTileByte & 0x80) >> 6);
                lowTileByte <<= 1;
                highTileByte <<= 1;
                data <<= 4;
                data |= (uint)(a | p1 | p2);
            }
            tileData |= data;
        }

        private uint FetchTileData()
        {
            return (uint)(tileData >> 32);
        }

        private byte BackgroundPixel()
        {
            if ((ppuMask & 0x08) == 0)
            {
                return 0;
            }

            uint data = FetchTileData() >> ((7 - fineX) * 4);
            return (byte)(data & 0x0F);
        }

        private (byte spriteIndex, byte color) SpritePixel()
        {
            if ((ppuMask & 0x10) == 0)
            {
                return (0, 0);
            }

            for (int i = 0; i < spriteCount; i++)
            {
                int offset = (cycle - 1) - spritePositions[i];
                if (offset < 0 || offset > 7)
                {
                    continue;
                }

                offset = 7 - offset;
                byte color = (byte)((spritePatterns[i] >> (offset * 4)) & 0x0F);
                if ((color & 0x03) == 0)
                {
                    continue;
                }

                return ((byte)i, color);
            }

            return (0, 0);
        }

        private void RenderPixel()
        {
            int x = cycle - 1;
            int y = scanline;
            if (x < 0 || x >= 256 || y < 0 || y >= 240)
            {
                return;
            }

            byte background = BackgroundPixel();
            (byte spriteSlot, byte sprite) = SpritePixel();

            if (x < 8 && (ppuMask & 0x02) == 0)
            {
                background = 0;
            }
            if (x < 8 && (ppuMask & 0x04) == 0)
            {
                sprite = 0;
            }

            bool bgOpaque = (background & 0x03) != 0;
            bool spriteOpaque = (sprite & 0x03) != 0;
            byte colorAddress;

            if (!bgOpaque && !spriteOpaque)
            {
                colorAddress = 0;
            }
            else if (!bgOpaque && spriteOpaque)
            {
                colorAddress = (byte)(sprite | 0x10);
            }
            else if (bgOpaque && !spriteOpaque)
            {
                colorAddress = background;
            }
            else
            {
                if (spriteIndexes[spriteSlot] == 0 && x < 255)
                {
                    flagSpriteZeroHit = 1;
                    // 调试日志已禁用。
                }

                if (spritePriorities[spriteSlot] == 0)
                {
                    colorAddress = (byte)(sprite | 0x10);
                }
                else
                {
                    colorAddress = background;
                }
            }

            byte paletteIndex = ReadPalette(colorAddress);
            frameBuffer[y * 256 + x] = NesPalette[paletteIndex & 0x3F];
        }

        private void EvaluateSprites()
        {
            int spriteHeight = (ppuCtrl & 0x20) == 0 ? 8 : 16;
            int count = 0;
            int overflowCandidateIndex = -1;

            for (int i = 0; i < 64; i++)
            {
                if (count >= 8 && overflowCandidateIndex < 0)
                {
                    overflowCandidateIndex = i;
                }

                byte y = oam[i * 4 + 0];
                byte attributes = oam[i * 4 + 2];
                byte x = oam[i * 4 + 3];
                int row = scanline - y;
                if (row < 0 || row >= spriteHeight)
                {
                    continue;
                }

                if (count < 8)
                {
                    spritePatterns[count] = FetchSpritePattern(i, row);
                    spritePositions[count] = x;
                    spritePriorities[count] = (byte)((attributes >> 5) & 0x01);
                    spriteIndexes[count] = (byte)i;
                }
                count++;
            }

            spriteCount = Math.Min(count, 8);

            if (overflowCandidateIndex >= 0 && HasSpriteOverflowBug(overflowCandidateIndex, spriteHeight, scanline))
            {
                flagSpriteOverflow = 1;
            }
            // 调试日志已禁用。
        }

        private bool HasSpriteOverflowBug(int ninthSpriteIndex, int spriteHeight, int evaluationScanline)
        {
            int n = ninthSpriteIndex;
            int m = 0;

            while (n < 64)
            {
                byte candidate = oam[n * 4 + m];
                int row = evaluationScanline - candidate;
                if (row >= 0 && row < spriteHeight)
                {
                    return true;
                }

                n++;
                m = (m + 1) & 0x03;
            }

            return false;
        }

        private void UpdateSpriteOverflowTiming()
        {
            if (cycle == 1)
            {
                scheduledSpriteOverflowCycle = CalculateSpriteOverflowCycle();
                return;
            }

            if (flagSpriteOverflow != 0 || scheduledSpriteOverflowCycle == 0 || cycle != scheduledSpriteOverflowCycle)
            {
                return;
            }

            flagSpriteOverflow = 1;
            // 调试日志已禁用。
        }

        private int CalculateSpriteOverflowCycle()
        {
            int spriteHeight = (ppuCtrl & 0x20) == 0 ? 8 : 16;
            int matchedSprites = 0;
            int evalCycle = 64;
            int spriteIndex = 0;

            for (; spriteIndex < 64; spriteIndex++)
            {
                evalCycle += 2;
                if (evalCycle > 256)
                {
                    break;
                }

                int row = scanline - oam[spriteIndex * 4];
                if (row < 0 || row >= spriteHeight)
                {
                    continue;
                }

                matchedSprites++;
                if (matchedSprites > 8)
                {
                    return evalCycle;
                }

                evalCycle += 6;
                if (evalCycle > 256)
                {
                    break;
                }

                if (matchedSprites == 8)
                {
                    spriteIndex++;
                    break;
                }
            }

            if (matchedSprites < 8)
            {
                return 0;
            }

            int n = spriteIndex;
            int m = 0;
            while (n < 64)
            {
                evalCycle += 2;
                if (evalCycle > 256)
                {
                    break;
                }

                byte candidate = oam[n * 4 + m];
                int row = scanline - candidate;
                if (row >= 0 && row < spriteHeight)
                {
                    return evalCycle;
                }

                n++;
                m = (m + 1) & 0x03;
            }

            return 0;
        }

        private uint FetchSpritePattern(int spriteIndex, int row)
        {
            byte tile = oam[spriteIndex * 4 + 1];
            byte attributes = oam[spriteIndex * 4 + 2];
            ushort address;

            if ((ppuCtrl & 0x20) == 0)
            {
                if ((attributes & 0x80) != 0)
                {
                    row = 7 - row;
                }

                ushort table = (ushort)(((ppuCtrl >> 3) & 0x01) * 0x1000);
                address = (ushort)(table + (tile * 16) + row);
            }
            else
            {
                if ((attributes & 0x80) != 0)
                {
                    row = 15 - row;
                }

                ushort table = (ushort)((tile & 0x01) * 0x1000);
                byte adjustedTile = (byte)(tile & 0xFE);
                if (row > 7)
                {
                    adjustedTile++;
                    row -= 8;
                }
                address = (ushort)(table + (adjustedTile * 16) + row);
            }

            byte palette = (byte)((attributes & 0x03) << 2);
            byte low = ReadPPU(address);
            byte high = ReadPPU((ushort)(address + 8));
            uint data = 0;

            for (int i = 0; i < 8; i++)
            {
                byte p1;
                byte p2;
                if ((attributes & 0x40) != 0)
                {
                    p1 = (byte)(low & 0x01);
                    p2 = (byte)((high & 0x01) << 1);
                    low >>= 1;
                    high >>= 1;
                }
                else
                {
                    p1 = (byte)((low & 0x80) >> 7);
                    p2 = (byte)((high & 0x80) >> 6);
                    low <<= 1;
                    high <<= 1;
                }

                data <<= 4;
                data |= (uint)(palette | p1 | p2);
            }

            return data;
        }

        private void Tick()
        {
            if (oddFrameSkipRenderingEnabled && oddFrame && scanline == 261 && cycle == 339)
            {
                // 调试日志已禁用。
                cycle = 0;
                scanline = 0;
                frameCount++;
                oddFrame = false;
                oddFrameSkipRenderingEnabled = IsRenderingEnabled();
                return;
            }

            cycle++;
            if (cycle > 340)
            {
                cycle = 0;
                scanline++;
                if (scanline > 261)
                {
                    scanline = 0;
                    frameCount++;
                    oddFrame = !oddFrame;
                }
            }

            oddFrameSkipRenderingEnabled = IsRenderingEnabled();
        }

        private bool IsRenderingEnabled()
        {
            return (ppuMask & 0x18) != 0;
        }

        private void IncrementScrollX()
        {
            if ((vramAddr & 0x001F) == 31)
            {
                vramAddr &= 0xFFE0;
                vramAddr ^= 0x0400;
            }
            else
            {
                vramAddr++;
            }
        }

        private void IncrementScrollY()
        {
            if ((vramAddr & 0x7000) != 0x7000)
            {
                vramAddr += 0x1000;
            }
            else
            {
                vramAddr &= 0x8FFF;
                int coarseY = (vramAddr & 0x03E0) >> 5;
                if (coarseY == 29)
                {
                    coarseY = 0;
                    vramAddr ^= 0x0800;
                }
                else if (coarseY == 31)
                {
                    coarseY = 0;
                }
                else
                {
                    coarseY++;
                }
                vramAddr = (ushort)((vramAddr & 0xFC1F) | (coarseY << 5));
            }
        }

        private void CopyHorizontalScroll()
        {
            vramAddr = (ushort)((vramAddr & 0xFBE0) | (tempAddr & 0x041F));
        }

        private void CopyVerticalScroll()
        {
            vramAddr = (ushort)((vramAddr & 0x841F) | (tempAddr & 0x7BE0));
        }

        private byte ReadPPU(ushort address)
        {
            address &= 0x3FFF;

            if (address < 0x2000)
            {
                return cartridge?.ReadCHR(address) ?? 0;
            }
            if (address < 0x3F00)
            {
                return vram[GetMirroredVRAMAddress(address)];
            }

            return ReadPalette((byte)(address & 0x1F));
        }

        private void WritePPU(ushort address, byte value)
        {
            address &= 0x3FFF;

            if (address < 0x2000)
            {
                cartridge?.WriteCHR(address, value);
                return;
            }
            if (address < 0x3F00)
            {
                vram[GetMirroredVRAMAddress(address)] = value;
                return;
            }

            WritePalette((byte)(address & 0x1F), value);
        }

        private byte ReadPalette(byte address)
        {
            byte index = address;
            if (index >= 0x10 && (index & 0x03) == 0)
            {
                index -= 0x10;
            }

            byte value = paletteRam[index & 0x1F];
            if ((ppuMask & 0x01) != 0)
            {
                value &= 0x30;
            }
            return value;
        }

        private void WritePalette(byte address, byte value)
        {
            byte index = address;
            if (index >= 0x10 && (index & 0x03) == 0)
            {
                index -= 0x10;
            }
            paletteRam[index & 0x1F] = value;
        }

        private int GetMirroredVRAMAddress(ushort address)
        {
            if (address >= 0x3000 && address < 0x3F00)
            {
                address -= 0x1000;
            }

            int offset = (address - 0x2000) & 0x0FFF;
            int table = offset / 0x400;
            int local = offset & 0x3FF;

            MirrorMode mode = cartridge?.MirrorMode ?? MirrorMode.Horizontal;
            int mappedTable = mode switch
            {
                MirrorMode.Horizontal => table >> 1,
                MirrorMode.Vertical => table & 1,
                MirrorMode.SingleScreenLower => 0,
                MirrorMode.SingleScreenUpper => 1,
                MirrorMode.FourScreen => table,
                _ => table & 1
            };

            return ((mappedTable & 0x03) * 0x400) + local;
        }

        public int Scanline => scanline;
        public int Cycle => cycle;
        public byte PPUCTRL => ppuCtrl;
        public byte PPUMASK => ppuMask;
        public byte PPUSTATUS => (byte)((openBus & 0x1F) | (flagSpriteOverflow << 5) | (flagSpriteZeroHit << 6) | (nmiOccurred ? 0x80 : 0));

        public uint[] GetFrameBuffer()
        {
            return frameBuffer;
        }

        public byte[] GetOAM()
        {
            return oam;
        }

        public int GetSpriteCount()
        {
            return spriteCount;
        }

        public bool IsSprite0OnLine()
        {
            for (int i = 0; i < spriteCount; i++)
            {
                if (spriteIndexes[i] == 0)
                {
                    return true;
                }
            }
            return false;
        }

        public byte ReadVRAMDebug(ushort address)
        {
            return ReadPPU(address);
        }

        public bool IsFrameComplete()
        {
            return frameComplete;
        }

        public void ClearFrameComplete()
        {
            frameComplete = false;
        }

        public bool IsNMITriggered()
        {
            return nmiTriggered;
        }

        public void ClearNMI()
        {
            nmiTriggered = false;
        }

        public void DebugFrameBuffer()
        {
        }

        public byte GetPaletteColor(int address)
        {
            return ReadPPU((ushort)address);
        }

        public ushort GetVRAMAddress()
        {
            return vramAddr;
        }

        public byte GetVRAMByte(ushort address)
        {
            return ReadPPU(address);
        }

        public byte[] GetVRAMData()
        {
            return vram;
        }
    }
}
