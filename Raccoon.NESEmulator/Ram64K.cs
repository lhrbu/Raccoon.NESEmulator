using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raccoon.NESEmulator
{
    public class Ram64K:IMapper
    {
        private readonly Memory<byte> _content = MemoryPool<byte>.Shared.Rent(0x10000).Memory;
        public Ram64K()
        {
            _content.Span.Fill(0xFF);
        }

        public void Load(Stream stream, ushort address = 0, int size = 0)
        {
            if (size <= 0)
                size = (int)(stream.Length - stream.Position);
            size = Math.Min(size, 65536 - address);
            stream.Read(_content.Span.Slice(address, size));
        }

        public byte Read(ushort address) => _content.Span[address];

        public ushort Read16(ushort address)
        {
            byte a = Read(address);
            byte b = Read(++address);
            return (ushort)((b << 8) | a);
        }

        public void Write(ushort address, byte value) => _content.Span[address] = value;

        public void Write16(ushort address, ushort value)
        {
            Write(address, (byte)(value & 0xFF));
            Write(++address, (byte)(value >> 8));
        }
    }
}
