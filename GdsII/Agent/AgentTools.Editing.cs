using System.Globalization;
using System.Text;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    public static partial class AgentTools
    {
        ///<summary>The most new shapes one array of copies makes - past it, a cell and an array placement is the right tool.</summary>
        private const int MostCopies = 10_000;

        private static void addEditing(List<AgentTool> tools)
        {
            tools.Add(new AgentTool(
                "move",
                "Moves shapes by a distance, in microns. Placements move the whole cell they place.",
                true,
                new[]
                {
                    shapesParameter("The shapes to move, by number."),
                    needs("dx", AgentValue.Number, "How far right, in microns. Negative goes left."),
                    needs("dy", AgentValue.Number, "How far up, in microns. Negative goes down.")
                },
                move));

            tools.Add(new AgentTool(
                "copy",
                "Copies shapes, putting each copy a distance from its original. Answers with the copies' numbers.",
                true,
                new[]
                {
                    shapesParameter("The shapes to copy, by number."),
                    needs("dx", AgentValue.Number, "How far right of the original, in microns. Negative goes left."),
                    needs("dy", AgentValue.Number, "How far above the original, in microns. Negative goes down.")
                },
                copy));

            tools.Add(new AgentTool(
                "array",
                "Repeats shapes in a grid of columns and rows, the originals in the first place. A single placement of a "
                + "cell becomes one array placement; anything else is copied into each place.",
                true,
                new[]
                {
                    shapesParameter("The shapes to repeat, by number."),
                    needs("columns", AgentValue.Integer, "How many across, counting the original."),
                    needs("rows", AgentValue.Integer, "How many up, counting the original."),
                    needs("spacing", AgentValue.Point, "[x, y] in microns: how far apart the columns are, and the rows. Negative goes left or down.")
                },
                array));

            tools.Add(new AgentTool(
                "turn",
                "Turns or mirrors shapes about a point - by default the middle of what is turned. 90 is a quarter turn "
                + "counterclockwise; flip_x mirrors left to right, flip_y top to bottom. Placements turn the cell they place.",
                true,
                new[]
                {
                    shapesParameter("The shapes to turn, by number."),
                    oneOf("turn", true, "Which turn: 90, 180 or 270 degrees counterclockwise, or a mirror.", "90", "180", "270", "flip_x", "flip_y"),
                    may("about", AgentValue.Point, "The point to turn about, [x, y] in microns.")
                },
                turn));

            tools.Add(new AgentTool(
                "align",
                "Lines shapes up on one edge or middle of the group, or spreads them so their middles are evenly spaced "
                + "between the two outermost (spread_x, spread_y; three or more shapes).",
                true,
                new[]
                {
                    shapesParameter("The shapes to line up, by number.", 2),
                    oneOf("to", true, "What to line them up on.", "left", "center_x", "right", "bottom", "center_y", "top", "spread_x", "spread_y")
                },
                align));

            tools.Add(new AgentTool(
                "delete",
                "Deletes shapes.",
                true,
                new[] { shapesParameter("The shapes to delete, by number.") },
                delete));

            tools.Add(new AgentTool(
                "change_layer",
                "Moves shapes onto another layer, leaving where they are and what they look like alone.",
                true,
                new[]
                {
                    shapesParameter("The shapes to move, by number."),
                    layer("The layer to put them on, by name or numbers.")
                },
                changeLayer));

            tools.Add(new AgentTool(
                "boolean",
                "Combines shapes into new ones: union merges them, intersect keeps where they all overlap, subtract takes "
                + "the rest out of the first, xor keeps where exactly one covers. The result goes on the first shape's "
                + "layer unless layer says, and replaces the shapes unless keep is true.",
                true,
                new[]
                {
                    shapesParameter("The shapes to combine, by number - the first is the one subtract cuts from.", 2),
                    oneOf("operation", true, "How to combine them.", "union", "intersect", "subtract", "xor"),
                    may("layer", AgentValue.Text, "The layer for the result, by name or numbers."),
                    may("keep", AgentValue.Flag, "Keep the shapes that went in, rather than replacing them.")
                },
                boolean));

            tools.Add(new AgentTool(
                "grow",
                "Moves every edge of shapes outward by a distance, or inward for a negative one, with square corners. "
                + "Shapes on one layer that come to overlap are merged. Replaces the shapes.",
                true,
                new[]
                {
                    shapesParameter("The shapes to grow, by number."),
                    needs("by", AgentValue.Number, "How far each edge moves, in microns. Negative shrinks.")
                },
                grow));

            tools.Add(new AgentTool(
                "corners",
                "Rounds or cuts the corners of shapes. A rectangle stays one shape; a path becomes a polygon.",
                true,
                new[]
                {
                    shapesParameter("The shapes, by number."),
                    oneOf("style", true, "round makes each corner an arc; cut takes it off in a straight line.", "round", "cut"),
                    needs("size", AgentValue.Number, "The radius of a round corner, or how far along each edge a cut goes, in microns."),
                    may("steps", AgentValue.Integer, "For round: how many straight pieces make a quarter turn, 1 to 64. 8 when not given.")
                },
                corners));

            tools.Add(new AgentTool(
                "make_cell",
                "Makes a new cell. Given shapes, moves them into it and places it where they were, so nothing moves on "
                + "the screen; given none, makes it empty, to open with open_cell and draw in.",
                true,
                new[]
                {
                    needs("name", AgentValue.Text, "The new cell's name, unused in the library. Letters, digits and underscores are safest."),
                    may("shapes", AgentValue.Shapes, "Shapes to move into it, by number.")
                },
                makeCell));

            tools.Add(new AgentTool(
                "place_cell",
                "Places a cell in the cell being worked in - once, or as a grid with columns and rows. GDSII mirrors "
                + "before it turns: mirror flips the cell top to bottom, and then turn turns it counterclockwise.",
                true,
                new[]
                {
                    needs("cell", AgentValue.Text, "The cell to place, by name."),
                    needs("at", AgentValue.Point, "Where the placed cell's origin goes, [x, y] in microns."),
                    oneOf("turn", false, "Degrees counterclockwise. 0 when not given.", "0", "90", "180", "270"),
                    may("mirror", AgentValue.Flag, "Mirror the cell top to bottom, before turning it."),
                    may("magnification", AgentValue.Number, "How many times bigger to draw it. 1 when not given."),
                    may("columns", AgentValue.Integer, "For a grid: how many across."),
                    may("rows", AgentValue.Integer, "For a grid: how many up."),
                    may("spacing", AgentValue.Point, "For a grid: [x, y] in microns between columns and between rows.")
                },
                placeCell));

            tools.Add(new AgentTool(
                "flatten",
                "Replaces placements with copies of what they place, drawn exactly where they were: turned, mirrored and "
                + "magnified the way each placement drew them. One level - a placement inside comes out as a placement. An "
                + "array placement comes apart into every place it has. A cell whose last placement is flattened leaves "
                + "the library with it.",
                true,
                new[] { shapesParameter("The placements to flatten, by number.") },
                flatten));

            tools.Add(new AgentTool(
                "rename_cell",
                "Renames a cell. Every placement of it follows, so nothing moves on the screen.",
                true,
                new[]
                {
                    needs("cell", AgentValue.Text, "The cell to rename, by the name it has now."),
                    needs("name", AgentValue.Text, "Its new name, unused in the library. Letters, digits and underscores are safest.")
                },
                renameCell));

            tools.Add(new AgentTool(
                "delete_cell",
                "Deletes a cell from the library, with everything in it. Refused while anything places it: flatten or "
                + "delete those placements first.",
                true,
                new[] { needs("cell", AgentValue.Text, "The cell to delete, by name.") },
                deleteCell));

            tools.Add(new AgentTool(
                "undo_last_change",
                "Takes back your own most recent change, while nothing else has been done since.",
                true,
                Array.Empty<AgentParameter>(),
                undoLast));
        }

        ///<summary>A box moved by a distance - refused when that would carry it out of reach.</summary>
        private static Bounds shifted(AgentSession session, Bounds box, int dx, int dy)
        {
            if (box.IsEmpty)
                return box;

            long left = (long)box.Left + dx;
            long bottom = (long)box.Bottom + dy;
            long right = (long)box.Right + dx;
            long top = (long)box.Top + dy;

            if (Math.Max(Math.Abs(left), Math.Abs(right)) > AgentSession.Reach || Math.Max(Math.Abs(bottom), Math.Abs(top)) > AgentSession.Reach)
                throw new AgentProblem($"That would take shapes past {session.Microns(AgentSession.Reach)} µm from the origin, further than a layout reaches.");

            return new Bounds((int)left, (int)bottom, (int)right, (int)top);
        }

        private static AgentResult move(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            int dx = session.Units(arguments.Number("dx"), "dx");
            int dy = session.Units(arguments.Number("dy"), "dy");

            if (dx == 0 && dy == 0)
                throw new AgentProblem($"dx and dy are both nothing on this file's {session.Microns(1)} µm grid, so nothing would move.");

            var cell = found[0].Cell;
            var ids = found.Select(shape => shape.Id).ToList();

            var change = new AgentChange
            {
                Label = "AI move",
                Cell = cell,
                Says = made => $"Moved {shapes(found.Count)} in {Hierarchy.NameOf(cell)} by {session.Point(dx, dy)} µm: {numbered(ids)}."
            };

            foreach (var shape in found)
            {
                var box = session.BoxOf(shape.Model);

                change.Edits.Add(new MoveElement(cell, shape.Model, dx, dy));
                change.Covers(box);
                change.Covers(shifted(session, box, dx, dy));
            }

            return apply(session, change);
        }

        private static AgentResult copy(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            int dx = session.Units(arguments.Number("dx"), "dx");
            int dy = session.Units(arguments.Number("dy"), "dy");

            if (dx == 0 && dy == 0)
                throw new AgentProblem("A copy put no distance away would sit exactly on its original. Give dx or dy.");

            var cell = found[0].Cell;

            var change = new AgentChange
            {
                Label = "AI copy",
                Cell = cell,
                Says = made => $"Copied {shapes(found.Count)} in {Hierarchy.NameOf(cell)} by {session.Point(dx, dy)} µm: {numbered(made)}."
            };

            foreach (var shape in found)
            {
                if (AddElement.CopyOf(session.Gds, cell, shape.Model, dx, dy) is not AddElement copied)
                    throw new AgentProblem($"#{shape.Id} could not be copied: its records are not in the library.");

                change.Add(copied);
                change.Covers(shifted(session, session.BoxOf(shape.Model), dx, dy));
            }

            return apply(session, change);
        }

        private static AgentResult array(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            int columns = arguments.Integer("columns");
            int rows = arguments.Integer("rows");

            if (columns < 1 || rows < 1 || columns > short.MaxValue || rows > short.MaxValue)
                throw new AgentProblem($"columns and rows have to be from 1 to {short.MaxValue}.");

            if (columns * (long)rows < 2)
                throw new AgentProblem("One column of one row is the shapes as they are. Ask for more than one of either.");

            var spacing = arguments.Point("spacing");
            int stepX = session.Units(spacing.X, "The column spacing");
            int stepY = session.Units(spacing.Y, "The row spacing");

            if (columns > 1 && stepX == 0)
                throw new AgentProblem("More than one column needs a column spacing, spacing's x, that is not nothing.");

            if (rows > 1 && stepY == 0)
                throw new AgentProblem("More than one row needs a row spacing, spacing's y, that is not nothing.");

            var cell = found[0].Cell;
            string grid = $"{columns} columns by {rows} rows, {session.Microns(stepX)} by {session.Microns(stepY)} µm apart";

            //One placement becomes one array record, which is what the format has them for.
            if (found.Count == 1 && found[0].Model.Element is SrefModel)
            {
                var only = found[0];

                if (ElementRecords.Copied(session.Gds, only.Model) is not List<Record> span
                    || Hierarchy.AsArray(span, columns, rows, stepX, 0, 0, stepY) is not List<Record> made)
                    throw new AgentProblem($"#{only.Id} could not be made into an array.");

                var placement = new AgentChange
                {
                    Label = "AI array",
                    Cell = cell,
                    Says = added => $"Made #{only.Id} into an array of {grid}, in {Hierarchy.NameOf(cell)}: it is now {numbered(added)}."
                };

                var box = session.BoxOf(only.Model);

                placement.Edits.Add(new DeleteElement(session.Gds, cell, only.Model));
                placement.Add(new AddElement(session.Gds, cell, cell.Elements.IndexOf(only.Model), made, "Array"));
                placement.Covers(box);
                placement.Covers(shifted(session, box, (columns - 1) * stepX, (rows - 1) * stepY));

                return apply(session, placement);
            }

            long copies = ((columns * (long)rows) - 1) * found.Count;

            if (copies > MostCopies)
                throw new AgentProblem($"That would make {copies} copies, and an array of copies stops at {MostCopies}. Make the shapes into a cell with make_cell, and array its placement instead.");

            var change = new AgentChange
            {
                Label = "AI array",
                Cell = cell,
                Says = made => $"Repeated {shapes(found.Count)} in {Hierarchy.NameOf(cell)} into {grid}: {shapes(made.Count)} added, {numbered(made)}."
            };

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (row == 0 && column == 0)
                        continue;

                    int dx = column * stepX;
                    int dy = row * stepY;

                    foreach (var shape in found)
                    {
                        if (AddElement.CopyOf(session.Gds, cell, shape.Model, dx, dy) is not AddElement copied)
                            throw new AgentProblem($"#{shape.Id} could not be copied: its records are not in the library.");

                        change.Add(copied);
                        change.Covers(shifted(session, session.BoxOf(shape.Model), dx, dy));
                    }
                }
            }

            return apply(session, change);
        }

        ///<summary>The turn a tool's choice names, about a point, as a transform.</summary>
        private static Transform turnFor(string choice, double pivotX, double pivotY)
        {
            if (choice == "90")
                return Turning.About(Turn.Quarter, pivotX, pivotY);

            if (choice == "180")
                return Turning.About(Turn.Quarter, pivotX, pivotY).Then(Turning.About(Turn.Quarter, pivotX, pivotY));

            if (choice == "270")
                return Turning.About(Turn.ThreeQuarters, pivotX, pivotY);

            if (choice == "flip_x")
                return Turning.About(Turn.FlipX, pivotX, pivotY);

            return Turning.About(Turn.FlipY, pivotX, pivotY);
        }

        ///<summary>Every coordinate pair through a transform, rounded once, at the end.</summary>
        private static int[] through(int[] values, Transform transform)
        {
            var moved = new int[values.Length];

            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                (double x, double y) = transform.ApplyTo(values[i], values[i + 1]);

                moved[i] = (int)Math.Round(x);
                moved[i + 1] = (int)Math.Round(y);
            }

            //An odd trailing value would be a malformed record, carried across as everything else here does.
            if (values.Length % 2 != 0)
                moved[^1] = values[^1];

            return moved;
        }

        private static Bounds through(Bounds box, Transform transform)
        {
            if (box.IsEmpty)
                return box;

            return Bounds.Of(new List<Element.Point>
            {
                transform.Apply(box.Left, box.Bottom),
                transform.Apply(box.Right, box.Bottom),
                transform.Apply(box.Right, box.Top),
                transform.Apply(box.Left, box.Top)
            });
        }

        private static AgentResult turn(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            string choice = arguments.Text("turn");
            var cell = found[0].Cell;

            var whole = Bounds.Empty;

            foreach (var shape in found)
                whole = whole.Union(session.BoxOf(shape.Model));

            double pivotX;
            double pivotY;

            if (arguments.OptionalPoint("about") is (double X, double Y) about)
            {
                pivotX = session.Units(about.X, "about");
                pivotY = session.Units(about.Y, "about");
            }
            else
            {
                if (whole.IsEmpty)
                    throw new AgentProblem("Those shapes cover nothing to find the middle of. Say where to turn them about.");

                //A whole unit, so a quarter turn maps the grid onto itself and nothing is rounded.
                pivotX = Math.Round((whole.Left + (double)whole.Right) / 2, MidpointRounding.AwayFromZero);
                pivotY = Math.Round((whole.Bottom + (double)whole.Top) / 2, MidpointRounding.AwayFromZero);
            }

            var transform = turnFor(choice, pivotX, pivotY);
            var ids = found.Select(shape => shape.Id).ToList();

            string what = choice switch
            {
                "90" => "a quarter turn counterclockwise",
                "180" => "half a turn",
                "270" => "a quarter turn clockwise",
                "flip_x" => "mirrored left to right",
                _ => "mirrored top to bottom"
            };

            //Placements are written afresh, so they come back as new elements with new numbers - said in the answer.
            var rebuiltIds = new List<int>();

            var change = new AgentChange
            {
                Label = "AI turn",
                Cell = cell,
                Says = made =>
                {
                    var said = new StringBuilder($"Turned {shapes(found.Count)} in {Hierarchy.NameOf(cell)} {what} about {session.Point(pivotX, pivotY)}: {numbered(ids)}.");

                    for (int i = 0; i < rebuiltIds.Count && i < made.Count; i++)
                        said.Append($" The placement #{rebuiltIds[i]} is now #{made[i]}.");

                    return said.ToString();
                }
            };

            foreach (var shape in found)
            {
                var box = session.BoxOf(shape.Model);

                change.Covers(box);
                change.Covers(through(box, transform));

                //A placement's own records through the turn, so its orientation composes with it, an array's
                //places turn as a grid, and whatever else it carries comes across - see Hierarchy.Through.
                if (Hierarchy.PlacedBy(shape.Model) is not null)
                {
                    if (ElementRecords.Copied(session.Gds, shape.Model) is not List<Record> span)
                        throw new AgentProblem($"#{shape.Id} could not be turned: its records are not in the library.");

                    if (Hierarchy.Through(span, transform) is not List<Record> rebuilt)
                        throw new AgentProblem($"#{shape.Id} would be turned further from the origin than a layout reaches.");

                    //Out and back in at the same place in the cell, so the cell's order does not change.
                    change.Edits.Add(new DeleteElement(session.Gds, cell, shape.Model));
                    change.Add(new AddElement(session.Gds, cell, cell.Elements.IndexOf(shape.Model), rebuilt, "Turn"));

                    rebuiltIds.Add(shape.Id);

                    continue;
                }

                var before = ReshapeElement.CoordinatesOf(shape.Model);

                if (before.Length == 0)
                    continue;

                change.Edits.Add(new ReshapeElement(cell, shape.Model, through(before, transform), "Turn"));
            }

            return apply(session, change);
        }

        private static AgentResult align(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            string to = arguments.Text("to");
            var cell = found[0].Cell;

            var boxes = new List<Bounds>();

            foreach (var shape in found)
            {
                var box = session.BoxOf(shape.Model);

                if (box.IsEmpty)
                    throw new AgentProblem($"#{shape.Id} covers nothing to line up.");

                boxes.Add(box);
            }

            IReadOnlyList<(int Dx, int Dy)> offsets;
            string how;

            if (to == "spread_x" || to == "spread_y")
            {
                if (found.Count < 3)
                    throw new AgentProblem("Spreading needs three shapes or more: the outer two stay put and the rest are spaced between them.");

                var along = Along.X;
                how = "spread evenly across";

                if (to == "spread_y")
                {
                    along = Along.Y;
                    how = "spread evenly up";
                }

                offsets = Aligning.SpacedOut(boxes, along);
            }
            else
            {
                var edge = to switch
                {
                    "left" => Edge.LeastX,
                    "center_x" => Edge.MiddleX,
                    "right" => Edge.MostX,
                    "bottom" => Edge.LeastY,
                    "center_y" => Edge.MiddleY,
                    _ => Edge.MostY
                };

                how = $"lined up on their {to.Replace("center_x", "middles across").Replace("center_y", "middles up")}";

                if (to == "left" || to == "right" || to == "bottom" || to == "top")
                    how += " edges";

                offsets = Aligning.Aligned(boxes, edge);
            }

            var moved = new List<int>();

            var change = new AgentChange
            {
                Label = "AI align",
                Cell = cell,
                Says = made => $"{shapes(found.Count)} in {Hierarchy.NameOf(cell)} {how}: moved {numbered(moved)}."
            };

            for (int i = 0; i < found.Count; i++)
            {
                (int dx, int dy) = offsets[i];

                if (dx == 0 && dy == 0)
                    continue;

                moved.Add(found[i].Id);

                change.Edits.Add(new MoveElement(cell, found[i].Model, dx, dy));
                change.Covers(boxes[i]);
                change.Covers(shifted(session, boxes[i], dx, dy));
            }

            if (change.Edits.Count == 0)
                return answered($"Those {shapes(found.Count)} are {how} already; nothing moved.");

            return apply(session, change);
        }

        private static AgentResult delete(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            var cell = found[0].Cell;
            var ids = found.Select(shape => shape.Id).ToList();

            var change = new AgentChange
            {
                Label = "AI delete",
                Cell = cell,
                Says = made => $"Deleted {shapes(found.Count)} from {Hierarchy.NameOf(cell)}: {numbered(ids)}."
            };

            foreach (var shape in found)
            {
                change.Covers(session.BoxOf(shape.Model));
                change.Edits.Add(new DeleteElement(session.Gds, cell, shape.Model));
            }

            return apply(session, change);
        }

        private static AgentResult changeLayer(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            var onto = session.LayerNamed(arguments.Text("layer"));
            var cell = found[0].Cell;

            var moved = new List<int>();

            var change = new AgentChange
            {
                Label = "AI change layer",
                Cell = cell,
                Says = made => $"Put {numbered(moved)} on {session.LayerName(onto)} in {Hierarchy.NameOf(cell)}."
            };

            foreach (var shape in found)
            {
                if (ElementRecords.LayerOf(shape.Model) is not LayerKey on)
                    throw new AgentProblem($"#{shape.Id} is a placement of a cell, which has no layer of its own.");

                if (on == onto)
                    continue;

                moved.Add(shape.Id);

                change.Edits.Add(new RelayerElement(session.Gds, cell, shape.Model, onto));
                change.Covers(session.BoxOf(shape.Model));
            }

            if (change.Edits.Count == 0)
                return answered($"Those shapes are all on {session.LayerName(onto)} already.");

            return apply(session, change);
        }

        ///<summary>The outlines of shapes that have area - refused for a label, a placement or a path of no width.</summary>
        private static List<List<Element.Point>> outlinesOf(List<AgentShape> found, string doing)
        {
            var outlines = new List<List<Element.Point>>();

            foreach (var shape in found)
            {
                if (GdsFlattener.OutlineOf(shape.Model) is not List<Element.Point> outline || outline.Count < 3)
                    throw new AgentProblem($"#{shape.Id} is a {kindOf(shape.Model)} with no area of its own to {doing}.");

                outlines.Add(outline);
            }

            return outlines;
        }

        private static AgentResult boolean(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            string operation = arguments.Text("operation");
            bool keep = arguments.Flag("keep", false);
            var cell = found[0].Cell;

            var outlines = outlinesOf(found, "combine");

            var how = operation switch
            {
                "union" => BooleanOperation.Or,
                "intersect" => BooleanOperation.And,
                "subtract" => BooleanOperation.Not,
                _ => BooleanOperation.Xor
            };

            var combined = Booleans.CombineAll(outlines, how)
                .Where(outline => outline.Count >= 3)
                .ToList();

            if (combined.Count == 0)
            {
                string why = operation switch
                {
                    "intersect" => "the shapes have no area in common",
                    "subtract" => "the others cover all of the first",
                    _ => "the shapes cover each other exactly"
                };

                throw new AgentProblem($"The {operation} is empty - {why} - so nothing was changed.");
            }

            LayerKey onLayer;

            if (arguments.OptionalText("layer") is string named)
                onLayer = session.LayerNamed(named);
            else
                onLayer = ElementRecords.LayerOf(found[0].Model) ?? new LayerKey(0, 0);

            //Where two edges cross, the corner is worked out - so it goes on the grid of the layer the result goes on.
            var snapped = madeOnGrid(session, combined, onLayer, $"the {operation}");

            if (snapped.Outlines.Count == 0)
                throw new AgentProblem($"The {operation} is nowhere wider than a step of {session.GridSaid(onLayer)}, so on it nothing is left - nothing was changed.");

            var result = snapped.Outlines;
            string onTheGrid = madeNote(session, onLayer, snapped);

            var ids = found.Select(shape => shape.Id).ToList();

            string done = operation switch
            {
                "union" => $"Merged {numbered(ids)}",
                "intersect" => $"Intersected {numbered(ids)}",
                "subtract" => $"Took {numbered(ids.Skip(1).ToList())} out of #{ids[0]}",
                _ => $"Took the exclusive or of {numbered(ids)}"
            };

            string replacing = ", replacing them";

            if (keep)
                replacing = ", keeping them";

            var change = new AgentChange
            {
                Label = $"AI {operation}",
                Cell = cell,
                Says = made => $"{done} on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}{replacing}: {numbered(made)}.{onTheGrid}"
            };

            foreach (var shape in found)
            {
                change.Covers(session.BoxOf(shape.Model));

                if (!keep)
                    change.Edits.Add(new DeleteElement(session.Gds, cell, shape.Model));
            }

            foreach (var outline in result)
            {
                change.Add(new AddElement(session.Gds, cell, onLayer, outline));
                change.Covers(outline);
            }

            return apply(session, change);
        }

        private static AgentResult grow(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            double asked = arguments.Number("by");
            int by = session.Units(asked, "by");

            if (by == 0)
                throw new AgentProblem($"Growing by {session.Plain(asked)} µm is nothing on this file's {session.Microns(1)} µm grid.");

            var cell = found[0].Cell;
            var outlines = outlinesOf(found, "grow");

            //Layer by layer, in the order they were named, so a shape keeps its layer and only its own layer merges.
            var layers = new List<LayerKey>();
            var byLayer = new Dictionary<LayerKey, List<IReadOnlyList<Element.Point>>>();

            for (int i = 0; i < found.Count; i++)
            {
                var key = ElementRecords.LayerOf(found[i].Model) ?? new LayerKey(0, 0);

                if (!byLayer.ContainsKey(key))
                {
                    layers.Add(key);
                    byLayer[key] = new List<IReadOnlyList<Element.Point>>();
                }

                byLayer[key].Add(outlines[i]);
            }

            string grown = $"Grew {numbered(found.Select(shape => shape.Id).ToList())} by {session.Microns(by)} µm on every side";

            if (by < 0)
                grown = $"Shrank {numbered(found.Select(shape => shape.Id).ToList())} by {session.Microns(-by)} µm on every side";

            //What the grid did, a sentence a layer and each sentence once.
            var notes = new List<string>();

            var change = new AgentChange
            {
                Label = "AI grow",
                Cell = cell,
                Says = made => $"{grown} in {Hierarchy.NameOf(cell)}, replacing them: {numbered(made)}.{string.Concat(notes)}"
            };

            foreach (var shape in found)
            {
                change.Covers(session.BoxOf(shape.Model));
                change.Edits.Add(new DeleteElement(session.Gds, cell, shape.Model));
            }

            int flattened = 0;
            var flattenedOn = layers[0];

            foreach (var key in layers)
            {
                //Every edge moved, so every corner is worked out - and goes on the grid of its own layer.
                var snapped = madeOnGrid(session, Booleans.Grow(byLayer[key], by), key, "what they grow into");

                foreach (var outline in snapped.Outlines)
                {
                    if (outline.Count < 3)
                        continue;

                    change.Add(new AddElement(session.Gds, cell, key, outline));
                    change.Covers(outline);
                }

                if (snapped.Flattened > 0)
                {
                    flattened += snapped.Flattened;
                    flattenedOn = key;
                }

                string note = madeNote(session, key, snapped);

                if (note.Length > 0 && !notes.Contains(note))
                    notes.Add(note);
            }

            if (change.Adding.Count == 0 && flattened > 0)
                throw new AgentProblem($"Shrinking by {session.Microns(-by)} µm leaves nothing of those shapes wider than a step of {session.GridSaid(flattenedOn)}, so nothing was changed.");

            if (change.Adding.Count == 0)
                throw new AgentProblem($"Shrinking by {session.Microns(-by)} µm leaves nothing of those shapes - they are narrower than twice that - so nothing was changed.");

            return apply(session, change);
        }

        private static AgentResult corners(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));
            string style = arguments.Text("style");
            int size = session.Length(arguments.Number("size"), "The size");
            int steps = arguments.OptionalInteger("steps") ?? PathBuilder.DefaultBendVertices;

            if (steps < 1 || steps > 64)
                throw new AgentProblem($"\"steps\" has to be from 1 to 64, and is {steps}.");

            var cell = found[0].Cell;
            var outlines = outlinesOf(found, "round");
            var changed = new List<int>();
            var grids = new List<string>();

            string how = $"rounded to a radius of {session.Microns(size)} µm";

            if (style == "cut")
                how = $"cut {session.Microns(size)} µm along each edge";

            var change = new AgentChange
            {
                Label = $"AI {style} corners",
                Cell = cell,
                Says = made =>
                {
                    var said = new StringBuilder($"The corners of {numbered(changed)} in {Hierarchy.NameOf(cell)} are {how}, less where an edge is too short to give that much.");

                    if (made.Count > 0)
                        said.Append($" The paths among them became polygons with new numbers: {numbered(made)}.");

                    foreach (string grid in grids)
                        said.Append(grid);

                    return said.ToString();
                }
            };

            for (int i = 0; i < found.Count; i++)
            {
                var shape = found[i];
                var outline = outlines[i];
                var key = ElementRecords.LayerOf(shape.Model) ?? new LayerKey(0, 0);

                List<Element.Point> worked;

                if (style == "cut")
                    worked = Corners.Cut(outline, size);
                else
                    worked = Corners.Round(outline, size, steps);

                //The new corners on the deck's grid, as a drawn curve's are.
                var made = onGrid(session, worked, key, "use a larger size, or fewer steps.");

                if (made.Count < 3 || made.SequenceEqual(outline))
                    continue;

                string grid = gridNote(session, key, worked, "The new corners are");

                if (grid.Length > 0 && !grids.Contains(grid))
                    grids.Add(grid);

                changed.Add(shape.Id);
                change.Covers(outline);

                //A boundary has its corners rewritten where it stands, which keeps its number; a path has no corners
                //of its own, so it becomes the boundary of its outline.
                if (shape.Model.Element is BoundaryModel)
                {
                    if (!made[0].Equals(made[^1]))
                        made.Add(made[0]);

                    var coordinates = new int[made.Count * 2];

                    for (int k = 0; k < made.Count; k++)
                    {
                        coordinates[k * 2] = made[k].X;
                        coordinates[(k * 2) + 1] = made[k].Y;
                    }

                    change.Edits.Add(new ReshapeElement(cell, shape.Model, coordinates, "Corners"));

                    continue;
                }

                change.Edits.Add(new DeleteElement(session.Gds, cell, shape.Model));
                change.Add(new AddElement(session.Gds, cell, key, made));
            }

            if (change.Edits.Count == 0)
                return answered("None of those shapes has a corner that changes; nothing was done.");

            return apply(session, change);
        }

        private static AgentResult makeCell(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            string name = AddElement.AsAscii(arguments.Text("name")).Trim();

            if (name.Length == 0)
                throw new AgentProblem("A cell needs a name, in printable ASCII.");

            if (Hierarchy.Named(gds, name) is not null)
                throw new AgentProblem($"There is already a cell called {name}.");

            if (!arguments.Has("shapes"))
            {
                var empty = new AgentChange
                {
                    Label = "AI make cell",
                    Cell = null,
                    Says = made => $"Made the empty cell {name}. Open it with open_cell to draw in it, and place it with place_cell."
                };

                empty.Edits.Add(new AddStructure(gds, name, new List<Record>()));

                return apply(session, empty);
            }

            var found = session.Shapes(arguments.Shapes("shapes"));
            var holder = found[0].Cell;
            var contents = new List<Record>();

            //Copied before anything is deleted, since deleting takes the records out of the library this reads.
            foreach (var shape in found)
            {
                if (ElementRecords.Copied(gds, shape.Model) is not List<Record> span)
                    throw new AgentProblem($"#{shape.Id} could not be copied: its records are not in the library.");

                contents.AddRange(span);
            }

            string holderName = Hierarchy.NameOf(holder);

            var change = new AgentChange
            {
                Label = "AI make cell",
                Cell = holder,
                Says = made => $"Made the cell {name} from {shapes(found.Count)} of {holderName} and placed it at (0, 0) in {holderName}, "
                    + $"so they draw exactly where they were: the placement is {numbered(made)}. The shapes inside it have new numbers; "
                    + "open it with open_cell and use find_shapes to see them."
            };

            change.Edits.Add(new AddStructure(gds, name, contents));

            foreach (var shape in found)
            {
                change.Covers(session.BoxOf(shape.Model));
                change.Edits.Add(new DeleteElement(gds, holder, shape.Model));
            }

            change.Add(new AddElement(gds, holder, Hierarchy.PlacementRecords(name, new Element.Point(0, 0), false, 0), "Place"));

            return apply(session, change);
        }

        private static AgentResult placeCell(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            var into = session.CellToEdit();
            string intoName = Hierarchy.NameOf(into);
            string name = arguments.Text("cell").Trim();

            if (Hierarchy.Named(gds, name) is null)
            {
                var names = Hierarchy.Names(gds);

                throw new AgentProblem($"There is no cell called \"{name}\". The cells are {string.Join(", ", names.Take(AgentSession.MostListed))}.");
            }

            if (Hierarchy.Reaches(gds, name, intoName))
            {
                if (name == intoName)
                    throw new AgentProblem($"A cell cannot be placed inside itself, and {name} is the cell being worked in.");

                throw new AgentProblem($"{name} already contains {intoName}, so placing it in {intoName} would make {intoName} contain itself.");
            }

            var at = session.At(arguments.Point("at"), "at");
            double angle = double.Parse(arguments.OptionalText("turn") ?? "0", CultureInfo.InvariantCulture);
            bool mirrored = arguments.Flag("mirror", false);
            double magnification = arguments.OptionalNumber("magnification") ?? 1;

            if (magnification <= 0 || magnification > 1000)
                throw new AgentProblem($"\"magnification\" has to be more than 0 and at most 1000, and is {session.Plain(magnification)}.");

            var records = Hierarchy.WithTransform(Hierarchy.PlacementRecords(name, at, false, 0), at, mirrored, angle, magnification)
                ?? throw new AgentProblem($"{name} could not be placed.");

            string grid = "";
            bool gridded = arguments.Has("columns") || arguments.Has("rows") || arguments.Has("spacing");

            if (gridded)
            {
                int columns = arguments.OptionalInteger("columns") ?? 1;
                int rows = arguments.OptionalInteger("rows") ?? 1;

                if (columns < 1 || rows < 1 || columns > short.MaxValue || rows > short.MaxValue)
                    throw new AgentProblem($"columns and rows have to be from 1 to {short.MaxValue}.");

                if (!arguments.Has("spacing") && columns * (long)rows > 1)
                    throw new AgentProblem("A grid needs spacing, [x, y] in microns between the columns and between the rows.");

                var spacing = arguments.OptionalPoint("spacing") ?? (0, 0);
                int stepX = session.Units(spacing.X, "The column spacing");
                int stepY = session.Units(spacing.Y, "The row spacing");

                if (columns > 1 && stepX == 0)
                    throw new AgentProblem("More than one column needs a column spacing, spacing's x, that is not nothing.");

                if (rows > 1 && stepY == 0)
                    throw new AgentProblem("More than one row needs a row spacing, spacing's y, that is not nothing.");

                if (columns * (long)rows > 1)
                {
                    records = Hierarchy.AsArray(records, columns, rows, stepX, 0, 0, stepY)
                        ?? throw new AgentProblem($"{name} could not be placed as a grid.");

                    grid = $" as a grid of {columns} columns by {rows} rows, {session.Microns(stepX)} by {session.Microns(stepY)} µm apart";
                }
            }

            var how = new List<string>();

            if (mirrored)
                how.Add("mirrored");

            if (angle != 0)
                how.Add($"turned {angle.ToString(CultureInfo.InvariantCulture)}°");

            if (magnification != 1)
                how.Add($"magnified {session.Plain(magnification)} times");

            string turned = "";

            if (how.Count > 0)
                turned = ", " + string.Join(", ", how);

            //Parsed once here, the way the edit will parse it, to know what it will cover before it is placed.
            int from = 0;
            var box = session.BoxOf(new ElementModel(ref from, records));

            string covering = "";

            if (!box.IsEmpty)
                covering = $", covering {session.Span(box)}";

            var change = new AgentChange
            {
                Label = "AI place cell",
                Cell = into,
                Says = made => $"Placed {name} in {intoName} at {session.Point(at)}{turned}{grid}{covering}: {numbered(made)}."
            };

            change.Add(new AddElement(gds, into, records, "Place"));
            change.Covers(box);

            return apply(session, change);
        }

        ///
        ///Placements replaced with what they place, through <see cref="Flattening.Edits"/> - the same arithmetic
        ///the app's Flatten button does, so the two cannot take a placement apart differently.
        ///
        ///Each refusal is checked here first, in words the model can act on; what the library can still refuse
        ///after them is a coordinate that will not fit.
        ///
        private static AgentResult flatten(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            var found = session.Shapes(arguments.Shapes("shapes"));
            var cell = found[0].Cell;
            string cellName = Hierarchy.NameOf(cell);
            long writing = 0;

            foreach (var shape in found)
            {
                if (Hierarchy.PlacedBy(shape.Model) is not string placed)
                    throw new AgentProblem($"#{shape.Id} is a {kindOf(shape.Model)}, not a placement - only a placement of a cell has anything to flatten.");

                if (Hierarchy.Named(gds, placed) is null)
                    throw new AgentProblem($"#{shape.Id} places {placed}, which is not in the library, so there is nothing to put in its place.");

                writing += Flattening.CountOf(gds, shape.Model);
            }

            if (writing > Flattening.MostElements)
                throw new AgentProblem($"That would write {writing} shapes into {cellName}, and one flatten stops at {Flattening.MostElements}. An array that size is better kept as a placement.");

            var placements = found.Select(shape => shape.Model).ToList();
            var going = Flattening.CellsGoing(gds, placements);

            if (Flattening.Edits(gds, cell, placements) is not List<LayoutEdit> edits)
                throw new AgentProblem("Those placements could not be flattened: something in them would land further from the origin than a layout reaches.");

            var ids = found.Select(shape => shape.Id).ToList();

            string their = "its";

            if (ids.Count > 1)
                their = "their";

            var change = new AgentChange
            {
                Label = "AI flatten",
                Cell = cell,
                Says = made =>
                {
                    var said = new StringBuilder();

                    if (made.Count == 0)
                        said.Append($"Flattened {numbered(ids)} in {cellName}. What it placed is empty, so nothing came in {their} place.");
                    else
                        said.Append($"Flattened {numbered(ids)} into {cellName}: {shapes(made.Count)} in {their} place, drawn where they were - {numbered(made)}.");

                    if (going.Count == 1)
                        said.Append($" {going[0]} was placed nowhere else, so it has gone from the library too.");
                    else if (going.Count > 1)
                        said.Append($" {string.Join(", ", going)} were placed nowhere else, so they have gone from the library too.");

                    return said.ToString();
                }
            };

            foreach (var shape in found)
                change.Covers(session.BoxOf(shape.Model));

            foreach (var edit in edits)
            {
                if (edit is AddElement adding)
                    change.Add(adding);
                else
                    change.Edits.Add(edit);
            }

            return apply(session, change);
        }

        private static AgentResult renameCell(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            string was = Hierarchy.NameOf(cellCalled(gds, arguments.Text("cell")));
            string name = AddElement.AsAscii(arguments.Text("name")).Trim();

            if (name.Length == 0)
                throw new AgentProblem("A cell needs a name, in printable ASCII.");

            if (name == was)
                return answered($"{was} is called that already; nothing was changed.");

            if (Hierarchy.Named(gds, name) is not null)
                throw new AgentProblem($"There is already a cell called {name}.");

            int placed = Hierarchy.PlacementsOf(gds, was);
            string following;

            if (placed == 0)
                following = " Nothing places it.";
            else if (placed == 1)
                following = " Its placement names it by the new name too.";
            else
                following = $" All {placed} of its placements name it by the new name too.";

            var change = new AgentChange
            {
                Label = "AI rename cell",
                Cell = null,
                Says = made => $"Renamed {was} to {name}.{following}"
            };

            change.Edits.Add(new RenameStructure(gds, was, name));

            return apply(session, change);
        }

        ///
        ///A cell taken out of the library - refused while anything places it, and said where.
        ///
        ///**Placed is refused, not cascaded.** Taking a placed cell out leaves every placement of it naming a
        ///cell that is not there: a file that opens, and draws nothing where the instances were. The app refuses
        ///the same way and for the same reason, and the fix is the model's to choose - flatten them, or delete them.
        ///
        private static AgentResult deleteCell(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            var structure = cellCalled(gds, arguments.Text("cell"));
            string name = Hierarchy.NameOf(structure);

            var placing = new List<string>();

            foreach (var other in gds.StreamFormat.Structures)
            {
                int here = Hierarchy.Places(other).Count(placed => placed == name);

                if (here == 1)
                    placing.Add(Hierarchy.NameOf(other));
                else if (here > 1)
                    placing.Add($"{Hierarchy.NameOf(other)} ({here} times)");
            }

            if (placing.Count > 0)
                throw new AgentProblem($"{name} is placed in {string.Join(", ", placing.Take(AgentSession.MostListed))}. Flatten or delete those placements first - deleting it now would leave them naming a cell that is not there.");

            int places = Hierarchy.Places(structure).Count;
            int drawn = structure.Elements.Count - places;
            bool workedIn = session.Cell == name;

            string holding = "; it was empty";

            if (places > 0)
                holding = $", with its {shapes(drawn)} and {counted(places, "placement")}";
            else if (drawn > 0)
                holding = $", with its {shapes(drawn)}";

            var change = new AgentChange
            {
                Label = "AI delete cell",
                Cell = null,
                Says = made =>
                {
                    string said = $"Deleted {name} from the library{holding}.";

                    if (workedIn)
                        said += " It was the cell being worked in - choose another with open_cell.";

                    return said;
                }
            };

            change.Edits.Add(new RemoveStructure(gds, name));

            return apply(session, change);
        }

        private static AgentResult undoLast(AgentArguments arguments, AgentSession session)
        {
            string undone = session.UndoMine();

            return new AgentResult { Text = $"Took back my last change: {undone}.", Changed = true };
        }
    }
}
