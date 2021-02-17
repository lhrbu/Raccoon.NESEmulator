using System;

namespace Raccoon.NESEmulator
{
    public interface ICPU
    {
        byte A { get; }
        bool Carry { get; }
        ulong Cycles { get; }
        byte Data { get; }
        bool Decimal { get; }
        bool Interrupt { get; }
        bool IRQ { get; }
        bool JAM { get; }
        bool Negative { get; }
        bool NMI { get; }
        byte Opcode { get; }
        bool Overflow { get; }
        ushort PC { get; }
        bool Reset { get; }
        byte SP { get; }
        byte Status { get; set; }
        byte X { get; }
        byte Y { get; }
        bool Zero { get; }

        event EventHandler? OnExecutingOneCycle;
        void ExecuteNextOpcode();
        void Jump(ushort address);
        void SetReset();
        void SetIRQ();
        void SetNMI();
    }
}