using System.Globalization;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///Reads back the dump <see cref="GDS.AsText"/> writes, so an edit made in the text view can become a
    ///file again.
    ///
    ///The format is one record per line, <c>TYPE: values </c> - a name, a colon and a space, the payload,
    ///and a trailing space. That trailing space is why the separators are peeled off one at a time rather
    ///than trimmed: an ASCII payload's own leading and trailing spaces are part of the value, and
    ///trimming would eat them.
    ///
    ///Numbers are parsed invariantly, matching the way they are written. That is not a detail - Blazor
    ///WebAssembly takes its culture from the browser, so on a comma-decimal one a locale-sensitive parse
    ///would read "0.001" as 1 and quietly change the file's units.
    ///
    ///Nothing here decides what a payload means. Each line's data type comes from the low byte of its
    ///record type word, the same rule the byte reader uses, and the payload is handed to the same
    ///RecordData encoders - so a record built from text is indistinguishable from one read out of a file,
    ///and a file that is dumped and read back comes out byte for byte identical.
    ///</summary>
    public static class TextFormat
    {
        private const string HexPrefix = "0x";

        ///<summary>
        ///Every non-blank line as a record. Throws <see cref="InvalidDataException"/> naming the line, on
        ///the assumption that a person typed it: a save that silently dropped what it could not read would
        ///be worse than one that refuses.
        ///</summary>
        public static List<Record> ParseRecords(string text)
        {
            var records = new List<Record>();

            //Split rather than ReadLines so the line number is the one the editor shows. Monaco writes
            //CRLF on Windows, so the carriage return has to come off before anything else is measured.
            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                //Blank lines are how a dump ends and how a person separates things while editing.
                if (line.Trim().Length == 0)
                    continue;

                records.Add(parseLine(line, i + 1));
            }

            if (records.Count == 0)
                throw new InvalidDataException("This text contains no GDSII records.");

            return records;
        }

        private static Record parseLine(string line, int lineNumber)
        {
            int colon = line.IndexOf(':');

            if (colon < 0)
                throw new InvalidDataException($"Line {lineNumber} is not a record: it has no colon separating the record type from its value. Expected \"TYPE: value\", got \"{line}\".");

            string name = line.Substring(0, colon).Trim();
            RecordType type = recordTypeOf(name, lineNumber);

            //The data type comes from the type word's low byte, exactly as it does when reading bytes -
            //LAYER is 0x0D02, type 0x0D carrying INT2 - so text and binary cannot disagree about it.
            var dataType = (RecordDataType)((short)type & 0xFF);

            string value = valueOf(line, colon);

            return new Record((short)type, payloadOf(value, dataType, name, lineNumber));
        }

        ///<summary>
        ///Everything after the colon, with the one space the writer puts after the colon and the one it
        ///puts at the end of the line removed - and only those. Both are optional so that a line typed by
        ///hand without them still reads.
        ///</summary>
        private static string valueOf(string line, int colon)
        {
            string value = line.Substring(colon + 1);

            if (value.StartsWith(' '))
                value = value.Substring(1);

            if (value.EndsWith(' '))
                value = value.Substring(0, value.Length - 1);

            return value;
        }

        private static RecordType recordTypeOf(string name, int lineNumber)
        {
            //IsDefined as well as TryParse: TryParse also accepts a bare number, which would turn a typo
            //into a record type that does not exist.
            if (!Enum.TryParse(name, ignoreCase: true, out RecordType type) || !Enum.IsDefined(typeof(RecordType), type))
                throw new InvalidDataException($"Line {lineNumber} names an unknown record type \"{name}\".");

            return type;
        }

        private static byte[] payloadOf(string value, RecordDataType dataType, string name, int lineNumber)
        {
            //A record with nothing after the colon carries no payload, which is how every NODATA record is
            //written and how a record that declares a type and holds nothing comes back.
            if (value.Length == 0)
                return Array.Empty<byte>();

            //Bit arrays are written as hex, and so is any payload that could not be made sense of when it
            //was read. Both come back as the bytes they went out as.
            if (value.StartsWith(HexPrefix, StringComparison.OrdinalIgnoreCase))
                return hexBytes(value, name, lineNumber);

            switch (dataType)
            {
                case RecordDataType.INT2:
                    return new Int2Data(parseAll<short>(value, name, lineNumber, short.TryParse)).Encode();

                case RecordDataType.INT4:
                    return new Int4Data(parseAll<int>(value, name, lineNumber, int.TryParse)).Encode();

                case RecordDataType.REAL8:
                    return new Real8Data(parseAll<double>(value, name, lineNumber, double.TryParse)).Encode();

                case RecordDataType.ASCII:
                    return new AsciiData(value).Encode();

                default:
                    //NODATA with something after the colon, or REAL4, which no record type declares. Both
                    //are written as hex when they occur, so anything else here was typed by hand.
                    throw new InvalidDataException($"Line {lineNumber}: {name} holds {dataType}, which is written as hex bytes such as \"0x0005\". Got \"{value}\".");
            }
        }

        private delegate bool TryParseNumber<T>(string text, NumberStyles styles, IFormatProvider provider, out T value);

        ///<summary>
        ///Splits on whitespace and parses every piece, so one bad value fails the line rather than being
        ///skipped. Invariant, to match how they were written.
        ///</summary>
        private static T[] parseAll<T>(string value, string name, int lineNumber, TryParseNumber<T> tryParse)
        {
            string[] pieces = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var values = new T[pieces.Length];

            for (int i = 0; i < pieces.Length; i++)
            {
                if (!tryParse(pieces[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    throw new InvalidDataException($"Line {lineNumber}: {name} expects {typeof(T).Name} values, but \"{pieces[i]}\" is not one - or is out of range for it.");
            }

            return values;
        }

        private static byte[] hexBytes(string value, string name, int lineNumber)
        {
            string digits = value.Substring(HexPrefix.Length);

            //Two digits per byte, the way AppendHex writes them, so an odd count means one was lost.
            if (digits.Length == 0 || digits.Length % 2 != 0)
                throw new InvalidDataException($"Line {lineNumber}: {name} needs an even number of hex digits, two per byte. Got \"{value}\".");

            var bytes = new byte[digits.Length / 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(digits.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                    throw new InvalidDataException($"Line {lineNumber}: {name} has \"{digits.Substring(i * 2, 2)}\" where a pair of hex digits belongs, in \"{value}\".");
            }

            return bytes;
        }
    }
}
