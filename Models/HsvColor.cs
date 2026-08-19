using System.Globalization;

namespace GDSViewer.Models
{
    ///<summary>
    ///A color as hue, saturation and value.
    ///
    ///What a picker has to work in, and the reason this exists: the app stores colors as "#rrggbb", but a
    ///square you drag a point around is two of these three numbers with the third on a slider beside it.
    ///Going straight from a pointer position to red, green and blue has no sensible answer.
    ///
    ///Kept out of the GdsII library on purpose. The format has nothing to say about color - a layer's is
    ///the app's own choosing - so this belongs with the shell's models rather than in something about
    ///GDSII.
    ///</summary>
    public readonly record struct HsvColor
    {
        ///<summary>Degrees around the wheel, 0 to 360. Red is at both ends.</summary>
        public double Hue { get; }

        ///<summary>How much of the hue there is, 0 to 1. Zero is gray whatever the hue says.</summary>
        public double Saturation { get; }

        ///<summary>How bright, 0 to 1. Zero is black whatever the other two say.</summary>
        public double Value { get; }

        public HsvColor(double hue, double saturation, double value)
        {
            //Wrapped rather than clamped, since 360 and 0 are the same place and a drag can pass either.
            Hue = ((hue % 360) + 360) % 360;

            Saturation = clamp(saturation);
            Value = clamp(value);
        }

        ///<summary>
        ///Reads "#rrggbb". Anything else comes back black rather than throwing: this parses a color out of
        ///storage or off an element, and a picker that refuses to open because one is malformed would be
        ///worse than one that opens on the wrong color.
        ///</summary>
        public static HsvColor FromHex(string? hex)
        {
            if (!tryParseChannels(hex, out double red, out double green, out double blue))
                return new HsvColor(0, 0, 0);

            double max = Math.Max(red, Math.Max(green, blue));
            double min = Math.Min(red, Math.Min(green, blue));
            double span = max - min;

            double hue = 0;

            //A gray has no hue to find - every channel is the same, so there is no largest one to measure
            //from. Left at zero, which is what keeps the slider still while the saturation is dragged out.
            if (span > 0)
            {
                if (max == red)
                    hue = 60 * (((green - blue) / span) % 6);
                else if (max == green)
                    hue = 60 * (((blue - red) / span) + 2);
                else
                    hue = 60 * (((red - green) / span) + 4);
            }

            double saturation = 0;

            if (max > 0)
                saturation = span / max;

            return new HsvColor(hue, saturation, max);
        }

        ///<summary>Back to "#rrggbb", which is what everything else in the app stores and draws with.</summary>
        public string ToHex()
        {
            double chroma = Value * Saturation;
            double sector = Hue / 60;
            double second = chroma * (1 - Math.Abs((sector % 2) - 1));

            double red = 0;
            double green = 0;
            double blue = 0;

            if (sector < 1)
            {
                red = chroma;
                green = second;
            }
            else if (sector < 2)
            {
                red = second;
                green = chroma;
            }
            else if (sector < 3)
            {
                green = chroma;
                blue = second;
            }
            else if (sector < 4)
            {
                green = second;
                blue = chroma;
            }
            else if (sector < 5)
            {
                red = second;
                blue = chroma;
            }
            else
            {
                red = chroma;
                blue = second;
            }

            //What the chroma left off the bottom: value is the brightest channel, so the rest lift to meet it.
            double lift = Value - chroma;

            return $"#{channel(red + lift)}{channel(green + lift)}{channel(blue + lift)}";
        }

        ///<summary>
        ///The same color as three 0-to-255 channels, for the boxes beside the picker.
        ///
        ///Through the hex rather than repeating the conversion, so the numbers in those boxes cannot
        ///disagree with the color the layer is actually drawn in - there is one way out of here.
        ///</summary>
        public (int Red, int Green, int Blue) ToRgb()
        {
            string hex = ToHex();

            return (level(hex, 1), level(hex, 3), level(hex, 5));
        }

        ///<summary>
        ///Back from three channels. Used when one of the boxes is typed in, so the field and the slider
        ///move to wherever the typed color sits.
        ///</summary>
        public static HsvColor FromRgb(int red, int green, int blue)
        {
            return FromHex($"#{clampLevel(red):x2}{clampLevel(green):x2}{clampLevel(blue):x2}");
        }

        private static int level(string hex, int at)
        {
            return int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static int clampLevel(int amount)
        {
            if (amount < 0)
                return 0;

            if (amount > 255)
                return 255;

            return amount;
        }

        private static string channel(double amount)
        {
            int level = (int)Math.Round(clamp(amount) * 255);

            return level.ToString("x2", CultureInfo.InvariantCulture);
        }

        private static double clamp(double amount)
        {
            if (amount < 0)
                return 0;

            if (amount > 1)
                return 1;

            return amount;
        }

        private static bool tryParseChannels(string? hex, out double red, out double green, out double blue)
        {
            red = 0;
            green = 0;
            blue = 0;

            if (hex is null)
                return false;

            string text = hex.Trim().TrimStart('#');

            if (text.Length != 6)
                return false;

            if (!tryParseChannel(text[..2], out red))
                return false;

            if (!tryParseChannel(text.Substring(2, 2), out green))
                return false;

            return tryParseChannel(text.Substring(4, 2), out blue);
        }

        private static bool tryParseChannel(string pair, out double amount)
        {
            amount = 0;

            if (!int.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int level))
                return false;

            amount = level / 255.0;

            return true;
        }
    }
}
