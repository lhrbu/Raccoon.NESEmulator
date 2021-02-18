using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raccoon.NESEmulator.Test
{
    public class TestSuite
    {
        private readonly ICPU _cpu;
        private readonly IMapper _ram;
        public TestSuite(ICPU cpu,IMapper ram)
        {
            _cpu = cpu;
            _ram = ram;
        }

        public bool TestQuick()
        {
            Console.WriteLine("Running general quick tests... ");

            using (FileStream file = new FileStream("quick.bin", FileMode.Open, FileAccess.Read))
                _ram.Load(file, 0x4000);

            // Set reset vector to the start of the code
            _ram.Write16(0xFFFC, 0x4000);
            // Set IRQ vector to test BRK
            _ram.Write16(0xFFFE, 0x45A4); // IRQ
            _cpu.SetReset();

            //Display = true;

            while (_cpu.PC != 0x45CA)
            {
                _cpu.ExecuteNextOpcode();
            }
            //Display = false;


            byte result = _ram.Read(0x0210);
            //Console.SetCursorPosition(x, y);
            if (result == 0xFF)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK!");
                Console.ResetColor();
                return true;
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL: " + result.ToString("X2"));
            Console.ResetColor();
            return false;
        }

        public bool TestFull()
        {
            Console.WriteLine("Running general full tests... ");
            //StorePos();

            using (FileStream file = new FileStream("full.bin", FileMode.Open, FileAccess.Read))
                _ram.Load(file);

            // Set reset vector to the start of the code
            _ram.Write16(0xFFFC, 0x1000);
            _cpu.SetReset();

            bool trapped = false;
            //Display = true;
            while (_cpu.PC != 0x3B1C)
            {
                _cpu.ExecuteNextOpcode();
                if (_cpu.Cycles > 81000000)
                {
                    trapped = true;
                    break;
                }
            }
            //Display = false;
            if (!trapped)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OK!");
                Console.ResetColor();
                return true;
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL: " + _cpu.PC.ToString("X4"));
            Console.ResetColor();
            return false;
        }

    }
}
