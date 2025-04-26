namespace StringSquash
{
    class BitWriter
    {
        private byte[] buffer;
        private int bitOffset;

        public BitWriter(int initialCapacity = 64)
        {
            buffer = new byte [(initialCapacity + 7) >> 3];
            bitOffset = 0;
        }
        
        public void PutByte(byte b) => PutUInt(b, 8);

        public void PutUInt(uint u, int bits)
        {
            CheckSize(bitOffset + bits);
            PackUInt(u, bits, buffer, bitOffset);
            bitOffset += bits;
        }

        static void PackUInt(uint src, int nBits, Span<byte> dest, int destOffset)
        {
            if (nBits <= 8)
            {
                PackBits((byte) src, nBits, dest, destOffset);
                return;
            }

            PackBits((byte) src, 8, dest, destOffset);
            destOffset += 8;
            nBits -= 8;
            if (nBits <= 8)
            {
                PackBits((byte) (src >> 8), nBits, dest, destOffset);
                return;
            }

            PackBits((byte) (src >> 8), 8, dest, destOffset);
            destOffset += 8;
            nBits -= 8;
            if (nBits <= 8)
            {
                PackBits((byte) (src >> 16), nBits, dest, destOffset);
                return;
            }

            PackBits((byte) (src >> 16), 8, dest, destOffset);
            destOffset += 8;
            nBits -= 8;
            PackBits((byte) (src >> 24), nBits, dest, destOffset);
        }

        static void PackBits(byte src, int nBits, Span<byte> dest, int destOffset)
        {
            src = (byte) (src & (0xFF >> (8 - nBits)));
            int p = destOffset >> 3;
            int bitsUsed = destOffset & 0x7;
            int bitsFree = 8 - bitsUsed;
            int bitsLeft = bitsFree - nBits;
            if (bitsLeft >= 0)
            {
                int mask = (0xFF >> bitsFree) | (0xFF << (8 - bitsLeft));
                dest[p] = (byte) (
                    (dest[p] & mask) |
                    (src << bitsUsed));
                return;
            }

            dest[p] = (byte) (
                (dest[p] & (0xFF >> bitsFree)) |
                (src << bitsUsed)
            );
            p++;
            dest[p] = (byte) (
                (dest[p] & (0xFF << (nBits - bitsFree))) |
                (src >> bitsFree)
            );
        }
        
        void CheckSize(int nBits)
        {
            int byteLen = (nBits + 7) >> 3;
            if (buffer.Length < byteLen)
            {
                var growthAmount = Math.Max(byteLen, buffer.Length + (buffer.Length / 2));
                Array.Resize(ref buffer, growthAmount);
            }
        }

        public byte[] GetCopy()
        {
            var b = new byte[ByteLength];
            for (int i = 0; i < ByteLength; i++) b[i] = buffer[i];
            return b;
        }

        public int Padding()
        {
            var curr = (bitOffset & 0x7);
            return curr > 0 ? 8 - curr : 0;
        }
        
        public int ByteLength => (bitOffset + 7) >> 3;
    }
}
