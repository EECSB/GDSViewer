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
        ///Set when flattening stopped at its ceiling with more still to come: <see cref="GdsFlattener.MostElements"/>,
        ///or the one its caller gave - see <see cref="GdsFlattener.Flatten(GDS, int)"/>.
        ///
        ///**Which means what is here is not the whole layout**, and everything downstream of it - the
        ///picture, the area of a layer, the extent, a traced net - is about a part of the file. Nothing may
        ///treat that as an ordinary result, and the view says so where it cannot be missed.
        ///
        public bool Stopped { get; set; }

        ///
        ///Where the elements are, worked out the first time something asks and kept.
        ///
        ///**Built again if the list has been replaced or has changed length since**, which are the ways of
        ///changing a layout after the fact that anything could plausibly do by accident. A layout is meant to
        ///be replaced rather than edited - an edit flattens again, or <see cref="GdsFlattener.Reflatten"/>
        ///makes a new one - so this is a guard and not a promise to follow every change.
        ///
        public LayoutIndex Index
        {
            get
            {
                if (index is null || !index.Indexes(Elements))
                    index = new LayoutIndex(Elements);

                return index;
            }
        }

        private LayoutIndex? index;

        ///
        ///The same index, built offering to stop as it goes - see <see cref="LayoutIndex.BuildAsync"/> - and kept,
        ///so <see cref="Index"/> hands back this one afterwards rather than building its own.
        ///
        ///For a caller that wants a layout's first picture to cost nothing but the picture: the index is a walk
        ///of every element with arithmetic on each, as long as the flatten on a real chip, and built here it is
        ///one that can be stopped.
        ///
        public async ValueTask<LayoutIndex> IndexAsync(Func<ValueTask> pause)
        {
            if (index is null || !index.Indexes(Elements))
                index = await LayoutIndex.BuildAsync(Elements, pause);

            return index;
        }

        ///<summary>Hands this layout an index worked out from the one before it; see <see cref="LayoutIndex.After"/>.</summary>
        internal void Indexed(LayoutIndex worked)
        {
            index = worked;
        }
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
        ///| Elements  | Drawn in | WebAssembly memory |
        ///|-----------|----------|--------------------|
        ///| 200,000   | 1.3 s    | 239 MB             |
        ///| 400,000   | 2.4 s    | 495 MB             |
        ///| 800,000   | 4.8 s    | 919 MB             |
        ///| 1,600,000 | 9.4 s    | 1,732 MB           |
        ///| 3,200,000 | never    |                    |
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

        ///<summary>Where the walk offers to stop, when its caller gave it a pause - see <see cref="FlattenAsync(GDS, Func{ValueTask})"/>.</summary>
        private readonly Pauses pauses;

        ///<summary>How many elements each structure draws, placements and all, worked out as a walk that pauses asks - see <see cref="drawnBy"/>.</summary>
        private readonly Dictionary<string, long> drawn = new Dictionary<string, long>();

        ///<summary>Where this flatten stops: <see cref="MostElements"/>, unless its caller gave a ceiling of its own.</summary>
        private readonly int most;

        #endregion **************************************************************************



        #region Constructors ****************************************************************

        private GdsFlattener(GDS gds, Func<ValueTask>? pause, int most)
        {
            layers = gds.AdditionalInformation.Layers;
            pauses = new Pauses(pause);
            this.most = most;

            foreach (var structure in gds.StreamFormat.Structures)
                structuresByName[nameOf(structure)] = structure;
        }

        #endregion **************************************************************************



        #region Entry point *****************************************************************

        public static FlattenedLayout Flatten(GDS gds)
        {
            return Flatten(gds, MostElements);
        }

        ///
        ///The same, stopping at <paramref name="mostElements"/> rather than at <see cref="MostElements"/>.
        ///
        ///**For a caller that is not where the ceiling was measured**, which is a browser tab: the command line
        ///flattens on a desktop, with the machine's memory rather than a tab's two gigabytes - and a test can ask for a
        ///small ceiling without lowering the one every other test flattens under, which is one number for the process.
        ///
        public static FlattenedLayout Flatten(GDS gds, int mostElements)
        {
            Interlocked.Increment(ref flattens);

            var flattener = new GdsFlattener(gds, null, mostElements);

            return flattener.run(gds);
        }

        ///
        ///The same, offering to stop as it goes: <paramref name="pause"/> is awaited every so many elements, so a
        ///caller on a single thread can let its page answer while a real chip flattens - and throw from it to give
        ///the flatten up. See <see cref="Pauses"/>.
        ///
        ///**The same elements in the same order**, from the same pieces: what a structure draws is drawn by the
        ///walk <see cref="Flatten(GDS)"/> takes, and only the stepping from one placement to the next is this one's
        ///own - see <see cref="appendStructureAsync"/>, which says why it is not simply that walk made async.
        ///
        public static async ValueTask<FlattenedLayout> FlattenAsync(GDS gds, Func<ValueTask> pause)
        {
            Interlocked.Increment(ref flattens);

            var flattener = new GdsFlattener(gds, pause, MostElements);

            return await flattener.runAsync(gds);
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
            return Flatten(gds, structureName, MostElements);
        }

        ///<summary>The same, stopping at <paramref name="mostElements"/> - see <see cref="Flatten(GDS, int)"/>.</summary>
        public static FlattenedLayout Flatten(GDS gds, string structureName, int mostElements)
        {
            var flattener = new GdsFlattener(gds, null, mostElements);

            return flattener.runFrom(structureName);
        }

        ///
        ///The same, offering to stop as it goes - for a picture of one cell drawn while the page goes on answering, which
        ///for a chip's top cell is the whole chip. See <see cref="FlattenAsync(GDS, Func{ValueTask})"/>.
        ///
        public static async ValueTask<FlattenedLayout> FlattenAsync(GDS gds, string structureName, Func<ValueTask> pause)
        {
            var flattener = new GdsFlattener(gds, pause, MostElements);

            return await flattener.runFromAsync(structureName);
        }

        private FlattenedLayout runFrom(string structureName)
        {
            if (structuresByName.TryGetValue(structureName, out var structure))
                appendStructure(structure, Transform.Identity, 0, null);

            return layout;
        }

        private async ValueTask<FlattenedLayout> runFromAsync(string structureName)
        {
            if (structuresByName.TryGetValue(structureName, out var structure))
                await appendStructureAsync(structure, Transform.Identity, 0, null);

            return layout;
        }

        private FlattenedLayout run(GDS gds)
        {
            foreach (var structure in topLevelStructures(gds))
                appendStructure(structure, Transform.Identity, 0, null);

            return layout;
        }

        private async ValueTask<FlattenedLayout> runAsync(GDS gds)
        {
            foreach (var structure in topLevelStructures(gds))
                await appendStructureAsync(structure, Transform.Identity, 0, null);

            return layout;
        }

        ///
        ///The layout after an edit that changed some elements in place, worked out from the layout before it -
        ///or null when it cannot be, and the library has to be flattened again.
        ///
        ///**An edit used to flatten the whole library again**, which on a real chip is most of what the edit
        ///costs: 222,274 elements for the RGB mixer, four to seven seconds of a twenty-second drag on the dev
        ///server, to move one rectangle. But moving a shape, dragging a corner, turning it, changing its layer
        ///or retyping a label changes what that one element draws and nothing else - the same elements in the
        ///same order, every one of them where it was in the file but these. So those are drawn again from the
        ///library, at every place their cell is placed, and everything else is the very same object it was,
        ///box and index and all. See <see cref="LayoutEdit.Reshaped"/>, which is how an edit says which.
        ///
        ///**Null for anything it cannot be sure of**, which is cheaper than being wrong: a placement changed,
        ///which moves everything under it; an element that draws nothing now, or that drew nothing before and
        ///might now; nothing changed that this layout draws at all. The caller flattens again, which is always
        ///right.
        ///
        ///Not counted in <see cref="Flattens"/>, which counts whole-library flattens and is what a test reads
        ///to see that a move no longer is one.
        ///
        public static FlattenedLayout? Reflatten(FlattenedLayout previous, GDS gds, IReadOnlyCollection<ElementModel>? reshaped)
        {
            if (reshaped is null || reshaped.Count == 0)
                return null;

            foreach (var model in reshaped)
            {
                if (model.Element is SrefModel || model.Element is ArefModel)
                    return null;
            }

            var changed = new HashSet<ElementModel>(reshaped);
            var layers = gds.AdditionalInformation.Layers;
            var elements = new List<Element>(previous.Elements.Count);
            var redrawn = new List<int>();

            for (int i = 0; i < previous.Elements.Count; i++)
            {
                var element = previous.Elements[i];

                if (element.Source is not ElementSource source || !changed.Contains(source.Model))
                {
                    elements.Add(element);

                    continue;
                }

                if (placedAgain(source, layers) is not Element placed)
                    return null;

                elements.Add(placed);
                redrawn.Add(i);
            }

            if (redrawn.Count == 0)
                return null;

            var layout = new FlattenedLayout
            {
                Elements = elements,
                UnresolvedReferences = previous.UnresolvedReferences,
                DepthLimitReached = previous.DepthLimitReached,
                Stopped = previous.Stopped
            };

            layout.Indexed(LayoutIndex.After(previous.Index, elements, redrawn));

            return layout;
        }

        ///
        ///The area an element covers, in its own cell's coordinates - a boundary's or a box's corners, a path's
        ///outline - or null for one that covers none: a label, a placement, a path of no width.
        ///
        ///**What an edit that works on areas has to start from.** A path's XY is its centerline, and reading it
        ///as corners takes a wire with one bend for the triangle its three points make - which is what growing
        ///or combining a path did until this was here. The outline is the flattener's own, so an edit works on
        ///exactly the shape the picture shows.
        ///
        public static List<Element.Point>? OutlineOf(ElementModel model)
        {
            var element = model.Element;

            if (element is TextModel || element is SrefModel || element is ArefModel)
                return null;

            if (element.XY?.Data is not Int4Data xy)
                return null;

            var values = xy.Values;
            var points = new List<Element.Point>(values.Length / 2);

            for (int i = 0; i + 1 < values.Length; i += 2)
                points.Add(new Element.Point(values[i], values[i + 1]));

            if (element is not PathModel path)
                return points;

            if (widthOf(path) <= 0)
                return null;

            return PathOutline.Build(points, widthOf(path), pathTypeOf(path), extensionOf(path.BGNEXTN), extensionOf(path.ENDEXTN));
        }

        ///<summary>One element drawn again where it was drawn before - same placement, same source.</summary>
        private static Element? placedAgain(ElementSource source, Dictionary<LayerKey, Layer> layers)
        {
            var element = source.Model.Element;

            if (element is TextModel text)
                return textOf(text, source.Placement, source, layers);

            return geometryOf(element, source.Placement, source, layers);
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
            if (enter(structure, transform, depth, parent) is not ElementSource.Visit visit)
                return;

            foreach (var elementModel in structure.Elements)
                appendElement(elementModel, transform, depth, visit);

            expanding.Remove(visit.Structure);
        }

        ///
        ///The same expansion, stepping from one placement to the next with an offer to stop between - for
        ///<see cref="FlattenAsync(GDS, Func{ValueTask})"/>.
        ///
        ///**Only a placement that draws a lot is walked this way**, and anything that draws less is drawn by the plain
        ///walk above in one go. A loop inside an async method keeps its locals on the heap, and in the browser's
        ///interpreter that made a flatten a third slower when every structure went through here. A chip is mostly
        ///the cheap case - a top cell placing thousands of standard cells pauses between them and draws each one
        ///plainly - and what a structure draws is counted before it is walked into: see <see cref="drawnBy"/>.
        ///
        private async ValueTask appendStructureAsync(StructureModel structure, Transform transform, int depth, ElementSource.Visit? parent)
        {
            if (enter(structure, transform, depth, parent) is not ElementSource.Visit visit)
                return;

            foreach (var elementModel in structure.Elements)
            {
                var element = elementModel.Element;

                if (element is SrefModel sref && drawsALot(sref, 1))
                    await appendReferenceAsync(sref.SNAME, placementOf(sref.Strans, sref.XY, 0, transform).Then(transform), depth, visit);
                else if (element is ArefModel aref && drawsALot(aref, placesIn(aref)))
                    await appendArrayAsync(aref, transform, depth, visit);
                else
                    appendElement(elementModel, transform, depth, visit);

                await pauses.Tick();
            }

            expanding.Remove(visit.Structure);
        }

        ///
        ///Whether a structure may be expanded here, and the visit its elements share if so - or null when it may not:
        ///the layout has reached its ceiling, the nesting is too deep, or the structure is inside itself.
        ///
        private ElementSource.Visit? enter(StructureModel structure, Transform transform, int depth, ElementSource.Visit? parent)
        {
            //
            //Checked here rather than at each Add, because this is the point the work is decided at: an
            //array of ten thousand places calls into this ten thousand times, and stopping at the door of
            //each one costs nothing where stopping per element would still walk every reference.
            //
            if (layout.Elements.Count >= most)
            {
                layout.Stopped = true;

                return null;
            }

            if (depth > MaximumDepth)
            {
                layout.DepthLimitReached = true;

                return null;
            }

            string name = nameOf(structure);

            //A structure may legitimately appear many times in one layout, but not inside itself.
            if (!expanding.Add(name))
            {
                layout.DepthLimitReached = true;

                return null;
            }

            //Built once for the whole structure and shared by every element in it, which is what keeps the
            //chain and the transform from being stored on each of them - see ElementSource.Visit.
            return new ElementSource.Visit
            {
                Structure = name,
                Transform = transform,
                Path = reached(parent, name),
                Parent = parent
            };
        }

        ///
        ///How many elements a placement draws below which the walk that pauses draws it in one go: a few milliseconds of
        ///flattening on the slowest host this runs on, so a pause is never far off.
        ///
        private const long DrawnInOneGo = 4096;

        ///<summary>Whether <paramref name="places"/> placements of what <paramref name="placement"/> names draw more than <see cref="DrawnInOneGo"/> between them.</summary>
        private bool drawsALot(ElementType placement, long places)
        {
            return places * drawnBy(referencedName(placement) ?? "") > DrawnInOneGo;
        }

        ///
        ///How many elements a structure draws, with everything it places - counted once a structure and kept, which is
        ///a walk of the library's own elements rather than of the layout they make.
        ///
        ///**An estimate to decide by, not a count to rely on.** A structure that reaches itself counts nothing for the
        ///second visit, as the flatten draws nothing for it, and anything past the flatten's ceiling counts as the
        ///ceiling. All this is asked is whether a placement is big.
        ///
        private long drawnBy(string name)
        {
            if (drawn.TryGetValue(name, out long known))
                return known;

            if (!structuresByName.TryGetValue(name, out var structure))
                return 0;

            //Under way, so a structure that reaches itself finds nothing more on the way back round.
            drawn[name] = 0;

            long count = 0;

            foreach (var elementModel in structure.Elements)
            {
                var element = elementModel.Element;

                if (element is SrefModel || element is ArefModel)
                    count += placesIn(element) * drawnBy(referencedName(element) ?? "");
                else
                    count++;

                if (count >= most)
                {
                    count = most;

                    break;
                }
            }

            drawn[name] = count;

            return count;
        }

        ///<summary>How many places a placement puts its structure: one for an SREF, the columns times the rows for an AREF, none for one that cannot be read.</summary>
        private static long placesIn(ElementType placement)
        {
            if (placement is not ArefModel aref)
                return 1;

            if (aref.COLROW?.Data is not Int2Data colrow || colrow.Values.Length < 2)
                return 0;

            return Math.Max(0, (long)colrow.Values[0]) * Math.Max(0, (long)colrow.Values[1]);
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
            if (geometryOf(element, transform, source, layers) is Element placed)
                layout.Elements.Add(placed);
        }

        ///<summary>One of those placed, or null when it draws nothing: on a layer the table does not hold, or with no corners.</summary>
        private static Element? geometryOf(ElementType element, Transform transform, ElementSource source, Dictionary<LayerKey, Layer> layers)
        {
            if (element is not IHasLayer hasLayer)
                return null;

            if (!layers.TryGetValue(KeyOf(hasLayer), out var layer))
                return null;

            if (element.XY?.Data is not Int4Data xy)
                return null;

            var values = xy.Values;
            Element placed;

            //A path's XY is a centerline, so it becomes an outline before being placed. Outlining first
            //and transforming after is what lets a magnified placement scale the width along with the
            //shape - widening it afterwards would need the transform picked apart again.
            if (element is PathModel path)
            {
                var centerline = new List<Element.Point>(values.Length / 2);

                for (int i = 0; i + 1 < values.Length; i += 2)
                    centerline.Add(new Element.Point(values[i], values[i + 1]));

                var outline = PathOutline.Build(centerline, widthOf(path), pathTypeOf(path), extensionOf(path.BGNEXTN), extensionOf(path.ENDEXTN));

                //A path of no width has no outline to build, so what comes back is the centerline itself -
                //a run rather than a ring, which is what the renderer has to be told. See Element.IsOpen.
                placed = new Element { Layer = layer, Source = source, IsOpen = widthOf(path) <= 0, Points = new List<Element.Point>(outline.Count) };

                foreach (var point in outline)
                    placed.Points.Add(transform.Apply(point.X, point.Y));
            }
            else
            {
                //
                //**Straight from the record into a list of the right size.** This went through a list of the
                //corners as read and was then copied into one that grew as it went - three lists and their
                //arrays for every shape, where one will do. A flatten is the bulk of what opening a real chip
                //costs, and of an edit that adds or takes away a shape, and allocation is the bulk of a flatten.
                //
                placed = new Element { Layer = layer, Source = source, Points = new List<Element.Point>(values.Length / 2) };

                for (int i = 0; i + 1 < values.Length; i += 2)
                    placed.Points.Add(transform.Apply(values[i], values[i + 1]));
            }

            if (placed.Points.Count == 0)
                return null;

            return placed;
        }

        ///
        ///A path's width as a size. No WIDTH record is no width, which the format treats as zero; a negative one
        ///is the format's absolute width - one the parent's magnification does not scale - and is that size, as
        ///every other reader draws it. Taken as it stood, a path of WIDTH -200 was a hairline with no height.
        ///
        private static int widthOf(PathModel path)
        {
            if (path.WIDTH?.Data is Int4Data width)
                return (int)Math.Min(int.MaxValue, Math.Abs((long)width.Value));

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
            if (textOf(text, transform, source, layers) is Element placed)
                layout.Elements.Add(placed);
        }

        private static Element? textOf(TextModel text, Transform transform, ElementSource source, Dictionary<LayerKey, Layer> layers)
        {
            if (!layers.TryGetValue(KeyOf(text), out var layer))
                return null;

            if (text.XY?.Data is not Int4Data xy || xy.Values.Length < 2)
                return null;

            var placed = new Element
            {
                Layer = layer,
                Source = source,
                Text = (text.TextBody?.STRING?.Data as AsciiData)?.Value ?? "",
                Presentation = TextPresentation.From(text.TextBody?.PRESENTATION?.Data)
            };

            placed.Points.Add(transform.Apply(xy.Values[0], xy.Values[1]));

            return placed;
        }

        ///<summary>An AREF's instances, each where the array puts it - see <see cref="placesOf"/>.</summary>
        private void appendArray(ArefModel aref, Transform transform, int depth, ElementSource.Visit parent)
        {
            foreach (var placement in placesOf(aref, transform))
                appendReference(aref.SNAME, placement, depth, parent);
        }

        ///
        ///The same, offering to stop between one instance and the next - see <see cref="appendStructureAsync"/>. Each
        ///instance is walked into the way that one walks a placement: in one go when it draws little.
        ///
        private async ValueTask appendArrayAsync(ArefModel aref, Transform transform, int depth, ElementSource.Visit parent)
        {
            bool eachALot = drawsALot(aref, 1);

            foreach (var placement in placesOf(aref, transform))
            {
                if (eachALot)
                    await appendReferenceAsync(aref.SNAME, placement, depth, parent);
                else
                    appendReference(aref.SNAME, placement, depth, parent);

                //Each place an offer to stop, since a place whose cell is missing adds no element to count.
                await pauses.Tick();
            }
        }

        ///<summary>
        ///An AREF places a grid of instances. Its XY holds three points - the origin, then the far end of
        ///the column run and of the row run - so a single step is that span divided by the count.
        ///
        ///Each instance's transform, composed into <paramref name="transform"/>, column by column; none for an array
        ///whose numbers cannot be read.
        ///</summary>
        private static IEnumerable<Transform> placesOf(ArefModel aref, Transform transform)
        {
            if (aref.XY?.Data is not Int4Data xy || xy.Values.Length < 6)
                yield break;

            if (aref.COLROW?.Data is not Int2Data colrow || colrow.Values.Length < 2)
                yield break;

            int columns = colrow.Values[0];
            int rows = colrow.Values[1];

            if (columns < 1 || rows < 1)
                yield break;

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

                    yield return orientation.Then(Transform.ForTranslation(x, y)).Then(transform);
                }
            }
        }

        private void appendReference(Record? sname, Transform transform, int depth, ElementSource.Visit parent)
        {
            if (referenced(sname) is StructureModel structure)
                appendStructure(structure, transform, depth + 1, parent);
        }

        private async ValueTask appendReferenceAsync(Record? sname, Transform transform, int depth, ElementSource.Visit parent)
        {
            if (referenced(sname) is StructureModel structure)
                await appendStructureAsync(structure, transform, depth + 1, parent);
        }

        ///<summary>The structure a placement names, or null - with the name noted as unresolved - when the library does not hold it.</summary>
        private StructureModel? referenced(Record? sname)
        {
            string name = (sname?.Data as AsciiData)?.Value ?? "";

            if (!structuresByName.TryGetValue(name, out var structure))
            {
                layout.UnresolvedReferences.Add(name);

                return null;
            }

            return structure;
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
