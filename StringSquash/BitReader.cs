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
            if (bits <= 0 || bits > 32)
                throw new ArgumentOutOfRangeException();
            var retval = UnpackUInt(array, bits, bitsOffset);
            bitsOffset += bits;
            return retval;
        }
        
        public byte GetByte()
        {
            var b = UnpackBits(array, 8, bitsOffset);
            bitsOffset += 8;
            return b;
        }

        public bool GetBool()
        {
            return UnpackBits(array, 1, bitsOffset++) != 0;
        }

        static uint UnpackUInt(ReadOnlySpan<byte> buffer, int nBits, int readOffset)
        {
            //Byte 1
            uint retval;
            if (nBits <= 8)
            {
                return UnpackBits(buffer, nBits, readOffset);
            }

            retval = UnpackBits(buffer, 8, readOffset);
            nBits -= 8;
            readOffset += 8;
            //Byte 2
            if (nBits <= 8)
            {
                return retval | (uint) (UnpackBits(buffer, nBits, readOffset) << 8);
            }

            retval |= (uint) (UnpackBits(buffer, 8, readOffset) << 8);
            nBits -= 8;
            readOffset += 8;
            //Byte 3
            if (nBits <= 8)
            {
                return retval | (uint) (UnpackBits(buffer, nBits, readOffset) << 16);
            }

            retval |= (uint) (UnpackBits(buffer, 8, readOffset) << 16);
            nBits -= 8;
            readOffset += 8;
            //Byte 4
            return retval | (uint) (UnpackBits(buffer, nBits, readOffset) << 24);
        }

        static byte UnpackBits(ReadOnlySpan<byte> buffer, int nBits, int readOffset)
        {
            int bytePtr = readOffset >> 3;
            int startIndex = readOffset - (bytePtr * 8);
            if (startIndex == 0 && nBits == 8)
                return buffer[bytePtr];

            byte returnValue = (byte) (buffer[bytePtr] >> startIndex);
            var remainingBits = nBits - (8 - startIndex);
            if (remainingBits < 1)
            {
                //Mask out
                return (byte) (returnValue & (0xFF >> (8 - nBits)));
            }

            byte second = buffer[bytePtr + 1];
            second &= (byte) (255 >> (8 - remainingBits));
            return (byte) (returnValue | (byte) (second << (nBits - remainingBits)));
        }
    }
}
