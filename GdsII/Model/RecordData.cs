using System.Globalization;
using System.Text;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///One record's decoded payload.
    ///
    ///The subclasses are exactly the GDSII data types, and each one owns its decoding, its encoding and
    ///the way it prints in the text view. Those three used to sit in three separate switches - one
    ///picking a decoder, one picking an encoder, one picking a format - which is the same shape that let
    ///setData drift from the format for ten record types. Here a new data type cannot be added without
    ///implementing all three.
    ///</summary>
    public abstract class RecordData
    {
        ///<summary>The GDSII data type this payload is, which is what Record reports as its DataType.</summary>
        public abstract RecordDataType Type { get; }

        ///<summary>Encodes the payload back to the bytes a GDSII record carries.</summary>
        public abstract byte[] Encode();

        ///<summary>
        ///How many bytes Encode will produce, without producing them.
        ///
        ///Only there so that writing a whole library can size its buffer up front and fill it once,
        ///instead of growing a stream a record at a time and copying it out at the end. Every payload
        ///knows this from what it holds, so it costs nothing to ask.
        ///</summary>
        public abstract int EncodedLength { get; }

        ///<summary>Appends this payload the way the text view shows it.</summary>
        public abstract void AppendText(StringBuilder builder);

        ///<summary>
        ///One value, or several separated by spaces.
        ///
        ///Kept in one place because the text view's format is what <see cref="TextFormat"/> reads back, so the
        ///two have to agree about it down to the separator.
        ///
        ///Formatted invariantly on purpose. This dump is a data format rather than prose - the tests
        ///compare it exactly and Deserialize(string) is meant to read it back - so it must not follow the
        ///reader's locale. Blazor WebAssembly takes its culture from the browser, so on a comma-decimal
        ///one the default formatting would write UNITS as "0,001", which nothing can parse back and which
        ///would make the text view disagree with itself between two machines.
        ///
        ///The IFormattable constraint is what makes that unmissable: a numeric payload cannot be appended
        ///here without choosing a culture.
        ///</summary>
        protected static void AppendValues<T>(StringBuilder builder, T[] values) where T : IFormattable
        {
            if (values.Length == 1)
            {
                builder.Append(values[0].ToString(null, CultureInfo.InvariantCulture));

                return;
            }

            foreach (T value in values)
                builder.Append(value.ToString(null, CultureInfo.InvariantCulture)).Append(' ');
        }

        ///<summary>
        ///How many values a payload of this size holds, rejecting one that does not divide evenly.
        ///
        ///Worth checking rather than truncating, because the many records that carry exactly one value
        ///read Values[0]: a payload one byte short decodes to an empty array, and the stray byte surfaces
        ///as an IndexOutOfRangeException out of a renderer instead of a file being refused where it is
        ///read. LAYER, WIDTH and MAG all reach it that way.
        ///</summary>
        protected static int ValueCount(byte[] data, int bytesPerValue, RecordDataType type)
        {
            if (data.Length % bytesPerValue != 0)
                throw new InvalidDataException($"A {type} payload holds {bytesPerValue}-byte values, so its length must be a multiple of {bytesPerValue}. This one is {data.Length} bytes.");

            return data.Length / bytesPerValue;
        }

        protected static void AppendHex(StringBuilder builder, byte[] bytes)
        {
            builder.Append("0x");

            foreach (byte value in bytes)
                builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
    }

    ///<summary>Two-byte signed integers.</summary>
    public sealed class Int2Data : RecordData
    {
        public Int2Data(params short[] values)
        {
            Values = values;
        }

        public short[] Values { get; }

        ///<summary>The first value, for the many records that carry exactly one - LAYER, DATATYPE and so on.</summary>
        public short Value
        {
            get { return Values[0]; }
        }

        public override RecordDataType Type
        {
            get { return RecordDataType.INT2; }
        }

        public static Int2Data Decode(byte[] data)
        {
            var values = new short[ValueCount(data, 2, RecordDataType.INT2)];

            for (int i = 0; i < values.Length; i++)
            {
                int index = i * 2;

                values[i] = (short)((data[index] << 8) | data[index + 1]);
            }

            return new Int2Data(values);
        }

        public override int EncodedLength
        {
            get { return Values.Length * 2; }
        }

        public override byte[] Encode()
        {
            var bytes = new byte[Values.Length * 2];

            for (int i = 0; i < Values.Length; i++)
            {
                int index = i * 2;

                bytes[index] = (byte)(Values[i] >> 8);
                bytes[index + 1] = (byte)Values[i];
            }

            return bytes;
        }

        public override void AppendText(StringBuilder builder)
        {
            AppendValues(builder, Values);
        }
    }

    ///<summary>Four-byte signed integers. XY coordinate lists are the common case.</summary>
    public sealed class Int4Data : RecordData
    {
        public Int4Data(params int[] values)
        {
            Values = values;
        }

        public int[] Values { get; }

        public int Value
        {
            get { return Values[0]; }
        }

        public override RecordDataType Type
        {
            get { return RecordDataType.INT4; }
        }

        public static Int4Data Decode(byte[] data)
        {
            var values = new int[ValueCount(data, 4, RecordDataType.INT4)];

            for (int i = 0; i < values.Length; i++)
            {
                int index = i * 4;

                values[i] = (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
            }

            return new Int4Data(values);
        }

        public override int EncodedLength
        {
            get { return Values.Length * 4; }
        }

        public override byte[] Encode()
        {
            var bytes = new byte[Values.Length * 4];

            for (int i = 0; i < Values.Length; i++)
            {
                int index = i * 4;

                bytes[index] = (byte)(Values[i] >> 24);
                bytes[index + 1] = (byte)(Values[i] >> 16);
                bytes[index + 2] = (byte)(Values[i] >> 8);
                bytes[index + 3] = (byte)Values[i];
            }

            return bytes;
        }

        public override void AppendText(StringBuilder builder)
        {
            AppendValues(builder, Values);
        }
    }

    ///<summary>
    ///Eight-byte reals in GDSII's own format, which is not IEEE 754: a sign bit, a seven-bit excess-64
    ///exponent, and a 56-bit mantissa read as a fraction, giving fraction * 16^exponent.
    ///</summary>
    public sealed class Real8Data : RecordData
    {
        ///<summary>
        ///What the mantissa is a fraction of: the binary point sits to its left, so seven bytes read as an
        ///integer over 2^56. KLayout does the same in both directions - its reader scales by
        ///16^(exponent - 64 - 14) and its writer divides by 16^(exponent - 14), and 16^14 is 2^56.
        ///
        ///This was 2^56 - 1 until the two were compared. Nothing observable changed: over two million
        ///random mantissas the two divisors decode to the same double every time, the gap being 1.4e-17
        ///relative where a double's precision is 2.2e-16. The old comment claimed the deviation was what
        ///made a value re-encode to the bytes it came from, which is not so - encoding inverts decoding
        ///whatever they divide by, as long as they agree.
        ///</summary>
        private const double Mantissa = 72057594037927936.0;//2^56

        public Real8Data(params double[] values)
        {
            Values = values;
        }

        public double[] Values { get; }

        public double Value
        {
            get { return Values[0]; }
        }

        public override RecordDataType Type
        {
            get { return RecordDataType.REAL8; }
        }

        public static Real8Data Decode(byte[] data)
        {
            var values = new double[ValueCount(data, 8, RecordDataType.REAL8)];

            for (int i = 0; i < values.Length; i++)
                values[i] = decodeOne(data, i * 8);

            return new Real8Data(values);
        }

        public override int EncodedLength
        {
            get { return Values.Length * 8; }
        }

        public override byte[] Encode()
        {
            var bytes = new byte[Values.Length * 8];

            for (int i = 0; i < Values.Length; i++)
                encodeOne(Values[i]).CopyTo(bytes, i * 8);

            return bytes;
        }

        public override void AppendText(StringBuilder builder)
        {
            AppendValues(builder, Values);
        }

        private static double decodeOne(byte[] data, int offset)
        {
            //The top bit of the first byte is the sign; the remaining seven are the excess-64 exponent.
            bool negative = (data[offset] & 0b10000000) != 0;
            int exponent = (data[offset] & 0b01111111) - 64;

            //Bytes 1 through 7 are the mantissa, most significant first.
            ulong mantissa = 0;

            for (int i = 1; i < 8; i++)
                mantissa = (mantissa << 8) | data[offset + i];

            if (mantissa == 0)
                return 0;

            double value = ((double)mantissa / Mantissa) * Math.Pow(16, exponent);

            if (negative)
                value = -value;

            return value;
        }

        private static byte[] encodeOne(double value)
        {
            var data = new byte[8];

            //A zero mantissa reads back as zero whatever the exponent, so all-zero bytes are the
            //canonical encoding of 0 and there is nothing to normalize.
            if (value == 0)
                return data;

            bool negative = value < 0;
            double magnitude = Math.Abs(value);

            //Normalize into [1/16, 1), the fraction range the format defines, counting the powers of 16
            //taken out. Both loops only multiply or divide by 16, which is exact in binary floating
            //point, so nothing is lost here.
            int exponent = 0;
            while (magnitude >= 1)
            {
                magnitude = magnitude / 16;
                exponent++;
            }

            while (magnitude < 1.0 / 16)
            {
                magnitude = magnitude * 16;
                exponent--;
            }

            if (exponent < -64 || exponent > 63)
                throw new OverflowException($"{value} is outside the range a GDSII eight-byte real can represent.");

            ulong mantissa = (ulong)Math.Round(magnitude * Mantissa);

            data[0] = (byte)(exponent + 64);

            if (negative)
                data[0] = (byte)(data[0] | 0b10000000);

            for (int i = 1; i < 8; i++)
                data[i] = (byte)(mantissa >> (8 * (7 - i)));

            return data;
        }
    }

    ///<summary>An ASCII string, null-padded to an even length on the wire.</summary>
    public sealed class AsciiData : RecordData
    {
        public AsciiData(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public override RecordDataType Type
        {
            get { return RecordDataType.ASCII; }
        }

        public static AsciiData Decode(byte[] data)
        {
            int length = data.Length;

            //Drop the pad byte an odd-length string is padded to even with.
            if (data[length - 1] == 0)
                length--;

            return new AsciiData(Encoding.ASCII.GetString(data, 0, length));
        }

        ///<summary>Rounded up to even, the same padding Encode applies.</summary>
        public override int EncodedLength
        {
            get { return Value.Length + (Value.Length % 2); }
        }

        public override byte[] Encode()
        {
            int length = Value.Length;

            //Every GDSII record length must be even, so an odd string gets a trailing null.
            if (length % 2 != 0)
                length++;

            var bytes = new byte[length];

            Encoding.ASCII.GetBytes(Value, 0, Value.Length, bytes, 0);

            return bytes;
        }

        public override void AppendText(StringBuilder builder)
        {
            builder.Append(Value);
        }
    }

    ///<summary>A bit field, such as STRANS, PRESENTATION or ELFLAGS. See BitFields.cs to read the flags.</summary>
    public sealed class BitArrayData : RecordData
    {
        public BitArrayData(byte[] value)
        {
            Value = value;
        }

        public byte[] Value { get; }

        public override RecordDataType Type
        {
            get { return RecordDataType.BITARRAY; }
        }

        public override int EncodedLength
        {
            get { return Value.Length; }
        }

        public override byte[] Encode()
        {
            return Value;
        }

        public override void AppendText(StringBuilder builder)
        {
            AppendHex(builder, Value);
        }
    }

    ///<summary>
    ///A payload kept as raw bytes because nothing can be made of it: REAL4, which no record type
    ///declares; a NODATA record that carries data anyway; or a data-type code out of a malformed type
    ///word. Holding the bytes means such a record still writes back out exactly as it came in, rather
    ///than being silently dropped.
    ///</summary>
    public sealed class RawData : RecordData
    {
        public RawData(RecordDataType declaredType, byte[] value)
        {
            Type = declaredType;
            Value = value;
        }

        public byte[] Value { get; }

        public override RecordDataType Type { get; }

        public override int EncodedLength
        {
            get { return Value.Length; }
        }

        public override byte[] Encode()
        {
            return Value;
        }

        public override void AppendText(StringBuilder builder)
        {
            AppendHex(builder, Value);
        }
    }
}
