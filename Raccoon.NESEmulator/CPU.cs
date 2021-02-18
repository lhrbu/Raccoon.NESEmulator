using Raccoon.Devkits.InterceptProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raccoon.NESEmulator
{
    [Interceptor(typeof(CPUExecuteLogInterceptor))]
    public class CPU : ICPU
    {
        private readonly IMapper _ram;
        // public RAM64K RAM { get { return _ram; } }

        //readonly byte[] _bcdToDec;
        //readonly byte[] _decToBcd;

        public byte Opcode { get; private set; }
        public byte Data { get; private set; }
        ushort Address;

        // Registers
        public byte A { get; private set; }
        public byte X { get; private set; }
        public byte Y { get; private set; }
        public byte SP { get; private set; }
        public ushort PC { get; private set; }

        // Flags
        public bool Carry { get; private set; }    //0x1
        public bool Zero { get; private set; }     //0x2
        public bool Interrupt { get; private set; }//0x4
        public bool Decimal { get; private set; }  //0x8
        //bool _break;  //0x10 only exists on stack
        //bool _unused; //0x20
        public bool Overflow { get; private set; } //0x40
        public bool Negative { get; private set; } //0x80

        public byte Status
        {
            get
            {
                return (byte)
                    ((Carry ? 0x1 : 0) |
                    (Zero ? 0x2 : 0) |
                    (Interrupt ? 0x4 : 0) |
                    (Decimal ? 0x8 : 0) |
                    0x10 | //(_break ? 0x10 : 0) |
                    0x20 |
                    (Overflow ? 0x40 : 0) |
                    (Negative ? 0x80 : 0));
            }
            set
            {
                Carry = (value & 0x1) != 0;
                Zero = (value & 0x2) != 0;
                Interrupt = (value & 0x4) != 0;
                Decimal = (value & 0x8) != 0;
                //_break = (value & 0x10) != 0;
                Overflow = (value & 0x40) != 0;
                Negative = (value & 0x80) != 0;
            }
        }



        public bool NMI { get; private set; }
        public bool IRQ { get; private set; }
        public bool Reset { get; private set; }

        public bool JAM { get; private set; }

        public ulong Cycles { get; private set; }

        public event EventHandler? OnExecutingOneCycle;

        public CPU(IMapper ram)
        {
            /*
            // Generate BCD conversion tables
            _bcdToDec = new byte[0x100];
            _decToBcd = new byte[0x100];
            for (int i = 0; i <= 0xFF; i++)
            {
                _bcdToDec[i] = (byte)(i / 16 * 10 + i % 16);
                _decToBcd[i] = (byte)(i / 10 * 16 + i % 10);
            }
            */
            _ram = ram;
            SetReset();
        }



        void CountCycle(uint cycles = 1)
        {
            while (cycles-- > 0)
            {
                ++Cycles;
                OnExecutingOneCycle?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Jumps to the specified address.
        /// </summary>
        /// <param name="address">The address to jump to.</param>
        /// 
        public void Jump(ushort address)
        {
            PC = _ram.Read16(address);
        }


        #region Helper Functions

        ushort Combine(byte a, byte b)
        {
            ushort value = b;
            value <<= 8;
            value |= a;
            return value;
        }

        void Push(byte data)
        {
            _ram.Write((ushort)(SP | 0x0100), data);
            SP--;
        }
        void Push16(ushort data)
        {
            Push((byte)(data >> 8));
            Push((byte)(data & 0xFF));
        }

        byte Pop()
        {
            SP++;
            return _ram.Read((ushort)(SP | 0x0100));
        }
        ushort Pop16()
        {
            byte a = Pop();
            byte b = Pop();
            return Combine(a, b);
        }

        void CheckPageBoundaries(ushort a, ushort b)
        {
            if ((a & 0xFF00) != (b & 0xFF00))
                CountCycle();
        }

        // Addressing modes
        /*
        ushort ZeroPage(byte argA)
        {
            return argA;
        }
        */
        ushort ZeroPageX(byte address)
        {
            return (ushort)((address + X) & 0xFF);
        }
        ushort ZeroPageY(byte address)
        {
            return (ushort)((address + Y) & 0xFF);
        }
        /*
        ushort Absolute(byte argA, byte argB)
        {
            return Combine(argA, argB);
        }
        */
        ushort AbsoluteX(ushort address, bool checkPage = false)
        {
            //ushort address = Combine(addrA, addrB);
            ushort trAddress = (ushort)(address + X);
            if (checkPage)
                CheckPageBoundaries(address, trAddress);
            return trAddress;
        }
        ushort AbsoluteY(ushort address, bool checkPage = false)
        {
            //ushort address = Combine(addrA, addrB);
            ushort trAddress = (ushort)(address + Y);
            if (checkPage)
                CheckPageBoundaries(address, trAddress);
            return trAddress;
        }
        ushort IndirectX(byte address)
        {
            return _ram.Read16((ushort)((address + X) & 0xFF));
        }
        ushort IndirectY(byte address, bool checkPage = false)
        {
            ushort value = _ram.Read16(address);
            ushort translatedAddress = (ushort)(value + Y);
            if (checkPage)
                CheckPageBoundaries(value, translatedAddress);
            return translatedAddress;
        }

        #endregion


        #region Opcode Implementations

        void SetZN(byte value)
        {
            Zero = value == 0;
            Negative = (value & 0x80) != 0;
        }

        void ADC(byte value)
        {
            if (!Decimal)
            {
                int result = A + value + (Carry ? 1 : 0);
                Overflow = ((A ^ result) & (value ^ result) & 0x80) != 0;
                Carry = result > 0xFF;
                A = (byte)result;
                SetZN(A);
            }
            else
            {
                // Low nybble
                int low = (A & 0xF) + (value & 0xF) + (Carry ? 0x1 : 0);
                bool halfCarry = (low > 0x9);

                // High nybble
                int high = (A & 0xF0) + (value & 0xF0) + (halfCarry ? 0x10 : 0);
                Carry = (high > 0x9F);

                // Set flags on the binary result
                byte binary = (byte)((low & 0xF) + (high & 0xF0));
                SetZN(binary);
                Overflow = ((A ^ binary) & (value ^ binary) & 0x80) != 0;
                //_overflow = ((_a ^ value) & 0x80) == 0 && binary > 127 && binary < 0x180;

                // Decimal adjust
                if (halfCarry)
                    low += 0x6;
                if (Carry)
                    high += 0x60;

                A = (byte)((low & 0xF) + (high & 0xF0));
            }
        }
        void ADC(ushort address)
        {
            ADC(_ram.Read(address));
        }

        void AND(byte value)
        {
            A = (byte)(A & value);
            SetZN(A);
        }
        void AND(ushort address)
        {
            AND(_ram.Read(address));
        }

        byte ASL(byte value)
        {
            Carry = (value & 0x80) != 0;
            value <<= 1;
            SetZN(value);
            return value;
        }
        void ASL(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, ASL(value));
        }

        void Bxx(byte value)
        {
            ushort address = (ushort)(PC + (sbyte)value);
            CheckPageBoundaries(PC, address);
            PC = address;
            CountCycle();
        }

        void BIT(ushort address)
        {
            byte value = _ram.Read(address);
            Overflow = (value & 0x40) != 0;
            Negative = (value & 0x80) != 0;
            value &= A;
            Zero = value == 0;
        }

        void Cxx(byte value, byte register)
        {
            Carry = (register >= value);
            value = (byte)(register - value);
            SetZN(value);
        }
        void Cxx(ushort address, byte register)
        {
            Cxx(_ram.Read(address), register);
        }

        byte Dxx(byte value)
        {
            value--;
            SetZN(value);
            return value;
        }
        void DEC(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, Dxx(value));
        }

        void EOR(byte value)
        {
            A ^= value;
            SetZN(A);
        }
        void EOR(ushort address)
        {
            byte value = _ram.Read(address);
            EOR(value);
        }

        byte Ixx(byte value)
        {
            value++;
            SetZN(value);
            return value;
        }
        void INC(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, Ixx(value));
        }

        void LDA(byte value)
        {
            A = value;
            SetZN(value);
        }
        void LDA(ushort address)
        {
            LDA(_ram.Read(address));
        }
        void LDX(byte value)
        {
            X = value;
            SetZN(value);
        }
        void LDX(ushort address)
        {
            LDX(_ram.Read(address));
        }
        void LDY(byte value)
        {
            Y = value;
            SetZN(value);
        }
        void LDY(ushort address)
        {
            LDY(_ram.Read(address));
        }

        byte LSR(byte value)
        {
            Carry = (value & 0x1) != 0;
            value >>= 1;
            SetZN(value);
            return value;
        }
        void LSR(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, LSR(value));
        }

        void ORA(byte value)
        {
            A |= value;
            SetZN(A);
        }
        void ORA(ushort address)
        {
            ORA(_ram.Read(address));
        }

        byte ROL(byte value)
        {
            bool oldCarry = Carry;
            Carry = (value & 0x80) != 0;
            value <<= 1;
            if (oldCarry) value |= 0x1;
            SetZN(value);
            return value;
        }
        void ROL(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, ROL(value));
        }

        byte ROR(byte value)
        {
            bool oldCarry = Carry;
            Carry = (value & 0x1) != 0;
            value >>= 1;
            if (oldCarry) value |= 0x80;
            SetZN(value);
            return value;
        }
        void ROR(ushort address)
        {
            byte value = _ram.Read(address);
            _ram.Write(address, ROR(value));
        }

        void SBC(byte value)
        {
            if (!Decimal)
            {
                int result = 0xFF + A - value + (Carry ? 1 : 0);
                Overflow = ((A ^ result) & (~value ^ result) & 0x80) != 0;
                Carry = result > 0xFF;
                A = (byte)result;
                SetZN(A);
            }
            else
            {
                // Low nybble
                int low = 0xF + (A & 0xF) - (value & 0xF) + (Carry ? 0x1 : 0);
                bool halfCarry = (low > 0xF);

                // High nybble
                int high = 0xF0 + (A & 0xF0) - (value & 0xF0) + (halfCarry ? 0x10 : 0);
                Carry = (high > 0xFF);

                // Set flags on the binary result
                byte binary = (byte)((low & 0xF) + (high & 0xF0));
                SetZN(binary);
                Overflow = ((A ^ binary) & (~value ^ binary) & 0x80) != 0;
                //_overflow = ((_a ^ value) & 0x80) != 0 && result >= 0x80 && result < 0x180;

                // Decimal adjust
                if (!halfCarry)
                    low -= 0x6;
                if (!Carry)
                    high -= 0x60;

                A = (byte)((low & 0xF) + (high & 0xF0));
            }
        }
        void SBC(ushort address)
        {
            SBC(_ram.Read(address));
        }

        #endregion


        public void SetNMI()
        {
            NMI = true;
        }

        public void SetIRQ()
        {
            IRQ = true;
        }

        /// <summary>
        /// Resets the CPU.
        /// </summary>
        public void SetReset()
        {
            Reset = true;
        }
        /// <summary>
        /// Execute the next opcode.
        /// </summary>
        public void ExecuteNextOpcode()
        {
            if (Reset)
            {
                // Writes are ignored at reset, so don't push anything but do modify the stack pointer
                SP -= 3;
                Interrupt = true;
                PC = _ram.Read16(0xFFFC);
                Cycles = 0;
                CountCycle(7);
                Reset = false;
                JAM = false;
                NMI = false;
                IRQ = false;
                return;
            }

            if (JAM)
            {
                NMI = false;
                IRQ = false;
                return;
            }

            if (NMI)
            {
                Push16(PC);
                Push((byte)(Status & 0xEF)); // Mask off break flag
                Interrupt = true;
                PC = _ram.Read16(0xFFFA);
                CountCycle(7);
                NMI = false;
                IRQ = false;
                return;
            }
            else if (IRQ && !Interrupt)
            {
                Push16(PC);
                Push((byte)(Status & 0xEF)); // Mask off break flag
                Interrupt = true;
                PC = _ram.Read16(0xFFFE);
                CountCycle(7);
                IRQ = false;
                return;
            }

            Opcode = _ram.Read(PC);
            Data = _ram.Read((ushort)(PC + 1));
            Address = Combine(Data, _ram.Read((ushort)(PC + 2)));
            //Console.WriteLine($"{_opcode} : {_data}");
            //log.WriteLine($"{lineCount++}-> {_opcode} : {_data}");
            switch (Opcode)
            {
                // ADC
                case (0x69): // Immediate
                    PC += 2;
                    ADC(Data);
                    CountCycle(2);
                    break;
                case (0x65): // Zero Page
                    PC += 2;
                    ADC((ushort)Data);
                    CountCycle(3);
                    break;
                case (0x75): // Zero Page,X
                    PC += 2;
                    ADC(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0x6D): // Absolute
                    PC += 3;
                    ADC(Address);
                    CountCycle(4);
                    break;
                case (0x7D): // Absolute,X
                    PC += 3;
                    ADC(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0x79): // Absolute,Y
                    PC += 3;
                    ADC(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0x61): // Indirect,X
                    PC += 2;
                    ADC(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0x71): // Indirect,Y
                    PC += 2;
                    ADC(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                // AND
                case (0x29): // Immediate
                    PC += 2;
                    AND(Data);
                    CountCycle(2);
                    break;
                case (0x25): // Zero Page
                    PC += 2;
                    AND((ushort)Data);
                    CountCycle(2);
                    break;
                case (0x35): // Zero Page X
                    PC += 2;
                    AND(ZeroPageX(Data));
                    CountCycle(3);
                    break;
                case (0x2D): // Absolute
                    PC += 3;
                    AND(Address);
                    CountCycle(4);
                    break;
                case (0x3D): // Absolute,X
                    PC += 3;
                    AND(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0x39): // Absolute,Y
                    PC += 3;
                    AND(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0x21): // Indirect,X
                    PC += 2;
                    AND(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0x31): // Indirect,Y
                    PC += 2;
                    AND(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                case (0x0A): // Accumulator
                    PC += 1;
                    A = ASL(A);
                    CountCycle(2);
                    break;
                case (0x06): // Zero Page
                    PC += 2;
                    ASL((ushort)Data);
                    CountCycle(5);
                    break;
                case (0x16): // Zero Page,X
                    PC += 2;
                    ASL(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0x0E): // Absolute
                    PC += 3;
                    ASL(Address);
                    CountCycle(6);
                    break;
                case (0x1E): // Absolute,X
                    PC += 3;
                    ASL(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // BCC
                case (0x90): // Relative
                    PC += 2;
                    if (!Carry)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BCS
                case (0xB0): // Relative
                    PC += 2;
                    if (Carry)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BEQ
                case (0xF0): // Relative
                    PC += 2;
                    if (Zero)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BIT
                case (0x24): // Zero Page
                    PC += 2;
                    BIT((ushort)Data);
                    CountCycle(3);
                    break;
                case (0x2C): // Absolute
                    PC += 3;
                    BIT(Address);
                    CountCycle(4);
                    break;

                // BMI
                case (0x30): // Relative
                    PC += 2;
                    if (Negative)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BNE
                case (0xD0): // Relative
                    PC += 2;
                    if (!Zero)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BPL
                case (0x10): // Relative
                    PC += 2;
                    if (!Negative)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BRK
                case (0x00): // Implied
                    PC += 2;
#if BUG
                    if (_irq || _nmi || _reset) break; // Emulate the interrupt bug
#endif
                    Push16(PC);
                    Push(Status);
                    Interrupt = true;
                    PC = _ram.Read16(0xFFFE);
                    CountCycle(7);
                    break;

                // BVC
                case (0x50): // Relative
                    PC += 2;
                    if (!Overflow)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // BVS
                case (0x70): // Relative
                    PC += 2;
                    if (Overflow)
                        Bxx(Data);
                    CountCycle(2);
                    break;

                // CLC
                case (0x18): // Implied
                    PC += 1;
                    Carry = false;
                    CountCycle(2);
                    break;

                // CLD
                case (0xD8): // Implied
                    PC += 1;
                    Decimal = false;
                    CountCycle(2);
                    break;

                // CLI
                case (0x58): // Implied
                    PC += 1;
                    Interrupt = false;
                    CountCycle(2);
                    break;

                // CLV
                case (0xB8): // Implied
                    PC += 1;
                    Overflow = false;
                    CountCycle(2);
                    break;

                // CMP
                case (0xC9): // Immediate
                    PC += 2;
                    Cxx(Data, A);
                    CountCycle(2);
                    break;
                case (0xC5): // Zero Page
                    PC += 2;
                    Cxx((ushort)Data, A);
                    CountCycle(3);
                    break;
                case (0xD5): // Zero Page,X
                    PC += 2;
                    Cxx(ZeroPageX(Data), A);
                    CountCycle(4);
                    break;
                case (0xCD): // Absolute
                    PC += 3;
                    Cxx(Address, A);
                    CountCycle(4);
                    break;
                case (0xDD): // Absolute,X
                    PC += 3;
                    Cxx(AbsoluteX(Address, true), A);
                    CountCycle(4);
                    break;
                case (0xD9): // Absolute,Y
                    PC += 3;
                    Cxx(AbsoluteY(Address, true), A);
                    CountCycle(4);
                    break;
                case (0xC1): // Indirect,X
                    PC += 2;
                    Cxx(IndirectX(Data), A);
                    CountCycle(6);
                    break;
                case (0xD1): // Indirect,Y
                    PC += 2;
                    Cxx(IndirectY(Data, true), A);
                    CountCycle(5);
                    break;

                // CPX
                case (0xE0): // Immediate
                    PC += 2;
                    Cxx(Data, X);
                    CountCycle(2);
                    break;
                case (0xE4): // Zero Page
                    PC += 2;
                    Cxx((ushort)Data, X);
                    CountCycle(3);
                    break;
                case (0xEC): // Absolute
                    PC += 3;
                    Cxx(Address, X);
                    CountCycle(4);
                    break;

                // CPY
                case (0xC0): // Immediate
                    PC += 2;
                    Cxx(Data, Y);
                    CountCycle(2);
                    break;
                case (0xC4): // Zero Page
                    PC += 2;
                    Cxx((ushort)Data, Y);
                    CountCycle(3);
                    break;
                case (0xCC): // Absolute
                    PC += 3;
                    Cxx(Address, Y);
                    CountCycle(4);
                    break;

                // DEC
                case (0xC6): // Zero Page
                    PC += 2;
                    DEC((ushort)Data);
                    CountCycle(5);
                    break;
                case (0xD6): // Zero Page,X
                    PC += 2;
                    DEC(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0xCE): // Absolute
                    PC += 3;
                    DEC(Address);
                    CountCycle(6);
                    break;
                case (0xDE): // Absolute,X
                    PC += 3;
                    DEC(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // DEX
                case (0xCA): // Implied
                    PC += 1;
                    X = Dxx(X);
                    CountCycle(2);
                    break;

                // DEY
                case (0x88): // Implied
                    PC += 1;
                    Y = Dxx(Y);
                    CountCycle(2);
                    break;

                // EOR
                case (0x49): // Immediate
                    PC += 2;
                    EOR(Data);
                    CountCycle(2);
                    break;
                case (0x45): // Zero Page
                    PC += 2;
                    EOR((ushort)Data);
                    CountCycle(3);
                    break;
                case (0x55): // Zero Page,X
                    PC += 2;
                    EOR(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0x4D): // Absolute
                    PC += 3;
                    EOR(Address);
                    CountCycle(4);
                    break;
                case (0x5D): // Absolute,X
                    PC += 3;
                    EOR(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0x59): // Absolute,Y
                    PC += 3;
                    EOR(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0x41): // Indirect,X
                    PC += 2;
                    EOR(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0x51): // Indirect,Y
                    PC += 2;
                    EOR(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                // INC
                case (0xE6): // Zero Page
                    PC += 2;
                    INC((ushort)Data);
                    CountCycle(5);
                    break;
                case (0xF6): // Zero Page,X
                    PC += 2;
                    INC(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0xEE): // Absolute
                    PC += 3;
                    INC(Address);
                    CountCycle(6);
                    break;
                case (0xFE): // Absolute,X
                    PC += 3;
                    INC(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // INX
                case (0xE8): // Implied
                    PC += 1;
                    X = Ixx(X);
                    CountCycle(2);
                    break;

                // INY
                case (0xC8): // Implied
                    PC += 1;
                    Y = Ixx(Y);
                    CountCycle(2);
                    break;

                // JMP
                case (0x4C): // Absolute
                    PC = Address;
                    CountCycle(3);
                    break;
                case (0x6C): // Indirect
                    ushort address = Address;
#if BUG
                    if ((address & 0x00FF) == 0x00FF) // Emulate the indirect jump bug
                        _pc = (ushort)((_ram.Read((ushort)(address & 0xFF00)) << 8) | _ram.Read(address));
                    else
#endif
                    PC = _ram.Read16(address);
                    CountCycle(5);
                    break;

                // JSR
                case (0x20): // Absolute
                    Push16((ushort)(PC + 2)); // + 3 - 1
                    PC = Address;
                    CountCycle(6);
                    break;

                // LDA
                case (0xA9): // Immediate
                    PC += 2;
                    LDA(Data);
                    CountCycle(2);
                    break;
                case (0xA5): // Zero Page
                    PC += 2;
                    LDA((ushort)Data);
                    CountCycle(3);
                    break;
                case (0xB5): // Zero Page,X
                    PC += 2;
                    LDA(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0xAD): // Absolute
                    PC += 3;
                    LDA(Address);
                    CountCycle(4);
                    break;
                case (0xBD): // Absolute,X
                    PC += 3;
                    LDA(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0xB9): // Absolute,Y
                    PC += 3;
                    LDA(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0xA1): // Indirect,X
                    PC += 2;
                    LDA(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0xB1): // Indirect,Y
                    PC += 2;
                    LDA(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                // LDX
                case (0xA2): // Immediate
                    PC += 2;
                    LDX(Data);
                    CountCycle(2);
                    break;
                case (0xA6): // Zero Page
                    PC += 2;
                    LDX((ushort)Data);
                    CountCycle(3);
                    break;
                case (0xB6): // Zero Page,Y
                    PC += 2;
                    LDX(ZeroPageY(Data));
                    CountCycle(4);
                    break;
                case (0xAE): // Absolute
                    PC += 3;
                    LDX(Address);
                    CountCycle(4);
                    break;
                case (0xBE): // Absolute,Y
                    PC += 3;
                    LDX(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;

                // LDY
                case (0xA0): // Immediate
                    PC += 2;
                    LDY(Data);
                    CountCycle(2);
                    break;
                case (0xA4): // Zero Page
                    PC += 2;
                    LDY((ushort)Data);
                    CountCycle(3);
                    break;
                case (0xB4): // Zero Page,X
                    PC += 2;
                    LDY(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0xAC): // Absolute
                    PC += 3;
                    LDY(Address);
                    CountCycle(4);
                    break;
                case (0xBC): // Absolute,X
                    PC += 3;
                    LDY(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;

                // LSR
                case (0x4A): // Accumulator
                    PC += 1;
                    A = LSR(A);
                    CountCycle(2);
                    break;
                case (0x46): // Zero Page
                    PC += 2;
                    LSR((ushort)Data);
                    CountCycle(5);
                    break;
                case (0x56): // Zero Page,X
                    PC += 2;
                    LSR(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0x4E): // Absolute
                    PC += 3;
                    LSR(Address);
                    CountCycle(6);
                    break;
                case (0x5E): // Absolute,X
                    PC += 3;
                    LSR(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // NOP
                case (0xEA): // Implied
                    PC += 1;
                    CountCycle(2);
                    break;

                // ORA
                case (0x09): // Immediate
                    PC += 2;
                    ORA(Data);
                    CountCycle(2);
                    break;
                case (0x05): // Zero Page
                    PC += 2;
                    ORA((ushort)Data);
                    CountCycle(3);
                    break;
                case (0x15): // Zero Page,X
                    PC += 2;
                    ORA(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0x0D): // Absolute
                    PC += 3;
                    ORA(Address);
                    CountCycle(4);
                    break;
                case (0x1D): // Absolute,X
                    PC += 3;
                    ORA(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0x19): // Absolute,Y
                    PC += 3;
                    ORA(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0x01): // Indirect,X
                    PC += 2;
                    ORA(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0x11): // Indirect,Y
                    PC += 2;
                    ORA(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                // PHA
                case (0x48): // Implied
                    PC += 1;
                    Push(A);
                    CountCycle(3);
                    break;

                // PHP
                case (0x08): // Implied
                    PC += 1;
                    Push(Status);
                    CountCycle(3);
                    break;

                // PLA
                case (0x68): // Implied
                    PC += 1;
                    A = Pop();
                    SetZN(A);
                    CountCycle(4);
                    break;

                // PLP
                case (0x28): // Implied
                    PC += 1;
                    Status = Pop();
                    CountCycle(4);
                    break;

                // ROL
                case (0x2A): // Accumulator
                    PC += 1;
                    A = ROL(A);
                    CountCycle(2);
                    break;
                case (0x26): // Zero Page
                    PC += 2;
                    ROL((ushort)Data);
                    CountCycle(5);
                    break;
                case (0x36): // Zero Page,X
                    PC += 2;
                    ROL(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0x2E): // Absolute
                    PC += 3;
                    ROL(Address);
                    CountCycle(6);
                    break;
                case (0x3E): // Absolute,X
                    PC += 3;
                    ROL(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // ROR
                case (0x6A): // Accumulator
                    PC += 1;
                    A = ROR(A);
                    CountCycle(2);
                    break;
                case (0x66): // Zero Page
                    PC += 2;
                    ROR((ushort)Data);
                    CountCycle(5);
                    break;
                case (0x76): // Zero Page,X
                    PC += 2;
                    ROR(ZeroPageX(Data));
                    CountCycle(6);
                    break;
                case (0x6E): // Absolute
                    PC += 3;
                    ROR(Address);
                    CountCycle(6);
                    break;
                case (0x7E): // Absolute,X
                    PC += 3;
                    ROR(AbsoluteX(Address));
                    CountCycle(7);
                    break;

                // RTI
                case (0x40): // Implied
                    Status = Pop();
                    PC = Pop16();
                    CountCycle(6);
                    break;

                // RTS
                case (0x60): // Implied
                    PC = Pop16();
                    PC += 1;
                    CountCycle(6);
                    break;

                // SBC
                case (0xE9): // Immediate
                    PC += 2;
                    SBC(Data);
                    CountCycle(2);
                    break;
                case (0xE5): // Zero Page
                    PC += 2;
                    SBC((ushort)Data);
                    CountCycle(3);
                    break;
                case (0xF5): // Zero Page,X
                    PC += 2;
                    SBC(ZeroPageX(Data));
                    CountCycle(4);
                    break;
                case (0xED): // Absolute
                    PC += 3;
                    SBC(Address);
                    CountCycle(4);
                    break;
                case (0xFD): // Absolute,X
                    PC += 3;
                    SBC(AbsoluteX(Address, true));
                    CountCycle(4);
                    break;
                case (0xF9): // Absolute,Y
                    PC += 3;
                    SBC(AbsoluteY(Address, true));
                    CountCycle(4);
                    break;
                case (0xE1): // Indirect,X
                    PC += 2;
                    SBC(IndirectX(Data));
                    CountCycle(6);
                    break;
                case (0xF1): // Indirect,Y
                    PC += 2;
                    SBC(IndirectY(Data, true));
                    CountCycle(5);
                    break;

                // SEC
                case (0x38): // Implied
                    PC += 1;
                    Carry = true;
                    CountCycle(2);
                    break;

                // SED
                case (0xF8): // Implied
                    PC += 1;
                    Decimal = true;
                    CountCycle(2);
                    break;

                // SEI
                case (0x78): // Implied
                    PC += 1;
                    Interrupt = true;
                    CountCycle(2);
                    break;

                // STA
                case (0x85): // Zero Page
                    PC += 2;
                    _ram.Write((ushort)Data, A);
                    CountCycle(3);
                    break;
                case (0x95): // Zero Page,X
                    PC += 2;
                    _ram.Write(ZeroPageX(Data), A);
                    CountCycle(4);
                    break;
                case (0x8D): // Absolute
                    PC += 3;
                    _ram.Write(Address, A);
                    CountCycle(4);
                    break;
                case (0x9D): // Absolute,X
                    PC += 3;
                    _ram.Write(AbsoluteX(Address), A);
                    CountCycle(5);
                    break;
                case (0x99): // Absolute,Y
                    PC += 3;
                    _ram.Write(AbsoluteY(Address), A);
                    CountCycle(5);
                    break;
                case (0x81): // Indirect,X
                    PC += 2;
                    _ram.Write(IndirectX(Data), A);
                    CountCycle(6);
                    break;
                case (0x91): // Indirect,Y
                    PC += 2;
                    _ram.Write(IndirectY(Data), A);
                    CountCycle(6);
                    break;

                // STX
                case (0x86): // Zero Page
                    PC += 2;
                    _ram.Write((ushort)Data, X);
                    CountCycle(3);
                    break;
                case (0x96): // Zero Page,Y
                    PC += 2;
                    _ram.Write(ZeroPageY(Data), X);
                    CountCycle(4);
                    break;
                case (0x8E): // Absolute
                    PC += 3;
                    _ram.Write(Address, X);
                    CountCycle(4);
                    break;

                // STY
                case (0x84): // Zero Page
                    PC += 2;
                    _ram.Write((ushort)Data, Y);
                    CountCycle(3);
                    break;
                case (0x94): // Zero Page,X
                    PC += 2;
                    _ram.Write(ZeroPageX(Data), Y);
                    CountCycle(4);
                    break;
                case (0x8C): // Absolute
                    PC += 3;
                    _ram.Write(Address, Y);
                    CountCycle(4);
                    break;

                // TAX
                case (0xAA): // Implied
                    PC += 1;
                    X = A;
                    SetZN(X);
                    CountCycle(2);
                    break;

                // TAY
                case (0xA8): // Implied
                    PC += 1;
                    Y = A;
                    SetZN(Y);
                    CountCycle(2);
                    break;

                // TSX
                case (0xBA): // Implied
                    PC += 1;
                    X = SP;
                    SetZN(X);
                    CountCycle(2);
                    break;

                // TXA
                case (0x8A): // Implied
                    PC += 1;
                    A = X;
                    SetZN(A);
                    CountCycle(2);
                    break;

                // TXS
                case (0x9A): // Implied
                    PC += 1;
                    SP = X;
                    CountCycle(2);
                    break;

                // TYA
                case (0x98): // Implied
                    PC += 1;
                    A = Y;
                    SetZN(A);
                    CountCycle(2);
                    break;

                /////////////////////
                // Illegal opcodes //
                /////////////////////

                // ANC
                case (0x0B): // Immediate
                case (0x2B):
                    PC += 2;
                    AND(Data);
                    Carry = Negative;
                    CountCycle(2);
                    break;

                // KIL
                case (0x02):
                case (0x12):
                case (0x22):
                case (0x32):
                case (0x42):
                case (0x52):
                case (0x62):
                case (0x72):
                case (0x92):
                case (0xB2):
                case (0xD2):
                case (0xF2):
                    JAM = true;
                    break;

                //TODO: Count extra NOP cycles and double/triple bytes
                /*
                // NOP
                case (0x80):
                case (0x82):
                case (0xC2):
                case (0xE2):
                case (0x04):
                case (0x44):
                case (0x64):
                case (0x89):
                case (0x0C):
                case (0x14):
                case (0x34):
                case (0x54):
                case (0x74):
                case (0xD4):
                case (0xF4):
                case (0x1A):
                case (0x3A):
                case (0x5A):
                case (0x7A):
                case (0xDA):
                case (0xFA):
                case (0x1C):
                case (0x3C):
                case (0x5C):
                case (0x7C):
                case (0xDC):
                case (0xFC):
                    goto case (0xEA);
                    */
                default:
                    Console.WriteLine("Illegal opcode: " + Opcode.ToString("X2"));
                    goto case (0xEA); //NOP
            }

        }
    }
}
