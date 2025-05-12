using static StringSquash.BitPrimitives;

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

        public void Clear()
        {
            Array.Clear(buffer, 0, buffer.Length);
            bitOffset = 0;
        }

        public void PutBool(bool b)
        {
            CheckSize(bitOffset + 1);
            WriteBit(buffer, bitOffset, b);
            bitOffset++;
        }

        public void PutByte(byte b)
        {
            CheckSize(bitOffset + 8);
            WriteUInt8(buffer, bitOffset, b, 8);
            bitOffset += 8;
        }

        public void PutUInt(uint u, int bits)
        {
            CheckSize(bitOffset + 32); // Pad to 32 bits for writer
            WriteUInt32(buffer, bitOffset, u, bits);
            bitOffset += bits;
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
