using System.Globalization;

namespace GdsII
{
    ///<summary>
    ///One number a mark is drawn from: its name, what it means, and what it starts at.
    ///
    ///A length in microns unless <see cref="Whole"/> says it is a count or <see cref="Angle"/> that it is an angle in
    ///degrees - so whoever offers these to somebody can say which, and the maker turns the microns into the file's own
    ///units itself.
    ///</summary>
    public sealed record MarkSetting(string Name, string Says, double Start, bool Whole = false, bool Angle = false)
    {
        ///<summary>What the number is counted in, as a panel writes it after the box: µm, °, or nothing for a count.</summary>
        public string Unit
        {
            get
            {
                if (Whole)
                    return "";

                if (Angle)
                    return "°";

                return "µm";
            }
        }
    }

    ///<summary>What a mark came out as: its outlines, or why there are none.</summary>
    public sealed record MarkMade(List<List<Element.Point>> Outlines, string? Problem);

    ///<summary>
    ///One kind of mark: what it is called, what it is for, the numbers it is drawn from, and the drawing.
    ///</summary>
    public sealed class MarkMaker
    {
        private readonly Func<Func<string, double>, Func<double, int>, List<List<Element.Point>>> make;
        private readonly Func<Func<string, double>, string>? note;

        internal MarkMaker(
            string key,
            string name,
            string group,
            string says,
            IReadOnlyList<MarkSetting> settings,
            Func<Func<string, double>, Func<double, int>, List<List<Element.Point>>> make,
            string? family = null,
            string? placedOn = null,
            Func<Func<string, double>, string>? note = null)
        {
            Key = key;
            Name = name;
            Group = group;
            Says = says;
            Settings = settings;
            Family = family ?? key;
            PlacedOn = placedOn;

            this.make = make;
            this.note = note;
        }

        ///<summary>What it is known by in a session or a test, which no rewording of its name moves.</summary>
        public string Key { get; }

        public string Name { get; }

        ///<summary>Which kind of work it is for - alignment, test structures, analog, RF, antennas, photonics, microfluidics.</summary>
        public string Group { get; }

        public string Says { get; }

        public IReadOnlyList<MarkSetting> Settings { get; }

        ///
        ///The kinds whose numbers are one set, named by the key of the first of them: an alignment cross and the window
        ///it lands in, a spiral inductor and the underpass and vias that finish it. **A structure on several layers is a
        ///mark a layer** - see Marks - and its layers drawn from two sets of numbers would not meet, so a number typed for
        ///one of them is the number for every one of them that has it. A mark's own key when it shares with nothing.
        ///
        public string Family { get; }

        ///
        ///The key of the mark this one is drawn round the middle of, or null for one centered on its own. An inductor's
        ///underpass and vias are where the spiral's middle puts them, so the three put down at one point line up - and
        ///neither is centered on itself.
        ///
        public string? PlacedOn { get; }

        ///
        ///The mark, centered on the origin, in the file's own units.
        ///
        ///<paramref name="values"/> holds the settings by name - in microns for a length, as a count for a
        ///whole one - and anything it leaves out is taken at its start. <paramref name="units"/> turns microns
        ///into the file's units, which only the file can say.
        ///
        ///A setting that cannot make the mark - a ring narrower than its own waveguide, a frame whose line is
        ///wider than half of it - comes back as a problem rather than as a strange shape, since a mark drawn
        ///wrong on a mask is not one anybody finds until it is made. So does one that would be more outlines
        ///than one placement puts down - see <see cref="Marks.MostOutlines"/>.
        ///
        public MarkMade Make(IReadOnlyDictionary<string, double> values, Func<double, int> units)
        {
            foreach (var setting in Settings)
            {
                double value = valueIn(values, setting.Name);

                if (!(value > 0) || double.IsInfinity(value))
                    return refused($"{setting.Says}: it has to be more than nothing.");

                if (setting.Whole && (value != Math.Floor(value) || value > Marks.MostRepeats))
                    return refused($"{setting.Says}: it has to be a whole number, no more than {Marks.MostRepeats}.");
            }

            List<List<Element.Point>> outlines;

            try
            {
                outlines = make(name => valueIn(values, name), units);
            }
            catch (ArgumentException refusedWith)
            {
                return refused(refusedWith.Message);
            }

            if (outlines.Count > Marks.MostOutlines)
                return refused($"That comes to {outlines.Count} shapes, more than the {Marks.MostOutlines} one placement puts down: make it smaller, or coarser.");

            return new MarkMade(outlines, null);
        }

        ///
        ///What the numbers come to, for a kind that says - an inductor's inductance, a resistor's squares, a fill's
        ///density - or nothing. Worked out from the numbers alone, so a panel can say it as they are typed without drawing
        ///anything; nothing for numbers that cannot make the mark.
        ///
        public string NoteFor(IReadOnlyDictionary<string, double> values)
        {
            if (note is null)
                return "";

            foreach (var setting in Settings)
            {
                double value = valueIn(values, setting.Name);

                if (!(value > 0) || double.IsInfinity(value))
                    return "";
            }

            try
            {
                return note(name => valueIn(values, name));
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        private double valueIn(IReadOnlyDictionary<string, double> values, string name)
        {
            if (values.TryGetValue(name, out double given))
                return given;

            return Settings.First(setting => setting.Name == name).Start;
        }

        private static MarkMade refused(string why)
        {
            return new MarkMade(new List<List<Element.Point>>(), why);
        }
    }

    ///<summary>
    ///The structures a layout is made of besides its transistors: a frame round the die, the crosses a mask is aligned
    ///by, the structures a process is checked with, the passives of an analog or an RF circuit - inductors, capacitors,
    ///resistors, couplers, filters, resonators, antennas - a clock tree, pads and fill, and the first shapes of a
    ///photonic or a microfluidic chip. The first ten asked for 2026-10-21; the analog, RF, antenna, digital and chip
    ///ones 2026-11-01.
    ///
    ///**Generated rather than drawn**, because each is a handful of exact numbers: a cross is an arm length and
    ///a width, a vernier is a count and a pitch, a ring resonator is a radius, a width and a gap. Drawn by hand
    ///they are an afternoon of rectangles and a chance of one being a unit off; made from the numbers they are
    ///right, and can be made again at another size in a second.
    ///
    ///**Centered on the origin, on one layer, in the file's own units.** The 2D view carries what comes out like
    ///writing or a picture, so it can be put down anywhere, turned, and on whichever layer is being drawn on.
    ///A structure on several layers - an alignment cross and the window it lands in, a spiral inductor and the
    ///underpass and vias that bring its inner end out - is a mark a layer, made from one set of numbers (see
    ///<see cref="MarkMaker.Family"/>) and drawn round one middle (<see cref="MarkMaker.PlacedOn"/>), so its layers put
    ///down at one point line up.
    ///
    ///**What a mixer is made of, as far as one layer goes.** A mixer's core - a Gilbert cell, a diode ring - is
    ///transistors or diodes, many layers each under the process's own rules, and is a device rather than a mark. What is
    ///here is what surrounds it: the hybrids a balanced mixer splits and combines in (branch-line, rat-race) and the
    ///balun a double-balanced one is fed by (Marchand).
    ///
    ///**The starting numbers are a stated design, to be tuned rather than taped out.** The RF and antenna ones are a
    ///60 GHz line on 10 µm of oxide (εr 3.9), whose 50 Ω microstrip is 21 µm wide and whose quarter wave is about
    ///720 µm, by the textbook's closed forms rather than a field solver; the CPW resonator is a superconducting one on
    ///silicon; the analog ones use sky130's top metal and contacts where it has them.
    ///
    ///Every outline is hole-free, since GDSII has no holes: a ring comes out as a keyhole, the way
    ///<see cref="Booleans"/> cuts one.
    ///</summary>
    public static class Marks
    {
        #region Constants *******************************************************************

        ///<summary>The most bars, teeth, turns or groups a count may ask for - enough for any of them, and a ceiling on a typo.</summary>
        public const int MostRepeats = 1000;

        ///<summary>Corners on a ring that guides light: four times a drawn circle's, since a waveguide's sidewall roughness is the loss.</summary>
        private const int RingVertices = 256;

        ///
        ///The most outlines one mark puts down. Each is an element of its own, and a picture put down as 4,263 of them
        ///took twelve seconds - see MostPictureShapes in the 2D view. A fill or a tree that would be more is said, so its
        ///numbers can be made coarser, rather than left to stop the page.
        ///
        public const int MostOutlines = 2000;

        ///<summary>The most levels a clock tree is drawn to: 4,096 leaves, past which a tree is a clock router's to draw.</summary>
        private const int MostTreeLevels = 6;

        ///<summary>Corners a whole turn of an antenna's spiral gets, as many as a ring's, so its curve is as smooth.</summary>
        private const int TurnSteps = 256;

        ///<summary>The permeability of free space, in henries per meter - what every inductance here is a multiple of.</summary>
        private const double Mu0 = 4 * Math.PI * 1e-7;

        ///<summary>A coplanar line's effective permittivity on silicon: half of one more than silicon's 11.7, its field half in the silicon and half in the air.</summary>
        private const double SiliconPermittivity = 6.35;

        #endregion **************************************************************************



        #region The catalog *****************************************************************

        ///<summary>Every mark, in the order a picker lists them: alignment first, since a die starts there.</summary>
        public static IReadOnlyList<MarkMaker> All { get; } = new List<MarkMaker>
        {
            new MarkMaker(
                "frame",
                "Die frame",
                "Alignment",
                "A frame round a die - the scribe line it is cut along - as four bars that meet at the corners",
                new[]
                {
                    new MarkSetting("width", "The die's width, outside the frame", 5000),
                    new MarkSetting("height", "The die's height, outside the frame", 5000),
                    new MarkSetting("line", "How wide the frame is", 100)
                },
                (value, units) => DieFrame(units(value("width")), units(value("height")), units(value("line")))),

            new MarkMaker(
                "cross",
                "Alignment cross",
                "Alignment",
                "A plus a mask is aligned by, its two arms crossing at the middle",
                new[]
                {
                    new MarkSetting("arm", "Tip to tip", 100),
                    new MarkSetting("width", "How wide an arm is", 10)
                },
                (value, units) => new List<List<Element.Point>> { Cross(units(value("arm")), units(value("width"))) }),

            new MarkMaker(
                "window",
                "Alignment window",
                "Alignment",
                "Four squares round the space a cross of the same size lands in, with a gap all round it - for the other layer",
                new[]
                {
                    new MarkSetting("arm", "The cross's arm, tip to tip", 100),
                    new MarkSetting("width", "The cross's arm width", 10),
                    new MarkSetting("gap", "The gap left round the cross", 2)
                },
                (value, units) => Window(units(value("arm")), units(value("width")), units(value("gap"))),
                family: "cross"),

            new MarkMaker(
                "vernier",
                "Vernier scale",
                "Alignment",
                "A row of bars at one pitch, the middle one longer. One on each layer at pitches a little apart reads how far the two are out",
                new[]
                {
                    new MarkSetting("bars", "How many bars", 21, Whole: true),
                    new MarkSetting("width", "How wide a bar is", 2),
                    new MarkSetting("length", "How long a bar is", 20),
                    new MarkSetting("pitch", "Bar to bar", 4)
                },
                (value, units) => Vernier((int)value("bars"), units(value("width")), units(value("length")), units(value("pitch")))),

            new MarkMaker(
                "lines",
                "Lines and spaces",
                "Test structures",
                "Groups of five lines as wide as the spaces between them, each group narrower than the last - what a process resolves is the narrowest group that comes out",
                new[]
                {
                    new MarkSetting("widest", "The first group's line width", 10),
                    new MarkSetting("narrowest", "The last group's line width", 1),
                    new MarkSetting("groups", "How many groups", 6, Whole: true),
                    new MarkSetting("length", "How long a line is", 50)
                },
                (value, units) => LinesAndSpaces(units(value("widest")), units(value("narrowest")), (int)value("groups"), units(value("length")))),

            new MarkMaker(
                "greek",
                "Greek cross",
                "Test structures",
                "A cross with a pad on every arm, for a four-point measurement of a layer's sheet resistance",
                new[]
                {
                    new MarkSetting("width", "How wide an arm is", 10),
                    new MarkSetting("arm", "Middle to pad", 100),
                    new MarkSetting("pad", "A pad's side", 80)
                },
                (value, units) => new List<List<Element.Point>> { GreekCross(units(value("width")), units(value("arm")), units(value("pad"))) }),

            new MarkMaker(
                "comb",
                "Comb and serpentine",
                "Test structures",
                "A wire winding between the fingers of two combs, a spacing from both - a short between the wire and a comb is a defect bridging the spacing, and a break in the wire one cutting a line",
                new[]
                {
                    new MarkSetting("fingers", "How many fingers each comb has", 5, Whole: true),
                    new MarkSetting("width", "How wide the wire and the fingers are", 1),
                    new MarkSetting("spacing", "Between the wire and a finger", 1),
                    new MarkSetting("length", "How long a finger is", 50),
                    new MarkSetting("bus", "How wide a comb's spine is", 10)
                },
                (value, units) => CombAndSerpentine((int)value("fingers"), units(value("width")), units(value("spacing")), units(value("length")), units(value("bus")))),

            new MarkMaker(
                "ring",
                "Ring resonator",
                "Photonics",
                "A ring of waveguide beside a straight one, a gap apart - what couples light round the ring",
                new[]
                {
                    new MarkSetting("radius", "The ring's radius, to the middle of its waveguide", 10),
                    new MarkSetting("width", "How wide the waveguide is", 0.5),
                    new MarkSetting("gap", "Between the ring and the straight waveguide", 0.2),
                    new MarkSetting("bus", "How long the straight waveguide is", 30)
                },
                (value, units) => RingResonator(units(value("radius")), units(value("width")), units(value("gap")), units(value("bus")))),

            new MarkMaker(
                "grating",
                "Grating",
                "Photonics",
                "Teeth at one period across a waveguide - a grating that couples light in or out of the chip",
                new[]
                {
                    new MarkSetting("teeth", "How many teeth", 20, Whole: true),
                    new MarkSetting("period", "Tooth to tooth", 0.63),
                    new MarkSetting("tooth", "How wide a tooth is", 0.315),
                    new MarkSetting("length", "How long a tooth is", 10)
                },
                (value, units) => Grating((int)value("teeth"), units(value("period")), units(value("tooth")), units(value("length")))),

            new MarkMaker(
                "serpentine",
                "Serpentine channel",
                "Microfluidics",
                "A channel that runs back and forth, rounded at every turn - a mixer, or a length of channel in a small space",
                new[]
                {
                    new MarkSetting("width", "How wide the channel is", 100),
                    new MarkSetting("run", "How long one run is", 2000),
                    new MarkSetting("runs", "How many runs", 6, Whole: true),
                    new MarkSetting("pitch", "Run to run", 300)
                },
                (value, units) => new List<List<Element.Point>> { Serpentine(units(value("width")), units(value("run")), (int)value("runs"), units(value("pitch"))) }),

            new MarkMaker(
                "chamber",
                "Chamber",
                "Microfluidics",
                "A round chamber with a channel in and a channel out, each ending in a port to punch",
                new[]
                {
                    new MarkSetting("chamber", "The chamber's diameter", 1000),
                    new MarkSetting("width", "How wide a channel is", 100),
                    new MarkSetting("length", "How long each channel is", 1500),
                    new MarkSetting("port", "A port's diameter", 600)
                },
                (value, units) => Chamber(units(value("chamber")), units(value("width")), units(value("length")), units(value("port")))),

            new MarkMaker(
                "spiral",
                "Spiral inductor",
                "Analog",
                "A square spiral of a thick top metal with both ends out on its left: the outer one along its last turn, the inner one through an underpass on the metal below. Put the Inductor underpass and its vias down at the same point, on their own layers",
                inductorSettings(),
                (value, units) => SpiralInductor((int)value("turns"), units(value("width")), units(value("spacing")), units(value("inner")), units(value("lead"))),
                note: value => $"About {said(SpiralInductance(value("turns"), value("width"), value("spacing"), value("inner")) * 1e9)} nH, by Mohan's formula for a square spiral"),

            new MarkMaker(
                "underpass",
                "Inductor underpass",
                "Analog",
                "The bar that brings a spiral inductor's inner end out under its turns, on the metal below - from the spiral's numbers, put down at the same point as the spiral",
                inductorSettings(),
                (value, units) => InductorUnderpass((int)value("turns"), units(value("width")), units(value("spacing")), units(value("inner")), units(value("lead"))),
                family: "spiral",
                placedOn: "spiral"),

            new MarkMaker(
                "inductorvias",
                "Inductor vias",
                "Analog",
                "The vias at both ends of a spiral inductor's underpass, as many as fit the trace with a gap round each - from the spiral's numbers, put down at the same point as the spiral",
                inductorSettings(
                    new MarkSetting("via", "A via's side", 0.8),
                    new MarkSetting("gap", "Between vias, and from a via to the trace's edge", 0.8)),
                (value, units) => InductorVias((int)value("turns"), units(value("width")), units(value("spacing")), units(value("inner")), units(value("lead")), units(value("via")), units(value("gap"))),
                family: "spiral",
                placedOn: "spiral"),

            new MarkMaker(
                "loop",
                "Loop inductor",
                "Analog",
                "One octagonal turn with both leads out of its bottom side - the inductor of a millimeter-wave circuit, where a few hundred picohenries is all a tank wants and one turn needs no underpass",
                new[]
                {
                    new MarkSetting("diameter", "Across the octagon, side to side, outside the trace", 200),
                    new MarkSetting("width", "How wide the trace is", 10),
                    new MarkSetting("gap", "Between the two leads", 20),
                    new MarkSetting("lead", "How long the leads are", 100)
                },
                (value, units) => LoopInductor(units(value("diameter")), units(value("width")), units(value("gap")), units(value("lead"))),
                note: value => $"About {Math.Round(LoopInductance(value("diameter"), value("width")) * 1e12)} pH, as a round loop of the same area"),

            new MarkMaker(
                "idc",
                "Interdigitated capacitor",
                "Analog",
                "Two combs on one layer, each one's fingers between the other's - a capacitor from the edges alone: the RF one, and a mixed-signal chip's finger (MOM) capacitor at minimum width and spacing",
                new[]
                {
                    new MarkSetting("fingers", "How many fingers, both combs together", 10, Whole: true),
                    new MarkSetting("length", "How long a finger is", 100),
                    new MarkSetting("width", "How wide a finger is", 5),
                    new MarkSetting("gap", "Between neighboring fingers", 5),
                    new MarkSetting("end", "Between a finger's tip and the other comb", 5),
                    new MarkSetting("bus", "How wide a comb's spine is", 20)
                },
                (value, units) => InterdigitatedCapacitor((int)value("fingers"), units(value("length")), units(value("width")), units(value("gap")), units(value("end")), units(value("bus")))),

            new MarkMaker(
                "resistor",
                "Meander resistor",
                "Analog",
                "A strip of a resistive layer - poly, a diffusion - folded back and forth, with a square head at each end for its contacts",
                new[]
                {
                    new MarkSetting("width", "How wide the strip is", 2),
                    new MarkSetting("run", "How long one run is", 50),
                    new MarkSetting("runs", "How many runs", 6, Whole: true),
                    new MarkSetting("spacing", "Between one run and the next", 2),
                    new MarkSetting("head", "A contact head's side", 6)
                },
                (value, units) => MeanderResistor(units(value("width")), units(value("run")), (int)value("runs"), units(value("spacing")), units(value("head"))),
                note: value => $"About {said(ResistorSquares(value("width"), value("run"), value("runs"), value("spacing")))} squares between the heads, each corner counted as 0.56 of one - times the layer's sheet resistance"),

            new MarkMaker(
                "guard",
                "Guard ring",
                "Analog",
                "A ring round a device that takes up the carriers it would push into its neighbors - the same ring on the tap, the local interconnect and the metal, with Guard ring contacts between them from the same numbers",
                guardSettings(),
                (value, units) => GuardRing(units(value("width")), units(value("height")), units(value("ring")))),

            new MarkMaker(
                "guardcontacts",
                "Guard ring contacts",
                "Analog",
                "The contacts down the middle of a guard ring, all the way round - from the ring's numbers, put down at the same point",
                guardSettings(
                    new MarkSetting("contact", "A contact's side", 0.17),
                    new MarkSetting("pitch", "Contact to contact, at the least", 0.51)),
                (value, units) => GuardRingContacts(units(value("width")), units(value("height")), units(value("ring")), units(value("contact")), units(value("pitch"))),
                family: "guard"),

            new MarkMaker(
                "shield",
                "Patterned ground shield",
                "Analog",
                "Strips of poly or a low metal under an inductor, square to its turns, in four quarters that never close a loop - the substrate is shielded from the inductor's field, and no current runs round under it",
                new[]
                {
                    new MarkSetting("size", "The shield's side", 200),
                    new MarkSetting("bar", "How wide a strip is", 2),
                    new MarkSetting("slot", "Between strips", 1),
                    new MarkSetting("edge", "How wide the bar along each side is, that its strips hang from", 6)
                },
                (value, units) => PatternedGroundShield(units(value("size")), units(value("bar")), units(value("slot")), units(value("edge")))),

            new MarkMaker(
                "cpw",
                "Coplanar waveguide",
                "RF and microwave",
                "A signal line between two grounds on the same layer, a gap either side - its impedance set by its width and the gaps, with no ground plane under it",
                new[]
                {
                    new MarkSetting("length", "How long it is", 500),
                    new MarkSetting("signal", "How wide the signal line is", 20),
                    new MarkSetting("gap", "Between the signal line and each ground", 10),
                    new MarkSetting("ground", "How wide each ground is", 60)
                },
                (value, units) => CoplanarWaveguide(units(value("length")), units(value("signal")), units(value("gap")), units(value("ground")))),

            new MarkMaker(
                "cpwresonator",
                "CPW resonator",
                "RF and microwave",
                "A coplanar quarter-wave resonator wound back and forth, drawn as its two gaps - what is etched out of a superconducting ground plane - open at its start, where it couples, and shorted to the ground at its end",
                new[]
                {
                    new MarkSetting("length", "How long it is, along its middle", 4000),
                    new MarkSetting("width", "How wide the center line is", 10),
                    new MarkSetting("gap", "How wide each gap is", 6),
                    new MarkSetting("run", "How long one straight run is", 400),
                    new MarkSetting("pitch", "Run to run", 100)
                },
                (value, units) => CpwResonator(units(value("length")), units(value("width")), units(value("gap")), units(value("run")), units(value("pitch"))),
                note: value => $"A quarter wave this long on silicon (εeff about {said(SiliconPermittivity)}) resonates near {said(QuarterWaveGigahertz(value("length"), SiliconPermittivity))} GHz"),

            new MarkMaker(
                "branchline",
                "Branch-line coupler",
                "RF and microwave",
                "A square of four quarter-wave lines, the through arms wider than the branches, a port at every corner - a 90° hybrid, splitting a signal in two a quarter-cycle apart, as an image-reject or balanced mixer does",
                new[]
                {
                    new MarkSetting("quarter", "A side, between the middles of its arms - a quarter wavelength", 710),
                    new MarkSetting("through", "How wide the through arms are - for the ports' impedance over √2", 35),
                    new MarkSetting("branch", "How wide the branch arms are", 21),
                    new MarkSetting("port", "How wide a port line is", 21),
                    new MarkSetting("lead", "How long a port line is", 200)
                },
                (value, units) => BranchLineCoupler(units(value("quarter")), units(value("through")), units(value("branch")), units(value("port")), units(value("lead")))),

            new MarkMaker(
                "ratrace",
                "Rat-race coupler",
                "RF and microwave",
                "A ring a wavelength and a half round with four ports a quarter wave apart, the last pair three quarters - a 180° hybrid, whose sum and difference a single-balanced mixer takes its signal and oscillator in at",
                new[]
                {
                    new MarkSetting("radius", "The ring's radius, to the middle of its line", 700),
                    new MarkSetting("ring", "How wide the ring's line is - for the ports' impedance times √2", 11),
                    new MarkSetting("port", "How wide a port line is", 21),
                    new MarkSetting("lead", "How long a port line is", 200)
                },
                (value, units) => RatRaceCoupler(units(value("radius")), units(value("ring")), units(value("port")), units(value("lead")))),

            new MarkMaker(
                "balun",
                "Marchand balun",
                "RF and microwave",
                "A half-wave line over two quarter-wave lines, coupled across a gap - one signal in at the top left, two in opposite phase out between the bottom lines, the way a double-balanced mixer is fed. The bottom lines' outer ends go to ground",
                new[]
                {
                    new MarkSetting("quarter", "How long each coupled section is - a quarter wavelength", 720),
                    new MarkSetting("width", "How wide the lines are", 10),
                    new MarkSetting("gap", "Between the coupled lines", 5),
                    new MarkSetting("split", "Between the two bottom lines, where the outputs leave", 30),
                    new MarkSetting("lead", "How long the input and output lines are", 150)
                },
                (value, units) => MarchandBalun(units(value("quarter")), units(value("width")), units(value("gap")), units(value("split")), units(value("lead")))),

            new MarkMaker(
                "coupledfilter",
                "Coupled-line filter",
                "RF and microwave",
                "Half-wave resonators in a staircase, each coupled to the next along a quarter of its length across a gap - a band-pass filter. Every section alike here, where a designed one gives each section its own gap",
                new[]
                {
                    new MarkSetting("resonators", "How many resonators", 3, Whole: true),
                    new MarkSetting("quarter", "How long a coupled section is - a quarter wavelength", 720),
                    new MarkSetting("width", "How wide a line is", 20),
                    new MarkSetting("gap", "Between coupled lines", 10),
                    new MarkSetting("lead", "How far the input and output lines run on", 200)
                },
                (value, units) => CoupledLineFilter((int)value("resonators"), units(value("quarter")), units(value("width")), units(value("gap")), units(value("lead")))),

            new MarkMaker(
                "steppedfilter",
                "Stepped-impedance filter",
                "RF and microwave",
                "Narrow and wide sections of line in turn, each short against a wavelength - series inductance and shunt capacitance, a low-pass filter",
                new[]
                {
                    new MarkSetting("sections", "How many wide sections", 3, Whole: true),
                    new MarkSetting("wide", "How wide a wide section is", 100),
                    new MarkSetting("narrow", "How wide a narrow section is", 5),
                    new MarkSetting("wide run", "How long a wide section is", 120),
                    new MarkSetting("narrow run", "How long a narrow section is", 200),
                    new MarkSetting("port", "How wide the port lines are", 21),
                    new MarkSetting("lead", "How long the port lines are", 200)
                },
                (value, units) => SteppedImpedanceFilter((int)value("sections"), units(value("wide")), units(value("narrow")), units(value("wide run")), units(value("narrow run")), units(value("port")), units(value("lead")))),

            new MarkMaker(
                "radialstub",
                "Radial stub",
                "RF and microwave",
                "A fan at the end of a line - an open stub whose short at its frequency holds over a wider band than a straight one's, for a bias line or a match",
                new[]
                {
                    new MarkSetting("radius", "The fan's radius", 300),
                    new MarkSetting("angle", "How wide the fan opens", 60, Angle: true),
                    new MarkSetting("width", "How wide the line is", 21),
                    new MarkSetting("lead", "How long the line is", 200)
                },
                (value, units) => RadialStub(units(value("radius")), value("angle"), units(value("width")), units(value("lead")))),

            new MarkMaker(
                "srr",
                "Split-ring resonator",
                "RF and microwave",
                "Two rings one inside the other, each cut through, on opposite sides - a resonator far smaller than its wavelength, and the cell of a metamaterial",
                new[]
                {
                    new MarkSetting("radius", "The outer ring's radius, outside", 100),
                    new MarkSetting("width", "How wide each ring is", 10),
                    new MarkSetting("spacing", "Between the rings", 10),
                    new MarkSetting("split", "How wide the cuts are", 10)
                },
                (value, units) => SplitRingResonator(units(value("radius")), units(value("width")), units(value("spacing")), units(value("split")))),

            new MarkMaker(
                "patch",
                "Patch antenna",
                "Antennas",
                "A rectangle of metal over a ground plane, a little under half a guided wavelength long, fed by a line inset into a notch to where its impedance matches - it radiates off its two short edges",
                new[]
                {
                    new MarkSetting("width", "How wide the patch is", 1600),
                    new MarkSetting("length", "How long the patch is - a little under half a guided wavelength", 1265),
                    new MarkSetting("inset", "How far the feed reaches into the patch", 380),
                    new MarkSetting("notch", "The gap either side of the feed, inside the patch", 20),
                    new MarkSetting("feed", "How wide the feed line is", 21),
                    new MarkSetting("lead", "How long the feed line is, outside the patch", 500)
                },
                (value, units) => PatchAntenna(units(value("width")), units(value("length")), units(value("inset")), units(value("notch")), units(value("feed")), units(value("lead")))),

            new MarkMaker(
                "dipole",
                "Dipole antenna",
                "Antennas",
                "Two arms end to end, half a wavelength tip to tip, fed across the gap between them by two parallel lines",
                new[]
                {
                    new MarkSetting("length", "Tip to tip", 1250),
                    new MarkSetting("width", "How wide an arm is", 20),
                    new MarkSetting("gap", "Between the arms, where they are fed", 20),
                    new MarkSetting("lead", "How long the feed lines are", 300)
                },
                (value, units) => DipoleAntenna(units(value("length")), units(value("width")), units(value("gap")), units(value("lead")))),

            new MarkMaker(
                "bowtie",
                "Bowtie antenna",
                "Antennas",
                "Two triangles point to point - a dipole whose arms flare, which widens the band it works over",
                new[]
                {
                    new MarkSetting("length", "Tip to tip", 1250),
                    new MarkSetting("angle", "How far each triangle flares", 60, Angle: true),
                    new MarkSetting("gap", "Between the points, where it is fed", 20),
                    new MarkSetting("feed", "How wide each point is, cut off square", 20)
                },
                (value, units) => BowtieAntenna(units(value("length")), value("angle"), units(value("gap")), units(value("feed")))),

            new MarkMaker(
                "spiralantenna",
                "Spiral antenna",
                "Antennas",
                "Two arms wound round each other from the middle out, a spacing apart - an Archimedean spiral, circularly polarized over a band as wide as its inner and outer turns are apart",
                new[]
                {
                    new MarkSetting("turns", "How many turns each arm makes", 5, Whole: true),
                    new MarkSetting("width", "How wide an arm is", 10),
                    new MarkSetting("spacing", "Between the arms", 10),
                    new MarkSetting("inner", "How far from the middle the arms start", 30)
                },
                (value, units) => SpiralAntenna((int)value("turns"), units(value("width")), units(value("spacing")), units(value("inner")))),

            new MarkMaker(
                "htree",
                "Clock H-tree",
                "Digital",
                "An H with a smaller H on each of its four tips, level after level - a clock reaches every leaf over the same length of wire, so it arrives everywhere at once",
                new[]
                {
                    new MarkSetting("levels", "How many levels of H", 3, Whole: true),
                    new MarkSetting("span", "How wide the first H is", 1000),
                    new MarkSetting("width", "How wide the wire is", 4)
                },
                (value, units) => ClockHTree((int)value("levels"), units(value("span")), units(value("width"))),
                note: value => $"{said(Math.Pow(4, value("levels")))} leaves, each the same length of wire from the middle"),

            new MarkMaker(
                "rails",
                "Power rails",
                "Digital",
                "Rails a row apart, power and ground in turn, that rows of standard cells stand between - sky130's high-density cells are 2.72 µm tall, on 0.48 µm rails of met1",
                new[]
                {
                    new MarkSetting("rails", "How many rails", 6, Whole: true),
                    new MarkSetting("pitch", "Rail to rail - a row's height", 2.72),
                    new MarkSetting("width", "How wide a rail is", 0.48),
                    new MarkSetting("length", "How long the rails are", 100)
                },
                (value, units) => PowerRails((int)value("rails"), units(value("pitch")), units(value("width")), units(value("length")))),

            new MarkMaker(
                "pads",
                "Pad ring",
                "Chip",
                "Bond pads in a row along every side of a die, its corners left clear - with Pad openings from the same numbers on the passivation's layer",
                padSettings(),
                (value, units) => PadRing(units(value("die")), units(value("pad")), units(value("pitch")), units(value("inset"))),
                note: value => padsSaid(PadsPerSide(value("die"), value("pad"), value("pitch"), value("inset")))),

            new MarkMaker(
                "padopenings",
                "Pad openings",
                "Chip",
                "The windows in the passivation over a pad ring's pads, a margin inside each - from the ring's numbers, put down at the same point",
                padSettings(new MarkSetting("enclosure", "How far inside its pad an opening stops", 5)),
                (value, units) => PadOpenings(units(value("die")), units(value("pad")), units(value("pitch")), units(value("inset")), units(value("enclosure"))),
                family: "pads"),

            new MarkMaker(
                "fill",
                "Dummy fill",
                "Chip",
                "Squares in a grid over an area, to bring a layer's density up to what polishing it flat needs",
                new[]
                {
                    new MarkSetting("width", "The area's width", 100),
                    new MarkSetting("height", "The area's height", 100),
                    new MarkSetting("square", "A square's side", 2),
                    new MarkSetting("spacing", "Between squares", 2)
                },
                (value, units) => DummyFill(units(value("width")), units(value("height")), units(value("square")), units(value("spacing"))),
                note: value => fillSaid(value("width"), value("height"), value("square"), value("spacing")))
        };

        ///<summary>The maker with this key, or null.</summary>
        public static MarkMaker? Named(string key)
        {
            return All.FirstOrDefault(maker => maker.Key == key);
        }

        ///<summary>A spiral inductor's numbers, which its underpass and vias are drawn from too - and any of their own after them.</summary>
        private static MarkSetting[] inductorSettings(params MarkSetting[] more)
        {
            var settings = new List<MarkSetting>
            {
                new MarkSetting("turns", "How many turns", 3, Whole: true),
                new MarkSetting("width", "How wide the trace is", 10),
                new MarkSetting("spacing", "Between one turn and the next", 5),
                new MarkSetting("inner", "The hole in the middle, across", 100),
                new MarkSetting("lead", "How far the ends reach out past the last turn", 50)
            };

            settings.AddRange(more);

            return settings.ToArray();
        }

        ///<summary>A guard ring's numbers, which its contacts are drawn from too, and theirs after them.</summary>
        private static MarkSetting[] guardSettings(params MarkSetting[] more)
        {
            var settings = new List<MarkSetting>
            {
                new MarkSetting("width", "The space inside the ring, across", 20),
                new MarkSetting("height", "The space inside the ring, up and down", 10),
                new MarkSetting("ring", "How wide the ring is", 0.6)
            };

            settings.AddRange(more);

            return settings.ToArray();
        }

        ///<summary>A pad ring's numbers, which its openings are drawn from too, and theirs after them.</summary>
        private static MarkSetting[] padSettings(params MarkSetting[] more)
        {
            var settings = new List<MarkSetting>
            {
                new MarkSetting("die", "The die's side", 2000),
                new MarkSetting("pad", "A pad's side", 80),
                new MarkSetting("pitch", "Pad to pad", 120),
                new MarkSetting("inset", "From the die's edge to the pads", 50)
            };

            settings.AddRange(more);

            return settings.ToArray();
        }

        #endregion **************************************************************************



        #region Alignment *******************************************************************

        ///
        ///A die frame: four bars, the outside <paramref name="width"/> by <paramref name="height"/>,
        ///<paramref name="line"/> wide.
        ///
        ///**Four bars rather than one ring**, because a ring is a shape with a hole and GDSII has none - it
        ///would have to be a keyhole, a channel cut through the frame. The top and bottom bars run the full
        ///width and the sides fit between them, so they meet without overlapping.
        ///
        public static List<List<Element.Point>> DieFrame(int width, int height, int line)
        {
            if (line * 2 >= width || line * 2 >= height)
                throw new ArgumentException("The frame is wider than half the die, so there is no die inside it.");

            int left = -(width / 2);
            int bottom = -(height / 2);
            int right = left + width;
            int top = bottom + height;

            return new List<List<Element.Point>>
            {
                Shapes.Between(left, bottom, right, bottom + line),
                Shapes.Between(left, top - line, right, top),
                Shapes.Between(left, bottom + line, left + line, top - line),
                Shapes.Between(right - line, bottom + line, right, top - line)
            };
        }

        ///<summary>A plus, <paramref name="arm"/> tip to tip, each arm <paramref name="width"/> wide: one outline of twelve corners.</summary>
        public static List<Element.Point> Cross(int arm, int width)
        {
            if (width >= arm)
                throw new ArgumentException("An arm as wide as the cross is long is a square, not a cross.");

            int far = arm / 2;
            int near = width / 2;
            int nearHigh = width - near;

            return new List<Element.Point>
            {
                new Element.Point(-near, -far),
                new Element.Point(nearHigh, -far),
                new Element.Point(nearHigh, -near),
                new Element.Point(far, -near),
                new Element.Point(far, nearHigh),
                new Element.Point(nearHigh, nearHigh),
                new Element.Point(nearHigh, far),
                new Element.Point(-near, far),
                new Element.Point(-near, nearHigh),
                new Element.Point(-far, nearHigh),
                new Element.Point(-far, -near),
                new Element.Point(-near, -near)
            };
        }

        ///
        ///The four squares round where a cross of the same size lands, <paramref name="gap"/> clear of it.
        ///
        ///Each fills its quarter of the cross's square, less the gap from both arms - so a cross on one layer
        ///and this on the other, lined up, leave a gap of exactly that all the way round the cross.
        ///
        public static List<List<Element.Point>> Window(int arm, int width, int gap)
        {
            int far = arm / 2;
            int inner = (width / 2) + gap;

            if (inner >= far)
                throw new ArgumentException("The arms and the gap fill the whole cross, so there is no room for the squares.");

            return new List<List<Element.Point>>
            {
                Shapes.Between(-far, -far, -inner, -inner),
                Shapes.Between(inner, -far, far, -inner),
                Shapes.Between(inner, inner, far, far),
                Shapes.Between(-far, inner, -inner, far)
            };
        }

        ///<summary>
        ///<paramref name="bars"/> bars <paramref name="width"/> wide at <paramref name="pitch"/>, standing on one
        ///line, the middle one half as long again so the eye has somewhere to start counting from.
        ///</summary>
        public static List<List<Element.Point>> Vernier(int bars, int width, int length, int pitch)
        {
            if (width >= pitch)
                throw new ArgumentException("A bar as wide as the pitch leaves no space between bars.");

            var made = new List<List<Element.Point>>();
            int middle = bars / 2;
            long across = (long)(bars - 1) * pitch;
            int start = -(int)(across / 2) - (width / 2);

            //Standing on one line, which is placed so the whole of it - the long bar included - is centered.
            int longest = length + (length / 2);
            int bottom = -(longest / 2);

            for (int bar = 0; bar < bars; bar++)
            {
                int x = start + (bar * pitch);
                int tall = length;

                if (bar == middle)
                    tall = longest;

                made.Add(Shapes.Between(x, bottom, x + width, bottom + tall));
            }

            return made;
        }

        #endregion **************************************************************************



        #region Test structures *************************************************************

        ///
        ///Groups of five lines at equal line and space, from <paramref name="widest"/> down to
        ///<paramref name="narrowest"/> in steps that keep each group the same fraction narrower than the last.
        ///
        ///**A ratio rather than a difference**, because resolution is: a group a quarter narrower is the same
        ///test at a micron as at ten. A group's width is rounded to whole units and two groups that round to
        ///the same width are drawn once.
        ///
        public static List<List<Element.Point>> LinesAndSpaces(int widest, int narrowest, int groups, int length)
        {
            if (narrowest > widest)
                throw new ArgumentException("The narrowest group is wider than the widest.");

            var widths = new List<int>();

            for (int group = 0; group < groups; group++)
            {
                double step = 0;

                if (groups > 1)
                    step = group / (double)(groups - 1);

                int width = (int)Math.Round(widest * Math.Pow(narrowest / (double)widest, step));

                if (width > 0 && (widths.Count == 0 || widths[^1] != width))
                    widths.Add(width);
            }

            var made = new List<List<Element.Point>>();
            long x = 0;

            foreach (int width in widths)
            {
                for (int line = 0; line < 5; line++)
                {
                    made.Add(Shapes.Between((int)x, 0, (int)x + width, length));
                    x += width * 2;
                }

                //The space after a group is twice the widest line, so groups read as groups.
                x += widest * 2;
            }

            //Back to the middle, the way every other mark sits.
            long across = x - (widest * 2) - widths[^1];

            return shifted(made, -(int)(across / 2), -(length / 2));
        }

        ///
        ///A cross <paramref name="width"/> wide with a square pad of <paramref name="pad"/> on the end of each
        ///arm, <paramref name="arm"/> from the middle - one outline.
        ///
        public static List<Element.Point> GreekCross(int width, int arm, int pad)
        {
            if (width >= pad)
                throw new ArgumentException("An arm as wide as its pad has no pad on the end of it.");

            if (arm <= width)
                throw new ArgumentException("The arms are shorter than they are wide, so the pads would overlap.");

            var parts = new List<IReadOnlyList<Element.Point>>
            {
                Shapes.Rectangle(0, 0, (arm * 2), width),
                Shapes.Rectangle(0, 0, width, (arm * 2)),
                Shapes.Rectangle(arm + (pad / 2), 0, pad, pad),
                Shapes.Rectangle(-arm - (pad / 2), 0, pad, pad),
                Shapes.Rectangle(0, arm + (pad / 2), pad, pad),
                Shapes.Rectangle(0, -arm - (pad / 2), pad, pad)
            };

            return Booleans.Merge(parts)[0];
        }

        ///
        ///A wire <paramref name="width"/> wide winding up and down between the fingers of two combs, each with
        ///<paramref name="fingers"/> fingers <paramref name="length"/> long hanging from a spine <paramref name="bus"/> wide -
        ///every edge of the wire a <paramref name="spacing"/> from a comb, the wire's two ends out past the spines for probes.
        ///
        ///**The wire's runs are two pitches of line and space apart**, so a finger fits between each pair with a spacing on
        ///either side. A pair joined at the top leaves a gap open at the bottom, which the lower comb's finger comes up into,
        ///and the next pair is the other way up - so the wire passes every finger of both combs on both sides.
        ///
        public static List<List<Element.Point>> CombAndSerpentine(int fingers, int width, int spacing, int length, int bus)
        {
            if (length <= width + spacing)
                throw new ArgumentException("Fingers no longer than the wire is wide and spaced leave nothing for the wire to wind round.");

            long pitch = 2L * (width + spacing);
            int runs = (2 * fingers) + 1;

            fits(runs * pitch);

            //From a run's end to the comb across from it, and on past that comb's spine - as far as the wire's ends go.
            int clear = (width / 2) + spacing;
            int reach = clear + bus;

            var line = new List<Element.Point> { new Element.Point(0, -reach) };

            for (int run = 0; run < runs; run++)
            {
                int x = (int)(run * pitch);
                int to = 0;

                if (run % 2 == 0)
                    to = length;

                if (run == runs - 1)
                    to = length + reach;

                if (run > 0)
                    line.Add(new Element.Point(x, line[^1].Y));

                line.Add(new Element.Point(x, to));
            }

            int half = (int)(pitch / 2);
            int fingerLow = -(width / 2);
            var low = new List<IReadOnlyList<Element.Point>>();
            var high = new List<IReadOnlyList<Element.Point>>();

            for (int gap = 0; gap < runs - 1; gap++)
            {
                int left = (int)(gap * pitch) + half + fingerLow;

                if (gap % 2 == 0)
                    low.Add(Shapes.Between(left, -reach, left + width, length - clear));
                else
                    high.Add(Shapes.Between(left, clear, left + width, length + reach));
            }

            //The spines, from the first finger of each comb to its last.
            low.Add(Shapes.Between(half + fingerLow, -reach, (int)((runs - 3) * pitch) + half + fingerLow + width, -clear));
            high.Add(Shapes.Between((int)pitch + half + fingerLow, length + clear, (int)((runs - 2) * pitch) + half + fingerLow + width, length + reach));

            var made = new List<List<Element.Point>> { counterclockwise(PathOutline.Build(line, width, 0, 0, 0)) };

            made.AddRange(Booleans.Merge(low));
            made.AddRange(Booleans.Merge(high));

            return centered(made);
        }

        #endregion **************************************************************************



        #region Photonics *******************************************************************

        ///
        ///A ring of waveguide of <paramref name="radius"/> to its middle and <paramref name="width"/> wide, and a
        ///straight waveguide <paramref name="bus"/> long under it with <paramref name="gap"/> between the two.
        ///
        ///The ring is a keyhole, cut by <see cref="Booleans"/> where it is narrowest to the eye - a channel of no
        ///width that a mask writer draws as nothing. The ring is centered on the origin.
        ///
        public static List<List<Element.Point>> RingResonator(int radius, int width, int gap, int bus)
        {
            if (width >= radius * 2)
                throw new ArgumentException("A waveguide that wide has no ring inside it.");

            int outer = radius + (width / 2);
            int inner = radius - (width - (width / 2));

            var ring = Booleans.Combine(
                new[] { Shapes.Circle(0, 0, outer, RingVertices) },
                new[] { Shapes.Circle(0, 0, inner, RingVertices) },
                BooleanOperation.Not);

            int below = -outer - gap;

            ring.Add(Shapes.Between(-(bus / 2), below - width, bus - (bus / 2), below));

            return ring;
        }

        ///<summary>
        ///<paramref name="teeth"/> teeth <paramref name="tooth"/> wide at <paramref name="period"/>,
        ///<paramref name="length"/> long, side by side across the middle.
        ///</summary>
        public static List<List<Element.Point>> Grating(int teeth, int period, int tooth, int length)
        {
            if (tooth >= period)
                throw new ArgumentException("A tooth as wide as the period leaves no gap between teeth.");

            var made = new List<List<Element.Point>>();
            long across = ((long)(teeth - 1) * period) + tooth;
            int start = -(int)(across / 2);

            for (int each = 0; each < teeth; each++)
            {
                int x = start + (each * period);

                made.Add(Shapes.Between(x, -(length / 2), x + tooth, length - (length / 2)));
            }

            return made;
        }

        #endregion **************************************************************************



        #region Microfluidics ***************************************************************

        ///
        ///A channel <paramref name="width"/> wide running back and forth <paramref name="runs"/> times, each run
        ///<paramref name="run"/> long and <paramref name="pitch"/> from the next.
        ///
        ///Drawn as a path down its middle and then rounded at every turn, outside and in, to a radius of half
        ///the channel's width - a square turn traps bubbles, and a mixer that does is not mixing.
        ///
        public static List<Element.Point> Serpentine(int width, int run, int runs, int pitch)
        {
            if (width >= pitch)
                throw new ArgumentException("A channel as wide as the pitch runs into the next run.");

            var centerline = new List<Element.Point>();

            for (int each = 0; each < runs; each++)
            {
                int y = each * pitch;

                if (each % 2 == 0)
                {
                    centerline.Add(new Element.Point(0, y));
                    centerline.Add(new Element.Point(run, y));
                }
                else
                {
                    centerline.Add(new Element.Point(run, y));
                    centerline.Add(new Element.Point(0, y));
                }
            }

            var outline = PathOutline.Build(centerline, width, 0, 0, 0);
            var rounded = Corners.Round(outline, width / 2, 8);
            var box = Bounds.Of(rounded);

            return shifted(new List<List<Element.Point>> { rounded }, -(int)(((long)box.Left + box.Right) / 2), -(int)(((long)box.Bottom + box.Top) / 2))[0];
        }

        ///
        ///A round chamber of <paramref name="chamber"/> across, with a channel <paramref name="width"/> wide
        ///and <paramref name="length"/> long out of each side, ending in a port of <paramref name="port"/>.
        ///One outline, the chamber in the middle.
        ///
        public static List<List<Element.Point>> Chamber(int chamber, int width, int length, int port)
        {
            if (width >= chamber || width >= port)
                throw new ArgumentException("A channel as wide as the chamber or the port it joins is not a channel.");

            int reach = (chamber / 2) + length;

            var parts = new List<IReadOnlyList<Element.Point>>
            {
                Shapes.Circle(0, 0, chamber / 2),
                Shapes.Rectangle(0, 0, reach * 2, width),
                Shapes.Circle(-reach, 0, port / 2),
                Shapes.Circle(reach, 0, port / 2)
            };

            return Booleans.Merge(parts);
        }

        #endregion **************************************************************************



        #region Analog **********************************************************************

        ///<summary>A spiral's metal, and its inner end and the landing its underpass comes up to - all where the metal's middle puts them.</summary>
        private sealed record Inductor(List<List<Element.Point>> Metal, Element.Point Inner, Element.Point Exit);

        ///
        ///A square spiral inductor: <paramref name="turns"/> turns of a trace <paramref name="width"/> wide, a
        ///<paramref name="spacing"/> apart, round a hole <paramref name="inner"/> across - with the landing on the same layer
        ///that its underpass comes up to, out on the left. Centered on the two together; see <see cref="inductor"/>.
        ///
        public static List<List<Element.Point>> SpiralInductor(int turns, int width, int spacing, int inner, int lead)
        {
            return inductor(turns, width, spacing, inner, lead).Metal;
        }

        ///
        ///The underpass of the spiral the same numbers make, where that spiral's middle puts it: a bar the trace's width,
        ///from under the landing on the left to under the inner end, crossing beneath every turn on the way.
        ///
        public static List<List<Element.Point>> InductorUnderpass(int turns, int width, int spacing, int inner, int lead)
        {
            var spiral = inductor(turns, width, spacing, inner, lead);
            int low = width / 2;

            return new List<List<Element.Point>>
            {
                Shapes.Between(spiral.Exit.X - low, spiral.Inner.Y - low, spiral.Inner.X - low + width, spiral.Inner.Y - low + width)
            };
        }

        ///
        ///The vias at both ends of that underpass: squares <paramref name="via"/> on a side, as many to a row as fit across
        ///the trace with <paramref name="gap"/> between them and round them, in a square block at each end.
        ///
        public static List<List<Element.Point>> InductorVias(int turns, int width, int spacing, int inner, int lead, int via, int gap)
        {
            long pitch = (long)via + gap;
            int across = (int)((width - gap) / pitch);

            if (across < 1)
                throw new ArgumentException("Not one via fits across the trace with a gap round it: a smaller via or gap, or a wider trace.");

            var spiral = inductor(turns, width, spacing, inner, lead);
            var made = new List<List<Element.Point>>();

            foreach (var end in new[] { spiral.Inner, spiral.Exit })
                made.AddRange(block(end, across, via, pitch));

            return made;
        }

        ///
        ///**Turn k is a square of half-side h(k)** = (inner + width) / 2 + k × (width + spacing): up its left side, along
        ///the top, down the right side and back along the bottom - past its own left side to the next square's, which is
        ///how a spiral steps out. Each segment is a pitch from the ones beside it, and the outside comes to inner + 2 ×
        ///turns × width + 2 × (turns - 1) × spacing across, the outer diameter the inductance formulas are written for.
        ///
        ///**The inner end starts a pitch above the bottom of the first square**, so it is a spacing clear of the turn that
        ///runs under it. Both ends leave on the left: the outer one along the bottom of the last turn, `lead` past it; the
        ///inner one by the underpass to a landing as far out, higher by a pitch for every turn, so the two stay clear of
        ///each other however many turns there are. The ends are squared off half the trace's width past their points, so
        ///the inner one is a whole square for its vias.
        ///
        private static Inductor inductor(int turns, int width, int spacing, int inner, int lead)
        {
            if (inner <= spacing || inner < width)
                throw new ArgumentException("The hole in the middle is narrower than the trace, or than the spacing between turns, so there is no room for the inner end.");

            if (lead <= width)
                throw new ArgumentException("Ends that reach out no further than the trace is wide put the underpass's landing against the last turn.");

            long pitch = (long)width + spacing;
            long first = ((long)inner + width) / 2;
            long last = first + ((turns - 1) * pitch);

            fits(2 * (last + lead + width));

            int half(int turn)
            {
                return (int)(first + (turn * pitch));
            }

            var line = new List<Element.Point> { new Element.Point(-half(0), (int)(pitch - first)) };

            for (int turn = 0; turn < turns; turn++)
            {
                int side = half(turn);

                line.Add(new Element.Point(-side, side));
                line.Add(new Element.Point(side, side));
                line.Add(new Element.Point(side, -side));

                if (turn < turns - 1)
                    line.Add(new Element.Point(-half(turn + 1), -side));
                else
                    line.Add(new Element.Point(-side - lead, -side));
            }

            var innerEnd = line[0];
            var exit = new Element.Point(-(int)last - lead, innerEnd.Y);

            var metal = new List<List<Element.Point>>
            {
                counterclockwise(PathOutline.Build(line, width, 2, 0, 0)),
                Shapes.Rectangle(exit.X, exit.Y, width, width)
            };

            var box = Bounds.Of(metal.SelectMany(outline => outline));
            int byX = -(int)(((long)box.Left + box.Right) / 2);
            int byY = -(int)(((long)box.Bottom + box.Top) / 2);

            return new Inductor(
                shifted(metal, byX, byY),
                new Element.Point(innerEnd.X + byX, innerEnd.Y + byY),
                new Element.Point(exit.X + byX, exit.Y + byY));
        }

        ///
        ///A square spiral's inductance in henries, from microns, by Mohan's modified Wheeler formula: 2.34 μ0 n² d / (1 +
        ///2.75 ρ), where d is the mean of the outer and inner diameters and ρ their difference over their sum - within a
        ///few percent of a field solver over the spirals people draw. Mohan, Hershenson, Boyd and Lee, "Simple accurate
        ///expressions for planar spiral inductances", IEEE JSSC, 1999.
        ///
        public static double SpiralInductance(double turns, double width, double spacing, double inner)
        {
            double outer = inner + (2 * turns * width) + (2 * (turns - 1) * spacing);
            double mean = (outer + inner) / 2;
            double fill = (outer - inner) / (outer + inner);

            return 2.34 * Mu0 * turns * turns * (mean * 1e-6) / (1 + (2.75 * fill));
        }

        ///
        ///One octagonal turn <paramref name="diameter"/> across its flats, outside a trace <paramref name="width"/> wide,
        ///opened in the middle of its bottom side for two leads <paramref name="gap"/> apart and <paramref name="lead"/>
        ///long. The flats are top, bottom and sides, so the leads leave square to the one they come out of.
        ///
        public static List<List<Element.Point>> LoopInductor(int diameter, int width, int gap, int lead)
        {
            double apothem = (diameter - width) / 2.0;
            double corner = apothem / Math.Cos(Math.PI / 8);
            double leadX = (gap + width) / 2.0;

            if (leadX + (width / 2.0) > apothem * Math.Tan(Math.PI / 8))
                throw new ArgumentException("The leads and the gap between them are wider than the octagon's bottom side.");

            var line = new List<Element.Point> { at(leadX, -apothem - lead), at(leadX, -apothem) };

            //Round from the right end of the bottom side, counterclockwise, to its left end.
            for (int each = 0; each < 8; each++)
            {
                double angle = (-Math.PI / 2) + (Math.PI / 8) + (each * Math.PI / 4);

                line.Add(at(corner * Math.Cos(angle), corner * Math.Sin(angle)));
            }

            line.Add(at(-leadX, -apothem));
            line.Add(at(-leadX, -apothem - lead));

            return centered(new List<List<Element.Point>> { PathOutline.Build(line, width, 0, 0, 0) });
        }

        ///
        ///A single loop's inductance in henries, from microns, as a round loop of the same area: μ0 R (ln(8R / a) - 2), the
        ///strip taken as a round wire of radius a = 0.2235 × its width, a thin strip's geometric mean distance. Rough - tens
        ///of percent - which is what a single turn's leads leave any formula anyway.
        ///
        public static double LoopInductance(double diameter, double width)
        {
            double apothem = (diameter - width) / 2;
            double radius = apothem * Math.Sqrt(8 * Math.Tan(Math.PI / 8) / Math.PI);
            double wire = 0.2235 * width;

            return Mu0 * (radius * 1e-6) * (Math.Log(8 * radius / wire) - 2);
        }

        ///
        ///<paramref name="fingers"/> fingers <paramref name="width"/> wide and <paramref name="length"/> long,
        ///<paramref name="gap"/> apart, taken in turn from a spine <paramref name="bus"/> wide along the bottom and one
        ///along the top, each finger's tip <paramref name="end"/> short of the other spine - two outlines, the two plates.
        ///
        public static List<List<Element.Point>> InterdigitatedCapacitor(int fingers, int length, int width, int gap, int end, int bus)
        {
            if (fingers < 2)
                throw new ArgumentException("A capacitor needs a finger on each plate, so two at the least.");

            long pitch = (long)width + gap;
            long across = ((fingers - 1) * pitch) + width;

            fits(across);

            int top = bus + length + end;
            var low = new List<IReadOnlyList<Element.Point>> { Shapes.Between(0, 0, (int)across, bus) };
            var high = new List<IReadOnlyList<Element.Point>> { Shapes.Between(0, top, (int)across, top + bus) };

            for (int finger = 0; finger < fingers; finger++)
            {
                int x = (int)(finger * pitch);

                if (finger % 2 == 0)
                    low.Add(Shapes.Between(x, 0, x + width, bus + length));
                else
                    high.Add(Shapes.Between(x, bus + end, x + width, top + bus));
            }

            var made = Booleans.Merge(low);

            made.AddRange(Booleans.Merge(high));

            return centered(made);
        }

        ///
        ///A strip <paramref name="width"/> wide in <paramref name="runs"/> runs of <paramref name="run"/>, a
        ///<paramref name="spacing"/> apart and joined at alternate ends, with a square head <paramref name="head"/> on a
        ///side at each end for its contacts.
        ///
        ///**The heads stand out past the turns**, the strip's first and last runs carried on a half-width and a spacing to
        ///meet them, since a head wider than the strip beside a run's turn would come nearer it than a spacing.
        ///
        public static List<List<Element.Point>> MeanderResistor(int width, int run, int runs, int spacing, int head)
        {
            if (head < width)
                throw new ArgumentException("A contact head narrower than the strip is not a head.");

            if (run <= width)
                throw new ArgumentException("A run no longer than the strip is wide puts the turns at its two ends against each other.");

            long pitch = (long)width + spacing;

            fits(runs * pitch);

            int clear = (width / 2) + spacing;
            var line = new List<Element.Point> { new Element.Point(-clear, 0) };

            for (int each = 0; each < runs; each++)
            {
                int y = (int)(each * pitch);
                int to = 0;

                if (each % 2 == 0)
                    to = run;

                if (each == runs - 1 && each % 2 == 0)
                    to = run + clear;
                else if (each == runs - 1)
                    to = -clear;

                if (each > 0)
                    line.Add(new Element.Point(line[^1].X, y));

                line.Add(new Element.Point(to, y));
            }

            var end = line[^1];
            int low = head / 2;

            var parts = new List<IReadOnlyList<Element.Point>>
            {
                counterclockwise(PathOutline.Build(line, width, 0, 0, 0)),
                Shapes.Between(-clear - head, -low, -clear, head - low)
            };

            if (end.X > 0)
                parts.Add(Shapes.Between(end.X, end.Y - low, end.X + head, end.Y - low + head));
            else
                parts.Add(Shapes.Between(end.X - head, end.Y - low, end.X, end.Y - low + head));

            return centered(Booleans.Merge(parts));
        }

        ///
        ///A meander resistor's squares between its heads, from microns: its length along the middle over its width, each
        ///of its corners counted as 0.56 of a square rather than the whole one its middle gives it - which is how a current
        ///round a right angle crowds into the inside of it.
        ///
        public static double ResistorSquares(double width, double run, double runs, double spacing)
        {
            double along = (runs * run) + ((runs - 1) * (width + spacing)) + width + (2 * spacing);
            double corners = 2 * (runs - 1);

            return (along / width) - (corners * (1 - 0.56));
        }

        ///<summary>A ring <paramref name="ring"/> wide round a space <paramref name="width"/> by <paramref name="height"/> - a keyhole, as GDSII holds a ring.</summary>
        public static List<List<Element.Point>> GuardRing(int width, int height, int ring)
        {
            return Booleans.Combine(
                new[] { Shapes.Rectangle(0, 0, width + (2 * ring), height + (2 * ring)) },
                new[] { Shapes.Rectangle(0, 0, width, height) },
                BooleanOperation.Not);
        }

        ///
        ///Squares <paramref name="contact"/> on a side down the middle of that ring, a contact at each corner and as many
        ///along each side as fit at <paramref name="pitch"/>, spread evenly - so the four sides end the same way.
        ///
        public static List<List<Element.Point>> GuardRingContacts(int width, int height, int ring, int contact, int pitch)
        {
            if (contact >= ring)
                throw new ArgumentException("A contact as wide as the ring leaves no ring round it.");

            if (pitch <= contact)
                throw new ArgumentException("Contacts at a pitch no wider than they are run into each other.");

            double halfX = ((double)width + ring) / 2;
            double halfY = ((double)height + ring) / 2;
            var corners = new (double X, double Y)[] { (-halfX, -halfY), (halfX, -halfY), (halfX, halfY), (-halfX, halfY) };
            var made = new List<List<Element.Point>>();

            for (int side = 0; side < 4; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 4];
                double length = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
                int count = Math.Max(1, (int)Math.Floor(length / pitch));

                for (int each = 0; each < count; each++)
                {
                    double along = each / (double)count;
                    var middle = at(from.X + ((to.X - from.X) * along), from.Y + ((to.Y - from.Y) * along));

                    made.Add(Shapes.Rectangle(middle.X, middle.Y, contact, contact));
                }
            }

            return made;
        }

        ///
        ///A square shield <paramref name="size"/> across: four quarters, each a bar <paramref name="edge"/> wide along its
        ///side with strips <paramref name="bar"/> wide hanging from it toward the middle, <paramref name="slot"/> apart.
        ///
        ///**Square to the turns above it, and never round them.** An inductor's field drives a current in whatever is under
        ///it along its turns, and that current is the loss; a strip square to a turn gives it nowhere to go. So each quarter
        ///runs its strips across its side of the spiral, the quarters stand half a slot off the diagonals between them, and
        ///the side bars stop a slot short of the corners - nothing closes a loop round the middle, which a ring joining the
        ///bars would.
        ///
        public static List<List<Element.Point>> PatternedGroundShield(int size, int bar, int slot, int edge)
        {
            int half = size / 2;
            int clear = edge + slot;
            long pitch = (long)bar + slot;
            long room = (long)size - (2L * clear);

            if (room < bar)
                throw new ArgumentException("The side bars leave no room between them for a strip.");

            int strips = (int)((room + slot) / pitch);
            long first = -((strips * pitch) - slot) / 2;
            double offDiagonal = slot / Math.Sqrt(2);
            var quarter = new List<IReadOnlyList<Element.Point>> { Shapes.Between(-half + clear, -half, half - clear, -half + edge) };

            for (int each = 0; each < strips; each++)
            {
                int left = (int)(first + (each * pitch));
                int right = left + bar;
                int nearest = Math.Max(Math.Abs(left), Math.Abs(right));
                int top = (int)Math.Floor(-nearest - offDiagonal);

                if (top > -half + edge)
                    quarter.Add(Shapes.Between(left, -half, right, top));
            }

            var bottom = Booleans.Merge(quarter);
            var made = new List<List<Element.Point>>(bottom);

            for (int turn = 1; turn < 4; turn++)
            {
                foreach (var outline in bottom)
                    made.Add(quarterTurned(outline, turn));
            }

            return made;
        }

        #endregion **************************************************************************



        #region RF and microwave ************************************************************

        ///<summary>A signal line <paramref name="signal"/> wide between two grounds <paramref name="ground"/> wide, <paramref name="gap"/> from it on either side, <paramref name="length"/> long.</summary>
        public static List<List<Element.Point>> CoplanarWaveguide(int length, int signal, int gap, int ground)
        {
            int left = -(length / 2);
            int right = left + length;
            int low = -(signal / 2);
            int high = low + signal;

            return new List<List<Element.Point>>
            {
                Shapes.Between(left, low - gap - ground, right, low - gap),
                Shapes.Between(left, low, right, high),
                Shapes.Between(left, high + gap, right, high + gap + ground)
            };
        }

        ///
        ///A coplanar line <paramref name="length"/> long along its middle, wound in runs of <paramref name="run"/> a
        ///<paramref name="pitch"/> apart with a half circle at every turn, drawn as its two gaps <paramref name="gap"/> wide
        ///either side of a center line <paramref name="width"/> wide.
        ///
        ///**The gaps rather than the metal**, because a superconducting resonator is etched out of a ground plane that covers
        ///the chip: the line and the ground are what is left. The gaps go round the start, which is open and couples to a
        ///feed line laid beside it, and stop square at the end, where the center line runs into the ground - a quarter wave.
        ///The last run is as long as what is left of the length, in a turn if it comes to that.
        ///
        public static List<List<Element.Point>> CpwResonator(int length, int width, int gap, int run, int pitch)
        {
            if (pitch <= width + (2L * gap))
                throw new ArgumentException("Runs no further apart than the line and its gaps are wide leave no ground between them.");

            double radius = pitch / 2.0;
            double left = length;
            double x = 0;
            double y = 0;
            int heading = 1;
            var line = new List<Element.Point> { new Element.Point(0, 0) };

            while (left > 0)
            {
                double straight = Math.Min(run, left);

                x += heading * straight;
                left -= straight;
                line.Add(at(x, y));

                if (left <= 0)
                    break;

                //A half turn up round a middle a radius above, or as much of one as is left.
                double share = Math.Min(1, left / (Math.PI * radius));
                int steps = Math.Max(2, (int)Math.Ceiling(TurnSteps / 2.0 * share));

                for (int step = 1; step <= steps; step++)
                {
                    double angle = (-Math.PI / 2) + (heading * Math.PI * share * step / steps);

                    line.Add(at(x + (radius * Math.Cos(angle)), y + radius + (radius * Math.Sin(angle))));
                }

                left -= Math.PI * radius * share;

                if (share < 1)
                    break;

                y += pitch;
                heading = -heading;
            }

            var outside = PathOutline.Build(line, width + (2 * gap), 4, gap, 0);
            var inside = PathOutline.Build(line, width, 0, 0, 0);

            return centered(Booleans.Combine(new[] { outside }, new[] { inside }, BooleanOperation.Not));
        }

        ///<summary>Where a quarter-wave line of this length, in microns, resonates, in gigahertz: c / (4 × length × √εeff).</summary>
        public static double QuarterWaveGigahertz(double length, double permittivity)
        {
            return 299_792.458 / (4 * length * Math.Sqrt(permittivity));
        }

        ///
        ///A square of four lines <paramref name="quarter"/> apart between their middles - the top and bottom
        ///<paramref name="through"/> wide, the sides <paramref name="branch"/> wide - with a port line
        ///<paramref name="port"/> wide and <paramref name="lead"/> long out sideways from every corner. One outline, the
        ///square's hole keyholed.
        ///
        public static List<List<Element.Point>> BranchLineCoupler(int quarter, int through, int branch, int port, int lead)
        {
            if (through >= quarter || branch >= quarter)
                throw new ArgumentException("Arms as wide as the square is across leave no hole in the middle, and no coupler.");

            int h = quarter / 2;
            int t = through / 2;
            int b = branch / 2;
            int p = port / 2;
            int outer = h - b + branch;

            var parts = new List<IReadOnlyList<Element.Point>>
            {
                Shapes.Between(-h - b, h - t, outer, h - t + through),
                Shapes.Between(-h - b, -h - t, outer, -h - t + through),
                Shapes.Between(-h - b, -h - t, -h - b + branch, h - t + through),
                Shapes.Between(h - b, -h - t, outer, h - t + through),
                Shapes.Between(h, h - p, outer + lead, h - p + port),
                Shapes.Between(h, -h - p, outer + lead, -h - p + port),
                Shapes.Between(-h - b - lead, h - p, -h, h - p + port),
                Shapes.Between(-h - b - lead, -h - p, -h, -h - p + port)
            };

            return centered(Booleans.Merge(parts));
        }

        ///
        ///A ring of <paramref name="radius"/> to the middle of a line <paramref name="ring"/> wide, with port lines
        ///<paramref name="port"/> wide and <paramref name="lead"/> long straight out of it at 0°, 60°, 120° and 180°.
        ///
        ///**60° is a quarter wave on a ring a wavelength and a half round**, so the ports are a quarter wave apart in
        ///turn and the way back from the last to the first is the three quarters left - which is what makes the
        ///difference port's two paths half a wave apart.
        ///
        public static List<List<Element.Point>> RatRaceCoupler(int radius, int ring, int port, int lead)
        {
            if (ring >= radius)
                throw new ArgumentException("A ring's line as wide as its radius leaves no hole in the middle.");

            int inner = radius - (ring / 2);
            int outer = inner + ring;
            var parts = new List<IReadOnlyList<Element.Point>> { Shapes.Circle(0, 0, outer, RingVertices) };

            foreach (double degrees in new[] { 0.0, 60, 120, 180 })
                parts.Add(radialBar(degrees, radius, outer + lead, port));

            return centered(Booleans.Combine(parts, new[] { Shapes.Circle(0, 0, inner, RingVertices) }, BooleanOperation.Not));
        }

        ///
        ///A line two <paramref name="quarter"/> lengths and the <paramref name="split"/> long, a <paramref name="gap"/>
        ///over two lines <paramref name="quarter"/> long that are the split apart, all <paramref name="width"/> wide: the
        ///input line up out of the top line's left end, and an output line down out of each bottom line's inner end.
        ///
        public static List<List<Element.Point>> MarchandBalun(int quarter, int width, int gap, int split, int lead)
        {
            if (quarter <= width)
                throw new ArgumentException("A coupled section no longer than its line is wide has no room for the output line at its end.");

            int above = gap - (gap / 2);
            int below = -(gap / 2);
            int left = -quarter - (split / 2);
            int right = left + (2 * quarter) + split;
            int innerLeft = left + quarter;
            int innerRight = innerLeft + split;
            var made = new List<List<Element.Point>>();

            made.AddRange(Booleans.Merge(new[]
            {
                Shapes.Between(left, above, right, above + width),
                Shapes.Between(left, above, left + width, above + width + lead)
            }));

            made.AddRange(Booleans.Merge(new[]
            {
                Shapes.Between(left, below - width, innerLeft, below),
                Shapes.Between(innerLeft - width, below - width - lead, innerLeft, below)
            }));

            made.AddRange(Booleans.Merge(new[]
            {
                Shapes.Between(innerRight, below - width, right, below),
                Shapes.Between(innerRight, below - width - lead, innerRight + width, below)
            }));

            return centered(made);
        }

        ///
        ///<paramref name="resonators"/> lines two <paramref name="quarter"/> lengths long, each a <paramref name="gap"/>
        ///above the last and a quarter further along, so each runs beside the next for a quarter - with an input line
        ///before them and an output line after, each running on <paramref name="lead"/> past its coupled quarter.
        ///
        public static List<List<Element.Point>> CoupledLineFilter(int resonators, int quarter, int width, int gap, int lead)
        {
            long step = (long)width + gap;

            fits(((resonators + 2L) * quarter) + (2L * lead));
            fits((resonators + 2L) * step);

            var made = new List<List<Element.Point>> { Shapes.Between(-lead, 0, quarter, width) };

            for (int each = 1; each <= resonators; each++)
            {
                int y = (int)(each * step);

                made.Add(Shapes.Between((int)((each - 1L) * quarter), y, (int)((each + 1L) * quarter), y + width));
            }

            int last = (int)((resonators + 1L) * step);
            int start = (int)((long)resonators * quarter);

            made.Add(Shapes.Between(start, last, start + quarter + lead, last + width));

            return centered(made);
        }

        ///
        ///Port lines <paramref name="port"/> wide and <paramref name="lead"/> long either end of narrow and wide sections
        ///in turn - one more narrow than wide - <paramref name="narrow"/> and <paramref name="wide"/> wide, and
        ///<paramref name="narrowRun"/> and <paramref name="wideRun"/> long. One outline.
        ///
        public static List<List<Element.Point>> SteppedImpedanceFilter(int sections, int wide, int narrow, int wideRun, int narrowRun, int port, int lead)
        {
            if (narrow >= wide)
                throw new ArgumentException("Narrow sections as wide as the wide ones never step.");

            fits((2L * lead) + ((sections + 1L) * narrowRun) + ((long)sections * wideRun));

            var parts = new List<IReadOnlyList<Element.Point>>();
            int x = 0;

            void section(int length, int across)
            {
                int low = -(across / 2);

                parts.Add(Shapes.Between(x, low, x + length, low + across));
                x += length;
            }

            section(lead, port);

            for (int each = 0; each < sections; each++)
            {
                section(narrowRun, narrow);
                section(wideRun, wide);
            }

            section(narrowRun, narrow);
            section(lead, port);

            return centered(Booleans.Merge(parts));
        }

        ///
        ///A fan of <paramref name="radius"/> opening <paramref name="degrees"/> wide, on the end of a line
        ///<paramref name="width"/> wide and <paramref name="lead"/> long - the line carried into the fan as far as the fan is
        ///as wide as it, so the two are one shape rather than two that touch at a point.
        ///
        public static List<List<Element.Point>> RadialStub(int radius, double degrees, int width, int lead)
        {
            if (degrees >= 180)
                throw new ArgumentException("A fan opening 180° or more is not a stub but most of a disc.");

            double half = degrees * Math.PI / 360;
            double into = (width / (2 * Math.Tan(half))) + 1;

            if (into >= radius)
                throw new ArgumentException("The fan is narrower near its point than the line is wide for most of its radius: open it wider, or make it longer.");

            int corners = Math.Max(8, (int)Math.Ceiling(RingVertices * degrees / 360));
            var fan = new List<Element.Point> { new Element.Point(0, 0) };

            for (int each = 0; each <= corners; each++)
            {
                double angle = -half + (2 * half * each / corners);

                fan.Add(at(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }

            int low = -(width / 2);
            var line = Shapes.Between(-lead, low, (int)Math.Ceiling(into), low + width);

            return centered(Booleans.Merge(new[] { fan, line }));
        }

        ///
        ///Two rings <paramref name="width"/> wide, the outer one <paramref name="radius"/> to its outside and the inner one a
        ///<paramref name="spacing"/> inside it, each cut <paramref name="split"/> wide - the outer at the top, the inner at
        ///the bottom. Each cut stops in the space between the rings, so it opens its own ring and not the other.
        ///
        public static List<List<Element.Point>> SplitRingResonator(int radius, int width, int spacing, int split)
        {
            int innerRadius = radius - width - spacing;
            int hole = innerRadius - width;

            if (hole <= split)
                throw new ArgumentException("The inner ring is too small round its hole for its cut: a larger radius, or narrower rings, spacing or cut.");

            int low = -(split / 2);
            int high = low + split;
            int between = radius - width - (spacing / 2);

            var made = Booleans.Combine(
                new[] { Shapes.Circle(0, 0, radius, RingVertices) },
                new[] { Shapes.Circle(0, 0, radius - width, RingVertices), Shapes.Between(low, between, high, radius + width) },
                BooleanOperation.Not);

            made.AddRange(Booleans.Combine(
                new[] { Shapes.Circle(0, 0, innerRadius, RingVertices) },
                new[] { Shapes.Circle(0, 0, hole, RingVertices), Shapes.Between(low, -between, high, -(hole / 2)) },
                BooleanOperation.Not));

            return centered(made);
        }

        #endregion **************************************************************************



        #region Antennas ********************************************************************

        ///
        ///A patch <paramref name="width"/> by <paramref name="length"/>, a notch cut up into its bottom edge
        ///<paramref name="inset"/> deep and <paramref name="notch"/> wider than the feed on either side, and a feed line
        ///<paramref name="feed"/> wide from <paramref name="lead"/> below the patch up the notch to its end. One outline.
        ///
        public static List<List<Element.Point>> PatchAntenna(int width, int length, int inset, int notch, int feed, int lead)
        {
            if (inset >= length)
                throw new ArgumentException("A feed inset as deep as the patch is long cuts it in two.");

            if ((long)feed + (2L * notch) >= width)
                throw new ArgumentException("The feed and the notch either side of it are as wide as the patch.");

            int left = -(width / 2);
            int bottom = -(length / 2);
            int feedLeft = -(feed / 2);
            int feedRight = feedLeft + feed;

            var made = Booleans.Combine(
                new[] { Shapes.Between(left, bottom, left + width, bottom + length) },
                new[] { Shapes.Between(feedLeft - notch, bottom - 1, feedRight + notch, bottom + inset) },
                BooleanOperation.Not);

            made.Add(Shapes.Between(feedLeft, bottom - lead, feedRight, bottom + inset));

            return centered(Booleans.Merge(made));
        }

        ///
        ///Two arms <paramref name="width"/> wide, <paramref name="length"/> tip to tip with <paramref name="gap"/> between
        ///them, and a feed line down from each arm's inner end, <paramref name="lead"/> long - the two feed lines the gap
        ///apart, a pair a balanced signal comes up.
        ///
        public static List<List<Element.Point>> DipoleAntenna(int length, int width, int gap, int lead)
        {
            if (gap >= length)
                throw new ArgumentException("A gap as wide as the dipole is long leaves no arms.");

            int tip = length / 2;
            int inner = gap / 2;
            int low = -(width / 2);
            int high = low + width;
            var made = new List<List<Element.Point>>();

            made.AddRange(Booleans.Merge(new[]
            {
                Shapes.Between(-tip, low, -inner, high),
                Shapes.Between(-inner - width, low - lead, -inner, high)
            }));

            made.AddRange(Booleans.Merge(new[]
            {
                Shapes.Between(inner, low, tip, high),
                Shapes.Between(inner, low - lead, inner + width, high)
            }));

            return centered(made);
        }

        ///
        ///Two triangles <paramref name="length"/> tip to tip, each flaring <paramref name="degrees"/> wide from a point cut
        ///off <paramref name="feed"/> wide, the points <paramref name="gap"/> apart.
        ///
        public static List<List<Element.Point>> BowtieAntenna(int length, double degrees, int gap, int feed)
        {
            if (degrees >= 180)
                throw new ArgumentException("Triangles flaring 180° or more are not triangles.");

            if (gap >= length)
                throw new ArgumentException("A gap as wide as the antenna is long leaves no triangles.");

            double tip = length / 2.0;
            double point = gap / 2.0;
            double narrow = feed / 2.0;
            double wide = narrow + ((tip - point) * Math.Tan(degrees * Math.PI / 360));

            fits(2 * wide);

            var made = new List<List<Element.Point>>
            {
                new List<Element.Point> { at(point, -narrow), at(tip, -wide), at(tip, wide), at(point, narrow) },
                new List<Element.Point> { at(-tip, -wide), at(-point, -narrow), at(-point, narrow), at(-tip, wide) }
            };

            return centered(made);
        }

        ///
        ///Two arms <paramref name="width"/> wide, each <paramref name="turns"/> turns of an Archimedean spiral from
        ///<paramref name="inner"/> out, the second the first turned half way round.
        ///
        ///**The radius grows twice a width and a spacing every turn**, so the other arm, half a turn behind, falls
        ///halfway between - a <paramref name="spacing"/> from each turn of this one, measured out from the middle.
        ///
        public static List<List<Element.Point>> SpiralAntenna(int turns, int width, int spacing, int inner)
        {
            if (inner < width)
                throw new ArgumentException("Arms starting nearer the middle than they are wide run into each other there.");

            double growth = ((double)width + spacing) / Math.PI;

            fits(2 * (inner + (growth * 2 * Math.PI * turns) + width));

            int steps = turns * TurnSteps;
            var arms = new List<List<Element.Point>>();

            foreach (double start in new[] { 0, Math.PI })
            {
                var line = new List<Element.Point>(steps + 1);

                for (int step = 0; step <= steps; step++)
                {
                    double angle = 2 * Math.PI * step / TurnSteps;
                    double reach = inner + (growth * angle);

                    line.Add(at(reach * Math.Cos(angle + start), reach * Math.Sin(angle + start)));
                }

                arms.Add(PathOutline.Build(line, width, 0, 0, 0));
            }

            return centered(arms);
        }

        #endregion **************************************************************************



        #region Digital *********************************************************************

        ///
        ///An H <paramref name="span"/> wide and tall in wire <paramref name="width"/> wide, an H half its size on each of its
        ///four tips, and so on for <paramref name="levels"/> levels - merged into one outline, since a tree is one net.
        ///
        ///Each H a quarter of the one before keeps the ones on neighboring tips half its size apart, which leaves room
        ///for the wire while the last H is more than four wires across.
        ///
        public static List<List<Element.Point>> ClockHTree(int levels, int span, int width)
        {
            if (levels > MostTreeLevels)
                throw new ArgumentException($"More than {MostTreeLevels} levels is more than {said(Math.Pow(4, MostTreeLevels))} leaves - a tree that size is a clock router's to draw.");

            if (4.0 * width >= span / Math.Pow(2, levels - 1))
                throw new ArgumentException("At that many levels the last H is too small for wires that wide: fewer levels, a wider span, or a narrower wire.");

            var wires = new List<IReadOnlyList<Element.Point>>();

            void tree(double x, double y, double size, int level)
            {
                double half = size / 2;

                wires.Add(wire(x - half, y, x + half, y, width));
                wires.Add(wire(x - half, y - half, x - half, y + half, width));
                wires.Add(wire(x + half, y - half, x + half, y + half, width));

                if (level == levels)
                    return;

                foreach (double tipX in new[] { x - half, x + half })
                {
                    foreach (double tipY in new[] { y - half, y + half })
                        tree(tipX, tipY, half, level + 1);
                }
            }

            tree(0, 0, span, 1);

            return Booleans.Merge(wires);
        }

        ///<summary><paramref name="rails"/> rails <paramref name="width"/> wide and <paramref name="length"/> long, a <paramref name="pitch"/> apart - a row of cells between each two.</summary>
        public static List<List<Element.Point>> PowerRails(int rails, int pitch, int width, int length)
        {
            if (width >= pitch)
                throw new ArgumentException("Rails as wide as a row is tall leave no row between them.");

            long extent = (rails - 1L) * pitch;

            fits(extent);

            var made = new List<List<Element.Point>>();

            for (int each = 0; each < rails; each++)
                made.Add(Shapes.Rectangle(0, (int)(-(extent / 2) + (each * (long)pitch)), length, width));

            return made;
        }

        #endregion **************************************************************************



        #region Chip ************************************************************************

        ///<summary>Pads <paramref name="pad"/> on a side along every side of a die <paramref name="die"/> across, <paramref name="pitch"/> apart and <paramref name="inset"/> in from its edge.</summary>
        public static List<List<Element.Point>> PadRing(int die, int pad, int pitch, int inset)
        {
            return pads(die, pad, pitch, inset, pad);
        }

        ///<summary>The openings over that ring's pads, each <paramref name="enclosure"/> inside its pad all round.</summary>
        public static List<List<Element.Point>> PadOpenings(int die, int pad, int pitch, int inset, int enclosure)
        {
            if (2L * enclosure >= pad)
                throw new ArgumentException("An opening that far inside its pad leaves nothing of it.");

            return pads(die, pad, pitch, inset, pad - (2 * enclosure));
        }

        ///
        ///How many pads go along each side: as many as fit at the pitch between the rows of the two sides beside it, which
        ///hold the corners - and as far from them as two pads in a row are from each other.
        ///
        public static int PadsPerSide(double die, double pad, double pitch, double inset)
        {
            if (pitch <= pad)
                throw new ArgumentException("Pads at a pitch no wider than they are run into each other.");

            double room = die - (2 * inset) - (2 * pitch);

            if (room < pad)
                throw new ArgumentException("There is no room along the die's sides for a pad, clear of the corners.");

            return (int)Math.Floor((room - pad) / pitch) + 1;
        }

        private static List<List<Element.Point>> pads(int die, int pad, int pitch, int inset, int side)
        {
            int perSide = PadsPerSide(die, pad, pitch, inset);
            long extent = (perSide - 1L) * pitch;
            int line = (die / 2) - inset - (pad / 2);
            var made = new List<List<Element.Point>>();

            for (int each = 0; each < perSide; each++)
            {
                int along = (int)(-(extent / 2) + (each * (long)pitch));

                made.Add(Shapes.Rectangle(along, -line, side, side));
                made.Add(Shapes.Rectangle(line, along, side, side));
                made.Add(Shapes.Rectangle(-along, line, side, side));
                made.Add(Shapes.Rectangle(-line, -along, side, side));
            }

            return made;
        }

        ///<summary>What a pad ring comes to, said.</summary>
        private static string padsSaid(int perSide)
        {
            return $"{4 * perSide} pads, {perSide} a side";
        }

        ///<summary>Squares <paramref name="square"/> on a side, <paramref name="spacing"/> apart, in as many rows and columns as fit <paramref name="width"/> by <paramref name="height"/>.</summary>
        public static List<List<Element.Point>> DummyFill(int width, int height, int square, int spacing)
        {
            var (across, down) = FillCount(width, height, square, spacing);
            long pitch = (long)square + spacing;
            long wide = (across * pitch) - spacing;
            long tall = (down * pitch) - spacing;
            var made = new List<List<Element.Point>>();

            for (int row = 0; row < down; row++)
            {
                for (int column = 0; column < across; column++)
                {
                    int x = (int)(-(wide / 2) + (column * pitch));
                    int y = (int)(-(tall / 2) + (row * pitch));

                    made.Add(Shapes.Between(x, y, x + square, y + square));
                }
            }

            return made;
        }

        ///
        ///How many columns and rows of fill fit. Refused past <see cref="MostOutlines"/> before a square is drawn, since a
        ///fill is the one mark whose count grows as the square of what is typed.
        ///
        public static (int Across, int Down) FillCount(double width, double height, double square, double spacing)
        {
            if (square > width || square > height)
                throw new ArgumentException("Not one square fits in the area.");

            int across = (int)Math.Floor((width + spacing) / (square + spacing));
            int down = (int)Math.Floor((height + spacing) / (square + spacing));

            if ((long)across * down > MostOutlines)
                throw new ArgumentException($"That is {(long)across * down} squares, more than the {MostOutlines} one placement puts down: fill a smaller area at a time, or with larger squares.");

            return (across, down);
        }

        ///<summary>What a fill comes to, said: how many squares, and how much of the area they cover.</summary>
        private static string fillSaid(double width, double height, double square, double spacing)
        {
            var (across, down) = FillCount(width, height, square, spacing);
            double covered = across * down * square * square / (width * height);

            return $"{across * down} squares, {said(covered * 100)}% of the area";
        }

        #endregion **************************************************************************



        #region Drawing helpers *************************************************************

        ///<summary>A point at the whole coordinates nearest these.</summary>
        private static Element.Point at(double x, double y)
        {
            return new Element.Point((int)Math.Round(x), (int)Math.Round(y));
        }

        ///<summary>The outlines moved so the box round them all is centered on the origin, which is where a mark is held.</summary>
        private static List<List<Element.Point>> centered(List<List<Element.Point>> outlines)
        {
            var box = Bounds.Of(outlines.SelectMany(outline => outline));

            return shifted(outlines, -(int)(((long)box.Left + box.Right) / 2), -(int)(((long)box.Bottom + box.Top) / 2));
        }

        ///<summary>The outline turning counterclockwise, as every shape <see cref="Shapes"/> makes does - so a merge counts it in where it overlaps one, never out.</summary>
        private static List<Element.Point> counterclockwise(List<Element.Point> outline)
        {
            if (Measure.AreaOf(outline) < 0)
                outline.Reverse();

            return outline;
        }

        ///<summary>Refuses a mark that would run past the coordinates a GDSII file holds, before it is worked out wrong.</summary>
        private static void fits(double extent)
        {
            if (extent > int.MaxValue / 4)
                throw new ArgumentException("That is too big for this file: at its size it runs past the largest coordinate a GDSII file can hold. Make it smaller.");
        }

        ///<summary>A number as a note says it: to two places at most, the same in any browser's language.</summary>
        private static string said(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        ///<summary>An outline turned a quarter counterclockwise about the origin, <paramref name="quarters"/> times - exactly, being whole numbers.</summary>
        private static List<Element.Point> quarterTurned(List<Element.Point> outline, int quarters)
        {
            var turned = new List<Element.Point>(outline.Count);

            foreach (var point in outline)
            {
                int x = point.X;
                int y = point.Y;

                for (int each = 0; each < quarters; each++)
                    (x, y) = (-y, x);

                turned.Add(new Element.Point(x, y));
            }

            return turned;
        }

        ///<summary>A bar <paramref name="width"/> wide along a line out of the origin at an angle, from one distance out to another - counterclockwise, as Shapes draws.</summary>
        private static List<Element.Point> radialBar(double degrees, double from, double to, int width)
        {
            double angle = degrees * Math.PI / 180;
            double alongX = Math.Cos(angle);
            double alongY = Math.Sin(angle);
            double half = width / 2.0;

            return new List<Element.Point>
            {
                at((alongX * from) + (alongY * half), (alongY * from) - (alongX * half)),
                at((alongX * to) + (alongY * half), (alongY * to) - (alongX * half)),
                at((alongX * to) - (alongY * half), (alongY * to) + (alongX * half)),
                at((alongX * from) - (alongY * half), (alongY * from) + (alongX * half))
            };
        }

        ///<summary>A straight wire between two points on one axis, squared off half its width past them, so wires that meet overlap.</summary>
        private static List<Element.Point> wire(double x1, double y1, double x2, double y2, int width)
        {
            int low = width / 2;
            int left = (int)Math.Round(Math.Min(x1, x2)) - low;
            int bottom = (int)Math.Round(Math.Min(y1, y2)) - low;
            int right = (int)Math.Round(Math.Max(x1, x2)) - low + width;
            int top = (int)Math.Round(Math.Max(y1, y2)) - low + width;

            return Shapes.Between(left, bottom, right, top);
        }

        ///<summary>A square block of squares <paramref name="side"/> on a side, <paramref name="across"/> to a row at <paramref name="pitch"/>, centered on a point.</summary>
        private static List<List<Element.Point>> block(Element.Point middle, int across, int side, long pitch)
        {
            long extent = ((across - 1) * pitch) + side;
            long start = -(extent / 2);
            var made = new List<List<Element.Point>>();

            for (int row = 0; row < across; row++)
            {
                for (int column = 0; column < across; column++)
                {
                    int x = (int)(middle.X + start + (column * pitch));
                    int y = (int)(middle.Y + start + (row * pitch));

                    made.Add(Shapes.Between(x, y, x + side, y + side));
                }
            }

            return made;
        }

        #endregion **************************************************************************



        private static List<List<Element.Point>> shifted(List<List<Element.Point>> outlines, int byX, int byY)
        {
            var moved = new List<List<Element.Point>>(outlines.Count);

            foreach (var outline in outlines)
            {
                var copy = new List<Element.Point>(outline.Count);

                foreach (var point in outline)
                    copy.Add(new Element.Point(point.X + byX, point.Y + byY));

                moved.Add(copy);
            }

            return moved;
        }
    }
}
