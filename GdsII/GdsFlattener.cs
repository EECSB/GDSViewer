using static GdsII.GDS;

namespace GdsII
{
    ///<summary>
    ///The result of resolving a library's hierarchy: every element it draws, with coordinates already in
    ///top-level space, plus what could not be resolved along the way.
    ///</summary>
    public class FlattenedLayout
    {
        public List<Element> Elements { get; set; } = new List<Element>();

        ///<summary>
        ///Structure names an SREF or AREF asked for that the library does not contain. This is normal
        ///rather than exceptional: a standalone cell file references the other cells of its library
        ///without including them, so opening one on its own leaves those references dangling.
        ///</summary>
        public SortedSet<string> UnresolvedReferences { get; set; } = new SortedSet<string>();

        ///<summary>Set when nesting was cut short, which means the library references itself in a loop.</summary>
        public bool DepthLimitReached { get; set; }

        ///
        ///Set when flattening stopped at <see cref="GdsFlattener.MostElements"/> with more still to come.
        ///
        ///**Which means what is here is not the whole layout**, and everything downstream of it - the
        ///picture, the area of a layer, the extent, a traced net - is about a part of the file. Nothing may
        ///treat that as an ordinary result, and the view says so where it cannot be missed.
        ///
        public bool Stopped { get; set; }
    }

    ///<summary>
    ///Turns the nested library the parser builds into a flat list of drawable elements.
    ///
    ///Without this a viewer can only draw each structure at its own coordinates, so a referenced cell
    ///appears once at the origin rather than at each place it is actually instantiated. Doing it here
    ///rather than in a renderer means the 2D and 3D views agree, and neither has to know how a GDSII
    ///transform composes.
    ///</summary>
    public class GdsFlattener
    {
        #region Constants *******************************************************************

        ///<summary>
        ///How deep nesting may go before it is treated as a loop. Real hierarchies are a handful of
        ///levels; anything approaching this is a structure that reaches itself.
        ///</summary>
        private const int MaximumDepth = 64;

        ///
        ///How many elements flattening will produce before it stops.
        ///
        ///**Breadth, where <see cref="MaximumDepth"/> catches depth.** One `AREF` of a thousand-shape cell,
        ///a hundred by a hundred, is ten million elements out of a sixty-kilobyte file - and nothing in the
        ///format limits the counts. Without a ceiling the tab does not fail, it dies.
        ///
        ///**The number is measured in a browser, which is where it applies.** It used to be two million,
        ///inferred from managed heap on a desktop - a measurement taken somewhere other than where it is
        ///used, which is a mistake this project has made before. Opening layouts of doubling size in the
        ///published build says what it actually costs:
        ///
        /// | Elements  | Drawn in | WebAssembly memory |
        /// |-----------|----------|--------------------|
        /// | 200,000   | 1.3 s    | 239 MB             |
        /// | 400,000   | 2.4 s    | 495 MB             |
        /// | 800,000   | 4.8 s    | 919 MB             |
        /// | 1,600,000 | 9.4 s    | 1,732 MB           |
        /// | 3,200,000 | never    |                    |
        ///
        ///Dead linear, at about 1.1 KB an element - and the build links with `--max-memory=2147483648`, so
        ///two gigabytes is a wall rather than a slowdown. That puts the arithmetic limit near 1.9 million
        ///with nothing left over, and the old two million was *past* it: a file that size would have hung
        ///for minutes instead of stopping and saying so, which is the exact failure this exists to prevent.
        ///
        ///One and a half million is about 1.6 GB, inside the largest size measured to work and leaving room
        ///for the markup being built and the copy of it that crosses into JavaScript.
        ///
        ///**A count rather than an array cap**, because a budget is the thing actually being defended: a
        ///deep nest of ordinary `SREF`s reaches the same place with no array anywhere in it.
        ///
        ///**And it stops rather than throwing.** The OASIS reader refuses a file over its own limit, which
        ///is right for a reader - a half-read file is not a file. This is a viewer, and a layout you can see
        ///most of, with a banner saying so, is more use than one that will not open. What must not happen is
        ///the quiet version: geometry missing with nothing to say it. See <see cref="FlattenedLayout.Stopped"/>.
        ///
        ///Settable, because a number chosen here cannot know what machine it is running on.
        ///
        public static int MostElements { get; set; } = 1_500_000;

        #endregion **************************************************************************



        #region Fields **********************************************************************

        private readonly Dictionary<string, StructureModel> structuresByName = new Dictionary<string, StructureModel>();
        private readonly Dictionary<LayerKey, Layer> layers;
        private readonly FlattenedLayout layout = new FlattenedLayout();

        ///<summary>The structures currently being expanded, so a loop is caught rather than followed.</summary>
        private readonly HashSet<string> expanding = new HashSet<string>();

        #endregion **************************************************************************



        #region Constructors ****************************************************************

        private GdsFlattener(GDS gds)
        {
            layers = gds.AdditionalInformation.Layers;

            foreach (var structure in gds.StreamFormat.Structures)
                structuresByName[nameOf(structure)] = structure;
        }

        #endregion **************************************************************************



        #region Entry point *****************************************************************

        public static FlattenedLayout Flatten(GDS gds)
        {
            Interlocked.Increment(ref flattens);

            var flattener = new GdsFlattener(gds);

            return flattener.run(gds);
        }

        private static int flattens;

        ///
        ///How many whole-library flattens have run, for anything that needs to count them.
        ///
        ///**Because a flatten that comes back costs time and changes no picture.** Resolving the hierarchy is
        ///the expensive part of opening a file, and the app is built to do it once per open and once per
        ///edit - the shell keeps the result and hands it to whichever view is mounted, which is what makes a
        ///2D to 3D switch cost nothing. A view that quietly went back to flattening for itself would draw
        ///exactly the same layout, pass every correctness test there is, and be slower. Nothing but a count
        ///can see that, which is why there is one.
        ///
        ///Counted here rather than at the call sites, so a flatten added somewhere new is counted without
        ///anybody remembering to. <see cref="Flatten(GDS, string)"/> is deliberately not counted: flattening
        ///one named cell for a preview or to carry it is a different question from resolving the library.
        ///
        ///Interlocked because this is a library and somebody else's threads are not this code's business,
        ///though the app that ships it runs on one.
        ///
        public static int Flattens
        {
            get { return Volatile.Read(ref flattens); }
        }

        ///
        ///One named structure, flattened as though it were the top of the library.
        ///
        ///Which is a different question from the one <see cref="Flatten(GDS)"/> answers. That draws what the
        ///file draws - the structures nothing references - where a list of cells is asking about a cell by
        ///name: "nand2" means nand2, wherever it sits in the hierarchy and however many times something
        ///else places it. Whatever *it* places is expanded under it exactly as anywhere else.
        ///
        ///A name the library does not hold comes back empty rather than falling back on the file's own top.
        ///A drawing of the wrong cell is worse than no drawing, because nothing about it says it is wrong.
        ///
        public static FlattenedLayout Flatten(GDS gds, string structureName)
        {
            var flattener = new GdsFlattener(gds);

            return flattener.runFrom(structureName);
        }

        private FlattenedLayout runFrom(string structureName)
        {
            if (structuresByName.TryGetValue(structureName, out var structure))
                appendStructure(structure, Transform.Identity, 0, null);

            return layout;
        }

        private FlattenedLayout run(GDS gds)
        {
            foreach (var structure in topLevelStructures(gds))
                appendStructure(structure, Transform.Identity, 0, null);

            return layout;
        }

        ///<summary>
        ///The structures nothing else references. Drawing only these is what stops a referenced cell
        ///being drawn twice - once where it is placed, and again at the origin in its own right. When
        ///every structure is referenced, which means the library is circular, everything is treated as
        ///top level so that something is still drawn.
        ///</summary>
        private List<StructureModel> topLevelStructures(GDS gds)
        {
            var referenced = new HashSet<string>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (var elementModel in structure.Elements)
                {
                    string? name = referencedName(elementModel.Element);

                    if (name is not null)
                        referenced.Add(name);
                }
            }

            var roots = gds.StreamFormat.Structures.Where(structure => !referenced.Contains(nameOf(structure))).ToList();

            if (roots.Count == 0)
                return gds.StreamFormat.Structures;

            return roots;
        }

        #endregion **************************************************************************



        #region Walking *********************************************************************

        ///<summary>
        ///Expands one structure at one placement.
        ///
        ///<paramref name="parent"/> is the visit this was reached from, null at the top. Passed down rather
        ///than held in a field, because the walk recurses: a field would be overwritten by a nested
        ///structure and every element after it would claim the child's chain.
        ///</summary>
        private void appendStructure(StructureModel structure, Transform transform, int depth, ElementSource.Visit? parent)
        {
            //
            //Checked here rather than at each Add, because this is the point the work is decided at: an
            //array of ten thousand places calls into this ten thousand times, and stopping at the door of
            //each one costs nothing where stopping per element would still walk every reference.
            //
            if (layout.Elements.Count >= MostElements)
            {
                layout.Stopped = true;

                return;
            }

            if (depth > MaximumDepth)
            {
                layout.DepthLimitReached = true;

                return;
            }

            string name = nameOf(structure);

            //A structure may legitimately appear many times in one layout, but not inside itself.
            if (!expanding.Add(name))
            {
                layout.DepthLimitReached = true;

                return;
            }

            //Built once for the whole structure and shared by every element in it, which is what keeps the
            //chain and the transform from being stored on each of them - see ElementSource.Visit.
            var visit = new ElementSource.Visit
            {
                Structure = name,
                Transform = transform,
                Path = reached(parent, name),
                Parent = parent
            };

            foreach (var elementModel in structure.Elements)
                appendElement(elementModel, transform, depth, visit);

            expanding.Remove(name);
        }

        ///<summary>The chain to a structure, which is its parent's chain with the structure itself on the end.</summary>
        private static IReadOnlyList<string> reached(ElementSource.Visit? parent, string name)
        {
            if (parent is null)
                return new List<string> { name };

            var path = new List<string>(parent.Path.Count + 1);

            path.AddRange(parent.Path);
            path.Add(name);

            return path;
        }

        private void appendElement(ElementModel elementModel, Transform transform, int depth, ElementSource.Visit visit)
        {
            var element = elementModel.Element;

            if (element is SrefModel sref)
            {
                appendReference(sref.SNAME, placementOf(sref.Strans, sref.XY, 0, transform).Then(transform), depth, visit);

                return;
            }

            if (element is ArefModel aref)
            {
                appendArray(aref, transform, depth, visit);

                return;
            }

            var source = new ElementSource(visit, elementModel);

            if (element is TextModel text)
            {
                appendText(text, transform, source);

                return;
            }

            appendGeometry(element, transform, source);
        }

        ///<summary>Boundaries, paths, boxes and nodes: coordinate lists drawn on a layer.</summary>
        private void appendGeometry(ElementType element, Transform transform, ElementSource source)
        {
            if (element is not IHasLayer hasLayer)
                return;

            if (!layers.TryGetValue(KeyOf(hasLayer), out var layer))
                return;

            if (element.XY?.Data is not Int4Data xy)
                return;

            var points = new List<Element.Point>();

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
                points.Add(new Element.Point(xy.Values[i], xy.Values[i + 1]));

            //A path's XY is a centerline, so it becomes an outline before being placed. Outlining first
            //and transforming after is what lets a magnified placement scale the width along with the
            //shape - widening it afterwards would need the transform picked apart again.
            bool open = false;

            if (element is PathModel path)
            {
                //A path of no width has no outline to build, so what comes back is the centerline itself -
                //a run rather than a ring, which is what the renderer has to be told. See Element.IsOpen.
                open = widthOf(path) <= 0;

                points = PathOutline.Build(points, widthOf(path), pathTypeOf(path), extensionOf(path.BGNEXTN), extensionOf(path.ENDEXTN));
            }

            var placed = new Element { Layer = layer, Source = source, IsOpen = open };

            foreach (var point in points)
                placed.Points.Add(transform.Apply(point.X, point.Y));

            if (placed.Points.Count > 0)
                layout.Elements.Add(placed);
        }

        ///<summary>A path with no WIDTH record has no width, which the format treats as zero.</summary>
        private static int widthOf(PathModel path)
        {
            if (path.WIDTH?.Data is Int4Data width)
                return width.Value;

            return 0;
        }

        ///<summary>PATHTYPE defaults to 0, square ends flush with the endpoint.</summary>
        private static int pathTypeOf(PathModel path)
        {
            if (path.PATHTYPE?.Data is Int2Data pathType)
                return pathType.Value;

            return 0;
        }

        private static int extensionOf(Record? extension)
        {
            if (extension?.Data is Int4Data value)
                return value.Value;

            return 0;
        }

        ///<summary>A text element carries one anchor point and the string to show there.</summary>
        private void appendText(TextModel text, Transform transform, ElementSource source)
        {
            if (!layers.TryGetValue(KeyOf(text), out var layer))
                return;

            if (text.XY?.Data is not Int4Data xy || xy.Values.Length < 2)
                return;

            var placed = new Element
            {
                Layer = layer,
                Source = source,
                Text = (text.TextBody?.STRING?.Data as AsciiData)?.Value ?? "",
                Presentation = TextPresentation.From(text.TextBody?.PRESENTATION?.Data)
            };

            placed.Points.Add(transform.Apply(xy.Values[0], xy.Values[1]));

            layout.Elements.Add(placed);
        }

        ///<summary>
        ///An AREF places a grid of instances. Its XY holds three points - the origin, then the far end of
        ///the column run and of the row run - so a single step is that span divided by the count.
        ///</summary>
        private void appendArray(ArefModel aref, Transform transform, int depth, ElementSource.Visit parent)
        {
            if (aref.XY?.Data is not Int4Data xy || xy.Values.Length < 6)
                return;

            if (aref.COLROW?.Data is not Int2Data colrow || colrow.Values.Length < 2)
                return;

            int columns = colrow.Values[0];
            int rows = colrow.Values[1];

            if (columns < 1 || rows < 1)
                return;

            double originX = xy.Values[0];
            double originY = xy.Values[1];

            double columnStepX = (xy.Values[2] - originX) / columns;
            double columnStepY = (xy.Values[3] - originY) / columns;
            double rowStepX = (xy.Values[4] - originX) / rows;
            double rowStepY = (xy.Values[5] - originY) / rows;

            var orientation = placementOf(aref.Strans, null, 0, transform);

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    double x = originX + (column * columnStepX) + (row * rowStepX);
                    double y = originY + (column * columnStepY) + (row * rowStepY);

                    var placement = orientation.Then(Transform.ForTranslation(x, y)).Then(transform);

                    appendReference(aref.SNAME, placement, depth, parent);
                }
            }
        }

        private void appendReference(Record? sname, Transform transform, int depth, ElementSource.Visit parent)
        {
            string name = (sname?.Data as AsciiData)?.Value ?? "";

            if (!structuresByName.TryGetValue(name, out var structure))
            {
                layout.UnresolvedReferences.Add(name);

                return;
            }

            appendStructure(structure, transform, depth + 1, parent);
        }

        #endregion **************************************************************************



        #region Reading the model ***********************************************************

        ///<summary>
        ///Reads a placement out of its STRANS block. Magnification and angle default to no change when
        ///their records are absent, and <paramref name="parent"/> is the transform this placement will be
        ///composed into - needed only to honor the absolute flags.
        ///</summary>
        private static Transform placementOf(StransModel? strans, Record? xy, int pointIndex, Transform parent)
        {
            var flags = Strans.From(strans?.STRANS?.Data);

            double magnification = 1;
            double angleInDegrees = 0;

            if (strans?.MAG?.Data is Real8Data mag)
                magnification = mag.Value;

            if (strans?.ANGLE?.Data is Real8Data angle)
                angleInDegrees = angle.Value;

            //An absolute magnification or angle is measured against the world, not the structure holding
            //it. Since the result is about to be composed with the parent anyway, the parent's own
            //contribution is divided out here and the composition puts the intended value back.
            if (flags.AbsoluteMagnification && parent.Scale != 0)
                magnification = magnification / parent.Scale;

            if (flags.AbsoluteAngle)
                angleInDegrees = angleInDegrees - parent.AngleInDegrees;

            double dx = 0;
            double dy = 0;

            if (xy?.Data is Int4Data points && points.Values.Length >= (pointIndex * 2) + 2)
            {
                dx = points.Values[pointIndex * 2];
                dy = points.Values[(pointIndex * 2) + 1];
            }

            return Transform.ForPlacement(flags.ReflectAboutX, magnification, angleInDegrees, dx, dy);
        }

        private static string? referencedName(ElementType element)
        {
            if (element is SrefModel sref)
                return (sref.SNAME?.Data as AsciiData)?.Value;

            if (element is ArefModel aref)
                return (aref.SNAME?.Data as AsciiData)?.Value;

            return null;
        }

        private static string nameOf(StructureModel structure)
        {
            return (structure.STRNAME?.Data as AsciiData)?.Value ?? "";
        }

        ///<summary>
        ///The layer/datatype pair an element is on, which is what <see cref="AdditionalGDSInformation"/>
        ///keyed its layers by - so this has to agree with the pair built there or the lookup misses and the
        ///element is dropped.
        ///</summary>
        public static LayerKey KeyOf(IHasLayer element)
        {
            short number = -1;

            if (element.LAYER?.Data is Int2Data layer)
                number = layer.Value;

            short dataType = LayerKey.UnknownDataType;

            if (element.DataTypeRecord?.Data is Int2Data elementDataType)
                dataType = elementDataType.Value;

            return new LayerKey(number, dataType);
        }

        #endregion **************************************************************************
    }
}
