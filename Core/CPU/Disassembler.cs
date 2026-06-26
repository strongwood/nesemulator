using System;
using System.Collections.Generic;
using System.Text;

namespace NESEmulator.Core.CPU
{
    /// <summary>
    /// 6502指令反汇编器
    /// </summary>
    public class Disassembler
    {
        // 寻址模式
        public enum AddressingMode
        {
            Implied,        // 隐含寻址
            Accumulator,    // 累加器寻址
            Immediate,      // 立即寻址
            ZeroPage,       // 零页寻址
            ZeroPageX,      // 零页X变址
            ZeroPageY,      // 零页Y变址
            Relative,       // 相对寻址
            Absolute,       // 绝对寻址
            AbsoluteX,      // 绝对X变址
            AbsoluteY,      // 绝对Y变址
            Indirect,       // 间接寻址
            IndirectX,      // 间接X变址
            IndirectY       // 间接Y变址
        }

        // 指令信息结构
        private struct InstructionInfo
        {
            public string Mnemonic;
            public AddressingMode Mode;
            public int Length;

            public InstructionInfo(string mnemonic, AddressingMode mode, int length)
            {
                Mnemonic = mnemonic;
                Mode = mode;
                Length = length;
            }
        }

        // 指令表
        private static readonly Dictionary<byte, InstructionInfo> instructionTable = new Dictionary<byte, InstructionInfo>()
        {
            // LDA
            { 0xA9, new InstructionInfo("LDA", AddressingMode.Immediate, 2) },
            { 0xA5, new InstructionInfo("LDA", AddressingMode.ZeroPage, 2) },
            { 0xB5, new InstructionInfo("LDA", AddressingMode.ZeroPageX, 2) },
            { 0xAD, new InstructionInfo("LDA", AddressingMode.Absolute, 3) },
            { 0xBD, new InstructionInfo("LDA", AddressingMode.AbsoluteX, 3) },
            { 0xB9, new InstructionInfo("LDA", AddressingMode.AbsoluteY, 3) },
            { 0xA1, new InstructionInfo("LDA", AddressingMode.IndirectX, 2) },
            { 0xB1, new InstructionInfo("LDA", AddressingMode.IndirectY, 2) },
            
            // LDX
            { 0xA2, new InstructionInfo("LDX", AddressingMode.Immediate, 2) },
            { 0xA6, new InstructionInfo("LDX", AddressingMode.ZeroPage, 2) },
            { 0xB6, new InstructionInfo("LDX", AddressingMode.ZeroPageY, 2) },
            { 0xAE, new InstructionInfo("LDX", AddressingMode.Absolute, 3) },
            { 0xBE, new InstructionInfo("LDX", AddressingMode.AbsoluteY, 3) },
            
            // LDY
            { 0xA0, new InstructionInfo("LDY", AddressingMode.Immediate, 2) },
            { 0xA4, new InstructionInfo("LDY", AddressingMode.ZeroPage, 2) },
            { 0xB4, new InstructionInfo("LDY", AddressingMode.ZeroPageX, 2) },
            { 0xAC, new InstructionInfo("LDY", AddressingMode.Absolute, 3) },
            { 0xBC, new InstructionInfo("LDY", AddressingMode.AbsoluteX, 3) },
            
            // STA
            { 0x85, new InstructionInfo("STA", AddressingMode.ZeroPage, 2) },
            { 0x95, new InstructionInfo("STA", AddressingMode.ZeroPageX, 2) },
            { 0x8D, new InstructionInfo("STA", AddressingMode.Absolute, 3) },
            { 0x9D, new InstructionInfo("STA", AddressingMode.AbsoluteX, 3) },
            { 0x99, new InstructionInfo("STA", AddressingMode.AbsoluteY, 3) },
            { 0x81, new InstructionInfo("STA", AddressingMode.IndirectX, 2) },
            { 0x91, new InstructionInfo("STA", AddressingMode.IndirectY, 2) },
            
            // STX
            { 0x86, new InstructionInfo("STX", AddressingMode.ZeroPage, 2) },
            { 0x96, new InstructionInfo("STX", AddressingMode.ZeroPageY, 2) },
            { 0x8E, new InstructionInfo("STX", AddressingMode.Absolute, 3) },
            
            // STY
            { 0x84, new InstructionInfo("STY", AddressingMode.ZeroPage, 2) },
            { 0x94, new InstructionInfo("STY", AddressingMode.ZeroPageX, 2) },
            { 0x8C, new InstructionInfo("STY", AddressingMode.Absolute, 3) },
            
            // JMP
            { 0x4C, new InstructionInfo("JMP", AddressingMode.Absolute, 3) },
            { 0x6C, new InstructionInfo("JMP", AddressingMode.Indirect, 3) },
            
            // JSR
            { 0x20, new InstructionInfo("JSR", AddressingMode.Absolute, 3) },
            
            // RTS
            { 0x60, new InstructionInfo("RTS", AddressingMode.Implied, 1) },
            
            // Branch Instructions
            { 0x90, new InstructionInfo("BCC", AddressingMode.Relative, 2) },
            { 0xB0, new InstructionInfo("BCS", AddressingMode.Relative, 2) },
            { 0xF0, new InstructionInfo("BEQ", AddressingMode.Relative, 2) },
            { 0x30, new InstructionInfo("BMI", AddressingMode.Relative, 2) },
            { 0xD0, new InstructionInfo("BNE", AddressingMode.Relative, 2) },
            { 0x10, new InstructionInfo("BPL", AddressingMode.Relative, 2) },
            { 0x50, new InstructionInfo("BVC", AddressingMode.Relative, 2) },
            { 0x70, new InstructionInfo("BVS", AddressingMode.Relative, 2) },
            
            // Flag Instructions
            { 0x18, new InstructionInfo("CLC", AddressingMode.Implied, 1) },
            { 0xD8, new InstructionInfo("CLD", AddressingMode.Implied, 1) },
            { 0x58, new InstructionInfo("CLI", AddressingMode.Implied, 1) },
            { 0xB8, new InstructionInfo("CLV", AddressingMode.Implied, 1) },
            { 0x38, new InstructionInfo("SEC", AddressingMode.Implied, 1) },
            { 0xF8, new InstructionInfo("SED", AddressingMode.Implied, 1) },
            { 0x78, new InstructionInfo("SEI", AddressingMode.Implied, 1) },
            
            // Register Instructions
            { 0xAA, new InstructionInfo("TAX", AddressingMode.Implied, 1) },
            { 0xA8, new InstructionInfo("TAY", AddressingMode.Implied, 1) },
            { 0xBA, new InstructionInfo("TSX", AddressingMode.Implied, 1) },
            { 0x8A, new InstructionInfo("TXA", AddressingMode.Implied, 1) },
            { 0x9A, new InstructionInfo("TXS", AddressingMode.Implied, 1) },
            { 0x98, new InstructionInfo("TYA", AddressingMode.Implied, 1) },
            
            // Stack Instructions
            { 0x48, new InstructionInfo("PHA", AddressingMode.Implied, 1) },
            { 0x08, new InstructionInfo("PHP", AddressingMode.Implied, 1) },
            { 0x68, new InstructionInfo("PLA", AddressingMode.Implied, 1) },
            { 0x28, new InstructionInfo("PLP", AddressingMode.Implied, 1) },
            
            // Arithmetic Instructions
            // ADC
            { 0x69, new InstructionInfo("ADC", AddressingMode.Immediate, 2) },
            { 0x65, new InstructionInfo("ADC", AddressingMode.ZeroPage, 2) },
            { 0x75, new InstructionInfo("ADC", AddressingMode.ZeroPageX, 2) },
            { 0x6D, new InstructionInfo("ADC", AddressingMode.Absolute, 3) },
            { 0x7D, new InstructionInfo("ADC", AddressingMode.AbsoluteX, 3) },
            { 0x79, new InstructionInfo("ADC", AddressingMode.AbsoluteY, 3) },
            { 0x61, new InstructionInfo("ADC", AddressingMode.IndirectX, 2) },
            { 0x71, new InstructionInfo("ADC", AddressingMode.IndirectY, 2) },
            
            // SBC
            { 0xE9, new InstructionInfo("SBC", AddressingMode.Immediate, 2) },
            { 0xE5, new InstructionInfo("SBC", AddressingMode.ZeroPage, 2) },
            { 0xF5, new InstructionInfo("SBC", AddressingMode.ZeroPageX, 2) },
            { 0xED, new InstructionInfo("SBC", AddressingMode.Absolute, 3) },
            { 0xFD, new InstructionInfo("SBC", AddressingMode.AbsoluteX, 3) },
            { 0xF9, new InstructionInfo("SBC", AddressingMode.AbsoluteY, 3) },
            { 0xE1, new InstructionInfo("SBC", AddressingMode.IndirectX, 2) },
            { 0xF1, new InstructionInfo("SBC", AddressingMode.IndirectY, 2) },
            
            // CMP
            { 0xC9, new InstructionInfo("CMP", AddressingMode.Immediate, 2) },
            { 0xC5, new InstructionInfo("CMP", AddressingMode.ZeroPage, 2) },
            { 0xD5, new InstructionInfo("CMP", AddressingMode.ZeroPageX, 2) },
            { 0xCD, new InstructionInfo("CMP", AddressingMode.Absolute, 3) },
            { 0xDD, new InstructionInfo("CMP", AddressingMode.AbsoluteX, 3) },
            { 0xD9, new InstructionInfo("CMP", AddressingMode.AbsoluteY, 3) },
            { 0xC1, new InstructionInfo("CMP", AddressingMode.IndirectX, 2) },
            { 0xD1, new InstructionInfo("CMP", AddressingMode.IndirectY, 2) },
            
            // CPX
            { 0xE0, new InstructionInfo("CPX", AddressingMode.Immediate, 2) },
            { 0xE4, new InstructionInfo("CPX", AddressingMode.ZeroPage, 2) },
            { 0xEC, new InstructionInfo("CPX", AddressingMode.Absolute, 3) },
            
            // CPY
            { 0xC0, new InstructionInfo("CPY", AddressingMode.Immediate, 2) },
            { 0xC4, new InstructionInfo("CPY", AddressingMode.ZeroPage, 2) },
            { 0xCC, new InstructionInfo("CPY", AddressingMode.Absolute, 3) },
            
            // Logical Instructions
            // AND
            { 0x29, new InstructionInfo("AND", AddressingMode.Immediate, 2) },
            { 0x25, new InstructionInfo("AND", AddressingMode.ZeroPage, 2) },
            { 0x35, new InstructionInfo("AND", AddressingMode.ZeroPageX, 2) },
            { 0x2D, new InstructionInfo("AND", AddressingMode.Absolute, 3) },
            { 0x3D, new InstructionInfo("AND", AddressingMode.AbsoluteX, 3) },
            { 0x39, new InstructionInfo("AND", AddressingMode.AbsoluteY, 3) },
            { 0x21, new InstructionInfo("AND", AddressingMode.IndirectX, 2) },
            { 0x31, new InstructionInfo("AND", AddressingMode.IndirectY, 2) },
            
            // EOR
            { 0x49, new InstructionInfo("EOR", AddressingMode.Immediate, 2) },
            { 0x45, new InstructionInfo("EOR", AddressingMode.ZeroPage, 2) },
            { 0x55, new InstructionInfo("EOR", AddressingMode.ZeroPageX, 2) },
            { 0x4D, new InstructionInfo("EOR", AddressingMode.Absolute, 3) },
            { 0x5D, new InstructionInfo("EOR", AddressingMode.AbsoluteX, 3) },
            { 0x59, new InstructionInfo("EOR", AddressingMode.AbsoluteY, 3) },
            { 0x41, new InstructionInfo("EOR", AddressingMode.IndirectX, 2) },
            { 0x51, new InstructionInfo("EOR", AddressingMode.IndirectY, 2) },
            
            // ORA
            { 0x09, new InstructionInfo("ORA", AddressingMode.Immediate, 2) },
            { 0x05, new InstructionInfo("ORA", AddressingMode.ZeroPage, 2) },
            { 0x15, new InstructionInfo("ORA", AddressingMode.ZeroPageX, 2) },
            { 0x0D, new InstructionInfo("ORA", AddressingMode.Absolute, 3) },
            { 0x1D, new InstructionInfo("ORA", AddressingMode.AbsoluteX, 3) },
            { 0x19, new InstructionInfo("ORA", AddressingMode.AbsoluteY, 3) },
            { 0x01, new InstructionInfo("ORA", AddressingMode.IndirectX, 2) },
            { 0x11, new InstructionInfo("ORA", AddressingMode.IndirectY, 2) },
            
            // BIT
            { 0x24, new InstructionInfo("BIT", AddressingMode.ZeroPage, 2) },
            { 0x2C, new InstructionInfo("BIT", AddressingMode.Absolute, 3) },
            
            // Shift & Rotate Instructions
            // ASL
            { 0x0A, new InstructionInfo("ASL", AddressingMode.Accumulator, 1) },
            { 0x06, new InstructionInfo("ASL", AddressingMode.ZeroPage, 2) },
            { 0x16, new InstructionInfo("ASL", AddressingMode.ZeroPageX, 2) },
            { 0x0E, new InstructionInfo("ASL", AddressingMode.Absolute, 3) },
            { 0x1E, new InstructionInfo("ASL", AddressingMode.AbsoluteX, 3) },
            
            // LSR
            { 0x4A, new InstructionInfo("LSR", AddressingMode.Accumulator, 1) },
            { 0x46, new InstructionInfo("LSR", AddressingMode.ZeroPage, 2) },
            { 0x56, new InstructionInfo("LSR", AddressingMode.ZeroPageX, 2) },
            { 0x4E, new InstructionInfo("LSR", AddressingMode.Absolute, 3) },
            { 0x5E, new InstructionInfo("LSR", AddressingMode.AbsoluteX, 3) },
            
            // ROL
            { 0x2A, new InstructionInfo("ROL", AddressingMode.Accumulator, 1) },
            { 0x26, new InstructionInfo("ROL", AddressingMode.ZeroPage, 2) },
            { 0x36, new InstructionInfo("ROL", AddressingMode.ZeroPageX, 2) },
            { 0x2E, new InstructionInfo("ROL", AddressingMode.Absolute, 3) },
            { 0x3E, new InstructionInfo("ROL", AddressingMode.AbsoluteX, 3) },
            
            // ROR
            { 0x6A, new InstructionInfo("ROR", AddressingMode.Accumulator, 1) },
            { 0x66, new InstructionInfo("ROR", AddressingMode.ZeroPage, 2) },
            { 0x76, new InstructionInfo("ROR", AddressingMode.ZeroPageX, 2) },
            { 0x6E, new InstructionInfo("ROR", AddressingMode.Absolute, 3) },
            { 0x7E, new InstructionInfo("ROR", AddressingMode.AbsoluteX, 3) },
            
            // Increment & Decrement Instructions
            // INC
            { 0xE6, new InstructionInfo("INC", AddressingMode.ZeroPage, 2) },
            { 0xF6, new InstructionInfo("INC", AddressingMode.ZeroPageX, 2) },
            { 0xEE, new InstructionInfo("INC", AddressingMode.Absolute, 3) },
            { 0xFE, new InstructionInfo("INC", AddressingMode.AbsoluteX, 3) },
            
            // DEC
            { 0xC6, new InstructionInfo("DEC", AddressingMode.ZeroPage, 2) },
            { 0xD6, new InstructionInfo("DEC", AddressingMode.ZeroPageX, 2) },
            { 0xCE, new InstructionInfo("DEC", AddressingMode.Absolute, 3) },
            { 0xDE, new InstructionInfo("DEC", AddressingMode.AbsoluteX, 3) },
            
            // INX
            { 0xE8, new InstructionInfo("INX", AddressingMode.Implied, 1) },
            
            // INY
            { 0xC8, new InstructionInfo("INY", AddressingMode.Implied, 1) },
            
            // DEX
            { 0xCA, new InstructionInfo("DEX", AddressingMode.Implied, 1) },
            
            // DEY
            { 0x88, new InstructionInfo("DEY", AddressingMode.Implied, 1) },
            
            // System Instructions
            { 0x00, new InstructionInfo("BRK", AddressingMode.Implied, 1) },
            { 0x40, new InstructionInfo("RTI", AddressingMode.Implied, 1) },
            { 0xEA, new InstructionInfo("NOP", AddressingMode.Implied, 1) },
            
            // 非法指令
            { 0x02, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x12, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x22, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x32, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x42, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x52, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x62, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x72, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0x92, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0xB2, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0xD2, new InstructionInfo("STP", AddressingMode.Implied, 1) },
            { 0xF2, new InstructionInfo("STP", AddressingMode.Implied, 1) },
        };

        private readonly Memory.MemoryBus memoryBus;

        public Disassembler(Memory.MemoryBus memoryBus)
        {
            this.memoryBus = memoryBus;
        }

        /// <summary>
        /// 反汇编单条指令
        /// </summary>
        /// <param name="address">指令地址</param>
        /// <returns>反汇编结果</returns>
        public DisassembledInstruction DisassembleInstruction(ushort address)
        {
            byte opcode = memoryBus.Read(address);
            
            if (!instructionTable.TryGetValue(opcode, out InstructionInfo info))
            {
                // 未知指令
                return new DisassembledInstruction
                {
                    Address = address,
                    Bytes = new byte[] { opcode },
                    Instruction = $"???\t(${opcode:X2})",
                    Length = 1
                };
            }

            string instruction;
            byte[] bytes;
            
            switch (info.Mode)
            {
                case AddressingMode.Implied:
                case AddressingMode.Accumulator:
                    instruction = info.Mnemonic;
                    bytes = new byte[] { opcode };
                    break;
                    
                case AddressingMode.Immediate:
                    byte operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} #${operand:X2}";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                case AddressingMode.ZeroPage:
                    operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} ${operand:X2}";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                case AddressingMode.ZeroPageX:
                    operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} ${operand:X2},X";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                case AddressingMode.ZeroPageY:
                    operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} ${operand:X2},Y";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                case AddressingMode.Relative:
                    sbyte offset = (sbyte)memoryBus.Read((ushort)(address + 1));
                    ushort target = (ushort)(address + 2 + offset);
                    instruction = $"{info.Mnemonic} ${target:X4}";
                    bytes = new byte[] { opcode, (byte)offset };
                    break;
                    
                case AddressingMode.Absolute:
                    byte lowByte = memoryBus.Read((ushort)(address + 1));
                    byte highByte = memoryBus.Read((ushort)(address + 2));
                    ushort addr = (ushort)((highByte << 8) | lowByte);
                    instruction = $"{info.Mnemonic} ${addr:X4}";
                    bytes = new byte[] { opcode, lowByte, highByte };
                    break;
                    
                case AddressingMode.AbsoluteX:
                    lowByte = memoryBus.Read((ushort)(address + 1));
                    highByte = memoryBus.Read((ushort)(address + 2));
                    addr = (ushort)((highByte << 8) | lowByte);
                    instruction = $"{info.Mnemonic} ${addr:X4},X";
                    bytes = new byte[] { opcode, lowByte, highByte };
                    break;
                    
                case AddressingMode.AbsoluteY:
                    lowByte = memoryBus.Read((ushort)(address + 1));
                    highByte = memoryBus.Read((ushort)(address + 2));
                    addr = (ushort)((highByte << 8) | lowByte);
                    instruction = $"{info.Mnemonic} ${addr:X4},Y";
                    bytes = new byte[] { opcode, lowByte, highByte };
                    break;
                    
                case AddressingMode.Indirect:
                    lowByte = memoryBus.Read((ushort)(address + 1));
                    highByte = memoryBus.Read((ushort)(address + 2));
                    addr = (ushort)((highByte << 8) | lowByte);
                    instruction = $"{info.Mnemonic} (${addr:X4})";
                    bytes = new byte[] { opcode, lowByte, highByte };
                    break;
                    
                case AddressingMode.IndirectX:
                    operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} (${operand:X2},X)";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                case AddressingMode.IndirectY:
                    operand = memoryBus.Read((ushort)(address + 1));
                    instruction = $"{info.Mnemonic} (${operand:X2}),Y";
                    bytes = new byte[] { opcode, operand };
                    break;
                    
                default:
                    instruction = $"???\t(${opcode:X2})";
                    bytes = new byte[] { opcode };
                    break;
            }

            return new DisassembledInstruction
            {
                Address = address,
                Bytes = bytes,
                Instruction = instruction,
                Length = info.Length
            };
        }

        /// <summary>
        /// 反汇编指定范围的代码
        /// </summary>
        /// <param name="startAddress">起始地址</param>
        /// <param name="endAddress">结束地址</param>
        /// <returns>反汇编结果列表</returns>
        public List<DisassembledInstruction> DisassembleRange(ushort startAddress, ushort endAddress)
        {
            List<DisassembledInstruction> result = new List<DisassembledInstruction>();
            ushort address = startAddress;
            
            while (address <= endAddress)
            {
                DisassembledInstruction instruction = DisassembleInstruction(address);
                result.Add(instruction);
                address += (ushort)instruction.Length;
                
                // 防止无限循环
                if (result.Count > 1000)
                    break;
            }
            
            return result;
        }
    }

    /// <summary>
    /// 反汇编指令结果
    /// </summary>
    public class DisassembledInstruction
    {
        /// <summary>
        /// 指令地址
        /// </summary>
        public ushort Address { get; set; }
        
        /// <summary>
        /// 指令字节
        /// </summary>
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        
        /// <summary>
        /// 反汇编后的指令文本
        /// </summary>
        public string Instruction { get; set; } = string.Empty;
        
        /// <summary>
        /// 指令长度（字节数）
        /// </summary>
        public int Length { get; set; }
        
        /// <summary>
        /// 获取指令的十六进制表示
        /// </summary>
        public string BytesHex
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in Bytes)
                {
                    sb.Append($"{b:X2} ");
                }
                return sb.ToString().TrimEnd();
            }
        }
        
        /// <summary>
        /// 获取完整的反汇编行
        /// </summary>
        public string DisassemblyLine => $"${Address:X4}: {BytesHex,-8} {Instruction}";
    }
}