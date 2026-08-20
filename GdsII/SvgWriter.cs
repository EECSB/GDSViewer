using System.Globalization;
using System.Text;

namespace GdsII
{
    ///<summary>
    ///Turns a flattened layout into the SVG markup the 2D view drops inside its own &lt;svg&gt; element.
    ///
    ///Deliberately a plain class rather than part of the component. This is where that view's actual rules
    ///live - which layers are drawn, how a label is justified, how a number is written - and none of it
    ///needs a browser, a renderer or a DOM. In here it can be tested directly; inside the .razor file the
    ///only way to check any of it was to load the app and look, which is how the culture bugs in it
    ///survived a suite of 340 tests.
    ///</summary>
    public static class SvgWriter
    {
        ///<summary>
        ///Label height in layout units. Large next to a standard cell because the view shows a whole cell
        ///at once, and a name too small to read would not be worth drawing.
        ///</summary>
        public const int LabelFontSize = 60;

        ///<summary>
        ///Every element on a visible layer, as one polygon or one text element.
        ///
        ///Nothing here is escaped except a label's own string: the colors come from a fixed palette and the
        ///coordinates are integers out of the file, so a label is the only place a value from the file
        ///reaches the markup as text.
        ///
        ///<paramref name="labeledLayers"/> says whose TEXT elements to draw, separately from whose
        ///geometry. Labels need their own answer even though a label is on a layer like anything else,
        ///because one usually shares its layer with the shapes it annotates - so hiding the pair to be rid
        ///of the text takes the geometry with it, and on a dense cell the labels are the part that turns
        ///the view into a wall of writing.
        ///
        ///A set rather than a single switch, because which labels are worth reading is a per-layer
        ///question: the pin names on one metal layer can be the reason the view is open while every other
        ///layer's are noise. It is intersected with <paramref name="visibleLayers"/> below rather than
        ///trusted on its own, so a layer that is switched off cannot leave its labels floating over the
        ///geometry that is left.
        ///
        ///<paramref name="visibleLayers"/> is a set of pairs rather than whatever the caller happens to
        ///keep its layer list in. This took the shell's own row model until the library was separated out,
        ///which meant something about GDSII had a type called CheckboxItem in its public surface.
        ///</summary>
        public static string Build(FlattenedLayout layout, IReadOnlySet<LayerKey> visibleLayers, float opacity, IReadOnlySet<LayerKey> labeledLayers)
        {
            return Build(layout, visibleLayers, opacity, labeledLayers, null);
        }

        ///<summary>
        ///The same, with one cell singled out as the one being edited.
        ///
        ///Everything outside that cell is still drawn and is marked as out of it, so the view can fade it:
        ///editing in place means keeping the surroundings visible while only one cell answers to the
        ///pointer. Null for no context, which is the whole layout looked at rather than edited.
        ///</summary>
        public static string Build(
            FlattenedLayout layout,
            IReadOnlySet<LayerKey> visibleLayers,
            float opacity,
            IReadOnlySet<LayerKey> labeledLayers,
            CellContext? context,
            Bounds? shown = null,
            long smallest = 0,
            string picture = "")
        {
            var builder = new StringBuilder();

            //
            //Off the whole layout rather than off what is on screen.
            //
            //`shown` is the culling window, which changes on every pan - and a pattern whose tile changed
            //size as the view moved would crawl across the shapes. The layout's own extent is the one thing
            //here that does not move.
            //
            appendStyle(builder, layout, visibleLayers, opacity, TileFor(layout), picture);

            //One group per layer and editing state, each collecting the shapes that belong to it. Kept in
            //the order the first shape of each appeared, so what is drawn over what stays as it was: a
            //dictionary's own order is not promised, and the answer here is visible.
            var order = new List<(LayerKey Layer, string Classes, bool Open)>();
            var groups = new Dictionary<(LayerKey, string, bool), Merged>();
            var labels = new StringBuilder();

            for (int index = 0; index < layout.Elements.Count; index++)
            {
                var element = layout.Elements[index];

                //Skip the element if the layer it is on is disabled, or is not listed at all.
                if (!visibleLayers.Contains(element.Layer.Key))
                    continue;

                if (Beyond(element, shown, smallest))
                    continue;

                string classes = classesFor(element, context);

                if (element.Text is not null)
                {
                    if (labeledLayers.Contains(element.Layer.Key))
                        appendLabel(labels, element, index, classes);

                    continue;
                }

                var key = (element.Layer.Key, classes, element.IsOpen);

                if (!groups.TryGetValue(key, out var merged))
                {
                    merged = new Merged();

                    groups.Add(key, merged);
                    order.Add(key);
                }

                merged.Add(element, index);
            }

            foreach (var key in order)
                appendMerged(builder, key.Layer, key.Classes, key.Open, groups[(key.Layer, key.Classes, key.Open)], picture);

            //Labels last, so a name is never drawn under the geometry of a layer that comes after it.
            builder.Append(labels);

            return builder.ToString();
        }

        ///
        ///The shapes of one layer in one editing state, gathering into a single path.
        ///
        ///**A subpath each, not a union.** `Booleans.MergeByLayer` would also give one shape per layer and
        ///would dissolve every internal outline with it; a subpath per element keeps each shape's own
        ///stroke, so the picture still says where one shape ends and the next begins.
        ///
        private sealed class Merged
        {
            public readonly StringBuilder Drawn = new StringBuilder();
            public readonly StringBuilder Elements = new StringBuilder();

            public void Add(Element element, int index)
            {
                if (Elements.Length > 0)
                {
                    Drawn.Append(' ');
                    Elements.Append(' ');
                }

                Elements.Append(index.ToString(CultureInfo.InvariantCulture));

                appendSubpath(Drawn, element);
            }
        }

        ///
        ///One layer's shapes, as one node.
        ///
        ///**This is where the drawing stopped being one node per shape.** Measured in a browser, twenty
        ///thousand polygons cost 50.8 ms of raster per pan frame - about 18 frames a second, and the one
        ///number that had not moved through any of the work before this. The same picture as eight paths
        ///costs 16.8 ms, which is the display's own limit rather than the drawing's.
        ///
        ///What it costs is that a layer composites once rather than per shape, so two shapes on one layer
        ///that overlap stop double-darkening. On a real cell that is invisible - shapes on a single layer
        ///are the same conductor and do not overlap - and where it does show, the strokes still mark every
        ///shape. It is also what KLayout does, and what this app's own 3D view has always done.
        ///
        private static void appendMerged(StringBuilder builder, LayerKey layer, string classes, bool open, Merged merged, string picture)
        {
            builder.Append("<path class=\"");
            builder.Append(ClassFor(layer));

            //What tells this picture's shapes from another picture's on the same page; see PictureToken.
            if (picture.Length > 0)
            {
                builder.Append(' ');
                builder.Append(picture);
            }

            //An open run is stroked rather than filled: a path of no width has no outline, so what there is
            //to draw is the line down the middle of it. See Element.IsOpen.
            if (open)
            {
                builder.Append(' ');
                builder.Append(OpenRunClass);
            }

            if (classes.Length > 0)
            {
                builder.Append(' ');
                builder.Append(classes);
            }

            //nonzero, not evenodd, which would turn an overlap between two of a layer's shapes into a hole.
            builder.Append("\" fill-rule=\"nonzero\" ");
            builder.Append(ElementsAttribute);
            builder.Append("=\"");
            builder.Append(merged.Elements);
            builder.Append("\" d=\"");
            builder.Append(merged.Drawn);
            builder.Append("\"/>");
        }

        ///
        ///Whether a shape can be left out: off the screen, or too small to see.
        ///
        ///**The only two reasons a shape may be dropped, and both have to be safe to be wrong about.** Off
        ///screen is decided against a box the caller has already grown by a margin, so a shape half in and
        ///half out still draws; too small is decided against the area one pixel covers at the current zoom,
        ///which is a shape that would be drawn and never seen.
        ///
        ///A label is never dropped for being small - a name is the same size on screen at every zoom, so its
        ///own extent says nothing about whether it can be read.
        ///
        ///Both off by default, because most layouts do not need either and a viewer that quietly leaves
        ///things out is a viewer nobody should trust with a layout they are checking. See the threshold in
        ///Viewer2DSvg.
        ///
        private static bool Beyond(Element element, Bounds? shown, long smallest)
        {
            if (shown is Bounds visible && !visible.Intersects(element.Box))
                return true;

            if (smallest <= 0 || element.Text is not null)
                return false;

            //Its own box against a pixel's worth of area. Not the drawn area, which would need the shape
            //walked - a box is what is already to hand and is never smaller than what it holds.
            return element.Box.Width * element.Box.Height < smallest;
        }

        ///
        ///Everything every shape has in common, written once instead of on each of them.
        ///
        ///**The markup was mostly boilerplate.** Each shape carried its own `fill`, `opacity`, `stroke` and
        ///`stroke-width` - about a hundred bytes of attributes repeated per element, where the coordinates of
        ///a rectangle are forty. At twenty thousand elements that is a megabyte and a half of the same words
        ///over and over, marshalled into the browser and parsed there; measured, the whole document was 3.3 M
        ///characters and the browser's share of opening it was ninety-nine percent of the wall clock.
        ///
        ///The color is per layer, so it becomes a class per layer. Everything else is the same on every
        ///shape in the view and becomes one rule.
        ///
        ///**Rules rather than a wrapping group**, which would have been fewer bytes still and a different
        ///picture: opacity on a group composites the group as one, so overlapping shapes on a layer would
        ///stop double-darkening. A rule that matches each shape keeps the compositing exactly as it was.
        ///
        ///The `!important` declarations in app.css - the faded context, the outlined instances, the selection
        ///- still win over these, the same way they already won over the attributes these replace.
        ///
        private static void appendStyle(StringBuilder builder, FlattenedLayout layout, IReadOnlySet<LayerKey> visibleLayers, float opacity, long tile, string picture)
        {
            //Which layers are actually drawn, so the block names those and not a whole PDK's worth. Walked
            //rather than taken from the layer table because a layer with nothing on it needs no rule.
            var used = new Dictionary<LayerKey, Layer>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is null && visibleLayers.Contains(element.Layer.Key))
                    used.TryAdd(element.Layer.Key, element.Layer);
            }

            if (used.Count == 0)
                return;

            //
            //**Scoped by the generated class alone, not by an ancestor.**
            //
            //Scoping to `#gdsSVG` was the obvious way to write these and is wrong twice over. A downloaded
            //image and anything `gds svg` writes has no element with that id, so a standalone file came out
            //with no color at all - every shape black-stroked and unfilled. And a bare `polygon{...}` would
            //have been worse: the drawing preview, the rubber band and the snap mark are all polygons that
            //JavaScript puts inside the same SVG, and a stylesheet rule beats the attributes those set.
            //
            //A class no other element carries needs no scope. The shared declarations are repeated onto each
            //layer's rule rather than given one of their own - a few hundred bytes across the whole block,
            //against the megabytes this exists to save.
            //
            appendPatterns(builder, used, tile, picture);

            builder.Append("<style>");

            string transparency = FormatOpacity(opacity);

            //
            //**A second class in front of the layer's own, when the page holds more than one picture.**
            //
            //A `<style>` inside an inline SVG is not scoped to that SVG - the HTML parser hoists it into the
            //document, so every rule in it applies to every picture on the page. Two pictures of the same
            //file therefore wrote the same selector twice and the later one won for both: pointing at a cell
            //in the tree redrew the *whole layout* at the thumbnail's 0.85 instead of the slider's 0.5, and
            //moving the pointer away put it back. Measured, not deduced.
            //
            //Empty for the main view, so its markup and every spec that reads it are exactly as they were.
            //
            string scope = "";

            if (picture.Length > 0)
                scope = "." + picture;

            foreach (var layer in used)
            {
                string name = ClassFor(layer.Key);

                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "path{3}.{0}{{fill:{1};stroke:black;stroke-width:6;opacity:{2}}}",
                    name,
                    fillFor(layer.Key, layer.Value, picture),
                    transparency,
                    scope);

                //An open run is stroked rather than filled, and at a width that does not scale - it stands
                //in for a path of no width, which has no thickness to scale. See Element.IsOpen.
                //
                //Two classes, so this beats the rule above it on specificity rather than on order: the two
                //are written per layer, and a layer whose runs came before its rings would otherwise take
                //whichever was emitted last.
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "path{4}.{0}.{3}{{fill:none;stroke:{1};stroke-width:2;vector-effect:non-scaling-stroke;opacity:{2}}}",
                    name,
                    layer.Value.Color,
                    transparency,
                    OpenRunClass,
                    scope);
            }

            builder.Append("</style>");
        }

        ///
        ///What a layer's shapes are filled with: its color, or a pattern painted in it.
        ///
        ///The pattern carries the color rather than sitting over it - see <see cref="appendPatterns"/> -
        ///so this is one or the other and never both.
        ///
        private static string fillFor(LayerKey key, Layer layer, string picture)
        {
            if (layer.Fill == LayerFill.None)
                return layer.Color;

            return FormattableString.Invariant($"url(#{PatternIdFor(key, picture)})");
        }

        ///<summary>The id a layer's pattern is found under, prefixed so nothing else in the document collides.</summary>
        public const string PatternPrefix = "fill_";

        ///
        ///Where a layer's pattern is defined, for one picture.
        ///
        ///Scoped by picture for the same reason the rules above are, and this one bites harder: an id is
        ///resolved document-wide and the *first* match wins, so two pictures of one file would both take
        ///whichever was written first - and their tiles differ, because a tile is sized from the layout and
        ///a cell is smaller than the file it is in. The whole layout would be stippled at a thumbnail's
        ///pitch for as long as the thumbnail was on screen.
        ///
        public static string PatternIdFor(LayerKey key, string picture)
        {
            if (picture.Length == 0)
                return PatternPrefix + ClassFor(key);

            return FormattableString.Invariant($"{PatternPrefix}{picture}_{ClassFor(key)}");
        }

        ///
        ///A name turned into something that can be a CSS class and part of an id.
        ///
        ///Only what is safe in both, and never starting with a digit - a class may not. Names that differ
        ///only in punctuation collide here, which is harmless: two pictures with the same token draw the
        ///same file at the same tile, so taking either one's rules gives the same picture.
        ///
        public static string PictureToken(string name)
        {
            var token = new StringBuilder("p");

            foreach (char letter in name)
            {
                if (char.IsAsciiLetterOrDigit(letter))
                    token.Append(letter);
                else
                    token.Append('_');
            }

            return token.ToString();
        }

        ///
        ///How wide one repeat of a pattern is, as a fraction of the picture's longer side.
        ///
        ///**Sized from the layout rather than fixed**, because a database unit is not a length anybody can
        ///reason about: the bundled Mosfet is 2,000 units across and a full die is tens of millions, so one
        ///number of units is either invisible on the second or bigger than the first. A hundred repeats
        ///across the picture is a texture at any scale.
        ///
        ///The 2D view then holds it at a constant *screen* size as the zoom changes - see scalePatterns in
        ///the interop - which is what KLayout does and what makes a stipple readable when you are close in.
        ///This is the size a picture with no viewer gets: a downloaded SVG, or one `gds svg` wrote.
        ///
        ///**Thirty-two, arrived at by looking.** The first attempt was a hundred, which is a tile of ten
        ///units on a thousand-unit layout - three pixels at any size a picture is actually looked at, and
        ///every one of the seven patterns rendered as the same faint tone. Rendered side by side they were
        ///indistinguishable from each other and from a solid fill. At thirty-two a tile is ten pixels on a
        ///320px thumbnail and twenty on a full view, which is a texture you can name.
        ///
        private const int RepeatsAcross = 32;

        ///<summary>The smallest tile worth writing, so a degenerate layout cannot produce a zero-width pattern.</summary>
        private const long LeastTile = 1;

        ///
        ///How big one repeat of a pattern is, in the layout's own units.
        ///
        ///Public because the view has to write it into the markup for the interop to read back; a pattern's
        ///size is not something the DOM can be asked for once it is a transform.
        ///
        public static long TileFor(FlattenedLayout layout)
        {
            var whole = Bounds.Empty;

            foreach (var element in layout.Elements)
            {
                if (element.Text is null)
                    whole = whole.Union(element.Box);
            }

            if (whole.IsEmpty)
                return LeastTile;

            long across = Math.Max(whole.Width, whole.Height) / RepeatsAcross;

            return Math.Max(LeastTile, across);
        }

        ///
        ///One &lt;pattern&gt; per layer that asked for one, in the layer's own color.
        ///
        ///**The color goes into the pattern rather than under it.** The obvious build is a solid fill with a
        ///pattern painted over the top, which needs two paths per layer - and the picture is one path per
        ///layer precisely because twenty thousand nodes is what pan could not afford. A pattern whose tile
        ///already holds the background and the motif keeps the count where it is.
        ///
        ///`patternUnits="userSpaceOnUse"` so a tile is the same size everywhere rather than a fraction of
        ///each shape: the alternative, objectBoundingBox, gives a small shape a small pattern and a large
        ///one a large pattern, which is a picture where the texture says how big the shape is.
        ///
        ///Only the layers that are drawn and asked for one, the same as the rules below - a definition
        ///nothing references is bytes in every document for the sake of a layer that is switched off.
        ///
        private static void appendPatterns(StringBuilder builder, Dictionary<LayerKey, Layer> used, long tile, string picture)
        {
            bool any = false;

            foreach (var layer in used)
            {
                if (layer.Value.Fill == LayerFill.None)
                    continue;

                if (!any)
                {
                    builder.Append("<defs>");
                    any = true;
                }

                appendPattern(
                    builder,
                    PatternIdFor(layer.Key, picture),
                    layer.Value.Fill,
                    layer.Value.Color,
                    MarksColorOf(layer.Value),
                    tile,
                    layer.Value.PatternPixels);
            }

            if (any)
                builder.Append("</defs>");
        }

        ///
        ///What a shape action looks like, for the buttons and the menu lines that offer it.
        ///
        ///**One drawing per action, in one place, because there are two of everything.** Every one of these
        ///is offered twice - as a button on the selection panel and as a line in the menu over the shapes -
        ///and the panel's conditions and the menu's are already written to be the same conditions. A glyph
        ///drawn once in each would be the one part of that pair free to drift.
        ///
        ///In the library rather than in the component for the reason <see cref="SwatchFor"/> is: this is
        ///string building over a fixed set of names, it needs no browser, and here it can be tested.
        ///
        public enum ShapeIcon
        {
            ///<summary>Two shapes becoming the ground they cover between them.</summary>
            Union,

            ///<summary>The first, with the rest cut out of it.</summary>
            Subtract,

            ///<summary>Only where both cover.</summary>
            Intersect,

            ///<summary>Where an odd number cover, so an overlap of two cancels out.</summary>
            Exclude,

            AlignLeft,
            AlignCenterX,
            AlignRight,
            AlignTop,
            AlignMiddleY,
            AlignBottom,

            ///<summary>Even out the gaps left to right.</summary>
            SpaceAcross,

            ///<summary>Even out the gaps top to bottom.</summary>
            SpaceDown,

            //And the actions the menu offers that are not about how shapes sit relative to each other.
            //Drawn here rather than in the markup so a line and the panel button beside it cannot show
            //two different pictures of one action.
            Copy,
            Cut,
            Delete,
            Paste,
            Turn,
            Array,
            NewCell,

            ///
            ///Everything electrically joined to a shape, found by climbing the vias.
            ///
            ///**This is a reversal.** The menu deliberately left this line without a glyph - the note by the
            ///empty column said a mark nobody recognizes is worse than a gap, and that was right about the
            ///marks that had been tried. Three nodes joined by two runs is not one of those: it is how the
            ///word "net" is drawn everywhere, and what this action gives back is exactly a connected set.
            ///
            TraceNet,

            //The four the Turn line opens onto, and the pencil the cell bar renames with. Same
            //reasoning as the rest: each is a button and a menu line, and one drawing keeps the two
            //from becoming two pictures of one action.
            TurnLeft,
            TurnRight,
            MirrorAcross,
            MirrorDown,
            Rename
        }

        ///
        ///The icon's markup, to drop inside a 16-unit viewBox.
        ///
        ///**Two shapes and a rule, throughout.** Every one of these says something about how shapes sit
        ///relative to each other, so every one of them draws more than one shape - a single square could
        ///only ever be a picture of a square. The aligns and the spaces add the line the shapes are being
        ///brought to, which is what separates "left" from "right" at this size: the bars are nearly the
        ///same picture either way round, and the rule is what says which edge is meant.
        ///
        ///`currentColor` throughout, so a line in a menu and a button on a panel each take their own.
        ///
        public static string IconFor(ShapeIcon icon)
        {
            //Filled where the answer is an area and stroked where it is an edge, which is the distinction
            //the four booleans are actually about.
            const string Held = "fill=\"currentColor\"";
            const string Edge = "fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\"";
            const string Rule = "stroke=\"currentColor\" stroke-width=\"1.4\" stroke-linecap=\"round\"";

            if (icon == ShapeIcon.Union)
            {
                //Both solid: what is left is everything either of them covered.
                return $"<rect x=\"1.6\" y=\"1.6\" width=\"8.8\" height=\"8.8\" rx=\"1\" {Held} />"
                    + $"<rect x=\"5.6\" y=\"5.6\" width=\"8.8\" height=\"8.8\" rx=\"1\" {Held} />";
            }

            if (icon == ShapeIcon.Subtract)
            {
                //The first solid with the second's overlap gone, and the second left as an outline so it is
                //visibly the thing that did the cutting rather than a shape that survived.
                return "<path d=\"M1.6 2.6a1 1 0 0 1 1-1h6.8a1 1 0 0 1 1 1v3h-4.8v4.8h-3a1 1 0 0 1-1-1z\" "
                    + $"{Held} />"
                    + $"<rect x=\"5.6\" y=\"5.6\" width=\"8.8\" height=\"8.8\" rx=\"1\" {Edge} />";
            }

            if (icon == ShapeIcon.Intersect)
            {
                //Only the lens where they meet is solid; both outlines stay so there is something for it to
                //be the middle of.
                return $"<rect x=\"1.6\" y=\"1.6\" width=\"8.8\" height=\"8.8\" rx=\"1\" {Edge} />"
                    + $"<rect x=\"5.6\" y=\"5.6\" width=\"8.8\" height=\"8.8\" rx=\"1\" {Edge} />"
                    + $"<rect x=\"5.6\" y=\"5.6\" width=\"4.8\" height=\"4.8\" {Held} />";
            }

            if (icon == ShapeIcon.Exclude)
            {
                //The opposite of intersect, drawn as the opposite: both solid, and the overlap punched out.
                return "<path d=\"M1.6 2.6a1 1 0 0 1 1-1h6.8a1 1 0 0 1 1 1v3h-4.8v4.8h-3a1 1 0 0 1-1-1z\" "
                    + $"{Held} />"
                    + "<path d=\"M10.4 5.6h3a1 1 0 0 1 1 1v6.8a1 1 0 0 1-1 1h-6.8a1 1 0 0 1-1-1v-3h4.8z\" "
                    + $"{Held} />";
            }

            //
            //The clipboard three and the two that open onto more.
            //
            //These already exist as buttons on the selection panel, and what is drawn here is the same
            //geometry - a menu line and the square it duplicates showing two different pictures of one
            //action would be worse than the menu having no pictures at all.
            //
            if (icon == ShapeIcon.Copy)
            {
                return "<rect x=\"5.2\" y=\"1.8\" width=\"9\" height=\"9\" rx=\"1.4\" "
                    + $"{Edge} />"
                    + "<path d=\"M10.8 13.2v0.6a1.4 1.4 0 0 1-1.4 1.4H3.2a1.4 1.4 0 0 1-1.4-1.4V6.6a1.4 1.4 0 0 1 1.4-1.4h0.6\" "
                    + $"{Edge} stroke-linecap=\"round\" stroke-linejoin=\"round\" />";
            }

            if (icon == ShapeIcon.Cut)
            {
                return $"<path d=\"M4 1.8 11.4 12.2M12 1.8 4.6 12.2\" {Edge} stroke-linecap=\"round\" />"
                    + $"<circle cx=\"4.1\" cy=\"13\" r=\"1.9\" {Edge} />"
                    + $"<circle cx=\"11.9\" cy=\"13\" r=\"1.9\" {Edge} />";
            }

            if (icon == ShapeIcon.Delete)
            {
                return "<path d=\"M2.6 4.1h10.8M6.4 4.1V2.6h3.2v1.5M4.2 4.1l0.7 9.1a1.3 1.3 0 0 0 1.3 1.2h3.6a1.3 1.3 0 0 0 1.3-1.2l0.7-9.1\" "
                    + $"{Edge} stroke-linecap=\"round\" stroke-linejoin=\"round\" />"
                    + $"<path d=\"M6.7 6.7v5M9.3 6.7v5\" {Edge} stroke-linecap=\"round\" />";
            }

            if (icon == ShapeIcon.Paste)
            {
                //A board with a sheet on it, which is the shape every clipboard is drawn as.
                return "<path d=\"M5.4 2.4H3.6a1.2 1.2 0 0 0-1.2 1.2v9.8a1.2 1.2 0 0 0 1.2 1.2h6.2\" "
                    + $"{Edge} stroke-linecap=\"round\" stroke-linejoin=\"round\" />"
                    + $"<rect x=\"5.4\" y=\"1.2\" width=\"4.4\" height=\"2.4\" rx=\"0.8\" {Edge} />"
                    + $"<rect x=\"7.6\" y=\"6.6\" width=\"7\" height=\"8.2\" rx=\"1.2\" {Edge} />";
            }

            if (icon == ShapeIcon.Turn)
            {
                //
                //An arc with a head at each end, which is the gesture without being one of the sides.
                //
                //**It was TurnRight's own picture**, copied and never edited, so the line that opens the
                //submenu and the second line inside it drew the same glyph - the mistake
                //EveryActionDrawsADifferentPicture exists to catch, and it caught it. Two heads is what says
                //"this opens onto turning" rather than "this turns clockwise".
                //
                //Solid triangles centered on the arc's own two ends, for the reasons the two quarter turns
                //below give. The tangent at both ends of a top half-circle is straight down, so both heads
                //point down and outward - which is what says "either way" rather than "clockwise".
                return $"<path d=\"M3 8A5 5 0 0 1 13 8\" {Edge} stroke-linecap=\"round\" />"
                    + $"<path d=\"M3 9.9 4.9 6.9 1.1 6.9Z\" {Held} />"
                    + $"<path d=\"M13 9.9 14.9 6.9 11.1 6.9Z\" {Held} />";
            }

            if (icon == ShapeIcon.TraceNet)
            {
                //
                //Three nodes joined by two runs: a net, as the word is drawn everywhere.
                //
                //**The plan-view version did not survive being looked at.** It was two runs of metal meeting
                //at a right angle with the via filled in - honest about what the walk actually follows, and
                //at sixteen units a bar wide enough to read is 3.2 across, which a 1.3 stroke on both sides
                //leaves 0.6 of interior. Rendered at 150 pixels it was a solid L with a notch in it.
                //
                //Widening the bars to fix that lands on <see cref="ShapeIcon.Intersect"/>, which is already
                //two overlapping outlines with the overlap solid - so the layout-native picture is either
                //illegible or somebody else's glyph.
                //
                //Nodes and edges is the abstraction, and it is the honest one for *this* action: what the
                //button gives back is a connected set, and connectedness is the whole of what it found.
                //
                return $"<path d=\"M3.6 4.2 8 11 13 6.4\" {Edge} stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />"
                    + $"<circle cx=\"3.6\" cy=\"4.2\" r=\"2.1\" {Held} />"
                    + $"<circle cx=\"8\" cy=\"11\" r=\"2.1\" {Held} />"
                    + $"<circle cx=\"13\" cy=\"6.4\" r=\"2.1\" {Held} />";
            }

            if (icon == ShapeIcon.Array)
            {
                //A grid of four, which is what an array is: the same thing repeated across and down.
                return $"<rect x=\"1.6\" y=\"1.6\" width=\"5.4\" height=\"5.4\" rx=\"1\" {Edge} />"
                    + $"<rect x=\"9\" y=\"1.6\" width=\"5.4\" height=\"5.4\" rx=\"1\" {Edge} />"
                    + $"<rect x=\"1.6\" y=\"9\" width=\"5.4\" height=\"5.4\" rx=\"1\" {Edge} />"
                    + $"<rect x=\"9\" y=\"9\" width=\"5.4\" height=\"5.4\" rx=\"1\" {Edge} />";
            }

            if (icon == ShapeIcon.NewCell)
            {
                //A cell with a plus, the same pair the tree's place square uses - and for the same reason:
                //one of them makes a cell and the other puts one down, so they are two halves of a subject.
                return $"<rect x=\"1.3\" y=\"5.4\" width=\"8.6\" height=\"8.6\" rx=\"1.3\" {Edge} />"
                    + $"<path d=\"M9.6 3.6h5.6M12.4 0.8v5.6\" {Edge} stroke-linecap=\"round\" />";
            }

            //
            //The two quarter turns: an arc with a head on it, which way round being the whole of what each
            //says. Drawn as a three-quarter circle rather than a full one - a closed ring with an arrow on
            //it reads as a refresh, and the gap is what makes it a turn.
            //
            //
            //The two quarter turns: a three-quarter arc with a head on the end of it.
            //
            //**The head's corner is the arc's own last point, and its arms straddle the tangent there.** It
            //used to be an L placed near the end by eye - corner at 13.4,5 against an arc ending at
            //11.1,4.1, two and a half units adrift and pointing off the line it was meant to be on, which at
            //fifteen pixels reads as an arrowhead that has come loose.
            //
            //So the geometry is worked out rather than nudged. The arc is a circle of radius 5 about the
            //middle of the box, stopping at 315 degrees - a whole eighth short of the top - where the
            //tangent is exactly 45 degrees.
            //
            //**And the head is a solid triangle, not a stroked chevron.** The chevron was tried first, with
            //its corner exactly on the arc's last point and its arms straddling the tangent - correct, and
            //at fifteen pixels it read as a squared hook hanging off the arc rather than as a point, because
            //a 1.3 stroke with round caps turns a right-angled corner into a blob. A filled triangle is what
            //every rotate mark in the world uses, and it is already this set's own vocabulary: the two
            //mirrors are solid triangles about a dashed axis.
            //
            //Each one is an apex 1.9 along the tangent from the arc's end and a base 1.9 either side of it,
            //1.1 back - so the triangle sits centered on the point it belongs to rather than beside it.
            //
            if (icon == ShapeIcon.TurnLeft)
            {
                return $"<path d=\"M3 8A5 5 0 1 0 4.46 4.46\" {Edge} stroke-linecap=\"round\" />"
                    + $"<path d=\"M3.12 5.8 3.89 2.34 6.58 5.03Z\" {Held} />";
            }

            if (icon == ShapeIcon.TurnRight)
            {
                return $"<path d=\"M13 8A5 5 0 1 1 11.54 4.46\" {Edge} stroke-linecap=\"round\" />"
                    + $"<path d=\"M12.88 5.8 12.11 2.34 9.42 5.03Z\" {Held} />";
            }

            //
            //And the two mirrors: a solid half and a hollow half either side of the line they are reflected
            //about. The dashed axis is what says it is a reflection rather than two shapes side by side, and
            //solid-against-hollow is what says which one is the original.
            //
            if (icon == ShapeIcon.MirrorAcross)
            {
                return "<path d=\"M8 1.2v13.6\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-dasharray=\"2 1.8\" stroke-linecap=\"round\" />"
                    + $"<path d=\"M6.4 3.4v9.2L1.6 8z\" {Held} />"
                    + $"<path d=\"M9.6 3.4v9.2L14.4 8z\" {Edge} stroke-linejoin=\"round\" />";
            }

            if (icon == ShapeIcon.MirrorDown)
            {
                return "<path d=\"M1.2 8h13.6\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-dasharray=\"2 1.8\" stroke-linecap=\"round\" />"
                    + $"<path d=\"M3.4 6.4h9.2L8 1.6z\" {Held} />"
                    + $"<path d=\"M3.4 9.6h9.2L8 14.4z\" {Edge} stroke-linejoin=\"round\" />";
            }

            if (icon == ShapeIcon.Rename)
            {
                //A pencil over a line, which is the mark every editor uses for "type a new one here".
                return $"<path d=\"M2 14h12\" {Edge} stroke-linecap=\"round\" />"
                    + $"<path d=\"M11.1 1.9a1.5 1.5 0 0 1 2.1 2.1L6 11.2l-2.8 0.7 0.7-2.8z\" {Edge} stroke-linejoin=\"round\" />";
            }

            //
            //The aligns: two bars of different lengths, and the rule they are being brought to.
            //
            //Different lengths on purpose. Two equal bars against a line are already aligned whichever edge
            //you mean, so the icon would say nothing - it is the *unequal* pair that makes "their left edges"
            //a different picture from "their right edges".
            //
            if (icon == ShapeIcon.AlignLeft)
            {
                return $"<path d=\"M2.2 1.8v12.4\" {Rule} />"
                    + $"<rect x=\"4\" y=\"3.2\" width=\"9.8\" height=\"3.6\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"4\" y=\"9.2\" width=\"6\" height=\"3.6\" rx=\"0.8\" {Held} />";
            }

            if (icon == ShapeIcon.AlignCenterX)
            {
                return $"<path d=\"M8 1.4v13.2\" {Rule} />"
                    + $"<rect x=\"3.1\" y=\"3.2\" width=\"9.8\" height=\"3.6\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"5\" y=\"9.2\" width=\"6\" height=\"3.6\" rx=\"0.8\" {Held} />";
            }

            if (icon == ShapeIcon.AlignRight)
            {
                return $"<path d=\"M13.8 1.8v12.4\" {Rule} />"
                    + $"<rect x=\"2.2\" y=\"3.2\" width=\"9.8\" height=\"3.6\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"6\" y=\"9.2\" width=\"6\" height=\"3.6\" rx=\"0.8\" {Held} />";
            }

            if (icon == ShapeIcon.AlignTop)
            {
                return $"<path d=\"M1.8 2.2h12.4\" {Rule} />"
                    + $"<rect x=\"3.2\" y=\"4\" width=\"3.6\" height=\"9.8\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"9.2\" y=\"4\" width=\"3.6\" height=\"6\" rx=\"0.8\" {Held} />";
            }

            if (icon == ShapeIcon.AlignMiddleY)
            {
                return $"<path d=\"M1.4 8h13.2\" {Rule} />"
                    + $"<rect x=\"3.2\" y=\"3.1\" width=\"3.6\" height=\"9.8\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"9.2\" y=\"5\" width=\"3.6\" height=\"6\" rx=\"0.8\" {Held} />";
            }

            if (icon == ShapeIcon.AlignBottom)
            {
                return $"<path d=\"M1.8 13.8h12.4\" {Rule} />"
                    + $"<rect x=\"3.2\" y=\"2.2\" width=\"3.6\" height=\"9.8\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"9.2\" y=\"6\" width=\"3.6\" height=\"6\" rx=\"0.8\" {Held} />";
            }

            //
            //And the two spaces: three bars with the gaps between them equal, which is the whole of what
            //spacing out does. Three rather than two, because two shapes have no gap to divide - the same
            //reason the buttons themselves need three before they are offered.
            //
            if (icon == ShapeIcon.SpaceAcross)
            {
                return $"<rect x=\"1.4\" y=\"2.6\" width=\"2.8\" height=\"10.8\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"6.6\" y=\"2.6\" width=\"2.8\" height=\"10.8\" rx=\"0.8\" {Held} />"
                    + $"<rect x=\"11.8\" y=\"2.6\" width=\"2.8\" height=\"10.8\" rx=\"0.8\" {Held} />";
            }

            return $"<rect x=\"2.6\" y=\"1.4\" width=\"10.8\" height=\"2.8\" rx=\"0.8\" {Held} />"
                + $"<rect x=\"2.6\" y=\"6.6\" width=\"10.8\" height=\"2.8\" rx=\"0.8\" {Held} />"
                + $"<rect x=\"2.6\" y=\"11.8\" width=\"10.8\" height=\"2.8\" rx=\"0.8\" {Held} />";
        }

        ///
        ///What the marks in a layer's pattern are drawn in: the color chosen for them, or the layer's own.
        ///
        ///One place, because the answer is wanted in three - the picture, the swatch beside a layer, and the
        ///tile a settings popup offers - and a fallback written out three times is two chances for a layer
        ///to be hatched in one color on screen and another in the list that says what it looks like.
        ///
        public static string MarksColorOf(Layer layer)
        {
            if (layer.PatternColor is string marks && marks.Length > 0)
                return marks;

            return layer.Color;
        }

        ///
        ///One tile of a pattern, on its own, for a control that offers the choice.
        ///
        ///**The same code that fills the layer**, which is the point of it being here rather than eight
        ///hand-drawn icons in the markup. A swatch drawn by hand is a picture of what somebody believed the
        ///fill does, and the two drift the first time either is touched.
        ///
        ///The id is the caller's because a page carries several of these at once - one per choice offered,
        ///and again beside every layer in a list - and a duplicate id would have them all draw the first.
        ///
        ///`marks` is the pattern's own color, or null to draw it in `color` the way a layer that has not
        ///been given one does. `pixels` is the screen size the layer's pattern is held at, which the swatch
        ///scales its tile by - so a coarser pattern reads as coarser in the control that set it, rather than
        ///every size drawing the same picture.
        ///
        public static string SwatchFor(LayerFill fill, string color, string id, int box = 24, string? marks = null, int? pixels = null)
        {
            var builder = new StringBuilder();

            if (fill == LayerFill.None)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "<rect width=\"{0}\" height=\"{0}\" fill=\"{1}\" />", box, color);

                return builder.ToString();
            }

            //Three repeats across a swatch, against thirty-two across a layout. A tile has to be a good
            //fraction of an icon to be recognizable at all: at the layout's density a 24px square is the
            //flat tone that made every one of these look alike the first time they were drawn side by side.
            double wanted = (box / 3.0) * (pixels.GetValueOrDefault(Layer.DefaultPatternPixels) / (double)Layer.DefaultPatternPixels);

            //Held to a third of the swatch at the coarse end: a tile bigger than that is one repeat with the
            //rest cropped off, which is a picture of a corner rather than of a pattern.
            long tile = Math.Clamp((long)Math.Round(wanted), LeastTile, Math.Max(LeastTile, box / 2));

            builder.Append("<defs>");

            appendPattern(builder, id, fill, color, marks ?? color, tile, null);

            builder.Append("</defs>");

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<rect width=\"{0}\" height=\"{0}\" fill=\"url(#{1})\" />",
                box,
                id);

            return builder.ToString();
        }

        ///
        ///`ground` is the layer's color, washed out under the tile; `marks` is what the motif is drawn in,
        ///which is the same color unless somebody has chosen another. `pixels` is the screen size this
        ///pattern should be held at as the view zooms, written into the tag for the interop to read back -
        ///null for the usual size, which keeps it out of the markup of every picture that never changed it.
        ///
        private static void appendPattern(StringBuilder builder, string id, LayerFill fill, string ground, string marks, long tile, int? pixels)
        {
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<pattern id=\"{0}\" class=\"{1}\" patternUnits=\"userSpaceOnUse\" width=\"{2}\" height=\"{2}\"",
                id,
                PatternClass,
                tile);

            if (pixels is int wanted)
                builder.AppendFormat(CultureInfo.InvariantCulture, " {0}=\"{1}\"", PatternPixelsAttribute, wanted);

            builder.Append('>');

            //
            //The layer's color as the tile's ground, at a fraction of its strength.
            //
            //A pattern that left the tile clear would draw the motif on nothing, and a shape would read as
            //empty from any distance where the motif is too small to see - which on a layout at the fit is
            //most of it. Washed out rather than solid, so the motif over it is still a difference.
            //
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<rect width=\"{0}\" height=\"{0}\" fill=\"{1}\" opacity=\"0.35\" />",
                tile,
                ground);

            appendMotif(builder, fill, marks, tile);

            builder.Append("</pattern>");
        }

        ///<summary>Marks every generated pattern, so the view can find them all to rescale without knowing the layers.</summary>
        public const string PatternClass = "layerFill";

        ///
        ///Where a pattern carries the screen size it wants, for the interop that rescales them.
        ///
        ///On the tag rather than in a table handed over separately, because the thing that reads it is
        ///already walking these nodes to rescale them - see scalePatterns - and a table would be a second
        ///list of layers for the two sides to disagree about. Absent means the usual size.
        ///
        public const string PatternPixelsAttribute = "data-pixels";

        ///
        ///The marks inside one tile, per pattern.
        ///
        ///Written against the tile rather than at fixed sizes, so all of these hold their proportions at
        ///whatever the layout made a tile. The line weight is a sixth of a tile throughout: thinner
        ///disappears against a washed ground, thicker closes the gaps and every pattern becomes solid.
        ///
        private static void appendMotif(StringBuilder builder, LayerFill fill, string color, long tile)
        {
            string weight = FormattableString.Invariant($"{tile / 6.0:0.###}");
            string half = FormattableString.Invariant($"{tile / 2.0:0.###}");
            string whole = tile.ToString(CultureInfo.InvariantCulture);

            if (fill == LayerFill.Dots)
            {
                //One dot per tile, centered, rather than a lattice - the sparsest of these on purpose.
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<circle cx=\"{0}\" cy=\"{0}\" r=\"{1}\" fill=\"{2}\" />",
                    half,
                    FormattableString.Invariant($"{tile / 4.0:0.###}"),
                    color);

                return;
            }

            if (fill == LayerFill.Squares)
            {
                //Half the tile each way, so a quarter of it is covered and the gaps stay square too.
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<rect x=\"{0}\" y=\"{0}\" width=\"{0}\" height=\"{0}\" fill=\"{1}\" />",
                    half,
                    color);

                return;
            }

            if (fill == LayerFill.Grid)
            {
                //Two lines on the tile's own edges, which meet their neighbors' and make one mesh.
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<path d=\"M0 0H{0}M0 0V{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"{2}\" />",
                    whole,
                    color,
                    weight);

                return;
            }

            if (fill == LayerFill.Dashes)
            {
                //Half a tile of line and half of gap, on the tile's middle - a broken rule rather than a mesh.
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "<path d=\"M0 {0}H{1}\" fill=\"none\" stroke=\"{2}\" stroke-width=\"{3}\" />",
                    half,
                    half,
                    color,
                    weight);

                return;
            }

            //
            //And the three hatches, which are the same two strokes in some combination.
            //
            //**One line per tile, plus a stub across each corner it passes through.** A 45-degree line drawn
            //corner to corner meets its neighbor's only at that corner, so the join is a point and the hatch
            //reads as a row of separate strokes; the stubs carry it over the boundary. The first attempt drew
            //three full lines per tile instead, which is the same picture at three times the density - beside
            //the dots and the grid the hatches came out as a fine tone rather than as lines you could count.
            //
            string stub = FormattableString.Invariant($"{tile / 8.0:0.###}");
            string pastEnd = FormattableString.Invariant($"{tile + (tile / 8.0):0.###}");
            string beforeEnd = FormattableString.Invariant($"{tile - (tile / 8.0):0.###}");

            string forward = FormattableString.Invariant(
                $"M-{stub} {stub}L{stub} -{stub}M0 {whole}L{whole} 0M{beforeEnd} {pastEnd}L{pastEnd} {beforeEnd}");

            string backward = FormattableString.Invariant(
                $"M{beforeEnd} -{stub}L{pastEnd} {stub}M0 0L{whole} {whole}M-{stub} {beforeEnd}L{stub} {pastEnd}");

            if (fill == LayerFill.Diagonal)
                appendStroke(builder, forward, color, weight);
            else if (fill == LayerFill.BackDiagonal)
                appendStroke(builder, backward, color, weight);
            else if (fill == LayerFill.CrossHatch)
            {
                appendStroke(builder, forward, color, weight);
                appendStroke(builder, backward, color, weight);
            }
        }

        private static void appendStroke(StringBuilder builder, string path, string color, string weight)
        {
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<path d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"{2}\" />",
                path,
                color,
                weight);
        }

        ///
        ///The class a layer's shapes carry, which is how they get their color.
        ///
        ///A CSS class cannot hold a slash, and a layer number can be negative in a file nobody validated - so
        ///the pair is spelled with underscores and the sign becomes one too. `65/20` is `l65_20`.
        ///
        public static string ClassFor(LayerKey key)
        {
            return FormattableString.Invariant($"l{key.Number}_{key.DataType}").Replace('-', '_');
        }

        #region Design rule markers *********************************************************

        ///<summary>The group every marker sits in, so a spec and a stylesheet have one thing to name.</summary>
        public const string MarkersId = "drcMarkers";

        ///<summary>What a marker with area carries.</summary>
        public const string MarkerClass = "drcMarker";

        ///<summary>
        ///What an off-grid marker carries, which is a point rather than a region.
        ///
        ///Drawn as a zero-length line with a round cap, which renders as a dot of exactly the stroke's
        ///width - so it stays the same size on screen at every zoom, where a circle given a radius in
        ///database units would be a speck across a die and cover the cell close up. A corner in the wrong
        ///place has no extent, and inventing one would be drawing a fact that is not there.
        ///</summary>
        public const string MarkerPointClass = "drcMarkerPoint";

        ///<summary>Which rule a marker belongs to, so clicking one can say what it broke.</summary>
        public const string RuleAttribute = "data-rule";

        ///<summary>
        ///Design rule violations, as markup to lay over the layout.
        ///
        ///**Built into the drawing rather than appended to the DOM afterwards.** The selection highlight is
        ///put up by JavaScript and has to be put up *again* after every redraw, through a flag the render
        ///checks - which works, and is one more thing to remember whenever anything rebuilds the markup.
        ///Markers change only when a check is run, so they can be part of what is built and survive a redraw
        ///by not being separate from it.
        ///
        ///No fill, so what is underneath stays readable: a marker says where to look rather than replacing
        ///the thing being looked at.
        ///</summary>
        public static string Markers(IEnumerable<DrcViolation> violations)
        {
            var builder = new StringBuilder();

            foreach (var violation in violations)
            {
                if (violation.Marker.Count == 0)
                    continue;

                if (violation.Marker.Count < 3)
                    appendPoint(builder, violation);
                else
                    appendRegion(builder, violation);
            }

            if (builder.Length == 0)
                return "";

            return $"<g id=\"{MarkersId}\">{builder}</g>";
        }

        private static void appendRegion(StringBuilder builder, DrcViolation violation)
        {
            builder.Append("<polygon class=\"").Append(MarkerClass).Append("\" ");
            builder.Append(RuleAttribute).Append("=\"").Append(escaped(violation.RuleId)).Append("\" points=\"");

            bool first = true;

            foreach (var point in violation.Marker)
            {
                if (!first)
                    builder.Append(' ');

                builder.Append(point.X.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(point.Y.ToString(CultureInfo.InvariantCulture));

                first = false;
            }

            builder.Append("\" />");
        }

        private static void appendPoint(StringBuilder builder, DrcViolation violation)
        {
            var at = violation.Marker[0];

            string x = at.X.ToString(CultureInfo.InvariantCulture);
            string y = at.Y.ToString(CultureInfo.InvariantCulture);

            builder.Append("<line class=\"").Append(MarkerPointClass).Append("\" ");
            builder.Append(RuleAttribute).Append("=\"").Append(escaped(violation.RuleId)).Append("\" ");
            builder.Append("x1=\"").Append(x).Append("\" y1=\"").Append(y).Append("\" ");
            builder.Append("x2=\"").Append(x).Append("\" y2=\"").Append(y).Append("\" />");
        }

        ///<summary>
        ///A rule id fit to sit in an attribute.
        ///
        ///A deck is somebody's text file and an id is whatever they typed in it, so it reaches this having
        ///been checked for nothing at all - and it is written straight into markup the browser parses.
        ///
        ///The framework's encoder rather than a hand-written one. This started as five Replace calls, which
        ///is the same job a label's text already gets done for it a few hundred lines below by
        ///<c>WebUtility.HtmlEncode</c> - and a second escaper in one file is a second thing to be wrong
        ///about which characters matter.
        ///</summary>
        private static string escaped(string text)
        {
            return System.Net.WebUtility.HtmlEncode(text);
        }

        #endregion **************************************************************************

        ///<summary>
        ///What a shape is marked as, given which cell is being edited.
        ///
        ///Three states rather than two. The instance being looked through is what a click lands on; the
        ///cell's *other* instances move with it and are marked so they can be seen to; everything else is
        ///out of the context entirely. Saying only "in or out" would hide the thing an editor most needs
        ///to understand about a hierarchy, which is how much one change touches.
        ///</summary>
        private static string classesFor(Element element, CellContext? context)
        {
            if (context is null)
                return "";

            if (context.IsLookingThrough(element))
                return InContextClass;

            if (context.Holds(element))
                return AlsoAffectedClass;

            return OutOfContextClass;
        }

        ///<summary>The instance being edited through: what the pointer answers to.</summary>
        public const string InContextClass = "inContext";

        ///<summary>Another instance of the same cell, which an edit moves as well.</summary>
        public const string AlsoAffectedClass = "alsoAffected";

        ///<summary>Everything a change here would not touch.</summary>
        public const string OutOfContextClass = "outOfContext";

        ///<summary>
        ///The same, drawing every visible layer's labels or none of them. For a caller with no per-layer
        ///answer to give - the command line, which offers one --no-labels for the whole file.
        ///</summary>
        public static string Build(FlattenedLayout layout, IReadOnlySet<LayerKey> visibleLayers, float opacity, bool showLabels = true)
        {
            IReadOnlySet<LayerKey> labeledLayers = new HashSet<LayerKey>();

            if (showLabels)
                labeledLayers = visibleLayers;

            return Build(layout, visibleLayers, opacity, labeledLayers);
        }

        ///<summary>Every layer in a file, for a caller that wants to draw the lot.</summary>
        public static IReadOnlySet<LayerKey> AllLayers(FlattenedLayout layout)
        {
            var visible = new HashSet<LayerKey>();

            foreach (var element in layout.Elements)
                visible.Add(element.Layer.Key);

            return visible;
        }

        ///<summary>
        ///Writes the opacity the way the slider's value attribute and the SVG both need to read it back.
        ///
        ///This and TryParseOpacity are the two ends of the same value, which is why they sit together: the
        ///slider is rendered with one and its input is read with the other, and if they ever disagreed
        ///about the decimal separator the slider would move once and then stick.
        ///</summary>
        public static string FormatOpacity(float opacity)
        {
            return opacity.ToString(CultureInfo.InvariantCulture);
        }

        ///<summary>
        ///Reads the value a range input reports. Invariant, not the current culture: the DOM always sends
        ///a decimal point, but Blazor WebAssembly takes its culture from the browser, and in a
        ///comma-decimal one the point is the group separator - so "0.9" parses to 9 and the view goes
        ///fully opaque instead of nearly transparent, with nothing to indicate why.
        ///</summary>
        public static bool TryParseOpacity(string? value, out float opacity)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity);
        }

        ///<summary>
        ///Writes a number for a control's value attribute, and reads one back.
        ///
        ///The same pair as the opacity above and for exactly the same reason: a number input always reports
        ///a decimal point whatever the browser's language, and Blazor WebAssembly parses in the browser's
        ///culture - so in a comma-decimal one "0.5" comes back as five, and a grid pitch typed as half a
        ///micron silently becomes five of them.
        ///</summary>
        public static string FormatNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseNumber(string? value, out double number)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        ///
        ///One shape: what it is, which layer it is on, and where its corners are.
        ///
        ///**A run is stroked and a ring is filled.** A path of no width has no outline, so what arrives is
        ///the line down the middle of it - and a polygon closes that and fills the shape between its two
        ///ends. On a straight line that is invisible and on an arc it is a solid segment where there is a
        ///line. See <see cref="Element.IsOpen"/>.
        ///
        ///Everything else it used to carry - color, opacity, stroke - is a rule now; see
        ///<see cref="appendStyle"/>. What is left is what actually differs between one shape and the next.
        ///
        ///
        ///One shape, as a subpath: move to the first corner, line to the rest, close if it is a ring.
        ///
        ///**Straight into the builder, not `string.Join` over a `Select`.** That allocated an interpolated
        ///string per point and a joined one per shape before anything was appended - two and a third million
        ///strings for a half-million element layout, every one copied again on the way in and thrown away.
        ///
        ///Invariant, because SVG is a data format: a coordinate needs an ASCII minus, and a layout's
        ///coordinates are routinely negative.
        ///
        private static void appendSubpath(StringBuilder builder, Element element)
        {
            var points = element.Points;

            for (int i = 0; i < points.Count; i++)
            {
                if (i == 0)
                    builder.Append('M');
                else
                    builder.Append('L');

                //Comma-separated, which SVG allows and which keeps a corner written exactly as the `points`
                //attribute wrote it - so a coordinate reads the same in a path as it did in a polygon.
                builder.Append(points[i].X.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(points[i].Y.ToString(CultureInfo.InvariantCulture));
            }

            //A run is left open and a ring is closed, which is the same distinction the polyline and the
            //polygon used to carry between them.
            if (!element.IsOpen)
                builder.Append('Z');
        }

        ///<summary>
        ///What a label is tagged with, so a click on it can be traced back to the element that drew it.
        ///
        ///The number is the index into <see cref="FlattenedLayout.Elements"/>, not a running count of what
        ///was drawn: the two differ the moment a layer is switched off, and a caller that looked the wrong
        ///one up would get a real element that is not the one under the cursor.
        ///</summary>
        public const string ElementAttribute = "data-element";

        ///<summary>
        ///The same, for a merged path: the elements its subpaths came from, in order, so the nth subpath is
        ///the nth number.
        ///
        ///**The picture keeps its provenance even though it is no longer one node per shape.** Nothing reads
        ///the numbers - the hit test is C#'s now, and C# already knows which element it found - but a
        ///downloaded image or anything `gds svg` writes can still say which element drew which outline, and
        ///that is worth about six bytes a shape.
        ///
        ///The attribute itself is load-bearing, though: it is what tells a path the layout drew from one
        ///JavaScript put in, which is how the snapping index knows where to look for corners.
        ///</summary>
        public const string ElementsAttribute = "data-elements";

        ///<summary>Marks the path holding a layer's open runs, which are stroked rather than filled.</summary>
        public const string OpenRunClass = "openRun";

        ///<summary>
        ///Draws a pin label at its anchor, justified the way its PRESENTATION record asks, in its layer's
        ///color with a white halo so it stays readable over whatever geometry sits beneath it.
        ///
        ///The label's own STRANS - reflection, rotation and magnification - is deliberately not applied.
        ///The sample files carry magnifications of 0.1 to 0.3, reflections on 146 labels and quarter-turn
        ///angles, all of which are instructions for a mask writer; honoring them here would render pin names
        ///a few units tall, mirrored or sideways. A viewer's labels exist to be read, so only the
        ///positioning is taken. The values are unpacked and available if that trade is ever reversed.
        ///</summary>
        private static void appendLabel(StringBuilder builder, Element element, int index, string context)
        {
            //A label keeps its attributes: there are a few thousand of them in the largest bundled file
            //against hundreds of thousands of shapes, so the boilerplate that mattered on a shape does not
            //here - and its color is its own rather than its layer's flat fill.
            string classes = "";

            if (context.Length > 0)
                classes = " class=\"" + context + "\"";

            var anchor = element.Points[0];

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "<text{9} {7}=\"{8}\" x=\"{0}\" y=\"{1}\" fill=\"{2}\" font-size=\"{3}\" text-anchor=\"{4}\" dominant-baseline=\"{5}\" style=\"paint-order: stroke; stroke: white; stroke-width: 12px;\">{6}</text>",
                anchor.X,
                anchor.Y,
                element.Layer.Color,
                LabelFontSize,
                textAnchorFor(element.Presentation.Horizontal),
                baselineFor(element.Presentation.Vertical),
                System.Net.WebUtility.HtmlEncode(element.Text),
                ElementAttribute,
                index,
                classes);
        }

        ///<summary>X is not mirrored by this view, so horizontal justification maps across directly.</summary>
        private static string textAnchorFor(HorizontalPresentation horizontal)
        {
            if (horizontal == HorizontalPresentation.Center)
                return "middle";

            if (horizontal == HorizontalPresentation.Right)
                return "end";

            return "start";
        }

        ///<summary>
        ///Inverted on purpose. This view maps GDSII's upward Y straight onto SVG's downward Y, so text that
        ///the format says hangs below its anchor has to sit above it on screen to end up in the same place
        ///relative to the geometry it labels.
        ///</summary>
        private static string baselineFor(VerticalPresentation vertical)
        {
            if (vertical == VerticalPresentation.Middle)
                return "middle";

            if (vertical == VerticalPresentation.Bottom)
                return "hanging";

            return "auto";
        }
    }
}
