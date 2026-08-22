namespace GdsII
{
    ///<summary>Which part of a label sits at its anchor horizontally.</summary>
    public enum HorizontalPresentation
    {
        Left = 0,
        Center = 1,
        Right = 2
    }

    ///<summary>Which part of a label sits at its anchor vertically.</summary>
    public enum VerticalPresentation
    {
        Top = 0,
        Middle = 1,
        Bottom = 2
    }

    ///<summary>
    ///Reads the two-byte bit fields the format defines: STRANS, PRESENTATION and ELFLAGS.
    ///
    ///GDSII numbers the bits of such a field **from the left**, so bit 0 is the most significant of the
    ///first byte. Writing the masks out as hex constants is where these normally go wrong, so the bit
    ///numbers below are the ones from the format's own tables and BitField.IsSet does the translation.
    ///</summary>
    internal static class BitField
    {
        ///<summary>Reads the field as one big-endian value, or null when the record is absent or malformed.</summary>
        public static int? ValueOf(RecordData? data)
        {
            if (data is not BitArrayData bits || bits.Value.Length < 2)
                return null;

            return (bits.Value[0] << 8) | bits.Value[1];
        }

        public static bool IsSet(int value, int bitNumber)
        {
            return (value & (1 << (15 - bitNumber))) != 0;
        }
    }

    ///<summary>
    ///A STRANS flag word: how a placed structure or a label is oriented.
    ///</summary>
    public readonly struct Strans
    {
        //From the format's table: bit 0 reflects about the X axis before rotation, and bits 13 and 14
        //mark the magnification and angle as absolute.
        private const int ReflectBit = 0;
        private const int AbsoluteMagnificationBit = 13;
        private const int AbsoluteAngleBit = 14;

        public Strans(bool reflectAboutX, bool absoluteMagnification, bool absoluteAngle)
        {
            ReflectAboutX = reflectAboutX;
            AbsoluteMagnification = absoluteMagnification;
            AbsoluteAngle = absoluteAngle;
        }

        ///<summary>Mirror about the X axis, applied before the rotation.</summary>
        public bool ReflectAboutX { get; }

        ///<summary>
        ///The magnification is measured against the world rather than the containing structure, so it is
        ///not multiplied by the parent's.
        ///</summary>
        public bool AbsoluteMagnification { get; }

        ///<summary>The angle is measured against the world rather than the containing structure.</summary>
        public bool AbsoluteAngle { get; }

        ///<summary>No reflection, and both magnification and angle relative - what an absent record means.</summary>
        public static Strans Default
        {
            get { return new Strans(false, false, false); }
        }

        public static Strans From(RecordData? data)
        {
            int? value = BitField.ValueOf(data);

            if (value is null)
                return Default;

            return new Strans(
                BitField.IsSet(value.Value, ReflectBit),
                BitField.IsSet(value.Value, AbsoluteMagnificationBit),
                BitField.IsSet(value.Value, AbsoluteAngleBit));
        }

        ///<summary>
        ///The two bytes this writes as, so that a placement the editor turns goes back out through the same
        ///bit table it was read through rather than a second copy of it.
        ///</summary>
        public byte[] Encode()
        {
            int value = 0;

            if (ReflectAboutX)
                value |= 1 << (15 - ReflectBit);

            if (AbsoluteMagnification)
                value |= 1 << (15 - AbsoluteMagnificationBit);

            if (AbsoluteAngle)
                value |= 1 << (15 - AbsoluteAngleBit);

            return new byte[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }
    }

    ///<summary>
    ///A PRESENTATION flag word: how a label is justified about its anchor, and which of the four fonts
    ///it asks for. Note it carries no size - that comes from the MAG in the text's own STRANS block.
    ///</summary>
    public readonly struct TextPresentation
    {
        //Bits 10-11 select the font, 12-13 the vertical justification and 14-15 the horizontal, which
        //puts each pair in the low bits of the word once shifted.
        private const int FontShift = 4;
        private const int VerticalShift = 2;
        private const int HorizontalShift = 0;
        private const int PairMask = 0b11;

        public TextPresentation(HorizontalPresentation horizontal, VerticalPresentation vertical, int font)
        {
            Horizontal = horizontal;
            Vertical = vertical;
            Font = font;
        }

        public HorizontalPresentation Horizontal { get; }
        public VerticalPresentation Vertical { get; }

        ///<summary>Font 0 to 3. The format leaves what they look like to the tool.</summary>
        public int Font { get; }

        ///<summary>What an absent record means, per the format: left, top, font 0.</summary>
        public static TextPresentation Default
        {
            get { return new TextPresentation(HorizontalPresentation.Left, VerticalPresentation.Top, 0); }
        }

        ///<summary>
        ///The two bytes a PRESENTATION record carries, which is the other end of <see cref="From"/>.
        ///
        ///Here beside the reader rather than wherever a label happens to be written. A bit field whose two
        ///ends live apart is one that eventually disagrees with itself, and this field is numbered from the
        ///left - which is exactly the thing that gets written the wrong way round.
        ///</summary>
        public byte[] Encode()
        {
            int value = ((Font & PairMask) << FontShift)
                | (((int)Vertical & PairMask) << VerticalShift)
                | (((int)Horizontal & PairMask) << HorizontalShift);

            return new byte[] { (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) };
        }

        public static TextPresentation From(RecordData? data)
        {
            int? value = BitField.ValueOf(data);

            if (value is null)
                return Default;

            return new TextPresentation(
                (HorizontalPresentation)pair(value.Value, HorizontalShift, (int)HorizontalPresentation.Left),
                (VerticalPresentation)pair(value.Value, VerticalShift, (int)VerticalPresentation.Top),
                (value.Value >> FontShift) & PairMask);
        }

        ///<summary>
        ///Reads a two-bit selector. Only 0, 1 and 2 are defined, so a 3 - which the format does not
        ///assign a meaning to - falls back to the default rather than becoming a nameless enum value.
        ///</summary>
        private static int pair(int value, int shift, int fallback)
        {
            int selector = (value >> shift) & PairMask;

            if (selector > 2)
                return fallback;

            return selector;
        }
    }

    ///<summary>An ELFLAGS flag word, carried by any element.</summary>
    public readonly struct ElementFlags
    {
        //Bit 15 marks template data and bit 14 external data; the rest are reserved.
        private const int TemplateBit = 15;
        private const int ExternalBit = 14;

        public ElementFlags(bool templateData, bool externalData)
        {
            TemplateData = templateData;
            ExternalData = externalData;
        }

        public bool TemplateData { get; }
        public bool ExternalData { get; }

        public static ElementFlags Default
        {
            get { return new ElementFlags(false, false); }
        }

        public static ElementFlags From(RecordData? data)
        {
            int? value = BitField.ValueOf(data);

            if (value is null)
                return Default;

            return new ElementFlags(
                BitField.IsSet(value.Value, TemplateBit),
                BitField.IsSet(value.Value, ExternalBit));
        }
    }
}
