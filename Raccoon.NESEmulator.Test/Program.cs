using System;

namespace Raccoon.NESEmulator.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            TestSuite testSuite = new();
            bool test1 = testSuite.TestQuick();
            bool test2 = testSuite.TestFull();
        }
    }
}
