using System.Runtime.CompilerServices;

using static StringSquash.BitPrimitives;

namespace StringSquash
{
    ref struct BitReader
    {
        private ReadOnlySpan<byte> array;
        private int bitsOffset;
        public int BitsLeft => (array.Length * 8) - bitsOffset;

        public BitReader(ReadOnlySpan<byte> array, int bitsOffset)
        {
            this.array = array;
            this.bitsOffset = bitsOffset;
        }

        public uint GetUInt(int bits = 32)
        {
            var retval = ReadUInt32(array, bitsOffset, bits);
            bitsOffset += bits;
            return retval;
        }
        
        public byte GetByte()
        {
            if (!ValidateArgs(array.Length * 8, bitsOffset, 8, 8))
                ThrowArgumentOutOfRangeException();
            uint value = Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in array[bitsOffset >> 3]));
            var retval = (byte)ReadValue32(value, bitsOffset & 7, 8);
            bitsOffset += 8;
            return retval;
        }

        public bool GetBool()
        {
            return ReadBit(array, bitsOffset++);
        }
    }
}
