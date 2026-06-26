using NESEmulator.Core.Memory;
using System.Diagnostics;

namespace NESEmulator.Core.CPU
{
    public class CPU6502
    {
        private readonly MemoryBus memoryBus;
        
        // 寄存器
        public byte A { get; private set; }      // 累加器
        public byte X { get; private set; }      // X索引寄存器
        public byte Y { get; private set; }      // Y索引寄存器
        public byte SP { get; private set; }     // 堆栈指针
        public ushort PC { get; private set; }   // 程序计数器
        public byte P { get; private set; }      // 状态寄存器
        
        public void SetPC(ushort address) => PC = address;
        
        // 状态标志位
        private const byte FLAG_CARRY = 0x01;
        private const byte FLAG_ZERO = 0x02;
        private const byte FLAG_INTERRUPT = 0x04;
        private const byte FLAG_DECIMAL = 0x08;
        private const byte FLAG_BREAK = 0x10;
        private const byte FLAG_UNUSED = 0x20;
        private const byte FLAG_OVERFLOW = 0x40;
        private const byte FLAG_NEGATIVE = 0x80;
        
        private bool nmiPending = false;
        private bool irqPending = false;
        private int stallCycles = 0;
        private int cycles = 0;
        
        // 高精度时钟相关
        private long lastClockTime;
        private double cycleAccumulator;
        private const double CPU_CYCLE_TIME_NS = 558.73; // CPU周期时间（纳秒）
        private long totalInstructions = 0;

        public CPU6502(MemoryBus memoryBus)
        {
            this.memoryBus = memoryBus;
            Reset();
        }

        public void Reset()
        {
            A = 0;
            X = 0;
            Y = 0;
            SP = 0xFD;
            P = FLAG_UNUSED | FLAG_INTERRUPT;
            
            // 从重置向量读取PC
            byte lowByte = memoryBus.Read(0xFFFC);
            byte highByte = memoryBus.Read(0xFFFD);
            PC = (ushort)(lowByte | (highByte << 8));
            
            // Console.WriteLine($"CPU Reset: Reset vector 0xFFFC=0x{lowByte:X2}, 0xFFFD=0x{highByte:X2}, PC=0x{PC:X4}");
            // Console.Out.Flush();
            
            cycles = 0;
            stallCycles = 0;
            nmiPending = false;
            irqPending = false;
            cycleAccumulator = 0;
        }

        public int ExecuteInstruction()
        {
            int startCycles = cycles;

            if (stallCycles > 0)
            {
                stallCycles--;
                cycles++;
                return 1;
            }
            
            // 处理中断
            if (nmiPending)
            {
                HandleNMI();
                nmiPending = false;
                return cycles - startCycles;
            }
            
            if (irqPending && !GetFlag(FLAG_INTERRUPT))
            {
                HandleIRQ();
                irqPending = false;
                return cycles - startCycles;
            }
            
            // 读取指令
            ushort instructionPC = PC;
            byte opcode = memoryBus.Read(PC++);
            
            // 输出每条指令的详细信息
            // Console.WriteLine($"Executing instruction: PC=0x{instructionPC:X4}, Opcode=0x{opcode:X2}, A=0x{A:X2}, X=0x{X:X2}, Y=0x{Y:X2}, SP=0x{SP:X2}, P=0x{P:X2}");
            // Console.Out.Flush();
            
            // 执行指令
            ExecuteOpcode(opcode);
            
            // 指令执行后检查是否有新的NMI请求
            // 这处理了在VBlank期间写入PPUCTRL启用NMI的情况
            if (nmiPending)
            {
                // NMI在下一条指令之前处理
            }
            
            return cycles - startCycles;
        }
        
        /// <summary>
        /// 高精度指令执行方法
        /// 基于Fami项目的精确CPU时序控制
        /// </summary>
        public int ExecuteInstructionWithPrecisionTiming()
        {
            // 记录指令执行时间
            long currentTime = Stopwatch.GetTimestamp();
            
            // 执行标准指令逻辑
            int cyclesUsed = ExecuteInstruction();
            
            // 更新时钟统计
            lastClockTime = currentTime;
            totalInstructions++;
            
            return cyclesUsed;
        }
        
        /// <summary>
        /// 获取CPU时钟统计信息
        /// </summary>
        public CPUClockStats GetClockStats()
        {
            return new CPUClockStats
            {
                TotalInstructions = totalInstructions,
                TotalCycles = cycles,
                ProgramCounter = PC,
                Accumulator = A,
                IndexX = X,
                IndexY = Y,
                StackPointer = SP,
                StatusRegister = P,
                LastClockTime = lastClockTime,
                CycleAccumulator = cycleAccumulator,
                NMIPending = nmiPending,
                IRQPending = irqPending
            };
        }

        private void ExecuteOpcode(byte opcode)
        {
            switch (opcode)
            {
                // LDA - Load Accumulator
                case 0xA9: LDA_Immediate(); break;
                case 0xA5: LDA_ZeroPage(); break;
                case 0xB5: LDA_ZeroPageX(); break;
                case 0xAD: LDA_Absolute(); break;
                case 0xBD: LDA_AbsoluteX(); break;
                case 0xB9: LDA_AbsoluteY(); break;
                case 0xA1: LDA_IndirectX(); break;
                case 0xB1: LDA_IndirectY(); break;
                
                // LDX - Load X Register
                case 0xA2: LDX_Immediate(); break;
                case 0xA6: LDX_ZeroPage(); break;
                case 0xB6: LDX_ZeroPageY(); break;
                case 0xAE: LDX_Absolute(); break;
                case 0xBE: LDX_AbsoluteY(); break;
                
                // LDY - Load Y Register
                case 0xA0: LDY_Immediate(); break;
                case 0xA4: LDY_ZeroPage(); break;
                case 0xB4: LDY_ZeroPageX(); break;
                case 0xAC: LDY_Absolute(); break;
                case 0xBC: LDY_AbsoluteX(); break;
                
                // STA - Store Accumulator
                case 0x85: STA_ZeroPage(); break;
                case 0x95: STA_ZeroPageX(); break;
                case 0x8D: STA_Absolute(); break;
                case 0x9D: STA_AbsoluteX(); break;
                case 0x99: STA_AbsoluteY(); break;
                case 0x81: STA_IndirectX(); break;
                case 0x91: STA_IndirectY(); break;
                
                // JMP - Jump
                case 0x4C: JMP_Absolute(); break;
                case 0x6C: JMP_Indirect(); break;
                
                // JSR - Jump to Subroutine
                case 0x20: JSR(); break;
                
                // RTS - Return from Subroutine
                case 0x60: RTS(); break;
                
                // BEQ - Branch if Equal
                case 0xF0: BEQ(); break;
                
                // BNE - Branch if Not Equal
                case 0xD0: BNE(); break;
                
                // CMP - Compare
                case 0xC9: CMP_Immediate(); break;
                case 0xC5: CMP_ZeroPage(); break;
                case 0xD5: CMP_ZeroPageX(); break;
                case 0xCD: CMP_Absolute(); break;
                case 0xDD: CMP_AbsoluteX(); break;
                case 0xD9: CMP_AbsoluteY(); break;
                case 0xC1: CMP_IndirectX(); break;
                case 0xD1: CMP_IndirectY(); break;
                
                // INX - Increment X
                case 0xE8: INX(); break;
                
                // INY - Increment Y
                case 0xC8: INY(); break;
                
                // DEX - Decrement X
                case 0xCA: DEX(); break;
                
                // DEY - Decrement Y
                case 0x88: DEY(); break;
                
                // NOP - No Operation
                case 0xEA: NOP(); break;
                
                // ADC - Add with Carry
                case 0x69: ADC_Immediate(); break;
                case 0x65: ADC_ZeroPage(); break;
                case 0x75: ADC_ZeroPageX(); break;
                case 0x6D: ADC_Absolute(); break;
                case 0x7D: ADC_AbsoluteX(); break;
                case 0x79: ADC_AbsoluteY(); break;
                case 0x61: ADC_IndirectX(); break;
                case 0x71: ADC_IndirectY(); break;
                
                // SBC - Subtract with Carry
                case 0xE9: SBC_Immediate(); break;
                case 0xE5: SBC_ZeroPage(); break;
                case 0xF5: SBC_ZeroPageX(); break;
                case 0xED: SBC_Absolute(); break;
                case 0xFD: SBC_AbsoluteX(); break;
                case 0xF9: SBC_AbsoluteY(); break;
                case 0xE1: SBC_IndirectX(); break;
                case 0xF1: SBC_IndirectY(); break;
                
                // AND - Logical AND
                case 0x29: AND_Immediate(); break;
                case 0x25: AND_ZeroPage(); break;
                case 0x35: AND_ZeroPageX(); break;
                case 0x2D: AND_Absolute(); break;
                case 0x3D: AND_AbsoluteX(); break;
                case 0x39: AND_AbsoluteY(); break;
                case 0x21: AND_IndirectX(); break;
                case 0x31: AND_IndirectY(); break;
                
                // ORA - Logical OR
                case 0x09: ORA_Immediate(); break;
                case 0x05: ORA_ZeroPage(); break;
                case 0x15: ORA_ZeroPageX(); break;
                case 0x0D: ORA_Absolute(); break;
                case 0x1D: ORA_AbsoluteX(); break;
                case 0x19: ORA_AbsoluteY(); break;
                case 0x01: ORA_IndirectX(); break;
                case 0x11: ORA_IndirectY(); break;
                
                // EOR - Exclusive OR
                case 0x49: EOR_Immediate(); break;
                case 0x45: EOR_ZeroPage(); break;
                case 0x55: EOR_ZeroPageX(); break;
                case 0x4D: EOR_Absolute(); break;
                case 0x5D: EOR_AbsoluteX(); break;
                case 0x59: EOR_AbsoluteY(); break;
                case 0x41: EOR_IndirectX(); break;
                case 0x51: EOR_IndirectY(); break;
                
                // BRK - Break
                case 0x00: BRK(); break;
                
                // RTI - Return from Interrupt
                case 0x40: RTI(); break;
                
                // SEI - Set Interrupt Disable
                case 0x78: SEI(); break;
                
                // CLI - Clear Interrupt Disable
                case 0x58: CLI(); break;
                
                // CLD - Clear Decimal Mode
                case 0xD8: CLD(); break;
                
                // SED - Set Decimal Mode
                case 0xF8: SED(); break;
                
                // CLC - Clear Carry
                case 0x18: CLC(); break;
                
                // SEC - Set Carry
                case 0x38: SEC(); break;
                
                // CLV - Clear Overflow
                case 0xB8: CLV(); break;
                
                // STX - Store X Register
                case 0x86: STX_ZeroPage(); break;
                case 0x96: STX_ZeroPageY(); break;
                case 0x8E: STX_Absolute(); break;
                
                // STY - Store Y Register
                case 0x84: STY_ZeroPage(); break;
                case 0x94: STY_ZeroPageX(); break;
                case 0x8C: STY_Absolute(); break;
                
                // TAX - Transfer A to X
                case 0xAA: TAX(); break;
                
                // TXA - Transfer X to A
                case 0x8A: TXA(); break;
                
                // TAY - Transfer A to Y
                case 0xA8: TAY(); break;
                
                // TYA - Transfer Y to A
                case 0x98: TYA(); break;
                
                // TXS - Transfer X to Stack Pointer
                case 0x9A: TXS(); break;
                
                // TSX - Transfer Stack Pointer to X
                case 0xBA: TSX(); break;
                
                // PHA - Push Accumulator
                case 0x48: PHA(); break;
                
                // PLA - Pull Accumulator
                case 0x68: PLA(); break;
                
                // PHP - Push Processor Status
                case 0x08: PHP(); break;
                
                // PLP - Pull Processor Status
                case 0x28: PLP(); break;
                
                // CPX - Compare X Register
                case 0xE0: CPX_Immediate(); break;
                case 0xE4: CPX_ZeroPage(); break;
                case 0xEC: CPX_Absolute(); break;
                
                // CPY - Compare Y Register
                case 0xC0: CPY_Immediate(); break;
                case 0xC4: CPY_ZeroPage(); break;
                case 0xCC: CPY_Absolute(); break;
                
                // INC - Increment Memory
                case 0xE6: INC_ZeroPage(); break;
                case 0xF6: INC_ZeroPageX(); break;
                case 0xEE: INC_Absolute(); break;
                case 0xFE: INC_AbsoluteX(); break;
                
                // DEC - Decrement Memory
                case 0xC6: DEC_ZeroPage(); break;
                case 0xD6: DEC_ZeroPageX(); break;
                case 0xCE: DEC_Absolute(); break;
                case 0xDE: DEC_AbsoluteX(); break;
                
                // BIT - Bit Test
                case 0x24: BIT_ZeroPage(); break;
                case 0x2C: BIT_Absolute(); break;
                
                // ASL - Arithmetic Shift Left
                case 0x0A: ASL_Accumulator(); break;
                case 0x06: ASL_ZeroPage(); break;
                case 0x16: ASL_ZeroPageX(); break;
                case 0x0E: ASL_Absolute(); break;
                case 0x1E: ASL_AbsoluteX(); break;
                
                // LSR - Logical Shift Right
                case 0x4A: LSR_Accumulator(); break;
                case 0x46: LSR_ZeroPage(); break;
                case 0x56: LSR_ZeroPageX(); break;
                case 0x4E: LSR_Absolute(); break;
                case 0x5E: LSR_AbsoluteX(); break;
                
                // ROL - Rotate Left
                case 0x2A: ROL_Accumulator(); break;
                case 0x26: ROL_ZeroPage(); break;
                case 0x36: ROL_ZeroPageX(); break;
                case 0x2E: ROL_Absolute(); break;
                case 0x3E: ROL_AbsoluteX(); break;
                
                // ROR - Rotate Right
                case 0x6A: ROR_Accumulator(); break;
                case 0x66: ROR_ZeroPage(); break;
                case 0x76: ROR_ZeroPageX(); break;
                case 0x6E: ROR_Absolute(); break;
                case 0x7E: ROR_AbsoluteX(); break;
                
                // Branch Instructions
                case 0x10: BPL(); break; // Branch on Plus
                case 0x30: BMI(); break; // Branch on Minus
                case 0x50: BVC(); break; // Branch on Overflow Clear
                case 0x70: BVS(); break; // Branch on Overflow Set
                case 0x90: BCC(); break; // Branch on Carry Clear
                case 0xB0: BCS(); break; // Branch on Carry Set
                
                // Unofficial/Illegal Opcodes
                // STP - Stop the processor (halt until reset)
                case 0x02: case 0x12: case 0x22: case 0x32:
                case 0x42: case 0x52: case 0x62: case 0x72:
                case 0x92: case 0xB2: case 0xD2: case 0xF2:
                    STP(); break;
                
                // LAX - Load A and X with memory
                case 0xA7: LAX_ZeroPage(); break;
                case 0xB7: LAX_ZeroPageY(); break;
                case 0xAF: LAX_Absolute(); break;
                case 0xBF: LAX_AbsoluteY(); break;
                case 0xA3: LAX_IndirectX(); break;
                case 0xB3: LAX_IndirectY(); break;
                
                // SAX - Store A AND X
                case 0x87: SAX_ZeroPage(); break;
                case 0x97: SAX_ZeroPageY(); break;
                case 0x8F: SAX_Absolute(); break;
                case 0x83: SAX_IndirectX(); break;
                
                // DCP - Decrement memory then compare with A
                case 0xC7: DCP_ZeroPage(); break;
                case 0xD7: DCP_ZeroPageX(); break;
                case 0xCF: DCP_Absolute(); break;
                case 0xDF: DCP_AbsoluteX(); break;
                case 0xDB: DCP_AbsoluteY(); break;
                case 0xC3: DCP_IndirectX(); break;
                case 0xD3: DCP_IndirectY(); break;
                
                // ISC - Increment memory then subtract from A
                case 0xE7: ISC_ZeroPage(); break;
                case 0xF7: ISC_ZeroPageX(); break;
                case 0xEF: ISC_Absolute(); break;
                case 0xFF: ISC_AbsoluteX(); break;
                case 0xFB: ISC_AbsoluteY(); break;
                case 0xE3: ISC_IndirectX(); break;
                case 0xF3: ISC_IndirectY(); break;
                
                // SLO - Shift left then OR with A
                case 0x07: SLO_ZeroPage(); break;
                case 0x17: SLO_ZeroPageX(); break;
                case 0x0F: SLO_Absolute(); break;
                case 0x1F: SLO_AbsoluteX(); break;
                case 0x1B: SLO_AbsoluteY(); break;
                case 0x03: SLO_IndirectX(); break;
                case 0x13: SLO_IndirectY(); break;
                
                // RLA - Rotate left then AND with A
                case 0x27: RLA_ZeroPage(); break;
                case 0x37: RLA_ZeroPageX(); break;
                case 0x2F: RLA_Absolute(); break;
                case 0x3F: RLA_AbsoluteX(); break;
                case 0x3B: RLA_AbsoluteY(); break;
                case 0x23: RLA_IndirectX(); break;
                case 0x33: RLA_IndirectY(); break;
                
                // SRE - Shift right then XOR with A
                case 0x47: SRE_ZeroPage(); break;
                case 0x57: SRE_ZeroPageX(); break;
                case 0x4F: SRE_Absolute(); break;
                case 0x5F: SRE_AbsoluteX(); break;
                case 0x5B: SRE_AbsoluteY(); break;
                case 0x43: SRE_IndirectX(); break;
                case 0x53: SRE_IndirectY(); break;
                
                // RRA - Rotate right then add to A
                case 0x67: RRA_ZeroPage(); break;
                case 0x77: RRA_ZeroPageX(); break;
                case 0x6F: RRA_Absolute(); break;
                case 0x7F: RRA_AbsoluteX(); break;
                case 0x7B: RRA_AbsoluteY(); break;
                case 0x63: RRA_IndirectX(); break;
                case 0x73: RRA_IndirectY(); break;

                // Unofficial immediate ALU opcodes
                case 0x0B: case 0x2B: AAC_Immediate(); break;
                case 0x4B: ASR_Immediate(); break;
                case 0x6B: ARR_Immediate(); break;
                case 0xAB: ATX_Immediate(); break;
                case 0xCB: AXS_Immediate(); break;

                // Unofficial absolute indexed stores
                case 0x9C: SYA_AbsoluteX(); break;
                case 0x9E: SXA_AbsoluteY(); break;
                
                // NOP variants - No operation with different addressing modes
                case 0x04: case 0x44: case 0x64: NOP_ZeroPage(); break;
                case 0x14: case 0x34: case 0x54: case 0x74:
                case 0xD4: case 0xF4: NOP_ZeroPageX(); break;
                case 0x0C: NOP_Absolute(); break;
                case 0x1C: case 0x3C: case 0x5C: case 0x7C:
                case 0xDC: case 0xFC: NOP_AbsoluteX(); break;
                case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2: NOP_Immediate(); break;
                case 0x1A: case 0x3A: case 0x5A: case 0x7A:
                case 0xDA: case 0xFA: NOP(); break;
                
                // USBC - Unofficial SBC Immediate (same as SBC immediate)
                case 0xEB: SBC_Immediate(); break;
                
                default:
                    // 未实现的指令，跳过
                    // Console.WriteLine($"Warning: Unimplemented instruction 0x{opcode:X2} at PC=0x{PC-1:X4}");
                    cycles += 2;
                    break;
            }
        }

        #region 指令实现
        
        private void LDA_Immediate()
        {
            A = memoryBus.Read(PC++);
            SetZN(A);
            cycles += 2;
        }
        
        private void LDA_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 3;
        }
        
        private void LDA_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 4;
        }
        
        private void LDA_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 4;
        }
        
        private void LDA_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 4;
        }
        
        private void LDA_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 4;
        }
        
        private void LDA_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 6;
        }
        
        private void LDA_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            A = memoryBus.Read(addr);
            SetZN(A);
            cycles += 5;
        }
        
        private void LDX_Immediate()
        {
            X = memoryBus.Read(PC++);
            SetZN(X);
            cycles += 2;
        }
        
        private void LDX_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            X = memoryBus.Read(addr);
            SetZN(X);
            cycles += 3;
        }
        
        private void LDX_ZeroPageY()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + Y);
            X = memoryBus.Read(addr);
            SetZN(X);
            cycles += 4;
        }
        
        private void LDX_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            X = memoryBus.Read(addr);
            SetZN(X);
            cycles += 4;
        }
        
        private void LDX_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            X = memoryBus.Read(addr);
            SetZN(X);
            cycles += 4;
        }
        
        private void LDY_Immediate()
        {
            Y = memoryBus.Read(PC++);
            SetZN(Y);
            cycles += 2;
        }
        
        private void LDY_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            Y = memoryBus.Read(addr);
            SetZN(Y);
            cycles += 3;
        }
        
        private void LDY_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            Y = memoryBus.Read(addr);
            SetZN(Y);
            cycles += 4;
        }
        
        private void LDY_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            Y = memoryBus.Read(addr);
            SetZN(Y);
            cycles += 4;
        }
        
        private void LDY_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            Y = memoryBus.Read(addr);
            SetZN(Y);
            cycles += 4;
        }
        
        private void STA_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            memoryBus.Write(addr, A);
            cycles += 3;
        }
        
        private void STA_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            memoryBus.Write(addr, A);
            cycles += 4;
        }
        
        private void STA_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            // 添加调试输出，特别关注PPU寄存器写入
            if (addr >= 0x2000 && addr < 0x4000)
            {
                // Console.WriteLine($"CPU: STA writing to PPU register Address=0x{addr:X4}, Value=0x{A:X2}");
                // Console.Out.Flush();
            }
            memoryBus.Write(addr, A);
            cycles += 4;
        }
        
        private void STA_AbsoluteX()
        {
            ushort baseAddr = ReadWord(PC);
            PC += 2;
            DummyReadIndexedStore(baseAddr, X);
            ushort addr = (ushort)(baseAddr + X);
            memoryBus.Write(addr, A);
            cycles += 5;
        }
        
        private void STA_AbsoluteY()
        {
            ushort baseAddr = ReadWord(PC);
            PC += 2;
            DummyReadIndexedStore(baseAddr, Y);
            ushort addr = (ushort)(baseAddr + Y);
            memoryBus.Write(addr, A);
            cycles += 5;
        }
        
        private void STA_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            memoryBus.Write(addr, A);
            cycles += 6;
        }
        
        private void STA_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort baseAddr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            DummyReadIndexedStore(baseAddr, Y);
            ushort addr = (ushort)(baseAddr + Y);
            memoryBus.Write(addr, A);
            cycles += 6;
        }

        private void DummyReadIndexedStore(ushort baseAddr, byte index)
        {
            ushort dummyAddr = (ushort)((baseAddr & 0xFF00) | ((baseAddr + index) & 0x00FF));
            memoryBus.Read(dummyAddr);
        }
        
        private void JMP_Absolute()
        {
            ushort currentPC = (ushort)(PC - 1); // PC已经递增，减1得到指令地址
            ushort targetAddress = ReadWord(PC);
            // Console.WriteLine($"JMP: Executing JMP instruction at address 0x{currentPC:X4}, jumping to 0x{targetAddress:X4}");
            
            // 如果检测到在0xF880-0xF890范围内的跳转，输出内存内容
            if ((currentPC >= 0xF880 && currentPC <= 0xF890) || (targetAddress >= 0xF880 && targetAddress <= 0xF890))
            {
                // Console.WriteLine($"Memory debug - 0xF880-0xF890 range:");
            // for (ushort addr = 0xF880; addr <= 0xF890; addr++)
            // {
            //     byte value = memoryBus.Read(addr);
            //     Console.WriteLine($"  0x{addr:X4}: 0x{value:X2}");
            // }
            }
            
            // Console.Out.Flush();
            PC = targetAddress;
            cycles += 3;
        }
        
        private void JMP_Indirect()
        {
            ushort ptr = ReadWord(PC);
            // 6502 JMP indirect bug: if ptr is at page boundary (e.g., $xxFF),
            // the high byte is fetched from $xx00 instead of $(xx+1)00
            byte lowByte = memoryBus.Read(ptr);
            ushort highByteAddr;
            if ((ptr & 0x00FF) == 0x00FF)
            {
                // Page boundary bug: wrap around within the same page
                highByteAddr = (ushort)((ptr & 0xFF00));
            }
            else
            {
                highByteAddr = (ushort)(ptr + 1);
            }
            byte highByte = memoryBus.Read(highByteAddr);
            PC = (ushort)(lowByte | (highByte << 8));
            cycles += 5;
        }
        
        private void JSR()
        {
            ushort addr = ReadWord(PC);
            PC++;
            PushWord((ushort)(PC));
            PC = addr;
            cycles += 6;
        }
        
        private void RTS()
        {
            PC = (ushort)(PopWord() + 1);
            cycles += 6;
        }
        
        private void BEQ()
        {
            sbyte offset = (sbyte)memoryBus.Read(PC++);
            if (GetFlag(FLAG_ZERO))
            {
                PC = (ushort)(PC + offset);
                cycles += 3;
            }
            else
            {
                cycles += 2;
            }
        }
        
        private void BNE()
        {
            sbyte offset = (sbyte)memoryBus.Read(PC++);
            if (!GetFlag(FLAG_ZERO))
            {
                PC = (ushort)(PC + offset);
                cycles += 3;
            }
            else
            {
                cycles += 2;
            }
        }
        
        private void CMP_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            Compare(A, value);
            cycles += 2;
        }
        
        private void CMP_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 3;
        }
        
        private void CMP_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 4;
        }
        
        private void CMP_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 4;
        }
        
        private void CMP_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 4;
        }
        
        private void CMP_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 4;
        }
        
        private void CMP_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 6;
        }
        
        private void CMP_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            Compare(A, value);
            cycles += 5;
        }
        
        private void INX()
        {
            X++;
            SetZN(X);
            cycles += 2;
        }
        
        private void INY()
        {
            Y++;
            SetZN(Y);
            cycles += 2;
        }
        
        private void DEX()
        {
            X--;
            SetZN(X);
            cycles += 2;
        }
        
        private void DEY()
        {
            Y--;
            SetZN(Y);
            // Console.WriteLine($"CPU: DEY executed, Y register: 0x{Y:X2}, Zero flag: {GetFlag(FLAG_ZERO)}");
            cycles += 2;
        }
        
        private void NOP()
        {
            cycles += 2;
        }
        
        private void BRK()
        {
            PC++;
            byte lowByte = memoryBus.Read(0xFFFE);
            byte highByte = memoryBus.Read(0xFFFF);
            ushort irqVector = (ushort)(lowByte | (highByte << 8));
            // Console.WriteLine($"CPU: BRK instruction executed, reading IRQ vector: 0xFFFE=0x{lowByte:X2}, 0xFFFF=0x{highByte:X2}, vector address=0x{irqVector:X4}");
            
            // 检查向量地址是否合理（应该在ROM区域0x8000-0xFFFF）
            if (irqVector < 0x8000)
            {
                // Console.WriteLine($"Warning: IRQ vector address 0x{irqVector:X4} not in ROM area, ROM may not be loaded correctly or mapping error");
                // 暂停执行以避免无限循环
                return;
            }
            
            // 检查IRQ向量指向的地址是否也是BRK指令，防止无限循环
            byte instructionAtVector = memoryBus.Read(irqVector);
            if (instructionAtVector == 0x00) // BRK指令的操作码是0x00
            {
                // Console.WriteLine($"Error: IRQ vector 0x{irqVector:X4} points to another BRK instruction, this will cause an infinite loop. Stopping execution.");
                // 设置一个标志来停止模拟器
                throw new InvalidOperationException($"BRK instruction infinite loop: IRQ vector 0x{irqVector:X4} points to another BRK instruction");
            }
            
            PushWord(PC);
            PushByte((byte)(P | FLAG_BREAK | FLAG_UNUSED));
            SetFlag(FLAG_INTERRUPT, true);
            PC = irqVector;
            cycles += 7;
        }
        
        private void RTI()
        {
            byte value = PopByte();
            P = (byte)((value & 0xCF) | FLAG_UNUSED);
            PC = PopWord();
            cycles += 6;
        }
        
        private void SEI()
        {
            SetFlag(FLAG_INTERRUPT, true);
            cycles += 2;
        }
        
        private void CLI()
        {
            SetFlag(FLAG_INTERRUPT, false);
            cycles += 2;
        }
        
        private void CLD()
        {
            SetFlag(FLAG_DECIMAL, false);
            cycles += 2;
        }
        
        private void SED()
        {
            SetFlag(FLAG_DECIMAL, true);
            cycles += 2;
        }
        
        private void CLC()
        {
            SetFlag(FLAG_CARRY, false);
            cycles += 2;
        }
        
        private void SEC()
        {
            SetFlag(FLAG_CARRY, true);
            cycles += 2;
        }
        
        private void CLV()
        {
            SetFlag(FLAG_OVERFLOW, false);
            cycles += 2;
        }
        
        // ADC - Add with Carry implementations
        private void ADC_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            ADC(value);
            cycles += 2;
        }
        
        private void ADC_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 3;
        }
        
        private void ADC_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 4;
        }
        
        private void ADC_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 4;
        }
        
        private void ADC_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 4;
        }
        
        private void ADC_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 4;
        }
        
        private void ADC_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 6;
        }
        
        private void ADC_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            ADC(value);
            cycles += 5;
        }
        
        // SBC - Subtract with Carry implementations
        private void SBC_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            SBC(value);
            cycles += 2;
        }
        
        private void SBC_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 3;
        }
        
        private void SBC_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 4;
        }
        
        private void SBC_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 4;
        }
        
        private void SBC_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 4;
        }
        
        private void SBC_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 4;
        }
        
        private void SBC_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 6;
        }
        
        private void SBC_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            SBC(value);
            cycles += 5;
        }
        
        // AND - Logical AND implementations
        private void AND_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A &= value;
            SetZN(A);
            cycles += 2;
        }
        
        private void AND_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 3;
        }
        
        private void AND_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void AND_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void AND_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void AND_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void AND_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void AND_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            A &= value;
            SetZN(A);
            cycles += 5;
        }
        
        // ORA - Logical OR implementations
        private void ORA_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A |= value;
            SetZN(A);
            cycles += 2;
        }
        
        private void ORA_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 3;
        }
        
        private void ORA_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void ORA_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void ORA_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void ORA_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void ORA_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void ORA_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            A |= value;
            SetZN(A);
            cycles += 5;
        }
        
        // EOR - Exclusive OR implementations
        private void EOR_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A ^= value;
            SetZN(A);
            cycles += 2;
        }
        
        private void EOR_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 3;
        }
        
        private void EOR_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void EOR_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            
            A ^= value;
            SetZN(A);
            cycles += 4;
        }
        
        // BIT - Bit Test implementations
        private void BIT_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            BIT(value);
            cycles += 3;
        }
        
        private void BIT_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            BIT(value);
            cycles += 4;
        }
        
        private void BIT(byte value)
        {
            SetFlag(FLAG_ZERO, (A & value) == 0);
            SetFlag(FLAG_NEGATIVE, (value & 0x80) != 0);
            SetFlag(FLAG_OVERFLOW, (value & 0x40) != 0);
        }
        
        #region ASL - Arithmetic Shift Left
        private void ASL_Accumulator()
        {
            SetFlag(FLAG_CARRY, (A & 0x80) != 0);
            A = (byte)(A << 1);
            SetZN(A);
            cycles += 2;
        }
        
        private void ASL_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)(value << 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void ASL_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)(value << 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ASL_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)(value << 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ASL_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)(value << 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        #endregion
        
        #region LSR - Logical Shift Right
        private void LSR_Accumulator()
        {
            SetFlag(FLAG_CARRY, (A & 0x01) != 0);
            A = (byte)(A >> 1);
            SetZN(A);
            cycles += 2;
        }
        
        private void LSR_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void LSR_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void LSR_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void LSR_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)(value >> 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        #endregion
        
        #region ROL - Rotate Left
        private void ROL_Accumulator()
        {
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (A & 0x80) != 0);
            A = (byte)((A << 1) | (carry ? 1 : 0));
            SetZN(A);
            cycles += 2;
        }
        
        private void ROL_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (carry ? 1 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void ROL_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (carry ? 1 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ROL_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (carry ? 1 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ROL_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (carry ? 1 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        #endregion
        
        #region ROR - Rotate Right
        private void ROR_Accumulator()
        {
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (A & 0x01) != 0);
            A = (byte)((A >> 1) | (carry ? 0x80 : 0));
            SetZN(A);
            cycles += 2;
        }
        
        private void ROR_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (carry ? 0x80 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void ROR_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (carry ? 0x80 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ROR_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (carry ? 0x80 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void ROR_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            bool carry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (carry ? 0x80 : 0));
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        #endregion
        
        private void EOR_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void EOR_AbsoluteY()
        {
            ushort addr = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 4;
        }
        
        private void EOR_IndirectX()
        {
            byte ptr = (byte)(memoryBus.Read(PC++) + X);
            ushort addr = (ushort)(memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8));
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void EOR_IndirectY()
        {
            byte ptr = memoryBus.Read(PC++);
            ushort addr = (ushort)((memoryBus.Read(ptr) | (memoryBus.Read((byte)(ptr + 1)) << 8)) + Y);
            byte value = memoryBus.Read(addr);
            A ^= value;
            SetZN(A);
            cycles += 5;
        }
        
        // ADC and SBC helper methods
        private void ADC(byte value)
        {
            int result = A + value + (GetFlag(FLAG_CARRY) ? 1 : 0);
            SetFlag(FLAG_CARRY, result > 0xFF);
            SetFlag(FLAG_OVERFLOW, ((A ^ result) & (value ^ result) & 0x80) != 0);
            A = (byte)result;
            SetZN(A);
        }
        
        private void SBC(byte value)
        {
            int result = A - value - (GetFlag(FLAG_CARRY) ? 0 : 1);
            SetFlag(FLAG_CARRY, result >= 0);
            SetFlag(FLAG_OVERFLOW, ((A ^ result) & ((A ^ value) & 0x80)) != 0);
            A = (byte)result;
            SetZN(A);
        }
        
        // STX - Store X Register
        private void STX_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            memoryBus.Write(addr, X);
            cycles += 3;
        }
        
        private void STX_ZeroPageY()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + Y);
            memoryBus.Write(addr, X);
            cycles += 4;
        }
        
        private void STX_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            memoryBus.Write(addr, X);
            cycles += 4;
        }
        
        // STY - Store Y Register
        private void STY_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            memoryBus.Write(addr, Y);
            cycles += 3;
        }
        
        private void STY_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            memoryBus.Write(addr, Y);
            cycles += 4;
        }
        
        private void STY_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            memoryBus.Write(addr, Y);
            cycles += 4;
        }
        
        // Transfer Instructions
        private void TAX()
        {
            X = A;
            SetZN(X);
            cycles += 2;
        }
        
        private void TXA()
        {
            A = X;
            SetZN(A);
            cycles += 2;
        }
        
        private void TAY()
        {
            Y = A;
            SetZN(Y);
            cycles += 2;
        }
        
        private void TYA()
        {
            A = Y;
            SetZN(A);
            cycles += 2;
        }
        
        private void TXS()
        {
            SP = X;
            cycles += 2;
        }
        
        private void TSX()
        {
            X = SP;
            SetZN(X);
            cycles += 2;
        }
        
        // Stack Instructions
        private void PHA()
        {
            PushByte(A);
            cycles += 3;
        }
        
        private void PLA()
        {
            A = PopByte();
            SetZN(A);
            cycles += 4;
        }
        
        private void PHP()
        {
            // PHP pushes P with B flag set
            PushByte((byte)(P | FLAG_BREAK));
            cycles += 3;
        }
        
        private void PLP()
        {
            byte value = PopByte();
            // PLP: B flag (bit 4) is ignored when pulling from stack
            // The unused flag (bit 5) should always be set to 1
            // B flag should NOT be set - it only indicates software interrupt in pushed status
            P = (byte)((value & 0xCF) | FLAG_UNUSED);
            cycles += 4;
        }
        
        // Compare Instructions
        private void CPX_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            Compare(X, value);
            cycles += 2;
        }
        
        private void CPX_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            Compare(X, value);
            cycles += 3;
        }
        
        private void CPX_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            Compare(X, value);
            cycles += 4;
        }
        
        private void CPY_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            Compare(Y, value);
            cycles += 2;
        }
        
        private void CPY_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = memoryBus.Read(addr);
            Compare(Y, value);
            cycles += 3;
        }
        
        private void CPY_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(addr);
            Compare(Y, value);
            cycles += 4;
        }
        
        // Increment/Decrement Memory Instructions
        private void INC_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = (byte)(memoryBus.Read(addr) + 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void INC_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = (byte)(memoryBus.Read(addr) + 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void INC_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = (byte)(memoryBus.Read(addr) + 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void INC_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = (byte)(memoryBus.Read(addr) + 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        
        private void DEC_ZeroPage()
        {
            byte addr = memoryBus.Read(PC++);
            byte value = (byte)(memoryBus.Read(addr) - 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 5;
        }
        
        private void DEC_ZeroPageX()
        {
            byte addr = (byte)(memoryBus.Read(PC++) + X);
            byte value = (byte)(memoryBus.Read(addr) - 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void DEC_Absolute()
        {
            ushort addr = ReadWord(PC);
            PC += 2;
            byte value = (byte)(memoryBus.Read(addr) - 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 6;
        }
        
        private void DEC_AbsoluteX()
        {
            ushort addr = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = (byte)(memoryBus.Read(addr) - 1);
            memoryBus.Write(addr, value);
            SetZN(value);
            cycles += 7;
        }
        
        // Branch Instructions
        private void BPL() // Branch on Plus (N=0)
        {
            Branch(!GetFlag(FLAG_NEGATIVE));
        }
        
        private void BMI() // Branch on Minus (N=1)
        {
            Branch(GetFlag(FLAG_NEGATIVE));
        }
        
        private void BVC() // Branch on Overflow Clear (V=0)
        {
            Branch(!GetFlag(FLAG_OVERFLOW));
        }
        
        private void BVS() // Branch on Overflow Set (V=1)
        {
            Branch(GetFlag(FLAG_OVERFLOW));
        }
        
        private void BCC() // Branch on Carry Clear (C=0)
        {
            Branch(!GetFlag(FLAG_CARRY));
        }
        
        private void BCS() // Branch on Carry Set (C=1)
        {
            Branch(GetFlag(FLAG_CARRY));
        }
        
        private void Branch(bool condition)
        {
            sbyte offset = (sbyte)memoryBus.Read(PC++);
            cycles += 2;
            
            if (condition)
            {
                ushort oldPC = PC;
                PC = (ushort)(PC + offset);
                cycles += 1;
                
                // 跨页额外周期
                if ((oldPC & 0xFF00) != (PC & 0xFF00))
                {
                    cycles += 1;
                }
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        private ushort ReadWord(ushort address)
        {
            return (ushort)(memoryBus.Read(address) | (memoryBus.Read((ushort)(address + 1)) << 8));
        }
        
        private void PushByte(byte value)
        {
            memoryBus.Write((ushort)(0x0100 + SP), value);
            SP--;
        }
        
        private void PushWord(ushort value)
        {
            PushByte((byte)(value >> 8));
            PushByte((byte)(value & 0xFF));
        }
        
        private byte PopByte()
        {
            SP++;
            return memoryBus.Read((ushort)(0x0100 + SP));
        }
        
        private ushort PopWord()
        {
            byte low = PopByte();
            byte high = PopByte();
            return (ushort)(low | (high << 8));
        }
        
        private void SetFlag(byte flag, bool value)
        {
            if (value)
                P |= flag;
            else
                P &= (byte)~flag;
        }
        
        private bool GetFlag(byte flag)
        {
            return (P & flag) != 0;
        }
        
        private void SetZN(byte value)
        {
            SetFlag(FLAG_ZERO, value == 0);
            SetFlag(FLAG_NEGATIVE, (value & 0x80) != 0);
        }
        
        private void Compare(byte reg, byte value)
        {
            int result = reg - value;
            SetFlag(FLAG_CARRY, reg >= value);
            SetFlag(FLAG_ZERO, reg == value);
            SetFlag(FLAG_NEGATIVE, (result & 0x80) != 0);
        }
        
        #endregion
        
        #region 非法/非官方操作码实现
        
        // STP - 停止处理器
        private void STP()
        {
            // Console.WriteLine($"CPU: STP instruction executed, CPU stopped at PC=0x{(PC-1):X4}");
            // 在真实硬件中，STP会停止CPU直到复位
            // 在模拟器中，我们可以设置一个标志或者简单地不增加PC
            PC--; // 保持在当前指令，模拟停止状态
            cycles += 2;
        }
        
        // LAX - 加载累加器和X寄存器
        private void LAX_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 3;
        }
        
        private void LAX_ZPY()
        {
            byte address = (byte)(memoryBus.Read(PC++) + Y);
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 4;
        }
        
        private void LAX_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 4;
        }
        
        private void LAX_ABSY()
        {
            ushort baseAddress = ReadWord(PC);
            PC += 2;
            ushort address = (ushort)(baseAddress + Y);
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 4;
            if ((baseAddress & 0xFF00) != (address & 0xFF00))
                cycles += 1;
        }
        
        private void LAX_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 6;
        }
        
        private void LAX_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort baseAddress = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            ushort address = (ushort)(baseAddress + Y);
            byte value = memoryBus.Read(address);
            A = value;
            X = value;
            SetZN(value);
            cycles += 5;
            if ((baseAddress & 0xFF00) != (address & 0xFF00))
                cycles += 1;
        }
        
        // SAX - 存储A AND X
        private void SAX_ZP()
        {
            byte address = memoryBus.Read(PC++);
            memoryBus.Write(address, (byte)(A & X));
            cycles += 3;
        }
        
        private void SAX_ZPY()
        {
            byte address = (byte)(memoryBus.Read(PC++) + Y);
            memoryBus.Write(address, (byte)(A & X));
            cycles += 4;
        }
        
        private void SAX_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            memoryBus.Write(address, (byte)(A & X));
            cycles += 4;
        }
        
        private void SAX_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            memoryBus.Write(address, (byte)(A & X));
            cycles += 6;
        }
        
        // DCP - 递减内存然后比较
        private void DCP_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 5;
        }
        
        private void DCP_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 6;
        }
        
        private void DCP_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 6;
        }
        
        private void DCP_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 7;
        }
        
        private void DCP_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 7;
        }
        
        private void DCP_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 8;
        }
        
        private void DCP_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = (byte)(memoryBus.Read(address) - 1);
            memoryBus.Write(address, value);
            Compare(A, value);
            cycles += 8;
        }
        
        // ISC - 递增内存然后带借位减法
        private void ISC_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 5;
        }
        
        private void ISC_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 6;
        }
        
        private void ISC_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 6;
        }
        
        private void ISC_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 7;
        }
        
        private void ISC_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 7;
        }
        
        private void ISC_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 8;
        }
        
        private void ISC_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = (byte)(memoryBus.Read(address) + 1);
            memoryBus.Write(address, value);
            SBC_Value(value);
            cycles += 8;
        }
        
        // SLO - 算术左移然后OR
        private void SLO_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 5;
        }
        
        private void SLO_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void SLO_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void SLO_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void SLO_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void SLO_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 8;
        }
        
        private void SLO_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value <<= 1;
            memoryBus.Write(address, value);
            A |= value;
            SetZN(A);
            cycles += 8;
        }
        
        // RLA - 循环左移然后AND
        private void RLA_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 5;
        }
        
        private void RLA_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void RLA_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void RLA_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void RLA_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void RLA_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 8;
        }
        
        private void RLA_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x80) != 0);
            value = (byte)((value << 1) | (oldCarry ? 1 : 0));
            memoryBus.Write(address, value);
            A &= value;
            SetZN(A);
            cycles += 8;
        }
        
        // SRE - 逻辑右移然后XOR
        private void SRE_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 5;
        }
        
        private void SRE_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void SRE_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 6;
        }
        
        private void SRE_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void SRE_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 7;
        }
        
        private void SRE_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 8;
        }
        
        private void SRE_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = memoryBus.Read(address);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value >>= 1;
            memoryBus.Write(address, value);
            A ^= value;
            SetZN(A);
            cycles += 8;
        }
        
        // RRA - 循环右移然后ADC
        private void RRA_ZP()
        {
            byte address = memoryBus.Read(PC++);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 5;
        }
        
        private void RRA_ZPX()
        {
            byte address = (byte)(memoryBus.Read(PC++) + X);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 6;
        }
        
        private void RRA_ABS()
        {
            ushort address = ReadWord(PC);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 6;
        }
        
        private void RRA_ABSX()
        {
            ushort address = (ushort)(ReadWord(PC) + X);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 7;
        }
        
        private void RRA_ABSY()
        {
            ushort address = (ushort)(ReadWord(PC) + Y);
            PC += 2;
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 7;
        }
        
        private void RRA_INDX()
        {
            byte zpAddress = (byte)(memoryBus.Read(PC++) + X);
            ushort address = (ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8));
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 8;
        }
        
        private void RRA_INDY()
        {
            byte zpAddress = memoryBus.Read(PC++);
            ushort address = (ushort)((ushort)(memoryBus.Read(zpAddress) | (memoryBus.Read((byte)(zpAddress + 1)) << 8)) + Y);
            byte value = memoryBus.Read(address);
            bool oldCarry = GetFlag(FLAG_CARRY);
            SetFlag(FLAG_CARRY, (value & 0x01) != 0);
            value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
            memoryBus.Write(address, value);
            ADC_Value(value);
            cycles += 8;
        }
        
        // NOP变体 - 不同寻址模式的无操作指令
        // AAC/ANC - AND immediate, then copy bit 7 to carry
        private void AAC_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A &= value;
            SetZN(A);
            SetFlag(FLAG_CARRY, (A & 0x80) != 0);
            cycles += 2;
        }

        // ASR/ALR - AND immediate, then logical shift right accumulator
        private void ASR_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A &= value;
            SetFlag(FLAG_CARRY, (A & 0x01) != 0);
            A >>= 1;
            SetZN(A);
            cycles += 2;
        }

        // ARR - AND immediate, then rotate right with special C/V flag behavior
        private void ARR_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A &= value;
            A = (byte)((A >> 1) | (GetFlag(FLAG_CARRY) ? 0x80 : 0));
            SetZN(A);
            SetFlag(FLAG_CARRY, (A & 0x40) != 0);
            SetFlag(FLAG_OVERFLOW, ((A >> 5) & 0x01) != ((A >> 6) & 0x01));
            cycles += 2;
        }

        // ATX/LAX immediate - load immediate into A and X
        private void ATX_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            A = value;
            X = A;
            SetZN(A);
            cycles += 2;
        }

        // AXS/SBX - subtract immediate from A AND X, store in X
        private void AXS_Immediate()
        {
            byte value = memoryBus.Read(PC++);
            int result = (A & X) - value;
            SetFlag(FLAG_CARRY, result >= 0);
            X = (byte)result;
            SetZN(X);
            cycles += 2;
        }

        // SYA/SHY - store Y AND (base high byte + 1) to absolute,X
        private void SYA_AbsoluteX()
        {
            ushort baseAddress = ReadWord(PC);
            PC += 2;
            ushort address = (ushort)(baseAddress + X);
            if ((baseAddress & 0xFF00) == (address & 0xFF00))
            {
                byte value = (byte)(Y & (((baseAddress >> 8) + 1) & 0xFF));
                memoryBus.Write(address, value);
            }
            cycles += 5;
        }

        // SXA/SHX - store X AND (base high byte + 1) to absolute,Y
        private void SXA_AbsoluteY()
        {
            ushort baseAddress = ReadWord(PC);
            PC += 2;
            ushort address = (ushort)(baseAddress + Y);
            if ((baseAddress & 0xFF00) == (address & 0xFF00))
            {
                byte value = (byte)(X & (((baseAddress >> 8) + 1) & 0xFF));
                memoryBus.Write(address, value);
            }
            cycles += 5;
        }

        private void NOP_ZeroPage()
        {
            PC++; // 跳过零页地址
            cycles += 3;
        }
        
        private void NOP_ZeroPageX()
        {
            PC++; // 跳过零页地址
            cycles += 4;
        }
        
        private void NOP_Absolute()
        {
            PC += 2; // 跳过绝对地址
            cycles += 4;
        }
        
        private void NOP_AbsoluteX()
        {
            ushort baseAddress = ReadWord(PC);
            PC += 2;
            ushort address = (ushort)(baseAddress + X);
            cycles += 4;
            // 跨页额外周期
            if ((baseAddress & 0xFF00) != (address & 0xFF00))
                cycles += 1;
        }
        
        private void NOP_Immediate()
        {
            PC++; // 跳过立即数
            cycles += 2;
        }
        
        // 寻址模式的别名方法
        private void DCP_IndirectY() { DCP_INDY(); }
        private void ISC_IndirectY() { ISC_INDY(); }
        private void SLO_IndirectY() { SLO_INDY(); }
        private void RLA_IndirectY() { RLA_INDY(); }
        private void SRE_IndirectY() { SRE_INDY(); }
        private void RRA_IndirectY() { RRA_INDY(); }
        
        private void DCP_ZeroPageX() { DCP_ZPX(); }
        private void DCP_AbsoluteX() { DCP_ABSX(); }
        private void DCP_AbsoluteY() { DCP_ABSY(); }
        private void DCP_IndirectX() { DCP_INDX(); }
        
        private void ISC_ZeroPageX() { ISC_ZPX(); }
        private void ISC_AbsoluteX() { ISC_ABSX(); }
        private void ISC_AbsoluteY() { ISC_ABSY(); }
        private void ISC_IndirectX() { ISC_INDX(); }
        
        private void SLO_ZeroPageX() { SLO_ZPX(); }
        private void SLO_AbsoluteX() { SLO_ABSX(); }
        private void SLO_AbsoluteY() { SLO_ABSY(); }
        private void SLO_IndirectX() { SLO_INDX(); }
        
        private void RLA_ZeroPageX() { RLA_ZPX(); }
        private void RLA_AbsoluteX() { RLA_ABSX(); }
        private void RLA_AbsoluteY() { RLA_ABSY(); }
        private void RLA_IndirectX() { RLA_INDX(); }
        
        private void SRE_ZeroPageX() { SRE_ZPX(); }
        private void SRE_AbsoluteX() { SRE_ABSX(); }
        private void SRE_AbsoluteY() { SRE_ABSY(); }
        private void SRE_IndirectX() { SRE_INDX(); }
        
        private void RRA_ZeroPageX() { RRA_ZPX(); }
        private void RRA_AbsoluteX() { RRA_ABSX(); }
        private void RRA_AbsoluteY() { RRA_ABSY(); }
        private void RRA_IndirectX() { RRA_INDX(); }
        
        private void LAX_ZeroPageY() { LAX_ZPY(); }
        private void LAX_AbsoluteY() { LAX_ABSY(); }
        private void LAX_IndirectX() { LAX_INDX(); }
        private void LAX_IndirectY() { LAX_INDY(); }
        
        private void SAX_ZeroPageY() { SAX_ZPY(); }
        private void SAX_IndirectX() { SAX_INDX(); }
        
        // 零页和绝对寻址模式的别名方法
        private void DCP_ZeroPage() { DCP_ZP(); }
        private void DCP_Absolute() { DCP_ABS(); }
        
        private void ISC_ZeroPage() { ISC_ZP(); }
        private void ISC_Absolute() { ISC_ABS(); }
        
        private void SLO_ZeroPage() { SLO_ZP(); }
        private void SLO_Absolute() { SLO_ABS(); }
        
        private void RLA_ZeroPage() { RLA_ZP(); }
        private void RLA_Absolute() { RLA_ABS(); }
        
        private void SRE_ZeroPage() { SRE_ZP(); }
        private void SRE_Absolute() { SRE_ABS(); }
        
        private void RRA_ZeroPage() { RRA_ZP(); }
        private void RRA_Absolute() { RRA_ABS(); }
        
        private void LAX_ZeroPage() { LAX_ZP(); }
        private void LAX_Absolute() { LAX_ABS(); }
        
        private void SAX_ZeroPage() { SAX_ZP(); }
        private void SAX_Absolute() { SAX_ABS(); }
        
        // 辅助方法用于ADC和SBC
        private void ADC_Value(byte value)
        {
            int result = A + value + (GetFlag(FLAG_CARRY) ? 1 : 0);
            SetFlag(FLAG_CARRY, result > 0xFF);
            SetFlag(FLAG_OVERFLOW, ((A ^ result) & (value ^ result) & 0x80) != 0);
            A = (byte)result;
            SetZN(A);
        }
        
        private void SBC_Value(byte value)
        {
            int result = A - value - (GetFlag(FLAG_CARRY) ? 0 : 1);
            SetFlag(FLAG_CARRY, result >= 0);
            SetFlag(FLAG_OVERFLOW, ((A ^ result) & ((A ^ value) & 0x80)) != 0);
            A = (byte)result;
            SetZN(A);
        }
        
        #endregion
        
        #region 中断处理
        
        public void TriggerNMI()
        {
            nmiPending = true;
        }

        public void AddStallCycles(int additionalCycles)
        {
            stallCycles += additionalCycles;
        }
        
        public void TriggerIRQ()
        {
            irqPending = true;
        }

        public int TotalCycles => cycles;
        
        private void HandleNMI()
        {
            PushWord(PC);
            PushByte(P);
            SetFlag(FLAG_INTERRUPT, true);
            ushort nmiVector = (ushort)(memoryBus.Read(0xFFFA) | (memoryBus.Read(0xFFFB) << 8));
            // Console.WriteLine($"CPU: NMI interrupt triggered, vector address=0x{nmiVector:X4}");
            PC = nmiVector;
            cycles += 7;
        }
        
        private void HandleIRQ()
        {
            PushWord(PC);
            PushByte(P);
            SetFlag(FLAG_INTERRUPT, true);
            PC = (ushort)(memoryBus.Read(0xFFFE) | (memoryBus.Read(0xFFFF) << 8));
            cycles += 7;
        }
        
        #endregion
    }
}
