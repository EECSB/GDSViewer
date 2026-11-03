using System.Globalization;
using System.Text;
using static GdsII.GDS;

namespace GdsII
{
    public static partial class AgentTools
    {
        ///
        ///The most corners one element is given: what a GDSII XY record holds, less the point that closes a ring.
        ///
        ///The record's own limit rather than the two hundred older readers stopped at, which real files pass all
        ///the time - a circle asked for at a thousand sides is a reasonable thing to want.
        ///
        private const int MostCorners = 8190;

        private static void addDrawing(List<AgentTool> tools)
        {
            tools.Add(new AgentTool(
                "draw_rectangle",
                "Draws a rectangle on a layer in the cell being worked in. Give two opposite corners as from and to, or "
                + "the center with width and height - all in microns. Answers with the new shape's number.",
                true,
                new[]
                {
                    layer("The layer to draw on, by name or numbers."),
                    may("from", AgentValue.Point, "One corner, [x, y] in microns."),
                    may("to", AgentValue.Point, "The opposite corner, [x, y] in microns."),
                    may("center", AgentValue.Point, "The middle, [x, y] in microns - with width and height instead of from and to."),
                    may("width", AgentValue.Number, "Across, in microns, with center."),
                    may("height", AgentValue.Number, "Up, in microns, with center.")
                },
                drawRectangle));

            tools.Add(new AgentTool(
                "draw_polygon",
                "Draws a polygon on a layer through its corners, in order around the shape, in microns. It is closed "
                + "for you, and refused if its edges cross each other.",
                true,
                new[]
                {
                    layer("The layer to draw on, by name or numbers."),
                    needs("points", AgentValue.Points, "The corners, [[x, y], ...] in microns, in order around the shape.", 3)
                },
                drawPolygon));

            tools.Add(new AgentTool(
                "draw_circle",
                "Draws a circle, or an ellipse with radius_y, as a polygon with its corners on the curve.",
                true,
                new[]
                {
                    layer("The layer to draw on, by name or numbers."),
                    needs("center", AgentValue.Point, "The middle, [x, y] in microns."),
                    needs("radius", AgentValue.Number, "The radius in microns - along x, for an ellipse."),
                    may("radius_y", AgentValue.Number, "The radius along y in microns, for an ellipse."),
                    may("sides", AgentValue.Integer, "How many corners, 3 to 8190. 64 when not given.")
                },
                drawCircle));

            tools.Add(new AgentTool(
                "draw_ring",
                "Draws a ring - a circle with a round hole in the middle - as one outline, the way GDSII stores a hole.",
                true,
                new[]
                {
                    layer("The layer to draw on, by name or numbers."),
                    needs("center", AgentValue.Point, "The middle, [x, y] in microns."),
                    needs("outer_radius", AgentValue.Number, "The outside radius in microns."),
                    needs("inner_radius", AgentValue.Number, "The radius of the hole in microns, less than the outside."),
                    may("sides", AgentValue.Integer, "How many corners each circle has, 3 to 8190. 64 when not given.")
                },
                drawRing));

            tools.Add(new AgentTool(
                "draw_path",
                "Draws a wire of a set width along a line through points, as one GDSII path. With bend_radius the corners "
                + "become arcs of that radius, which needs room on the straight sides either side of each corner. When the "
                + "rule deck sets a manufacturing grid, a path whose outline would fall off it - at bends, slants or round "
                + "ends - is drawn as that outline instead, a polygon on the grid.",
                true,
                new[]
                {
                    layer("The layer to draw on, by name or numbers."),
                    needs("points", AgentValue.Points, "The centerline, [[x, y], ...] in microns, from one end to the other.", 2),
                    needs("width", AgentValue.Number, "How wide the wire is, in microns."),
                    may("bend_radius", AgentValue.Number, "Round each corner into an arc of this radius, in microns, measured to the centerline."),
                    oneOf("ends", false, "How the two ends finish: flush stops at the end point; round and extended reach half the width past it. Flush when not given.", "flush", "round", "extended")
                },
                drawPath));

            tools.Add(new AgentTool(
                "draw_text",
                "Writes text as shapes - a blocky 5 by 7 font, every stroke the same width - which is how lettering goes "
                + "onto a chip. Printable ASCII only. It reads left to right, upright, unless turned: GDSII mirrors before "
                + "it turns, so mirror flips the writing top to bottom and then turn turns it counterclockwise - mirror "
                + "with turn 180 is writing mirrored left to right.",
                true,
                new[]
                {
                    layer("The layer to write on, by name or numbers."),
                    needs("text", AgentValue.Text, "What to write, on one line."),
                    needs("at", AgentValue.Point, "Where the bottom left corner of the writing goes, [x, y] in microns - of the box round it, however it is turned."),
                    needs("height", AgentValue.Number, "How tall a capital letter is, in microns. A stroke is a seventh of it."),
                    oneOf("turn", false, "Degrees counterclockwise. 0 when not given.", "0", "90", "180", "270"),
                    may("mirror", AgentValue.Flag, "Mirror the writing top to bottom, before turning it.")
                },
                drawText));

            tools.Add(new AgentTool(
                "add_label",
                "Puts a text label at a point - the kind that names a net or a pin, drawn at no size and not part of the "
                + "geometry. For writing that is part of the chip, use draw_text.",
                true,
                new[]
                {
                    layer("The layer the label is on, by name or numbers - usually a pin or label layer."),
                    needs("text", AgentValue.Text, "What the label says. Printable ASCII."),
                    needs("at", AgentValue.Point, "Where it goes, [x, y] in microns. The label is centered on it.")
                },
                addLabel));
        }

        ///<summary>Outlines onto one layer of one cell, as one step.</summary>
        private static AgentResult drawOutlines(
            AgentSession session,
            StructureModel cell,
            LayerKey onLayer,
            IReadOnlyList<List<Element.Point>> outlines,
            string label,
            Func<IReadOnlyList<int>, string> says)
        {
            var change = new AgentChange { Label = label, Cell = cell, Says = says };

            foreach (var outline in outlines)
            {
                change.Add(new AddElement(session.Gds, cell, onLayer, outline));
                change.Covers(outline);
            }

            return apply(session, change);
        }

        ///
        ///An outline made ready to be a boundary, or refused with the reason.
        ///
        ///**Checked after rounding**, which is where a shape can stop being one: three corners a fraction of a
        ///unit apart round onto the same point, and a sliver that was a triangle in microns is a line in the file.
        ///<paramref name="grid"/> is what they were rounded to, as a refusal names it.
        ///
        ///**Whether its edges cross is asked of an outline somebody wrote corner by corner**, and
        ///<paramref name="crossing"/> says what to do about it. Null for one built here, which cannot cross - or
        ///which <see cref="onGrid"/> already asked, when a grid moved its corners.
        ///
        private static List<Element.Point> boundary(AgentSession session, List<Element.Point> corners, string grid, string? crossing)
        {
            var kept = new List<Element.Point>();

            foreach (var corner in corners)
            {
                if (kept.Count > 0 && kept[^1].Equals(corner))
                    continue;

                kept.Add(corner);
            }

            while (kept.Count > 1 && kept[0].Equals(kept[^1]))
                kept.RemoveAt(kept.Count - 1);

            if (kept.Count < 3)
                throw new AgentProblem($"A polygon needs at least three different corners, and on {grid} this has {kept.Count}.");

            if (kept.Count > MostCorners)
                throw new AgentProblem($"One shape can have at most {MostCorners} corners, and this has {kept.Count}.");

            //The crossing first: a bow tie's two halves wind opposite ways, so its area adds up to nothing too, and
            //"crosses itself" is the answer that says what to fix.
            if (crossing is not null && Grid.Crossing(kept) is (int first, int second))
            {
                throw new AgentProblem(
                    $"The outline crosses itself: the edge from corner {first} at {session.Point(kept[first])} crosses the edge "
                    + $"from corner {second} at {session.Point(kept[second])}. {crossing}");
            }

            if (Measure.AreaOf(kept) == 0)
                throw new AgentProblem("Its corners all lie on one line, so it would cover nothing.");

            return kept;
        }

        ///<summary>The file's own grid, as a refusal names it.</summary>
        private static string fileGrid(AgentSession session)
        {
            return $"this file's {session.Microns(1)} µm grid";
        }

        ///<summary>The grid a layer's corners go on, as a refusal names it: the deck's when it asks for one, the file's when not.</summary>
        private static string gridOf(AgentSession session, LayerKey layer)
        {
            if (session.GridFor(layer) > 1)
                return session.GridSaid(layer);

            return fileGrid(session);
        }

        ///
        ///Corners a tool worked out, put on the grid the deck asks of their layer: each moved to the nearest point of
        ///it, with the repeats that makes dropped, and closed again if they came closed. As they were when the deck
        ///asks for no grid.
        ///
        ///**Moved, not refused**: the model chose none of these corners, so one off the grid is the tool's to fix and
        ///not the model's - see <see cref="AgentSession.GridFor"/>. What the model can change is the shape, and
        ///<paramref name="fix"/> says how, for the one way a grid can spoil one: a corner within a step of an edge it
        ///is not on can be moved across that edge, since the two are rounded apart, and the outline then crosses
        ///itself. That takes something only a few steps across; a curve alone cannot do it, since rounding keeps
        ///each run of its corners in order along both axes.
        ///
        private static List<Element.Point> onGrid(AgentSession session, IReadOnlyList<Element.Point> corners, LayerKey layer, string fix)
        {
            int grid = session.GridFor(layer);

            if (grid <= 1)
                return corners.ToList();

            var moved = Grid.Snap(corners, grid);

            if (Grid.Crossing(moved) is (int first, int second))
            {
                throw new AgentProblem(
                    $"Put on {session.GridSaid(layer)}, the outline would cross itself: the edge from {session.Point(moved[first])} crosses "
                    + $"the one from {session.Point(moved[second])}, which is the grid folding something only a few of its steps across. To draw it, {fix}");
            }

            return moved;
        }

        ///
        ///What a boolean or a grow made, put on the grid the deck asks of the layer it goes on - see
        ///<see cref="Grid.SnapAll"/>. As it was when the deck asks for no grid.
        ///
        ///**Every corner of it is worked out**: where two edges crossed, or wherever an edge moved to. So it goes on the
        ///grid as a circle's corners do, and for the same reason - the model chose none of them.
        ///
        ///**Refused when the grid folds a piece**, since the answer less that piece is a different answer and there is
        ///no number the model could change to mend it: the fold is a corner within a step of an edge it is not on, at
        ///a sharp point or a narrow neck of the shapes it chose. A piece narrower than a step all along is left out and
        ///said - see <see cref="madeNote"/>.
        ///
        private static Grid.Snapped madeOnGrid(AgentSession session, List<List<Element.Point>> made, LayerKey layer, string what)
        {
            var snapped = Grid.SnapAll(made, session.GridFor(layer));

            if (snapped.Fold is (Element.Point first, Element.Point second))
            {
                throw new AgentProblem(
                    $"Put on {session.GridSaid(layer)}, {what} would cross itself: the edge from {session.Point(first)} crosses the one from "
                    + $"{session.Point(second)}, a corner within a step of an edge it is not on landing across it. Nothing was changed.");
            }

            return snapped;
        }

        ///<summary>What an answer adds when the deck's grid moved the corners a boolean or a grow worked out, or left pieces out.</summary>
        private static string madeNote(AgentSession session, LayerKey layer, Grid.Snapped snapped)
        {
            var said = new StringBuilder();

            if (snapped.Moved)
                said.Append($" The corners it worked out are on {session.GridSaid(layer)}.");

            if (snapped.Flattened == 1)
                said.Append(" One piece narrower than a step of it was left out.");
            else if (snapped.Flattened > 1)
                said.Append($" {snapped.Flattened} pieces narrower than a step of it were left out.");

            return said.ToString();
        }

        ///<summary>What an answer adds when the deck's grid moved corners a tool worked out, and nothing when it moved none.</summary>
        private static string gridNote(AgentSession session, LayerKey layer, IEnumerable<Element.Point> corners, string what)
        {
            int grid = session.GridFor(layer);

            if (grid <= 1 || corners.All(corner => corner.Equals(Grid.Snap(corner, grid))))
                return "";

            return $" {what} on {session.GridSaid(layer)}.";
        }

        private static AgentResult drawRectangle(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            bool cornered = arguments.Has("from") || arguments.Has("to");
            bool centered = arguments.Has("center") || arguments.Has("width") || arguments.Has("height");

            if (cornered && centered)
                throw new AgentProblem("Give either from and to, or center, width and height - not both.");

            if (!cornered && !centered)
                throw new AgentProblem("Give the rectangle as two opposite corners, from and to, or as its center, width and height.");

            List<Element.Point> outline;

            if (cornered)
            {
                var from = session.At(arguments.Point("from"), "from");
                var to = session.At(arguments.Point("to"), "to");

                if (from.X == to.X || from.Y == to.Y)
                    throw new AgentProblem($"from and to are opposite corners, so they have to differ in both x and y: {session.Point(from)} and {session.Point(to)} make a rectangle with no area.");

                outline = Shapes.Between(from.X, from.Y, to.X, to.Y);
            }
            else
            {
                var center = session.At(arguments.Point("center"), "center");
                int width = session.Length(arguments.Number("width"), "The width");
                int height = session.Length(arguments.Number("height"), "The height");

                outline = Shapes.Rectangle(center.X, center.Y, width, height);
            }

            var box = Bounds.Of(outline);
            string where = $"{session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}";

            return drawOutlines(
                session,
                cell,
                onLayer,
                new List<List<Element.Point>> { outline },
                "AI draw rectangle",
                made => $"Drew a {session.Microns(box.Width)} by {session.Microns(box.Height)} µm rectangle on {where}, from {session.Span(box)}: {numbered(made)}.");
        }

        private static AgentResult drawPolygon(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            var corners = arguments.Points("points").Select(point => session.At(point, "A corner")).ToList();
            var outline = boundary(session, corners, fileGrid(session), "Give the corners in order around the shape.");
            var box = Bounds.Of(outline);

            return drawOutlines(
                session,
                cell,
                onLayer,
                new List<List<Element.Point>> { outline },
                "AI draw polygon",
                made => $"Drew a polygon of {outline.Count} corners on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, within {session.Span(box)}: {numbered(made)}.");
        }

        private static int sidesOf(AgentArguments arguments)
        {
            int sides = arguments.OptionalInteger("sides") ?? Shapes.DefaultVertices;

            if (sides < Shapes.FewestVertices || sides > MostCorners)
                throw new AgentProblem($"\"sides\" has to be from {Shapes.FewestVertices} to {MostCorners}, and is {sides}.");

            return sides;
        }

        private static AgentResult drawCircle(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            var center = session.At(arguments.Point("center"), "center");
            int radius = session.Length(arguments.Number("radius"), "The radius");
            int radiusY = radius;

            if (arguments.OptionalNumber("radius_y") is double alongY)
                radiusY = session.Length(alongY, "radius_y");

            int sides = sidesOf(arguments);

            var curve = Shapes.Ellipse(center.X, center.Y, radius, radiusY, sides);
            var outline = boundary(session, onGrid(session, curve, onLayer, "use a larger radius, or fewer sides."), gridOf(session, onLayer), null);

            string what = $"circle of radius {session.Microns(radius)} µm";

            if (radiusY != radius)
                what = $"ellipse of radii {session.Microns(radius)} and {session.Microns(radiusY)} µm";

            string onTheGrid = gridNote(session, onLayer, curve, "Its corners are");

            return drawOutlines(
                session,
                cell,
                onLayer,
                new List<List<Element.Point>> { outline },
                "AI draw circle",
                made => $"Drew a {what} with {outline.Count} sides on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, centered on {session.Point(center)}: {numbered(made)}.{onTheGrid}");
        }

        private static AgentResult drawRing(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            var center = session.At(arguments.Point("center"), "center");
            int outer = session.Length(arguments.Number("outer_radius"), "The outer radius");
            int inner = session.Length(arguments.Number("inner_radius"), "The inner radius");
            int sides = sidesOf(arguments);

            if (inner >= outer)
                throw new AgentProblem($"The inner radius has to be less than the outer one, and {session.Microns(inner)} µm is not less than {session.Microns(outer)} µm.");

            (var outside, var hole) = Shapes.Ring(center.X, center.Y, outer, inner, sides);

            string fix = "use a wider ring, or fewer sides.";

            //On the grid before the hole is cut and again after: the keyhole joining the hole to the outside lands where
            //a line out of the hole meets the outside, which is worked out rather than a corner of either circle.
            var cut = Booleans.Combine(
                new[] { onGrid(session, outside, onLayer, fix) },
                new[] { onGrid(session, hole, onLayer, fix) },
                BooleanOperation.Not);

            var outlines = new List<List<Element.Point>>();

            foreach (var outline in cut)
            {
                var placed = onGrid(session, outline, onLayer, fix);

                if (placed.Count >= 3)
                    outlines.Add(placed);
            }

            if (outlines.Count == 0)
                throw new AgentProblem($"That ring rounds away to nothing on {gridOf(session, onLayer)}.");

            string onTheGrid = gridNote(session, onLayer, outside.Concat(hole), "Its corners are");

            return drawOutlines(
                session,
                cell,
                onLayer,
                outlines,
                "AI draw ring",
                made => $"Drew a ring of outer radius {session.Microns(outer)} µm and inner radius {session.Microns(inner)} µm on "
                    + $"{session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, centered on {session.Point(center)}: {numbered(made)}.{onTheGrid}");
        }

        private static AgentResult drawPath(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            int width = session.Length(arguments.Number("width"), "The width");

            var points = new List<Element.Point>();

            foreach (var given in arguments.Points("points"))
            {
                var point = session.At(given, "A point");

                if (points.Count == 0 || !points[^1].Equals(point))
                    points.Add(point);
            }

            if (points.Count < 2)
                throw new AgentProblem($"A path needs two different points, and on this file's {session.Microns(1)} µm grid these are all one.");

            var ends = Paths.Ends.Flush;
            string? finish = arguments.OptionalText("ends");

            if (finish == "round")
                ends = Paths.Ends.Round;
            else if (finish == "extended")
                ends = Paths.Ends.Extended;

            var centerline = points;
            List<Element.Point>? bentFrom = null;
            string bends = "";

            if (arguments.OptionalNumber("bend_radius") is double bendMicrons)
            {
                int radius = session.Length(bendMicrons, "The bend radius");

                if ((2L * radius) < width)
                    throw new AgentProblem($"A bend radius of {session.Microns(radius)} µm is under half the width, {session.Microns(width / 2.0)} µm, so the inside of each bend would fold over itself.");

                centerline = bent(session, points, radius);
                bentFrom = points;
                bends = $", its corners bent to a radius of {session.Microns(radius)} µm";
            }

            if (centerline.Count > MostCorners)
                throw new AgentProblem($"One path can have at most {MostCorners} points, and this has {centerline.Count}. Use fewer corners or split it.");

            string drew = $"Drew a path {session.Microns(width)} µm wide on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, "
                + $"through {centerline.Count} points from {session.Point(centerline[0])} to {session.Point(centerline[^1])}{bends}";

            string overItself = "";

            //
            //Drawn as its outline when the deck's grid cannot hold it as a path - see Grid.PathOutlineOff, which the
            //app's path tool asks too. A wire that comes back within its own width of itself has an outline that
            //crosses itself, and no one polygon is that - a path is the only thing that draws it, so it stays one.
            //
            if (Grid.PathOutlineOff(centerline, width, ends, session.GridFor(onLayer), bentFrom) is (List<Element.Point> outline, string why))
            {
                if (Grid.Crossing(outline) is not null)
                {
                    overItself = $" It comes back over itself, so it stays a path, since no one polygon is the outline of a wire that crosses itself - "
                        + $"and that outline is off {session.GridSaid(onLayer)} along its {why}.";
                }
                else
                {
                    var polygon = boundary(session, onGrid(session, outline, onLayer, "make it wider, or its bends larger."), session.GridSaid(onLayer), null);

                    var drawn = new AgentChange
                    {
                        Label = "AI draw path",
                        Cell = cell,
                        Says = made => $"{drew}, as a polygon of {polygon.Count} corners: {numbered(made)}. A path's outline is worked out along its "
                            + $"{why}, where it lands between grid points, so the outline itself went on {session.GridSaid(onLayer)}."
                    };

                    drawn.Add(new AddElement(session.Gds, cell, onLayer, polygon));
                    drawn.Covers(polygon);

                    return apply(session, drawn);
                }
            }

            var change = new AgentChange
            {
                Label = "AI draw path",
                Cell = cell,
                Says = made => $"{drew}: {numbered(made)}.{overItself}"
            };

            change.Add(new AddElement(session.Gds, cell, onLayer, centerline, width, ends));
            change.Covers(Bounds.Of(centerline).Grown(width));

            return apply(session, change);
        }

        ///
        ///A centerline through the given corners with each corner rounded into an arc of the radius.
        ///
        ///**The corners stay where they were asked for** - the straight runs aim at them, and each arc is the one
        ///that leaves one run and joins the next, the way a rounded corner on a drawing is. So a corner turning
        ///by an angle takes `radius × tan(angle / 2)` off the run on either side of it, and a run too short to give
        ///that is refused with the numbers, rather than drawn as a wire that loops back on itself.
        ///
        private static List<Element.Point> bent(AgentSession session, List<Element.Point> points, int radius)
        {
            int count = points.Count;
            var headings = new double[count - 1];
            var lengths = new double[count - 1];

            for (int i = 0; i < count - 1; i++)
            {
                double dx = points[i + 1].X - (double)points[i].X;
                double dy = points[i + 1].Y - (double)points[i].Y;

                headings[i] = Math.Atan2(dy, dx);
                lengths[i] = Math.Sqrt((dx * dx) + (dy * dy));
            }

            var turns = new double[count];
            var taken = new double[count];

            for (int corner = 1; corner < count - 1; corner++)
            {
                double turn = headings[corner] - headings[corner - 1];

                while (turn > Math.PI)
                    turn -= 2 * Math.PI;

                while (turn <= -Math.PI)
                    turn += 2 * Math.PI;

                if (Math.Abs(Math.Abs(turn) - Math.PI) < 1e-9)
                    throw new AgentProblem($"The path turns straight back on itself at {session.Point(points[corner])}, which no bend can do.");

                turns[corner] = turn;
                taken[corner] = radius * Math.Tan(Math.Abs(turn) / 2);
            }

            for (int i = 0; i < count - 1; i++)
            {
                double needed = taken[i] + taken[i + 1];

                if (lengths[i] - needed < -0.5)
                {
                    throw new AgentProblem(
                        $"The run from {session.Point(points[i])} to {session.Point(points[i + 1])} is {session.Microns(lengths[i])} µm long, and bends of radius "
                        + $"{session.Microns(radius)} µm at its ends need {session.Microns(needed)} µm of it. Use a smaller bend_radius, or move the corners apart.");
                }
            }

            var route = new PathBuilder(points[0], headings[0] * 180 / Math.PI);

            for (int i = 0; i < count - 1; i++)
            {
                double straight = Math.Max(0, lengths[i] - taken[i] - taken[i + 1]);

                if (straight > 0)
                    route.Straight(straight);

                if (i + 1 < count - 1 && turns[i + 1] != 0)
                    route.BendDeg(turns[i + 1] * 180 / Math.PI, radius);
            }

            return route.Centerline();
        }

        private static AgentResult drawText(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            string asked = arguments.Text("text");
            string text = AddElement.AsAscii(asked);

            if (text.Trim().Length == 0)
                throw new AgentProblem("There is nothing to write: the text is empty, or has no printable ASCII in it.");

            var at = session.At(arguments.Point("at"), "at");
            double height = arguments.Number("height");
            int pixel = session.Length(height / PixelFont.Height, "A stroke of the lettering, a seventh of the height,");

            //
            //The stroke and the start on the deck's grid, when it asks for one - which puts every corner on it, since
            //each is the start plus whole strokes. The stroke is rounded from the height asked for rather than from the
            //unit it already went to, so 1 µm on 5 nm is 145 nm a stroke, the step nearest a seventh of a micron.
            //
            int grid = session.GridFor(onLayer);
            string onTheGrid = "";

            if (grid > 1)
            {
                int stroke = Grid.Snap(height / PixelFont.Height / session.MicronsPerUnit, grid);
                var start = Grid.Snap(at, grid);

                if (stroke < grid)
                {
                    throw new AgentProblem(
                        $"A stroke of the lettering, a seventh of the height, would be {AgentSession.Given(height / PixelFont.Height)} µm, under half a step of "
                        + $"{session.GridSaid(onLayer)}. The smallest lettering on it is {session.Microns(grid * PixelFont.Height)} µm tall, a step a stroke.");
                }

                var moved = new List<string>();

                if (stroke != pixel)
                    moved.Add($"its strokes are {session.Microns(stroke)} µm");

                if (!start.Equals(at))
                    moved.Add($"it starts at {session.Point(start)}");

                if (moved.Count > 0)
                {
                    string said = string.Join(" and ", moved);

                    onTheGrid = $" {char.ToUpperInvariant(said[0])}{said[1..]}, on {session.GridSaid(onLayer)}.";
                }

                pixel = stroke;
                at = start;
            }

            int quarters = int.Parse(arguments.OptionalText("turn") ?? "0", CultureInfo.InvariantCulture) / 90;
            bool mirrored = arguments.Flag("mirror", false);
            var lit = PixelFont.Bitmap(text);

            if (mirrored)
                lit = Raster.MirroredDown(lit);

            //
            //A bitmap is read the way the layout is seen from above, its first row highest, and Raster turns it to the
            //right as it is read - so a quarter counterclockwise is a quarter to the left. The writing still begins at
            //its box's bottom left corner, since Polygons puts that corner of whatever it is given at the origin.
            //
            lit = Raster.Turned(lit, -quarters);

            var outlines = new List<List<Element.Point>>();

            foreach (var outline in Raster.Polygons(lit, pixel))
                outlines.Add(outline.Select(point => new Element.Point(point.X + at.X, point.Y + at.Y)).ToList());

            var box = Bounds.Empty;

            foreach (var outline in outlines)
                box = box.Union(Bounds.Of(outline));

            string dropped = "";

            if (text.Length < asked.Length)
                dropped = $" Left out {asked.Length - text.Length} characters the font cannot write.";

            var lies = new List<string>();

            if (mirrored)
                lies.Add("mirrored top to bottom");

            if (quarters > 0)
                lies.Add($"turned {quarters * 90}° counterclockwise");

            string how = "";

            if (lies.Count > 0)
                how = $", {string.Join(" and ", lies)}";

            return drawOutlines(
                session,
                cell,
                onLayer,
                outlines,
                "AI draw text",
                made => $"Wrote \"{text}\" on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, capitals {session.Microns(pixel * PixelFont.Height)} µm tall{how}, "
                    + $"covering {session.Span(box)}, as {shapes(made.Count)}: {numbered(made)}.{onTheGrid}{dropped}");
        }

        private static AgentResult addLabel(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));

            string text = AddElement.AsAscii(arguments.Text("text"));

            if (text.Length == 0)
                throw new AgentProblem("A label has to say something, in printable ASCII.");

            var at = session.At(arguments.Point("at"), "at");

            var change = new AgentChange
            {
                Label = "AI label",
                Cell = cell,
                Says = made => $"Put the label \"{text}\" on {session.LayerName(onLayer)} at {session.Point(at)} in {Hierarchy.NameOf(cell)}: {numbered(made)}."
            };

            change.Add(new AddElement(session.Gds, cell, onLayer, at, text));
            change.Covers(new Bounds(at.X, at.Y, at.X, at.Y));

            return apply(session, change);
        }
    }
}
