namespace GdsII
{
    ///
    ///The parts of a PNG file every writer here shares: the signature that opens it, the chunks it is made of and the
    ///check each one carries.
    ///
    ///**Two writers, one file format.** <see cref="MaskBitmaps"/> writes a layer as one bit a pixel, and
    ///<see cref="PngWriter"/> a layout in color, and the framing around their pixels is the same bytes either way.
    ///Kept once, so the two cannot write it differently.
    ///
    internal static class PngFile
    {
        ///<summary>The eight bytes every PNG starts with.</summary>
        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        ///<summary>One chunk: its length, its four-letter type, its data and the CRC over the type and the data.</summary>
        public static void Chunk(Stream file, string type, byte[] data)
        {
            var length = new byte[4];

            BigEndian(length, 0, (uint)data.Length);
            file.Write(length);

            var typed = new byte[4 + data.Length];

            for (int i = 0; i < 4; i++)
                typed[i] = (byte)type[i];

            Array.Copy(data, 0, typed, 4, data.Length);

            file.Write(typed);

            var check = new byte[4];

            BigEndian(check, 0, Crc(typed));
            file.Write(check);
        }

        public static void BigEndian(byte[] into, int at, uint value)
        {
            into[at] = (byte)(value >> 24);
            into[at + 1] = (byte)(value >> 16);
            into[at + 2] = (byte)(value >> 8);
            into[at + 3] = (byte)value;
        }

        ///<summary>The CRC-32 a PNG chunk carries over its type and data - the zlib one, by its table.</summary>
        public static uint Crc(byte[] bytes)
        {
            uint value = 0xFFFFFFFF;

            foreach (byte each in bytes)
                value = crcTable[(value ^ each) & 0xFF] ^ (value >> 8);

            return value ^ 0xFFFFFFFF;
        }

        private static readonly uint[] crcTable = makeTable();

        private static uint[] makeTable()
        {
            var table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint value = n;

                for (int k = 0; k < 8; k++)
                {
                    if ((value & 1) != 0)
                        value = 0xEDB88320 ^ (value >> 1);
                    else
                        value >>= 1;
                }

                table[n] = value;
            }

            return table;
        }
    }
}
