using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raccoon.NESEmulator
{
    public interface IMapper
    {
        byte Read(ushort address);
        ushort Read16(ushort address);
        void Write(ushort address, byte value);
        void Write16(ushort address, ushort value);
        void Load(Stream stream, ushort address = 0x0000, int size = 0);
    }
}
