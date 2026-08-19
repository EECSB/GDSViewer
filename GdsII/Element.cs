using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    public class Element
    {
        ///<summary>
        ///Required rather than nullable: every element is on a layer, and the flattener builds these with
        ///an object initializer, so the compiler can hold it to that instead of the layer being checked
        ///again at each of the places that colors and stacks by it.
        ///</summary>
        public required Layer Layer { get; set; }
        public List<Point> Points { get; set; } = new List<Point>();

        ///<summary>
        ///The label a TEXT element shows, or null for geometry. When it is set, Points holds the single
        ///anchor the label sits at rather than an outline.
        ///</summary>
        public string? Text { get; set; }

        ///<summary>How the label is justified about that anchor. Meaningless when Text is null.</summary>
        public TextPresentation Presentation { get; set; } = TextPresentation.Default;

        ///
        ///Whether these points are a run rather than a ring, and enclose nothing.
        ///
        ///**A path of no width has no outline**, so the flattener hands its centerline through unchanged -
        ///and a renderer that closes every set of points into a polygon then fills the shape between the two
        ///ends of it. On a straight line that is invisible; on an arc it is a solid segment where there is a
        ///line, which is a picture of something that is not there.
        ///
        ///A GDSII file rarely holds one. A DXF is full of them, since every line and every arc in a drawing
        ///is exactly this.
        ///
        public bool IsOpen { get; set; }

        ///
        ///The box this covers, worked out once when it is asked for and kept.
        ///
        ///**Every question about where a shape is starts here.** A rubber band asks it of every element in
        ///the layout on every drag; culling asks it of every element on every rebuild; the net tracer asks it
        ///of every candidate pair. Recomputed each time, that is a walk over every corner of every shape for
        ///something that cannot have changed - the points are only replaced wholesale, by an edit, which
        ///builds a new Element.
        ///
        ///Lazy rather than set by the flattener, so that anything building an Element by hand - a boolean's
        ///result, a test's fixture - gets it right without having to know to.
        ///
        public Bounds Box
        {
            get
            {
                box ??= Bounds.Of(Points);

                return box.Value;
            }
        }

        private Bounds? box;

        ///<summary>
        ///Where in the library this came from, or null when nothing put it there.
        ///
        ///Null for anything a caller built rather than flattened - a boolean's result is derived from
        ///several elements and belongs to none of them, and a test's fixture belongs to no file at all.
        ///Which is why this is nullable rather than required: a drawn shape usually has a source, and a
        ///shape that is only drawn does not need one.
        ///</summary>
        public ElementSource? Source { get; set; }


        public struct Point
        {
            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; set; }
            public int Y { get; set; }
        }
    }

    ///<summary>
    ///One structure in a chain of placements, with the transform that takes its own coordinates out to the
    ///layout's.
    ///</summary>
    public readonly record struct PlacementLevel(string Structure, Transform Placement);

    ///<summary>
    ///Where a flattened shape came from: which structure holds it, which of that structure's elements it
    ///is, and through what placement it is being seen.
    ///
    ///**Flattening is lossy in the one direction editing needs.** It answers "what is drawn and where",
    ///which is everything a viewer wants and nothing an editor can use: a shape on screen may be one of a
    ///thousand instances of a cell, and moving it means changing a coordinate in that cell rather than the
    ///one on screen. This is the way back.
    ///
    ///The model is held by reference rather than by an index into its structure's list, because the list
    ///is the thing an edit changes - inserting a shape would renumber every handle taken before it.
    ///</summary>
    public sealed class ElementSource
    {
        ///<summary>
        ///Everything about how a structure was reached, shared by every element in it.
        ///
        ///**One of these per visit rather than per shape.** All of it - the name, the chain, the composed
        ///transform - is a property of the visit and not of the individual element, and a transform is six
        ///doubles: held per element it was 48 bytes on each of half a million shapes for a value ten
        ///thousand of them shared. Measured at 580,000 elements the difference is tens of megabytes.
        ///
        ///Internal because it is a storage decision. The flat properties below are the API.
        ///</summary>
        internal sealed class Visit
        {
            public required string Structure { get; init; }
            public required Transform Transform { get; init; }
            public required IReadOnlyList<string> Path { get; init; }

            ///<summary>
            ///The visit this one was reached from, or null at the top.
            ///
            ///What makes climbing back out possible. The composed transform says how to get from this cell
            ///to the layout and nothing about the levels in between, so a chain of names alone can show a
            ///breadcrumb but cannot follow one - stepping up to the parent needs the parent's own
            ///transform, and this is where it is. One reference per visit, and there is already one visit
            ///per structure, so it costs nothing per shape.
            ///</summary>
            public Visit? Parent { get; init; }
        }

        private readonly Visit via;

        ///<summary>
        ///Built by the flattener and by nothing else, which is what makes it trustworthy: a source that
        ///could be constructed anywhere could name a structure that does not hold the element it names.
        ///</summary>
        internal ElementSource(Visit via, GDS.ElementModel model)
        {
            this.via = via;

            Model = model;
        }

        ///<summary>The element itself, in the library. What an edit changes.</summary>
        public GDS.ElementModel Model { get; }

        ///<summary>The structure holding this element. What an edit-in-place descends into.</summary>
        public string Structure
        {
            get { return via.Structure; }
        }

        ///<summary>
        ///The composed placement that brought it here: its own coordinates to the top level's.
        ///
        ///Identity for a shape in a top-level structure, which is the common case and the one where the
        ///drawn coordinates are already the ones in the file.
        ///</summary>
        public Transform Placement
        {
            get { return via.Transform; }
        }

        ///<summary>
        ///The chain of structures it was reached through, outermost first, ending in <see cref="Structure"/>.
        ///</summary>
        public IReadOnlyList<string> Path
        {
            get { return via.Path; }
        }

        ///<summary>
        ///Every level this shape was reached through, outermost first, ending in its own structure - each
        ///with the transform that takes *that* level's coordinates out to the layout's.
        ///
        ///Walked rather than stored, and allocated when asked for. It is wanted when somebody clicks a
        ///shape and not once per shape per draw, so the cost belongs on the click.
        ///</summary>
        public IReadOnlyList<PlacementLevel> Ancestry
        {
            get
            {
                var levels = new List<PlacementLevel>();

                for (var at = via; at is not null; at = at.Parent)
                    levels.Add(new PlacementLevel(at.Structure, at.Transform));

                levels.Reverse();

                return levels;
            }
        }

        ///<summary>How many placements deep. Zero for a top-level structure's own elements.</summary>
        public int Depth
        {
            get { return Path.Count - 1; }
        }

        ///<summary>
        ///Whether this shape's drawn coordinates are the ones the file holds - true only at the top level,
        ///where nothing has been placed through anything.
        ///</summary>
        public bool IsDirectlyEditable
        {
            get { return Depth == 0; }
        }

        ///<summary>
        ///A point in the layout's coordinates, brought back into this element's own.
        ///
        ///Null when the placement cannot be undone, which means a magnification of zero. Not rounded: the
        ///caller rounds once, when it writes, rather than at each step of a round trip.
        ///</summary>
        public (double X, double Y)? ToLocal(double x, double y)
        {
            if (Placement.Inverse() is not Transform back)
                return null;

            return back.ApplyTo(x, y);
        }

        ///<summary>A point in this element's own coordinates, out into the layout's.</summary>
        public (double X, double Y) ToLayout(double x, double y)
        {
            return Placement.ApplyTo(x, y);
        }

        public override string ToString()
        {
            return string.Join(" > ", Path);
        }
    }

    public class AdditionalGDSInformation
    {
        public AdditionalGDSInformation(GDS gds)
        {
            GetLayers(gds.StreamFormat.Structures);
        }

        ///<summary>
        ///The gap between layers up the stacking axis before the 3D view's slider says otherwise. Equal to
        ///a layer's own depth, so the stack starts out with the layers touching.
        ///
        ///Public because this is the number a file is opened at, and both the slider that moves it and the
        ///page that resets a layer's height have to start from the same one. They each held a copy, and one
        ///of them had drifted: the slider's field opened on 10, below its own minimum of 50, so it reported
        ///a spacing the file was never stacked at.
        ///</summary>
        public const int DefaultLayerSpacing = 50;

        public Dictionary<LayerKey, Layer> Layers { get; set; } = new Dictionary<LayerKey, Layer>();

        public void GetLayers(List<StructureModel> structures)
        {
            foreach (var structure in structures)
            {
                foreach (var elementModel in structure.Elements)
                {
                    if (elementModel.Element is not GDS.IHasLayer element)
                        continue;

                    if (element.LAYER?.Data is not Int2Data layerRecord)
                        continue;

                    var key = new LayerKey(layerRecord.Value, dataTypeOf(element));

                    if (!Layers.ContainsKey(key))
                        Layers.Add(key, new Layer(key, ""));
                }
            }

            if (Layers.Count == 0)
                return;

            SetStackingOffsets(DefaultLayerSpacing);
            assignColors(OrderedLayers());
        }

        ///<summary>
        ///Layer number first, then data type, so the order is the one a person reads: 65/16 before 65/20
        ///before 66/20. Both the stacking and the coloring walk it, and the sidebar lists it.
        ///</summary>
        public List<KeyValuePair<LayerKey, Layer>> OrderedLayers()
        {
            return Layers.OrderBy(entry => entry.Key).ToList();
        }

        ///
        ///Spaces the layers up the stacking axis, **one step per layer**, so every row in the list separates
        ///from the one below it by the same amount.
        ///
        ///**This was one step per layer *number*, and that is the reversal worth recording.** The reasoning
        ///for it was physical: 65/20 and 65/16 are drawn geometry and a pin on the one diffusion layer, not
        ///two depths in the wafer, so they shared a height - and a step per entry stretches the stack to
        ///however many purposes a file happens to use, 46 planes rather than 21 across the bundled corpus.
        ///
        ///It is wrong about the case that matters most, though, and the sky130 mapping made that visible: a
        ///contact is a `/44` purpose of the layer *below* it, so licon1 sat at poly's height and mcon at
        ///li1's - and a via drawn inside the metal it is supposed to climb from is not a physical reading of
        ///anything. Pulling the stack open is what the slider is for, and half the rows not moving apart is
        ///the complaint that is actually about the picture on screen.
        ///
        ///The cost is real and is the old comment's: a pin or a label purpose now floats a step above the
        ///geometry it annotates. A layermap with real heights in it is the answer to wanting the physical
        ///stack - see <see cref="Layer.Offset"/> - and this even spacing is what a file with no such
        ///mapping gets.
        ///
        ///Public because the 3D view's spacing slider re-runs it. It used to do its own pass, which is
        ///exactly where the two could disagree about what a step is.
        ///
        ///**Every layer moves, including one that has been given a height of its own.** Such a layer used to
        ///be skipped here, and that is what made this control a lie on any file with a process stack in it:
        ///the layers that had heights stayed exactly where they were while the rest spread past them, so
        ///dragging the slider pulled a layout apart around a clump that never budged. A height says where a
        ///layer rests - see <see cref="Layer.CustomHeight"/> - and the spread is measured from the slider's
        ///own minimum, so at rest the stack is untouched and every step past it separates everything.
        ///
        public void SetStackingOffsets(int spacing)
        {
            int step = 0;

            //
            //**How much wider than resting the slider is asking for.**
            //
            //Zero at the slider's own minimum, which is this default - so at rest every layer sits exactly
            //where it sat before this arithmetic existed, a real height included. Everything above that is
            //the spread, and it is applied to every layer rather than to some of them.
            //
            int spread = spacing - DefaultLayerSpacing;

            foreach (var layer in OrderedLayers())
            {
                int mine = step;

                step++;

                //A layer told where it belongs starts from there. Everything else starts from its place in
                //the order, which is the same number the resting stack has always used.
                int from = layer.Value.CustomHeight ?? (DefaultLayerSpacing * mine);

                layer.Value.Offset = from + (spread * mine);
            }
        }

        ///<summary>
        ///Puts a layer back on the automatic stack, at whatever height its place in the file gives it.
        ///
        ///The whole stack is recomputed rather than the one layer, for the same reason
        ///<see cref="RestorePaletteColors"/> reassigns every color: a height is a position in a sequence,
        ///and working one out on its own would need this to know the spacing the slider is currently at.
        ///</summary>
        public void RestoreStacking(LayerKey key, int spacing)
        {
            if (!Layers.TryGetValue(key, out var layer))
                return;

            layer.StackIsCustom = false;
            layer.CustomHeight = null;
            layer.Depth = DefaultLayerDepth;

            SetStackingOffsets(spacing);
        }

        ///<summary>How thick a layer is drawn before anything says otherwise.</summary>
        public const int DefaultLayerDepth = 50;

        ///<summary>
        ///The second half of the layer/datatype pair, or <see cref="LayerKey.UnknownDataType"/> when the
        ///element's own type record is missing or holds something other than an INT2. Reading it through
        ///IHasLayer is what keeps this from having to know which of the seven elements it is looking at.
        ///</summary>
        private static short dataTypeOf(GDS.IHasLayer element)
        {
            if (element.DataTypeRecord?.Data is Int2Data dataType)
                return dataType.Value;

            return LayerKey.UnknownDataType;
        }

        ///<summary>
        ///Puts the gradient colors back, discarding any a loaded layermap set. Paired with
        ///<see cref="LayerNames.Clear"/>, so dropping a mapping restores the look the file had on its own
        ///rather than needing it reopened.
        ///</summary>
        public void RestorePaletteColors()
        {
            if (Layers.Count == 0)
                return;

            assignColors(OrderedLayers());
        }

        ///<summary>
        ///Color goes by the **pair**, so drawn geometry and a pin on the same layer are told apart. This is
        ///the half where distinguishing them is the point, and it is what every layer-properties file does:
        ///KLayout's sky130 .lyp gives all 413 layer/datatype entries their own color.
        ///</summary>
        private void assignColors(List<KeyValuePair<LayerKey, Layer>> orderedLayers)
        {
            int colorStep = layerColors.Length / orderedLayers.Count;
            int i = 0;

            foreach (var layer in orderedLayers)
            {
                layer.Value.Color = layerColors[i];
                i += colorStep;
            }
        }

        #region Data ************************************************************************

        //Seed values used to create the 255 color palette. One color for each layer. 
        //https://vis4.net/labs/multihue/#colors=#b30000%20#7c1158%20#4421af%20#1a53ff%20#0d88e6%20#00b7c7%20#5ad45a%20#8be04e%20#ebdc78|steps=255|bez=0|coL=0
        //["#b30000", "#7c1158", "#4421af", "#1a53ff", "#0d88e6", "#00b7c7", "#5ad45a", "#8be04e", "#ebdc78"]
        private static string[] layerColors = new string[]
        {
            "#b30000", "#b20004", "#b00109", "#af010d", "#ad0211", "#ac0214", "#aa0318", "#a8031b", "#a7041e", "#a50420",
            "#a40523", "#a20526", "#a10628", "#9f062b", "#9d072d", "#9c0730", "#9a0832", "#980835", "#970937", "#950a3a",
            "#930a3c", "#910b3e", "#8f0b41", "#8e0c43", "#8c0d46", "#8a0d48", "#880e4a", "#860e4d", "#840f4f", "#820f51",
            "#801054", "#7e1156", "#7c1159", "#7b115b", "#7a125e", "#7a1261", "#791263", "#781366", "#771368", "#76146b",
            "#75146e", "#741470", "#731573", "#721576", "#701679", "#6f167b", "#6e177e", "#6c1781", "#6b1883", "#691886",
            "#671989", "#66198c", "#641a8f", "#621b91", "#601b94", "#5e1c97", "#5b1c9a", "#591d9d", "#561e9f", "#531ea2",
            "#501fa5", "#4d1fa8", "#4a20ab", "#4621ae", "#4422b0", "#4324b3", "#4325b5", "#4327b8", "#4229ba", "#422abc",
            "#412cbf", "#412ec1", "#402fc4", "#3f31c6", "#3f33c9", "#3e34cb", "#3d36ce", "#3c37d0", "#3b39d3", "#3a3ad5",
            "#393cd8", "#383dda", "#373fdd", "#3641df", "#3442e2", "#3344e5", "#3145e7", "#3047ea", "#2e48ec", "#2c4aef",
            "#2a4bf1", "#274df4", "#254ef7", "#2250f9", "#1f51fc", "#1b53fe", "#1b54fe", "#1c56fe", "#1d58fd", "#1e5afc",
            "#1f5cfb", "#1f5dfb", "#205ffa", "#2061f9", "#2163f8", "#2164f7", "#2166f7", "#2168f6", "#216af5", "#216bf4",
            "#216df4", "#216ff3", "#2170f2", "#2072f1", "#2073f0", "#1f75f0", "#1f77ef", "#1e78ee", "#1d7aed", "#1c7bec",
            "#1b7dec", "#1a7feb", "#1880ea", "#1782e9", "#1583e8", "#1385e8", "#1086e7", "#0d88e6", "#1089e5", "#128be4",
            "#148ce3", "#158ee2", "#168fe1", "#1891e0", "#1992df", "#1994de", "#1a95dd", "#1b97dc", "#1b98dc", "#1b9adb",
            "#1c9bda", "#1c9dd9", "#1c9ed8", "#1ca0d7", "#1ba1d6", "#1ba3d5", "#1aa4d4", "#1aa6d3", "#19a7d2", "#18a9d1",
            "#17aad0", "#16accf", "#14adce", "#13aecd", "#11b0cc", "#0eb1cb", "#0bb3ca", "#07b4c9", "#03b6c8", "#05b7c6",
            "#16b8c3", "#1fb9c0", "#26babd", "#2cbbb9", "#31bcb6", "#35bcb3", "#39bdb0", "#3cbeac", "#3fbfa9", "#42c0a6",
            "#45c1a3", "#47c29f", "#49c39c", "#4bc499", "#4dc595", "#4ec592", "#50c68f", "#51c78b", "#52c888", "#53c985",
            "#54ca81", "#55cb7e", "#56cc7a", "#57cd76", "#58ce73", "#58cf6f", "#59d06c", "#59d168", "#59d264", "#5ad360",
            "#5ad45c", "#5bd45a", "#5dd559", "#5fd559", "#60d559", "#62d658", "#64d658", "#65d658", "#67d757", "#69d757",
            "#6ad857", "#6cd856", "#6ed856", "#6fd956", "#71d955", "#72da55", "#74da54", "#75da54", "#77db54", "#78db53",
            "#7adb53", "#7bdc53", "#7ddc52", "#7edd52", "#80dd51", "#81dd51", "#82de51", "#84de50", "#85de50", "#87df4f",
            "#88df4f", "#89e04f", "#8be04e", "#8ee04f", "#91e050", "#95e052", "#98e053", "#9be055", "#9fe056", "#a2e057",
            "#a5e059", "#a8e05a", "#ace05c", "#afdf5d", "#b2df5e", "#b5df60", "#b8df61", "#bbdf62", "#bedf64", "#c1df65",
            "#c4df66", "#c7df67", "#cade69", "#ccde6a", "#cfde6b", "#d2de6d", "#d5de6e", "#d8de6f", "#dbdd70", "#dddd72",
            "#e0dd73", "#e3dd74", "#e6dc75", "#e8dc77", "#ebdc78"
        };

        #endregion **************************************************************************
    }

    ///<summary>
    ///What identifies a layer in a GDSII file: its number **and** its data type, not the number alone.
    ///
    ///The format pairs every element's LAYER with a second number saying what the shape is *for* - drawn
    ///geometry, a pin, a label - and that pair is what every real tool keys on. In sky130, 65/20 is
    ///diff.drawing and 65/16 is diff.pin. Keying on the number alone merged them: across the bundled
    ///corpus it collapsed 46 distinct pairs into 21 entries, so one checkbox hid drawn geometry together
    ///with pin shapes and the two were forced to share a color.
    ///
    ///A record struct because this is a dictionary key on the hot path - the generated Equals and
    ///GetHashCode compare the two fields directly, where a plain struct falls back to reflection.
    ///</summary>
    public readonly record struct LayerKey(short Number, short DataType) : IComparable<LayerKey>
    {
        ///<summary>
        ///The data type of an element whose own type record is missing or unreadable. Negative so that it
        ///can never collide with a real one, which the format writes as an unsigned value.
        ///</summary>
        public const short UnknownDataType = -1;

        ///<summary>The way the pair is written everywhere else - KLayout, Magic, a PDK layermap.</summary>
        public override string ToString()
        {
            if (DataType == UnknownDataType)
                return $"{Number}/?";

            return $"{Number}/{DataType}";
        }

        ///<summary>
        ///Layer number first, then data type: 65/16, 65/20, 66/20. The order the sidebar lists and the
        ///stacking walks, and the order a person reads a layermap in.
        ///
        ///Worth implementing rather than repeating an OrderBy().ThenBy() at each call site, because a
        ///record struct is equatable but *not* comparable - so ordering by the key itself compiles and then
        ///throws "at least one object must implement IComparable" at runtime.
        ///</summary>
        public int CompareTo(LayerKey other)
        {
            if (Number != other.Number)
                return Number.CompareTo(other.Number);

            return DataType.CompareTo(other.DataType);
        }
    }

    ///
    ///What a layer is for, as far as anything asking about connectivity needs to know.
    ///
    ///**Why this cannot be worked out from the file.** A GDSII file is numbered shapes and nothing else - it
    ///does not say that 68/20 is metal and 67/44 is the contact between two of them. That is PDK data, the
    ///same kind of thing a layer *name* is, and it arrives the same way: from a layermap the user supplies,
    ///or typed in. See <see cref="LayerNames"/>.
    ///
    ///Three values rather than a whole process description, because three is what tracing a net needs. A
    ///conductor carries current along itself; a via joins the conductors it sits between; everything else -
    ///implants, wells, markers, text - takes no part. Anything finer than that is a design rule, and this app
    ///has no business pretending to know one.
    ///
    public enum LayerRole
    {
        ///<summary>Takes no part in connectivity, which is what nothing having been said means.</summary>
        None,

        ///<summary>Carries a net along itself, and joins anything of the same layer number it touches.</summary>
        Conductor,

        ///<summary>Joins whatever it overlaps, which is how two different conductors ever meet.</summary>
        Via
    }

    ///
    ///What is drawn over a layer's color, so two layers of a similar shade are still two layers.
    ///
    ///**Color runs out before layers do.** A palette is a hue wheel divided by however many layers a file
    ///has, and past about a dozen the steps are smaller than the difference an overlapping stack of
    ///half-transparent shapes makes to any of them - so 66/20 and 67/20 are two greens and nothing on
    ///screen says which is which. A pattern is a second axis: the same green, dotted rather than solid,
    ///stays told apart at any opacity and in a screenshot somebody prints in gray.
    ///
    ///This is what every layout tool does - KLayout's stipples, Cadence's fill styles - and for the same
    ///reason rather than as decoration.
    ///
    ///<see cref="None"/> is the normal case and means a solid fill: nothing has said otherwise, and a file
    ///carries no such information, so this is somebody's choice or a layermap's the same way a name is.
    ///
    public enum LayerFill
    {
        ///<summary>Solid, which is what every layer is until somebody says otherwise.</summary>
        None,

        ///<summary>Scattered points. The lightest of these - it dims a color without hiding it.</summary>
        Dots,

        ///<summary>Filled squares on a lattice, which reads heavier than dots at the same pitch.</summary>
        Squares,

        ///<summary>Lines both ways, so the color shows through a mesh.</summary>
        Grid,

        ///<summary>Broken horizontal lines.</summary>
        Dashes,

        ///<summary>Lines running lower-left to upper-right, the usual hatch.</summary>
        Diagonal,

        ///<summary>The same the other way, which is the cheapest way to have a second hatch.</summary>
        BackDiagonal,

        ///<summary>Both diagonals at once - the heaviest of these, for a layer that should read as solid-ish.</summary>
        CrossHatch
    }

    public class Layer
    {
        #region Constructors ****************************************************************
       
        public Layer(LayerKey key, string layerColor, int layerOffset = 10, int layerDepth = AdditionalGDSInformation.DefaultLayerDepth)
        {
            Offset = layerOffset;
            Key = key;
            Color = layerColor;
            Depth = layerDepth;
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        public LayerKey Key { get; set; }

        ///<summary>The layer number alone, which is what a stacking height is decided by.</summary>
        public short Number
        {
            get { return Key.Number; }
        }

        public short DataType
        {
            get { return Key.DataType; }
        }

        ///<summary>
        ///What this layer is called, when something has said so. Null is the normal case: a GDSII file
        ///carries only numbers, and nothing in the format records what 65/20 means - that mapping is PDK
        ///data which has to come from outside the file. See <see cref="LayerNames"/>.
        ///</summary>
        public string? Name { get; set; }

        ///<summary>
        ///How far up the stack this layer sits, in database units - the **height** of a process file.
        ///
        ///Worked out from the layer's place in the file by default, one step per layer number; set by hand
        ///or by a layermap when the real stack is known. See <see cref="StackIsCustom"/>.
        ///</summary>
        public int Offset { get; set; }

        ///<summary>
        ///How far the layer is extruded, in database units - the **thickness** of a process file.
        ///
        ///For a file whose UNITS make a database unit a nanometer, which is the usual case and true of
        ///every bundled example, these two are nanometers and a real process table can be typed in as it
        ///stands.
        ///</summary>
        public int Depth { get; set; }

        ///<summary>
        ///Whether <see cref="Offset"/> and <see cref="Depth"/> were given rather than worked out.
        ///
        ///The same idea as <see cref="ColorIsCustom"/>, and it does more work: the automatic stack is
        ///recomputed whenever the 3D view's spacing slider moves, so without this a height typed in or read
        ///from a layermap would last until the next nudge of that slider. A layer that has been told where
        ///it belongs is left where it is, and the slider goes on spreading the rest around it.
        ///</summary>
        public bool StackIsCustom { get; set; }

        ///
        ///The height this layer was told to sit at, or null to take its place in the order.
        ///
        ///**Held apart from <see cref="Offset"/>, which is where the layer is actually drawn.** The two used
        ///to be the same field, and that is what made the spacing slider a lie on any file with a process
        ///stack: <see cref="AdditionalGDSInformation.SetStackingOffsets"/> skipped a layer that had been given
        ///a height, because writing a spread into `Offset` would have destroyed the height it was reading. So
        ///the layers that had one never moved, the rest spread past them, and dragging the slider pulled a
        ///layout apart around a clump that stayed where it was.
        ///
        ///With the asked-for height kept here, the drawn position can be worked out from scratch on every
        ///step - height plus the spread - so nothing compounds and nothing has to be left alone.
        ///
        ///Held apart from <see cref="StackIsCustom"/> too, which answers a different question: whether this
        ///layer has anything about its stack worth writing back out to a layermap. A thickness typed into the
        ///settings popup is worth writing and is not a position, and pinning a layer's height because its
        ///thickness was edited is exactly the kind of thing that made the slider stop working on it.
        ///
        public int? CustomHeight { get; set; }

        ///<summary>
        ///What this layer is for, when something has said. <see cref="LayerRole.None"/> is the normal case,
        ///and means the layer takes no part in tracing a net - which for a file with no layermap loaded is
        ///every layer, so nothing is traceable until somebody says what the metal is.
        ///</summary>
        public LayerRole Role { get; set; } = LayerRole.None;

        public string Color { get; set; }

        ///<summary>
        ///What is drawn over <see cref="Color"/>, so a layer is told apart by more than its shade.
        ///
        ///No companion "is custom" flag, unlike the color and the stack. Those two are worked out from the
        ///file - a palette divided by the layer count, an even spacing - so a session has to record which
        ///of them were overruled. There is no automatic pattern: <see cref="LayerFill.None"/> is every
        ///layer until something says otherwise, so anything else is already the answer to "was this set".
        ///</summary>
        public LayerFill Fill { get; set; } = LayerFill.None;

        ///
        ///What the pattern's marks are drawn in, or null to draw them in <see cref="Color"/>.
        ///
        ///**Null rather than a copy of the color**, which is what makes "follows the layer" a state and not
        ///a coincidence. A layer given the color's own value would look identical today and then stop
        ///following it the moment the layer was recolored - so a hatch set to match would silently become a
        ///hatch pinned to the shade the layer used to be.
        ///
        ///Meaningless while <see cref="Fill"/> is <see cref="LayerFill.None"/>, and kept anyway: switching a
        ///layer to solid and back should not be a way to lose the hatch color that was chosen for it.
        ///
        public string? PatternColor { get; set; }

        ///
        ///How big one repeat of the pattern is on screen, in pixels, or null to take the usual size.
        ///
        ///**In pixels, because that is the size a stipple is actually judged at.** The tile is written into
        ///the picture in the layout's own units, and the 2D view then holds it at a constant screen size as
        ///the zoom changes - so the number worth exposing is the one at the end of that, not the one at the
        ///start. A tile in database units would mean something different on every file.
        ///
        public int? PatternPixels { get; set; }

        ///<summary>The screen size of one repeat when nothing has said otherwise. See PatternPixels.</summary>
        public const int DefaultPatternPixels = 9;

        ///
        ///Whether anything about this layer was said rather than worked out from the file.
        ///
        ///What makes a layer worth a row in a stored mapping: a row exists so it can be applied again, and a
        ///layer nobody touched has nothing to apply. The seven things listed here are the seven
        ///<see cref="GdsII.LayerNames.Apply"/> can set, which is not a coincidence - anything it can put back
        ///has to be something this will write down.
        ///
        ///**A name and a role used to be the whole test**, which was true while they were the only two
        ///columns. A color by hand, a stack, a fill and the two pattern columns each arrived without coming
        ///back here, so a hatch chosen on a layer that had no name was stored nowhere and gone on the next
        ///refresh - and the row builder was already willing to write it, which is what made the setting look
        ///like it had worked until the page was reloaded.
        ///
        public bool WasSaid
        {
            get
            {
                //Empty counts as unset for the pattern color, the way the row builder reads it - the popup
                //writes an empty string for "follows the layer", which is the absence rather than a choice.
                return Name is not null
                    || ColorIsCustom
                    || StackIsCustom
                    || Role != LayerRole.None
                    || Fill != LayerFill.None
                    || (PatternColor is string marks && marks.Length > 0)
                    || PatternPixels is not null;
            }
        }

        ///<summary>
        ///Whether <see cref="Color"/> was chosen rather than taken from the gradient.
        ///
        ///What a session records. The palette is derived from how many layers a file has, so storing one
        ///of its colors would be storing something already known - and would then fight the palette if the
        ///file changed and the layer count with it.
        ///</summary>
        public bool ColorIsCustom { get; set; }

        ///<summary>
        ///How the layer is labeled in the UI. The numbers stay visible even when a name is known, the way
        ///KLayout's own layer panel shows both - a name is somebody's mapping where the numbers are what
        ///the file actually says, so dropping them would hide a mismatch between the two.
        ///</summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name))
                    return Key.ToString();

                return $"{Name} ({Key})";
            }
        }

        #endregion **************************************************************************



        #region Methods *********************************************************************





        #endregion **************************************************************************

    }
}
