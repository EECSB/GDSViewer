using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using GdsII;

namespace GDSViewer.Models
{
    ///
    ///A Generator structure drawn with its numbers marked on it, for the panel they are typed into (2026-11-01, asked for):
    ///a rat-race coupler's ring with its radius and its line's width measured on it, each number's box beside the part it
    ///sets.
    ///
    ///**A drawing, not the structure.** Its proportions are chosen so every number has something to point at - a ring
    ///resonator's gap is a fiftieth of its radius, and drawn to scale it is not there to point at - so it says which
    ///part a number sets, and the structure in hand under the pointer says what the numbers come to.
    ///
    public sealed class MarkFigure
    {
        ///<summary>The drawing's size in pixels: one for every kind, so choosing another does not move the panel's other controls.</summary>
        public const int Width = 320;

        public const int Height = 200;

        ///<summary>A number's box, centered on its point: its name over a box for five digits and a unit.</summary>
        public const int ChipWidth = 60;

        public const int ChipHeight = 35;

        internal MarkFigure(string svg, IReadOnlyDictionary<string, (int X, int Y)> chips)
        {
            Svg = svg;
            Chips = chips;
        }

        ///<summary>The drawing, an svg element of its own that the page puts down as it is.</summary>
        public string Svg { get; }

        ///<summary>Where each number's box is centered, by the setting's name.</summary>
        public IReadOnlyDictionary<string, (int X, int Y)> Chips { get; }
    }

    ///
    ///The drawing of every kind of Generator structure - see <see cref="MarkFigure"/>.
    ///
    ///**Each is drawn by hand**, in the panel's own pixels, from what <see cref="Marks"/> makes: the rat-race's ports at
    ///0°, 60°, 120° and 180°, an inductor's ends both out on its left, a resonator's gaps going round its open start. The
    ///structure is drawn in the layer blue, and what another kind of the same family puts down beside it - the cross
    ///round which a window is cut, the spiral an underpass runs under - faintly behind it.
    ///
    ///**A number is a dimension**, the way a drawing marks one: a line with an arrow at each end between the two edges it
    ///is the distance between, carried out on extension lines where the part itself has no room; arrows pointing in from
    ///outside a gap too narrow for them; a bracket over the things a count counts; an arc for an angle. Each is a group
    ///of its own, numbered as the settings are, so the page can light the one whose box is being pointed at or typed in.
    ///
    public static class MarkFigures
    {
        private static readonly ConcurrentDictionary<string, MarkFigure> made = new ConcurrentDictionary<string, MarkFigure>();

        ///<summary>The drawing of this kind, made once.</summary>
        public static MarkFigure For(MarkMaker maker)
        {
            return made.GetOrAdd(maker.Key, key => Draw(maker));
        }

        ///<summary>Whether this kind has a drawing of its own, rather than its numbers set out in two columns.</summary>
        public static bool Drawn(MarkMaker maker)
        {
            return drawing(maker.Key) is not null;
        }

        ///<summary>The drawing of this kind made afresh - what <see cref="For"/> keeps.</summary>
        public static MarkFigure Draw(MarkMaker maker)
        {
            if (drawing(maker.Key) is Func<MarkFigure> drawer)
                return drawer();

            return unDrawn(maker);
        }

        private static Func<MarkFigure>? drawing(string key)
        {
            switch (key)
            {
                case "frame":
                    return frame;
                case "cross":
                    return cross;
                case "window":
                    return window;
                case "vernier":
                    return vernier;
                case "lines":
                    return lines;
                case "greek":
                    return greek;
                case "comb":
                    return comb;
                case "ring":
                    return ring;
                case "grating":
                    return grating;
                case "serpentine":
                    return serpentine;
                case "chamber":
                    return chamber;
                case "spiral":
                    return spiral;
                case "underpass":
                    return underpass;
                case "inductorvias":
                    return inductorVias;
                case "loop":
                    return loop;
                case "idc":
                    return interdigitated;
                case "resistor":
                    return resistor;
                case "guard":
                    return guard;
                case "guardcontacts":
                    return guardContacts;
                case "shield":
                    return shield;
                case "cpw":
                    return coplanar;
                case "cpwresonator":
                    return coplanarResonator;
                case "branchline":
                    return branchLine;
                case "ratrace":
                    return ratRace;
                case "balun":
                    return balun;
                case "coupledfilter":
                    return coupledFilter;
                case "steppedfilter":
                    return steppedFilter;
                case "radialstub":
                    return radialStub;
                case "srr":
                    return splitRing;
                case "patch":
                    return patch;
                case "dipole":
                    return dipole;
                case "bowtie":
                    return bowtie;
                case "spiralantenna":
                    return spiralAntenna;
                case "htree":
                    return hTree;
                case "rails":
                    return rails;
                case "pads":
                    return pads;
                case "padopenings":
                    return padOpenings;
                case "fill":
                    return fill;
                default:
                    return null;
            }
        }

        ///<summary>A kind added to the library with no drawing yet: its numbers in two columns, so the panel still works.</summary>
        private static MarkFigure unDrawn(MarkMaker maker)
        {
            var chips = new Dictionary<string, (int X, int Y)>();

            for (int index = 0; index < maker.Settings.Count; index++)
                chips[maker.Settings[index].Name] = (90 + ((index % 2) * 140), 30 + ((index / 2) * 44));

            return new MarkFigure($"<svg class=\"markDrawing\" width=\"{MarkFigure.Width}\" height=\"{MarkFigure.Height}\" aria-hidden=\"true\"></svg>", chips);
        }



        #region Alignment *******************************************************************

        private static MarkFigure frame()
        {
            var s = new Sketch("frame");

            s.Box(95, 32, 225, 46);
            s.Box(95, 136, 225, 150);
            s.Box(95, 46, 109, 136);
            s.Box(211, 46, 225, 136);

            s.Across("width", 95, 225, 176, 150, 150);
            s.Chip("width", 160, 176);

            s.Upright("height", 32, 150, 262, 225, 225);
            s.Chip("height", 262, 91);

            s.Pinch("line", 95, 91, 109, 91);
            s.Chip("line", 50, 91);

            return s.Done();
        }

        private static MarkFigure cross()
        {
            var s = new Sketch("cross");

            s.Box(96, 77, 224, 99);
            s.Box(149, 24, 171, 152);

            s.Across("arm", 96, 224, 176, 99, 99);
            s.Chip("arm", 160, 176);

            s.Pinch("width", 149, 38, 171, 38);
            s.Chip("width", 218, 38);

            return s.Done();
        }

        ///<summary>The four squares, and faintly the cross of the same numbers that lands between them on the other layer.</summary>
        private static MarkFigure window()
        {
            var s = new Sketch("window");

            s.Companion();
            s.Box(102, 88, 218, 108);
            s.Box(150, 40, 170, 156);
            s.Structure();

            s.Box(102, 40, 142, 80);
            s.Box(178, 40, 218, 80);
            s.Box(178, 116, 218, 156);
            s.Box(102, 116, 142, 156);

            s.Across("arm", 102, 218, 178, 156, 156);
            s.Chip("arm", 160, 178);

            s.PinchUpright("width", 88, 108, 246, 218, 218);
            s.Chip("width", 282, 98);

            s.PinchAcross("gap", 142, 150, 20, 40, 40);
            s.Chip("gap", 198, 20);

            return s.Done();
        }

        private static MarkFigure vernier()
        {
            var s = new Sketch("vernier");

            for (int bar = 0; bar < 9; bar++)
            {
                double left = 93 + (bar * 16);
                double top = 90;

                if (bar == 4)
                    top = 60;

                s.Box(left, top, left + 6, 150);
            }

            s.Count("bars", 93, 40, 227, 40);
            s.Chip("bars", 160, 40);

            s.Upright("length", 90, 150, 50, 93, 93);
            s.Chip("length", 50, 120);

            s.PinchAcross("width", 221, 227, 74, 90, 90);
            s.Chip("width", 274, 74);

            s.PinchAcross("pitch", 205, 221, 170, 150, 150);
            s.Chip("pitch", 268, 170);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Test structures *************************************************************

        ///<summary>Three groups of five, each narrower than the last.</summary>
        private static MarkFigure lines()
        {
            var s = new Sketch("lines");

            for (int line = 0; line < 5; line++)
            {
                s.Box(80 + (line * 14), 70, 87 + (line * 14), 144);
                s.Box(155 + (line * 8), 70, 159 + (line * 8), 144);
                s.Box(203 + (line * 4), 70, 205 + (line * 4), 144);
            }

            s.PinchAcross("widest", 80, 87, 52, 70, 70);
            s.Chip("widest", 36, 52);

            s.PinchAcross("narrowest", 203, 205, 52, 70, 70);
            s.Chip("narrowest", 252, 52);

            s.Count("groups", 80, 162, 221, 162);
            s.Chip("groups", 150, 162);

            s.Upright("length", 70, 144, 252, 221, 221);
            s.Chip("length", 252, 107);

            return s.Done();
        }

        private static MarkFigure greek()
        {
            var s = new Sketch("greek");

            s.Box(110, 94, 190, 106);
            s.Box(144, 60, 156, 140);
            s.Box(190, 86, 218, 114);
            s.Box(82, 86, 110, 114);
            s.Box(136, 32, 164, 60);
            s.Box(136, 140, 164, 168);

            s.Upright("pad", 86, 114, 238, 218, 218);
            s.Chip("pad", 272, 100);

            //From the middle to the pad, which is where a pad's inner edge is.
            s.Span("arm", 150, 76, 190, 76);
            s.Extend("arm", 190, 84, 190, 73);
            s.Lead("arm", 190, 76, 204, 64);
            s.Chip("arm", 222, 52);

            s.Pinch("width", 126, 94, 126, 106);
            s.Chip("width", 100, 146);

            return s.Done();
        }

        ///<summary>Two fingers to a comb and the wire winding between them in five runs.</summary>
        private static MarkFigure comb()
        {
            var s = new Sketch("comb");

            s.Band(8, Ends.Butt, 88, 158, 88, 68, 124, 68, 124, 132, 160, 132, 160, 68, 196, 68, 196, 132, 232, 132, 232, 42);

            s.Box(102, 146, 182, 158);
            s.Box(102, 82, 110, 158);
            s.Box(174, 82, 182, 158);

            s.Box(138, 42, 218, 54);
            s.Box(138, 42, 146, 118);
            s.Box(210, 42, 218, 118);

            s.Count("fingers", 102, 176, 182, 176);
            s.Chip("fingers", 142, 177);

            s.Pinch("width", 228, 60, 236, 60);
            s.Chip("width", 278, 60);

            s.Pinch("spacing", 218, 104, 228, 104);
            s.Chip("spacing", 278, 104);

            s.Upright("length", 82, 146, 68, 102, 102);
            s.Chip("length", 36, 114);

            s.PinchUpright("bus", 146, 158, 200, 182, 182);
            s.Chip("bus", 238, 160);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Photonics *******************************************************************

        private static MarkFigure ring()
        {
            var s = new Sketch("ring");

            s.Ring(160, 84, 43, 53);
            s.Box(85, 147, 235, 157);

            s.Reach("radius", 160, 84, 160 + (48 * cos(30)), 84 - (48 * sin(30)));
            s.Chip("radius", 160, 84);

            s.Pinch("width", 160 + (43 * cos(150)), 84 - (43 * sin(150)), 160 + (53 * cos(150)), 84 - (53 * sin(150)));
            s.Chip("width", 72, 40);

            s.Pinch("gap", 160, 137, 160, 147);
            s.Lead("gap", 154, 142, 98, 132);
            s.Chip("gap", 70, 128);

            s.Across("bus", 85, 235, 180, 157, 157);
            s.Chip("bus", 160, 180);

            return s.Done();
        }

        private static MarkFigure grating()
        {
            var s = new Sketch("grating");

            for (int tooth = 0; tooth < 9; tooth++)
                s.Box(92 + (tooth * 16), 66, 100 + (tooth * 16), 146);

            s.Count("teeth", 92, 46, 228, 46);
            s.Chip("teeth", 160, 46);

            s.Upright("length", 66, 146, 262, 228, 228);
            s.Chip("length", 262, 106);

            s.PinchAcross("tooth", 92, 100, 168, 146, 146);
            s.Chip("tooth", 46, 168);

            s.PinchAcross("period", 204, 220, 168, 146, 146);
            s.Chip("period", 268, 168);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Microfluidics ***************************************************************

        ///<summary>Four runs, round at every turn and at both ends - the corners Marks rounds to half the channel's width.</summary>
        private static MarkFigure serpentine()
        {
            var s = new Sketch("serpentine");

            s.Band(14, Ends.Round, 92, 56, 228, 56, 228, 86, 92, 86, 92, 116, 228, 116, 228, 146, 92, 146);

            s.Pinch("width", 160, 49, 160, 63);
            s.Chip("width", 160, 21);

            s.Across("run", 92, 228, 180, 153, 153);
            s.Chip("run", 160, 180);

            s.Count("runs", 56, 56, 56, 146);
            s.Chip("runs", 52, 101);

            s.Upright("pitch", 56, 86, 252, 235, 235);
            s.Chip("pitch", 286, 71);

            return s.Done();
        }

        private static MarkFigure chamber()
        {
            var s = new Sketch("chamber");

            s.Disc(160, 104, 26);
            s.Box(90, 99, 230, 109);
            s.Disc(90, 104, 15);
            s.Disc(230, 104, 15);

            s.Across("chamber", 134, 186, 62, 97, 97);
            s.Chip("chamber", 160, 36);

            s.Span("port", 90, 89, 90, 119);
            s.Chip("port", 90, 148);

            //From the chamber's edge to the middle of the port.
            s.Across("length", 186, 230, 74, 97, 89);
            s.Chip("length", 262, 52);

            s.Pinch("width", 208, 99, 208, 109);
            s.Chip("width", 208, 146);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Analog **********************************************************************

        ///<summary>Two turns of the spiral, its outer end out along its last turn and the landing its underpass comes up to - Marks's own walk round, in the panel's pixels.</summary>
        private static readonly double[] spiralTrace = { 143, 123, 143, 67, 217, 67, 217, 141, 125, 141, 125, 49, 235, 49, 235, 159, 95, 159 };

        private static void spiralMetal(Sketch s)
        {
            s.Band(10, Ends.Square, spiralTrace);
            s.Box(90, 118, 100, 128);
        }

        ///<summary>The numbers a spiral, its underpass and its vias share, marked where they are on the spiral.</summary>
        private static void spiralNumbers(Sketch s)
        {
            s.Pinch("width", 180, 44, 180, 54);
            s.Chip("width", 180, 19);

            //Across the hole, under its own box - the hole is the one place with room for it.
            s.Span("inner", 148, 128, 212, 128);
            s.Chip("inner", 180, 100);

            s.Pinch("spacing", 222, 84, 230, 84);
            s.Chip("spacing", 274, 84);

            s.Count("turns", 212, 124, 240, 124);
            s.Chip("turns", 274, 124);

            s.Across("lead", 95, 125, 182, 164, 164);
            s.Chip("lead", 58, 182);
        }

        private static MarkFigure spiral()
        {
            var s = new Sketch("spiral");

            spiralMetal(s);
            spiralNumbers(s);

            return s.Done();
        }

        ///<summary>The underpass, under a faint spiral: from the landing on the left to under the inner end.</summary>
        private static MarkFigure underpass()
        {
            var s = new Sketch("underpass");

            s.Companion();
            spiralMetal(s);
            s.Structure();

            s.Box(90, 118, 148, 128);

            spiralNumbers(s);

            return s.Done();
        }

        ///
        ///The vias at both ends of the underpass, too small at the spiral's size to measure - so one landing is drawn again
        ///large, in a frame on the left, with its own two numbers on it.
        ///
        private static MarkFigure inductorVias()
        {
            var s = new Sketch("inductorvias");

            s.Companion();
            spiralMetal(s);
            s.Box(90, 118, 148, 128);
            s.Structure();

            foreach (double middle in new double[] { 95, 143 })
            {
                foreach (double across in new double[] { -3.65, 0.65 })
                {
                    foreach (double down in new double[] { -3.65, 0.65 })
                        s.Box(middle + across, 123 + down, middle + across + 3, 123 + down + 3);
                }
            }

            s.Callout(8, 44, 66, 102);
            s.Pointer(66, 84, 89, 120);

            s.Companion();
            s.Box(14, 50, 60, 96);
            s.Structure();

            s.Box(20, 56, 34, 70);
            s.Box(40, 56, 54, 70);
            s.Box(20, 76, 34, 90);
            s.Box(40, 76, 54, 90);

            s.PinchAcross("via", 20, 34, 41, 56, 56);
            s.Chip("via", 36, 20);

            s.PinchAcross("gap", 34, 40, 106, 90, 90);
            s.Chip("gap", 36, 126);

            spiralNumbers(s);

            return s.Done();
        }

        ///<summary>One octagonal turn, its two leads down out of the middle of its bottom side.</summary>
        private static MarkFigure loop()
        {
            var s = new Sketch("loop");
            var line = new List<double> { 174, 170, 174, 140 };

            for (int corner = 0; corner < 8; corner++)
            {
                double angle = -67.5 + (corner * 45);

                line.Add(160 + (58.45 * cos(angle)));
                line.Add(86 - (58.45 * sin(angle)));
            }

            line.AddRange(new double[] { 146, 140, 146, 170 });

            s.Band(12, Ends.Butt, line.ToArray());

            s.Span("diameter", 100, 86, 220, 86);
            s.Chip("diameter", 160, 86);

            s.Pinch("width", 100, 104, 112, 104);
            s.Chip("width", 56, 104);

            s.Pinch("gap", 152, 158, 168, 158);
            s.Chip("gap", 218, 158);

            s.Upright("lead", 140, 170, 126, 140, 140);
            s.Chip("lead", 90, 155);

            return s.Done();
        }

        ///<summary>Four fingers, two from the spine along the bottom and two from the one along the top.</summary>
        private static MarkFigure interdigitated()
        {
            var s = new Sketch("idc");

            s.Box(118, 138, 202, 152);
            s.Box(118, 74, 130, 152);
            s.Box(166, 74, 178, 152);

            s.Box(118, 48, 202, 62);
            s.Box(142, 48, 154, 126);
            s.Box(190, 48, 202, 126);

            s.Count("fingers", 118, 170, 202, 170);
            s.Chip("fingers", 160, 170);

            s.PinchUpright("bus", 138, 152, 100, 118, 118);
            s.Chip("bus", 62, 145);

            s.Upright("length", 62, 126, 236, 202, 202);
            s.Chip("length", 236, 94);

            s.Pinch("end", 196, 126, 196, 138);
            s.Lead("end", 200, 146, 222, 150);
            s.Chip("end", 250, 150);

            s.Pinch("gap", 130, 100, 142, 100);
            s.Chip("gap", 84, 100);

            s.Pinch("width", 142, 70, 154, 70);
            s.Lead("width", 132, 70, 114, 62);
            s.Chip("width", 86, 56);

            return s.Done();
        }

        ///<summary>Five runs folded back and forth, a contact head at each end.</summary>
        private static MarkFigure resistor()
        {
            var s = new Sketch("resistor");

            s.Band(8, Ends.Butt, 83, 132, 225, 132, 225, 116, 95, 116, 95, 100, 225, 100, 225, 84, 95, 84, 95, 68, 237, 68);
            s.Box(63, 122, 83, 142);
            s.Box(237, 58, 257, 78);

            s.Pinch("width", 160, 128, 160, 136);
            s.Chip("width", 160, 162);

            s.Across("run", 95, 225, 40, 64, 64);
            s.Chip("run", 160, 40);

            s.Count("runs", 52, 68, 52, 132);
            s.Chip("runs", 50, 100);

            s.Pinch("spacing", 212, 120, 212, 128);
            s.Chip("spacing", 264, 124);

            s.PinchUpright("head", 58, 78, 276, 257, 257);
            s.Chip("head", 276, 30);

            return s.Done();
        }

        private static void guardRing(Sketch s)
        {
            s.Rim(71, 44, 249, 152, 14);
        }

        ///<summary>The width and height of what the ring goes round, measured inside it, and the height carried out past it.</summary>
        private static void guardNumbers(Sketch s)
        {
            s.Span("width", 85, 118, 235, 118);
            s.Chip("width", 160, 118);

            s.Upright("height", 58, 138, 282, 235, 235);
            s.Chip("height", 282, 98);

            s.Pinch("ring", 71, 98, 85, 98);
            s.Chip("ring", 32, 98);
        }

        private static MarkFigure guard()
        {
            var s = new Sketch("guard");

            guardRing(s);
            guardNumbers(s);

            return s.Done();
        }

        ///<summary>The contacts down the middle of a faint ring: one at every corner, and as many along each side as the pitch fits.</summary>
        private static MarkFigure guardContacts()
        {
            var s = new Sketch("guardcontacts");

            s.Companion();
            guardRing(s);
            s.Structure();

            var corners = new (double X, double Y)[] { (78, 145), (242, 145), (242, 51), (78, 51) };

            for (int side = 0; side < 4; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 4];
                double length = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
                int count = Math.Max(1, (int)Math.Floor(length / 16));

                for (int each = 0; each < count; each++)
                {
                    double along = each / (double)count;
                    double x = from.X + ((to.X - from.X) * along);
                    double y = from.Y + ((to.Y - from.Y) * along);

                    s.Box(x - 3, y - 3, x + 3, y + 3);
                }
            }

            guardNumbers(s);

            s.PinchAcross("contact", 206.2, 212.2, 28, 48, 48);
            s.Chip("contact", 258, 26);

            s.PinchAcross("pitch", 140.6, 157, 28, 48, 48);
            s.Chip("pitch", 96, 26);

            return s.Done();
        }

        ///
        ///Four quarters of strips hanging from a bar along each side, each strip stopping half a slot short of the diagonals -
        ///Marks's quarter, worked out the same way and turned round the middle.
        ///
        private static MarkFigure shield()
        {
            var s = new Sketch("shield");
            const double half = 65;
            const double edge = 9;
            const double slot = 5;
            const double bar = 7;
            double clear = edge + slot;
            double pitch = bar + slot;
            int strips = (int)((((2 * half) - (2 * clear)) + slot) / pitch);
            double first = -((strips * pitch) - slot) / 2;
            double offDiagonal = slot / Math.Sqrt(2);

            //Each as the bottom quarter's corners, turned a quarter at a time: (x, y) to (-y, x), y up.
            void quarterBox(double left, double bottom, double right, double top)
            {
                for (int turn = 0; turn < 4; turn++)
                {
                    double x1 = left;
                    double y1 = bottom;
                    double x2 = right;
                    double y2 = top;

                    for (int each = 0; each < turn; each++)
                    {
                        (x1, y1) = (-y1, x1);
                        (x2, y2) = (-y2, x2);
                    }

                    s.Box(160 + Math.Min(x1, x2), 100 - Math.Max(y1, y2), 160 + Math.Max(x1, x2), 100 - Math.Min(y1, y2));
                }
            }

            quarterBox(-half + clear, -half, half - clear, -half + edge);

            for (int each = 0; each < strips; each++)
            {
                double left = first + (each * pitch);
                double right = left + bar;
                double nearest = Math.Max(Math.Abs(left), Math.Abs(right));
                double top = Math.Floor(-nearest - offDiagonal);

                quarterBox(left, -half, right, top);
            }

            s.Across("size", 95, 225, 182, 151, 151);
            s.Chip("size", 160, 182);

            s.Pinch("edge", 216, 100, 225, 100);
            s.Chip("edge", 268, 100);

            //Across the right quarter's second strip from the top, and the slot under its third from the bottom.
            double strip = 100 - (first + ((strips - 2) * pitch)) - bar;

            s.Pinch("bar", 204, strip, 204, strip + bar);
            s.Lead("bar", 206, strip - 10, 240, 48);
            s.Chip("bar", 268, 40);

            double slotLeft = 160 + first + bar;

            s.Pinch("slot", slotLeft, 153, slotLeft + slot, 153);
            s.Lead("slot", slotLeft - 10, 153, 82, 158);
            s.Chip("slot", 52, 160);

            return s.Done();
        }

        #endregion **************************************************************************



        #region RF and microwave ************************************************************

        private static MarkFigure coplanar()
        {
            var s = new Sketch("cpw");

            s.Box(85, 52, 235, 82);
            s.Box(85, 92, 235, 108);
            s.Box(85, 118, 235, 148);

            s.Across("length", 85, 235, 174, 148, 148);
            s.Chip("length", 160, 174);

            s.PinchUpright("signal", 92, 108, 250, 235, 235);
            s.Chip("signal", 284, 100);

            s.PinchUpright("gap", 82, 92, 66, 85, 85);
            s.Chip("gap", 34, 87);

            s.Upright("ground", 118, 148, 66, 85, 85);
            s.Chip("ground", 34, 133);

            return s.Done();
        }

        ///
        ///Three runs wound with two half turns, drawn as Marks draws them: the two gaps either side of the center line, round
        ///its open start and stopping square at its shorted end. Its length is the dashed line down its middle.
        ///
        private static MarkFigure coplanarResonator()
        {
            var s = new Sketch("cpwresonator");
            var middle = new List<(double X, double Y, double AlongX, double AlongY)>();

            void straight(double fromX, double toX, double y)
            {
                double along = Math.Sign(toX - fromX);

                for (int step = 0; step <= 10; step++)
                    middle.Add((fromX + ((toX - fromX) * step / 10), y, along, 0));
            }

            //A half turn round a middle, from its bottom to its top, out to the right or the left.
            void halfTurn(double x, double y, double outward)
            {
                for (int step = 1; step < 24; step++)
                {
                    double angle = Math.PI * step / 24;

                    middle.Add((x + (outward * 18 * Math.Sin(angle)), y + (18 * Math.Cos(angle)), outward * Math.Cos(angle), -Math.Sin(angle)));
                }
            }

            straight(70, 200, 140);
            halfTurn(200, 122, 1);
            straight(200, 70, 104);
            halfTurn(70, 86, -1);
            straight(70, 200, 68);

            //The gaps: between 4 and 10 either side of the middle, to its left and its right.
            foreach (double side in new double[] { 1, -1 })
            {
                var outline = new List<double>();

                foreach (var point in middle)
                {
                    outline.Add(point.X + (side * 10 * point.AlongY));
                    outline.Add(point.Y - (side * 10 * point.AlongX));
                }

                for (int index = middle.Count - 1; index >= 0; index--)
                {
                    outline.Add(middle[index].X + (side * 4 * middle[index].AlongY));
                    outline.Add(middle[index].Y - (side * 4 * middle[index].AlongX));
                }

                s.Shape(outline.ToArray());
            }

            //Round the open start: the gaps go on past it by a gap's width.
            s.Box(64, 130, 70, 150);

            var trace = new List<double>();

            foreach (var point in middle)
            {
                trace.Add(point.X);
                trace.Add(point.Y);
            }

            s.Trace("length", trace.ToArray());
            s.Chip("length", 34, 160);

            s.Pinch("width", 180, 64, 180, 72);
            s.Chip("width", 180, 30);

            s.Pinch("gap", 120, 58, 120, 64);
            s.Chip("gap", 110, 30);

            s.Across("run", 70, 200, 178, 150, 150);
            s.Chip("run", 135, 178);

            s.Upright("pitch", 104, 140, 246, 202, 202);
            s.Chip("pitch", 280, 122);

            return s.Done();
        }

        ///<summary>The square of four arms, the through arms top and bottom wider than the branches, and a port out sideways from every corner.</summary>
        private static MarkFigure branchLine()
        {
            var s = new Sketch("branchline");

            s.Box(114, 55, 206, 69);
            s.Box(114, 139, 206, 153);
            s.Box(114, 55, 122, 153);
            s.Box(198, 55, 206, 153);

            s.Box(202, 57, 240, 67);
            s.Box(202, 141, 240, 151);
            s.Box(80, 57, 118, 67);
            s.Box(80, 141, 118, 151);

            s.Across("quarter", 118, 202, 174, 153, 153);
            s.Chip("quarter", 160, 174);

            s.Pinch("through", 160, 55, 160, 69);
            s.Chip("through", 160, 26);

            s.Pinch("branch", 114, 104, 122, 104);
            s.Chip("branch", 162, 104);

            s.Pinch("port", 232, 57, 232, 67);
            s.Chip("port", 276, 62);

            s.Across("lead", 206, 240, 128, 128, 141);
            s.Chip("lead", 276, 128);

            return s.Done();
        }

        ///<summary>The ring with its four ports at 0°, 60°, 120° and 180° - the user's example, 2026-11-01.</summary>
        private static MarkFigure ratRace()
        {
            var s = new Sketch("ratrace");

            s.Ring(160, 122, 46, 54);

            foreach (double angle in new double[] { 0, 60, 120, 180 })
                s.Radial(160, 122, angle, 50, 84, 12);

            s.Reach("radius", 160, 122, 160 + (50 * cos(-30)), 122 - (50 * sin(-30)));
            s.Chip("radius", 160, 122);

            s.Pinch("ring", 160 + (46 * cos(225)), 122 - (46 * sin(225)), 160 + (54 * cos(225)), 122 - (54 * sin(225)));
            s.Chip("ring", 92, 180);

            s.Pinch("port", 236, 116, 236, 128);
            s.Chip("port", 252, 162);

            s.Across("lead", 214, 244, 104, 116, 116);
            s.Chip("lead", 252, 80);

            return s.Done();
        }

        ///<summary>The half-wave line over the two quarter-wave lines, its input up from the top left and an output down from each bottom line's inner end.</summary>
        private static MarkFigure balun()
        {
            var s = new Sketch("balun");

            s.Box(74, 86, 266, 96);
            s.Box(74, 56, 84, 96);
            s.Box(74, 104, 158, 114);
            s.Box(148, 104, 158, 144);
            s.Box(182, 104, 266, 114);
            s.Box(182, 104, 192, 144);

            s.Across("quarter", 74, 158, 176, 114, 144);
            s.Chip("quarter", 116, 176);

            s.PinchAcross("split", 158, 182, 160, 144, 144);
            s.Chip("split", 226, 160);

            s.Pinch("width", 74, 66, 84, 66);
            s.Chip("width", 122, 66);

            s.Pinch("gap", 240, 96, 240, 104);
            s.Chip("gap", 240, 60);

            s.Upright("lead", 114, 144, 132, 148, 148);
            s.Chip("lead", 100, 138);

            return s.Done();
        }

        ///<summary>Two resonators in a staircase between the input and the output, each beside the next for a quarter.</summary>
        private static MarkFigure coupledFilter()
        {
            var s = new Sketch("coupledfilter");

            s.Box(65, 125, 145, 135);
            s.Box(95, 105, 195, 115);
            s.Box(145, 85, 245, 95);
            s.Box(195, 65, 275, 75);

            s.Across("quarter", 145, 195, 50, 85, 65);
            s.Chip("quarter", 170, 26);

            s.Across("lead", 245, 275, 50, 85, 65);
            s.Chip("lead", 276, 24);

            s.Pinch("width", 80, 125, 80, 135);
            s.Chip("width", 46, 160);

            s.Pinch("gap", 110, 115, 110, 125);
            s.Chip("gap", 110, 160);

            s.Count("resonators", 205, 85, 205, 115);
            s.Chip("resonators", 205, 150);

            return s.Done();
        }

        ///<summary>A port line, then narrow and wide sections in turn - two wide here - and a port line again.</summary>
        private static MarkFigure steppedFilter()
        {
            var s = new Sketch("steppedfilter");

            s.Box(55, 93, 89, 107);
            s.Box(89, 97, 119, 103);
            s.Box(119, 78, 145, 122);
            s.Box(145, 97, 175, 103);
            s.Box(175, 78, 201, 122);
            s.Box(201, 97, 231, 103);
            s.Box(231, 93, 265, 107);

            s.Pinch("port", 66, 93, 66, 107);
            s.Lead("port", 66, 83, 52, 56);
            s.Chip("port", 40, 40);

            s.Span("wide", 132, 78, 132, 122);
            s.Lead("wide", 132, 78, 120, 56);
            s.Chip("wide", 110, 40);

            s.Count("sections", 119, 64, 201, 64);
            s.Chip("sections", 184, 40);

            s.Pinch("narrow", 216, 97, 216, 103);
            s.Lead("narrow", 216, 87, 240, 56);
            s.Chip("narrow", 256, 40);

            s.Across("lead", 55, 89, 136, 107, 107);
            s.Chip("lead", 72, 166);

            s.Across("narrow run", 145, 175, 136, 122, 122);
            s.Chip("narrow run", 150, 166);

            s.Across("wide run", 175, 201, 136, 122, 122);
            s.Chip("wide run", 218, 166);

            return s.Done();
        }

        ///<summary>A fan opening on the end of a line, its point where the line comes in.</summary>
        private static MarkFigure radialStub()
        {
            var s = new Sketch("radialstub");

            s.Fan(130, 100, 120, -30, 30);
            s.Box(70, 94, 141.4, 106);

            s.Reach("radius", 130, 100, 130 + (120 * cos(-20)), 100 - (120 * sin(-20)));
            s.Lead("radius", 130 + (120 * cos(-20)), 100 - (120 * sin(-20)), 262, 150);
            s.Chip("radius", 284, 152);

            s.Angle("angle", 130, 100, 46, -30, 30);
            s.Chip("angle", 212, 100);

            s.Pinch("width", 90, 94, 90, 106);
            s.Chip("width", 90, 140);

            s.Across("lead", 70, 130, 72, 94, 94);
            s.Chip("lead", 100, 44);

            return s.Done();
        }

        ///<summary>The two rings, the outer cut at the top and the inner at the bottom.</summary>
        private static MarkFigure splitRing()
        {
            var s = new Sketch("srr");

            s.CutRing(160, 100, 58, 70, 7, true);
            s.CutRing(160, 100, 36, 48, 7, false);

            s.Reach("radius", 160, 100, 160 + (70 * cos(-45)), 100 - (70 * sin(-45)));
            s.Chip("radius", 160, 100);

            s.Pinch("width", 90, 100, 102, 100);
            s.Chip("width", 46, 100);

            s.Pinch("spacing", 208, 100, 218, 100);
            s.Chip("spacing", 272, 100);

            s.PinchAcross("split", 153, 167, 22, 30, 30);
            s.Chip("split", 232, 22);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Antennas ********************************************************************

        ///<summary>The patch with its notch, and the feed line up from below into it.</summary>
        private static MarkFigure patch()
        {
            var s = new Sketch("patch");

            s.Shape(80, 44, 240, 44, 240, 144, 173, 144, 173, 104, 147, 104, 147, 144, 80, 144);
            s.Box(155, 104, 165, 184);

            s.Across("width", 80, 240, 24, 44, 44);
            s.Chip("width", 160, 24);

            s.Upright("length", 44, 144, 272, 240, 240);
            s.Chip("length", 272, 94);

            //Carried out past the patch's left edge from the top of the notch, so its box covers none of it.
            s.Upright("inset", 104, 144, 72, 147, 80);
            s.Chip("inset", 38, 124);

            s.Pinch("notch", 147, 124, 155, 124);
            s.Chip("notch", 112, 124);

            s.Pinch("feed", 155, 164, 165, 164);
            s.Chip("feed", 206, 164);

            s.Upright("lead", 144, 184, 132, 155, 155);
            s.Chip("lead", 100, 166);

            return s.Done();
        }

        private static MarkFigure dipole()
        {
            var s = new Sketch("dipole");

            s.Box(50, 95, 152, 105);
            s.Box(168, 95, 270, 105);
            s.Box(142, 95, 152, 155);
            s.Box(168, 95, 178, 155);

            s.Across("length", 50, 270, 60, 95, 95);
            s.Chip("length", 160, 60);

            s.Pinch("width", 240, 95, 240, 105);
            s.Chip("width", 240, 136);

            s.PinchAcross("gap", 152, 168, 172, 155, 155);
            s.Chip("gap", 222, 172);

            s.Upright("lead", 105, 155, 128, 142, 142);
            s.Chip("lead", 94, 130);

            return s.Done();
        }

        ///<summary>The two triangles point to point, the flare measured where their long edges would meet.</summary>
        private static MarkFigure bowtie()
        {
            var s = new Sketch("bowtie");
            double flare = Math.Tan(25 * Math.PI / 180);
            double wide = 5 + (102 * flare);

            s.Shape(168, 95, 270, 100 - wide, 270, 100 + wide, 168, 105);
            s.Shape(50, 100 - wide, 152, 95, 152, 105, 50, 100 + wide);

            s.Across("length", 50, 270, 178, 100 + wide, 100 + wide);
            s.Chip("length", 160, 178);

            s.Angle("angle", 168 - (5 / flare), 100, 40, -25, 25);
            s.Chip("angle", 236, 100);

            s.Pinch("gap", 152, 100, 168, 100);
            s.Chip("gap", 160, 66);

            s.Pinch("feed", 148, 95, 148, 105);
            s.Chip("feed", 160, 136);

            return s.Done();
        }

        ///<summary>Two arms of two turns each, the second the first turned half way round - Archimedean, as Marks winds them.</summary>
        private static MarkFigure spiralAntenna()
        {
            var s = new Sketch("spiralantenna");
            double growth = 12 / Math.PI;

            foreach (double start in new[] { 0, Math.PI })
            {
                var arm = new List<double>();

                for (int step = 0; step <= 160; step++)
                {
                    double angle = 4 * Math.PI * step / 160;
                    double reach = 12 + (growth * angle);

                    arm.Add(150 + (reach * Math.Cos(angle + start)));
                    arm.Add(104 - (reach * Math.Sin(angle + start)));
                }

                s.Band(6, Ends.Butt, arm.ToArray());
            }

            //From the middle out to where the first arm starts - inside the arms, so its box is outside them.
            s.Reach("inner", 150, 104, 162, 104);
            s.Chip("inner", 54, 40);

            s.Pinch("width", 150, 155, 150, 161);
            s.Chip("width", 150, 182);

            s.Pinch("spacing", 201, 104, 207, 104);
            s.Chip("spacing", 256, 104);

            s.Count("turns", 150 - (12 * 0.7071), 104 + (12 * 0.7071), 150 - (60 * 0.7071), 104 + (60 * 0.7071));
            s.Chip("turns", 54, 158);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Digital and chip ************************************************************

        ///<summary>An H with a smaller H on each of its four tips: two levels, numbered.</summary>
        private static MarkFigure hTree()
        {
            var s = new Sketch("htree");

            void h(double x, double y, double size)
            {
                double half = size / 2;

                s.Box(x - half - 2.5, y - 2.5, x + half + 2.5, y + 2.5);
                s.Box(x - half - 2.5, y - half - 2.5, x - half + 2.5, y + half + 2.5);
                s.Box(x + half - 2.5, y - half - 2.5, x + half + 2.5, y + half + 2.5);
            }

            h(160, 100, 80);

            foreach (double x in new double[] { 120, 200 })
            {
                foreach (double y in new double[] { 60, 140 })
                    h(x, y, 40);
            }

            s.Across("span", 120, 200, 182, 142.5, 142.5);
            s.Chip("span", 160, 182);

            s.Pinch("width", 217.5, 60, 222.5, 60);
            s.Chip("width", 266, 60);

            s.Tag("levels", 160, 91, "1");
            s.Tag("levels", 110, 132, "2");
            s.Chip("levels", 54, 100);

            return s.Done();
        }

        ///<summary>Four rails, and faintly the rows of cells that stand between them.</summary>
        private static MarkFigure rails()
        {
            var s = new Sketch("rails");
            var rows = new[] { new double[] { 24, 34, 18, 40, 26 }, new double[] { 30, 20, 44, 22, 28 }, new double[] { 18, 40, 26, 34, 24 } };

            s.Companion();

            for (int row = 0; row < rows.Length; row++)
            {
                double x = 79;
                double top = 53 + (row * 36);

                foreach (double wide in rows[row])
                {
                    s.Box(x, top, x + wide, top + 22);
                    x += wide + 4;
                }
            }

            s.Structure();

            for (int rail = 0; rail < 4; rail++)
                s.Box(75, 42 + (rail * 36), 245, 50 + (rail * 36));

            s.Count("rails", 40, 46, 40, 154);
            s.Chip("rails", 40, 100);

            s.Upright("pitch", 46, 82, 258, 245, 245);
            s.Chip("pitch", 290, 64);

            s.PinchUpright("width", 150, 158, 258, 245, 245);
            s.Chip("width", 290, 154);

            s.Across("length", 75, 245, 182, 158, 158);
            s.Chip("length", 160, 182);

            return s.Done();
        }

        ///<summary>Three pads along each side of a faint die, its corners clear.</summary>
        private static void padRing(Sketch s, double side)
        {
            foreach (double along in new double[] { -22, 0, 22 })
            {
                foreach (var (x, y) in new[] { (160 + along, 148.0), (208.0, 100 - along), (160 - along, 52.0), (112.0, 100 + along) })
                    s.Box(x - (side / 2), y - (side / 2), x + (side / 2), y + (side / 2));
            }
        }

        private static void padNumbers(Sketch s)
        {
            s.Across("die", 95, 225, 182, 165, 165);
            s.Chip("die", 160, 182);

            s.PinchUpright("pad", 71, 85, 248, 215, 215);
            s.Chip("pad", 282, 78);

            s.Upright("pitch", 93, 115, 72, 105, 105);
            s.Chip("pitch", 40, 104);

            s.Pinch("inset", 182, 35, 182, 45);
            s.Lead("inset", 186, 26, 226, 22);
            s.Chip("inset", 256, 20);
        }

        private static MarkFigure pads()
        {
            var s = new Sketch("pads");

            s.Companion();
            s.Box(95, 35, 225, 165);
            s.Structure();

            padRing(s, 14);
            padNumbers(s);

            return s.Done();
        }

        ///<summary>The openings, a margin inside faint pads.</summary>
        private static MarkFigure padOpenings()
        {
            var s = new Sketch("padopenings");

            s.Companion();
            s.Box(95, 35, 225, 165);
            padRing(s, 14);
            s.Structure();

            padRing(s, 8);
            padNumbers(s);

            s.Pinch("enclosure", 212, 122, 215, 122);
            s.Chip("enclosure", 264, 124);

            return s.Done();
        }

        ///<summary>Nine squares by six over a faint area.</summary>
        private static MarkFigure fill()
        {
            var s = new Sketch("fill");

            s.Companion();
            s.Box(85, 45, 235, 145);
            s.Structure();

            for (int row = 0; row < 6; row++)
            {
                for (int column = 0; column < 9; column++)
                    s.Box(91 + (column * 16), 50 + (row * 16), 101 + (column * 16), 60 + (row * 16));
            }

            s.Across("width", 85, 235, 170, 145, 145);
            s.Chip("width", 160, 170);

            s.Upright("height", 45, 145, 262, 235, 235);
            s.Chip("height", 262, 95);

            s.PinchAcross("square", 91, 101, 28, 50, 50);
            s.Chip("square", 46, 26);

            s.PinchUpright("spacing", 60, 66, 74, 91, 91);
            s.Chip("spacing", 40, 63);

            return s.Done();
        }

        #endregion **************************************************************************



        #region Drawing *********************************************************************

        private static double cos(double degrees)
        {
            return Math.Cos(degrees * Math.PI / 180);
        }

        private static double sin(double degrees)
        {
            return Math.Sin(degrees * Math.PI / 180);
        }

        ///<summary>How a band's ends are finished: flush with its last point, out half its width past it, or round.</summary>
        private enum Ends
        {
            Butt,
            Square,
            Round
        }

        ///
        ///One drawing being made: the structure, faintly what goes beside it, and a group of marks for each number.
        ///
        ///**The structure is drawn twice**, once in the outline's blue a little larger and once in the fill over it - so
        ///shapes that overlap, as an arm and its pad do, come out as one shape with one outline round the lot, which is
        ///what Marks merges them into.
        ///
        private sealed class Sketch
        {
            ///<summary>How far the outline stands out round a shape.</summary>
            private const double Outline = 1.2;

            private const double ArrowLength = 6;
            private const double ArrowHalf = 2.3;

            ///<summary>How far past a gap's two edges the arrows pointing in at them start.</summary>
            private const double Tail = 10;

            ///<summary>The space left between a shape and the extension line carried out from it, and how far that line runs past the dimension.</summary>
            private const double Clearance = 2;

            private const double Overshoot = 3;

            private readonly MarkMaker maker;
            private readonly StringBuilder companion = new StringBuilder();
            private readonly StringBuilder edges = new StringBuilder();
            private readonly StringBuilder fills = new StringBuilder();
            private readonly StringBuilder decor = new StringBuilder();
            private readonly Dictionary<string, StringBuilder> marks = new Dictionary<string, StringBuilder>();
            private readonly Dictionary<string, (int X, int Y)> chips = new Dictionary<string, (int X, int Y)>();
            private bool faint;

            public Sketch(string key)
            {
                maker = Marks.Named(key) ?? throw new ArgumentException($"There is no mark called {key}.");

                foreach (var setting in maker.Settings)
                    marks[setting.Name] = new StringBuilder();
            }

            ///<summary>What is drawn next is what another kind puts down beside this one, drawn faintly behind it.</summary>
            public void Companion()
            {
                faint = true;
            }

            ///<summary>What is drawn next is this kind's own structure.</summary>
            public void Structure()
            {
                faint = false;
            }

            public void Box(double left, double top, double right, double bottom)
            {
                shape($"<rect x=\"{n(left)}\" y=\"{n(top)}\" width=\"{n(right - left)}\" height=\"{n(bottom - top)}\"/>");
            }

            ///<summary>A polygon, its corners as x and y in turn.</summary>
            public void Shape(params double[] corners)
            {
                shape($"<polygon points=\"{points(corners)}\"/>");
            }

            public void Disc(double x, double y, double radius)
            {
                shape($"<circle cx=\"{n(x)}\" cy=\"{n(y)}\" r=\"{n(radius)}\"/>");
            }

            public void Ring(double x, double y, double inner, double outer)
            {
                shape($"<path fill-rule=\"evenodd\" d=\"{circle(x, y, outer)}{circle(x, y, inner)}\"/>");
            }

            ///<summary>A frame <paramref name="ring"/> wide whose outside is the box given - a guard ring.</summary>
            public void Rim(double left, double top, double right, double bottom, double ring)
            {
                string outside = $"M{n(left)} {n(top)}H{n(right)}V{n(bottom)}H{n(left)}Z";
                string inside = $"M{n(left + ring)} {n(top + ring)}H{n(right - ring)}V{n(bottom - ring)}H{n(left + ring)}Z";

                shape($"<path fill-rule=\"evenodd\" d=\"{outside}{inside}\"/>");
            }

            ///<summary>A bar <paramref name="width"/> wide along a line out of a middle at an angle, from one distance out to another.</summary>
            public void Radial(double x, double y, double degrees, double from, double to, double width)
            {
                double alongX = cos(degrees);
                double alongY = -sin(degrees);
                double half = width / 2;

                Shape(
                    x + (alongX * from) - (alongY * half), y + (alongY * from) + (alongX * half),
                    x + (alongX * to) - (alongY * half), y + (alongY * to) + (alongX * half),
                    x + (alongX * to) + (alongY * half), y + (alongY * to) - (alongX * half),
                    x + (alongX * from) + (alongY * half), y + (alongY * from) - (alongX * half));
            }

            ///<summary>A fan from a point, between two angles counterclockwise from the right.</summary>
            public void Fan(double x, double y, double radius, double from, double to)
            {
                var corners = new List<double> { x, y };

                for (int step = 0; step <= 30; step++)
                {
                    double angle = from + ((to - from) * step / 30);

                    corners.Add(x + (radius * cos(angle)));
                    corners.Add(y - (radius * sin(angle)));
                }

                Shape(corners.ToArray());
            }

            ///<summary>A ring cut through by a slot <paramref name="halfCut"/> either side of its middle, at the top or the bottom.</summary>
            public void CutRing(double x, double y, double inner, double outer, double halfCut, bool atTop)
            {
                double outerFrom = Math.Asin(halfCut / outer);
                double innerFrom = Math.Asin(halfCut / inner);
                double flip = 1;

                if (!atTop)
                    flip = -1;

                var corners = new List<double>();

                //Clockwise round the outside from the slot's right edge, then back round the inside.
                for (int step = 0; step <= 60; step++)
                {
                    double angle = outerFrom + (((2 * Math.PI) - (2 * outerFrom)) * step / 60);

                    corners.Add(x + (outer * Math.Sin(angle) * flip));
                    corners.Add(y - (outer * Math.Cos(angle) * flip));
                }

                for (int step = 60; step >= 0; step--)
                {
                    double angle = innerFrom + (((2 * Math.PI) - (2 * innerFrom)) * step / 60);

                    corners.Add(x + (inner * Math.Sin(angle) * flip));
                    corners.Add(y - (inner * Math.Cos(angle) * flip));
                }

                Shape(corners.ToArray());
            }

            ///<summary>A strip <paramref name="width"/> wide along a line of points, x and y in turn.</summary>
            public void Band(double width, Ends ends, params double[] line)
            {
                string finish = "";

                if (ends == Ends.Square)
                    finish = " stroke-linecap=\"square\"";
                else if (ends == Ends.Round)
                    finish = " stroke-linecap=\"round\" stroke-linejoin=\"round\"";

                if (faint)
                {
                    companion.Append($"<polyline points=\"{points(line)}\" fill=\"none\" stroke-width=\"{n(width)}\"{finish}/>");

                    return;
                }

                //A flush end has no outline of its own unless the outline's line runs on past it by the outline's width.
                double[] outlined = line;

                if (ends == Ends.Butt)
                    outlined = lengthened(line, Outline);

                edges.Append($"<polyline points=\"{points(outlined)}\" fill=\"none\" stroke-width=\"{n(width + (2 * Outline))}\"{finish}/>");
                fills.Append($"<polyline points=\"{points(line)}\" fill=\"none\" stroke-width=\"{n(width)}\"{finish}/>");
            }

            ///<summary>A frame round a part drawn again large, and a line from it to where that part is.</summary>
            public void Callout(double left, double top, double right, double bottom)
            {
                decor.Append($"<rect x=\"{n(left)}\" y=\"{n(top)}\" width=\"{n(right - left)}\" height=\"{n(bottom - top)}\" rx=\"4\"/>");
            }

            public void Pointer(double x1, double y1, double x2, double y2)
            {
                decor.Append($"<line x1=\"{n(x1)}\" y1=\"{n(y1)}\" x2=\"{n(x2)}\" y2=\"{n(y2)}\"/>");
                decor.Append($"<circle cx=\"{n(x2)}\" cy=\"{n(y2)}\" r=\"1.6\"/>");
            }

            ///<summary>A distance between two points: a line with an arrow out to each.</summary>
            public void Span(string name, double x1, double y1, double x2, double y2)
            {
                var to = marked(name);

                line(to, "", x1, y1, x2, y2);
                arrow(to, x1, y1, x1 - x2, y1 - y2);
                arrow(to, x2, y2, x2 - x1, y2 - y1);
            }

            ///<summary>A radius: from a middle, marked with a dot, out to an arrow.</summary>
            public void Reach(string name, double x1, double y1, double x2, double y2)
            {
                var to = marked(name);

                line(to, "", x1, y1, x2, y2);
                to.Append($"<circle class=\"markArrow\" cx=\"{n(x1)}\" cy=\"{n(y1)}\" r=\"1.8\"/>");
                arrow(to, x2, y2, x2 - x1, y2 - y1);
            }

            ///<summary>A gap too narrow for arrows inside it: arrows pointing in at its two edges from outside.</summary>
            public void Pinch(string name, double x1, double y1, double x2, double y2)
            {
                var to = marked(name);
                var (alongX, alongY) = unit(x2 - x1, y2 - y1);

                line(to, "", x1 - (alongX * Tail), y1 - (alongY * Tail), x2 + (alongX * Tail), y2 + (alongY * Tail));
                arrow(to, x1, y1, alongX, alongY);
                arrow(to, x2, y2, -alongX, -alongY);
            }

            ///<summary>A line carried out from an edge to a dimension.</summary>
            public void Extend(string name, double x1, double y1, double x2, double y2)
            {
                line(marked(name), " class=\"markExtend\"", x1, y1, x2, y2);
            }

            ///<summary>A thin line from a dimension to a box that could not sit on it.</summary>
            public void Lead(string name, double x1, double y1, double x2, double y2)
            {
                line(marked(name), " class=\"markLead\"", x1, y1, x2, y2);
            }

            ///<summary>A bracket over the things a count counts: a line with a tick across each end.</summary>
            public void Count(string name, double x1, double y1, double x2, double y2)
            {
                var to = marked(name);
                var (alongX, alongY) = unit(x2 - x1, y2 - y1);
                double tickX = -alongY * 4;
                double tickY = alongX * 4;

                line(to, "", x1, y1, x2, y2);
                line(to, "", x1 - tickX, y1 - tickY, x1 + tickX, y1 + tickY);
                line(to, "", x2 - tickX, y2 - tickY, x2 + tickX, y2 + tickY);
            }

            ///<summary>An angle: an arc round a point between two angles counterclockwise from the right, with an arrow at each end.</summary>
            public void Angle(string name, double x, double y, double radius, double from, double to)
            {
                var into = marked(name);
                double fromX = x + (radius * cos(from));
                double fromY = y - (radius * sin(from));
                double toX = x + (radius * cos(to));
                double toY = y - (radius * sin(to));
                int large = 0;

                if (Math.Abs(to - from) > 180)
                    large = 1;

                into.Append($"<path d=\"M{n(fromX)} {n(fromY)}A{n(radius)} {n(radius)} 0 {large} 0 {n(toX)} {n(toY)}\"/>");
                arrow(into, fromX, fromY, sin(from), cos(from));
                arrow(into, toX, toY, -sin(to), -cos(to));
            }

            ///<summary>A small number on the drawing - which level of a tree is which.</summary>
            public void Tag(string name, double x, double y, string text)
            {
                marked(name).Append($"<text class=\"markTag\" x=\"{n(x)}\" y=\"{n(y)}\">{WebUtility.HtmlEncode(text)}</text>");
            }

            ///<summary>A length along a path, dashed down its middle with an arrow at its end.</summary>
            public void Trace(string name, params double[] line)
            {
                var to = marked(name);
                int last = line.Length - 2;

                to.Append($"<polyline class=\"markTrace\" points=\"{points(line)}\"/>");
                arrow(to, line[last], line[last + 1], line[last] - line[last - 2], line[last + 1] - line[last - 1]);
            }

            ///<summary>A horizontal distance at height <paramref name="y"/>, carried down or up to it from the edges at <paramref name="from1"/> and <paramref name="from2"/>.</summary>
            public void Across(string name, double x1, double x2, double y, double from1, double from2)
            {
                extendDown(name, x1, from1, y);
                extendDown(name, x2, from2, y);
                Span(name, x1, y, x2, y);
            }

            ///<summary>A vertical distance at <paramref name="x"/>, carried out to it from the edges at <paramref name="from1"/> and <paramref name="from2"/>.</summary>
            public void Upright(string name, double y1, double y2, double x, double from1, double from2)
            {
                extendAcross(name, y1, from1, x);
                extendAcross(name, y2, from2, x);
                Span(name, x, y1, x, y2);
            }

            public void PinchAcross(string name, double x1, double x2, double y, double from1, double from2)
            {
                extendDown(name, x1, from1, y);
                extendDown(name, x2, from2, y);
                Pinch(name, x1, y, x2, y);
            }

            public void PinchUpright(string name, double y1, double y2, double x, double from1, double from2)
            {
                extendAcross(name, y1, from1, x);
                extendAcross(name, y2, from2, x);
                Pinch(name, x, y1, x, y2);
            }

            ///<summary>Where a number's box goes, centered.</summary>
            public void Chip(string name, int x, int y)
            {
                marked(name);

                chips[name] = (x, y);
            }

            public MarkFigure Done()
            {
                foreach (var setting in maker.Settings)
                {
                    if (!chips.ContainsKey(setting.Name))
                        throw new InvalidOperationException($"The drawing of the {maker.Name} has no box for {setting.Name}.");
                }

                var svg = new StringBuilder();

                svg.Append($"<svg class=\"markDrawing\" width=\"{MarkFigure.Width}\" height=\"{MarkFigure.Height}\" viewBox=\"0 0 {MarkFigure.Width} {MarkFigure.Height}\" aria-hidden=\"true\">");
                group(svg, "markCompanion", companion);
                group(svg, "markEdges", edges);
                group(svg, "markFills", fills);
                group(svg, "markDecor", decor);

                for (int index = 0; index < maker.Settings.Count; index++)
                    group(svg, $"markDim markDim{index}", marks[maker.Settings[index].Name]);

                svg.Append("</svg>");

                return new MarkFigure(svg.ToString(), chips);
            }

            private void shape(string element)
            {
                if (faint)
                {
                    companion.Append(element);

                    return;
                }

                edges.Append(element);
                fills.Append(element);
            }

            private StringBuilder marked(string name)
            {
                if (!marks.TryGetValue(name, out var to))
                    throw new ArgumentException($"The {maker.Name} has no setting called {name}.");

                return to;
            }

            private void extendDown(string name, double x, double from, double y)
            {
                double toward = Math.Sign(y - from);

                if (Math.Abs(y - from) <= Clearance + 1)
                    return;

                Extend(name, x, from + (toward * Clearance), x, y + (toward * Overshoot));
            }

            private void extendAcross(string name, double y, double from, double x)
            {
                double toward = Math.Sign(x - from);

                if (Math.Abs(x - from) <= Clearance + 1)
                    return;

                Extend(name, from + (toward * Clearance), y, x + (toward * Overshoot), y);
            }

            private static void group(StringBuilder svg, string name, StringBuilder content)
            {
                if (content.Length == 0)
                    return;

                svg.Append($"<g class=\"{name}\">");
                svg.Append(content);
                svg.Append("</g>");
            }

            private static void line(StringBuilder to, string kind, double x1, double y1, double x2, double y2)
            {
                to.Append($"<line{kind} x1=\"{n(x1)}\" y1=\"{n(y1)}\" x2=\"{n(x2)}\" y2=\"{n(y2)}\"/>");
            }

            ///<summary>An arrowhead with its point at the point given, pointing along the direction given.</summary>
            private static void arrow(StringBuilder to, double x, double y, double towardX, double towardY)
            {
                var (alongX, alongY) = unit(towardX, towardY);
                double backX = x - (alongX * ArrowLength);
                double backY = y - (alongY * ArrowLength);

                to.Append($"<path class=\"markArrow\" d=\"M{n(x)} {n(y)}L{n(backX - (alongY * ArrowHalf))} {n(backY + (alongX * ArrowHalf))}L{n(backX + (alongY * ArrowHalf))} {n(backY - (alongX * ArrowHalf))}Z\"/>");
            }

            private static (double X, double Y) unit(double x, double y)
            {
                double length = Math.Sqrt((x * x) + (y * y));

                if (length == 0)
                    return (1, 0);

                return (x / length, y / length);
            }

            ///<summary>The line with its first and last points moved out along it by a distance.</summary>
            private static double[] lengthened(double[] line, double by)
            {
                var longer = (double[])line.Clone();
                int last = line.Length - 2;
                var (startX, startY) = unit(line[0] - line[2], line[1] - line[3]);
                var (endX, endY) = unit(line[last] - line[last - 2], line[last + 1] - line[last - 1]);

                longer[0] += startX * by;
                longer[1] += startY * by;
                longer[last] += endX * by;
                longer[last + 1] += endY * by;

                return longer;
            }

            ///<summary>A whole circle as two half arcs, for a path.</summary>
            private static string circle(double x, double y, double radius)
            {
                return $"M{n(x - radius)} {n(y)}a{n(radius)} {n(radius)} 0 1 0 {n(2 * radius)} 0a{n(radius)} {n(radius)} 0 1 0 {n(-2 * radius)} 0Z";
            }

            private static string points(double[] corners)
            {
                var written = new StringBuilder();

                for (int index = 0; index + 1 < corners.Length; index += 2)
                {
                    if (index > 0)
                        written.Append(' ');

                    written.Append(n(corners[index]));
                    written.Append(',');
                    written.Append(n(corners[index + 1]));
                }

                return written.ToString();
            }

            ///<summary>A number as the drawing writes it: to two places, the same in any browser's language.</summary>
            private static string n(double value)
            {
                return value.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        #endregion **************************************************************************
    }
}
