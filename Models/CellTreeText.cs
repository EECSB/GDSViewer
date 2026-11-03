using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Models
{
    ///
    ///What a row of the cell tree says, for both views that draw one: the 2D view's beside the drawing, and the 3D
    ///view's beside the scene. One wording, since they are the same rows from the same Hierarchy.Tree.
    ///
    public static class CellTreeText
    {
        ///<summary>What a cell row says beside its name: how much is in the cell, and what places it.</summary>
        public static string Counts(Hierarchy.CellSummary cell)
        {
            if (cell.IsTop)
                return FormattableString.Invariant($"{cell.Elements:N0} · top");

            return FormattableString.Invariant($"{cell.Elements:N0} · placed {cell.PlacedBy:N0}×");
        }

        ///<summary>A cell row's title: what is in the cell, and how often it is placed.</summary>
        public static string About(Hierarchy.CellSummary cell)
        {
            string s = "s";

            if (cell.Elements == 1)
                s = "";

            string holds = FormattableString.Invariant($"{cell.Elements:N0} element{s}");

            if (cell.Places > 0)
                holds += FormattableString.Invariant($", {cell.Places:N0} of them placements");

            if (cell.IsTop)
                return holds + ". Nothing places it, so it is drawn on its own";

            string times = "s";

            if (cell.PlacedBy == 1)
                times = "";

            return holds + FormattableString.Invariant($". Placed {cell.PlacedBy:N0} time{times}");
        }

        ///
        ///What a shape row says: what kind of element it is, and where it starts.
        ///
        ///Not its number in the cell. An index means nothing to somebody reading a layout, where a kind and a
        ///corner is a thing that can be looked for on screen - and the corner is in the file's own units, which
        ///is what every other number in the tree is in.
        ///
        ///**Where it is drawn, under a placement**: the cell's own corner, put where that placement puts it - see
        ///Hierarchy.TreeRow.Drawn. The same shape under two placements is two places in the drawing, and saying the
        ///cell's own corner under both would make every placement of a cell read the same.
        ///
        public static string Shape(Hierarchy.TreeRow row)
        {
            if (row.Shape is null)
                return "shape";

            string kind = row.Shape.Element switch
            {
                GDS.BoundaryModel => "boundary",
                GDS.PathModel => "path",
                GDS.TextModel => "label",
                GDS.BoxModel => "box",
                GDS.NodeModel => "node",
                _ => "shape"
            };

            if (row.Shape.Element is GDS.TextModel text && text.TextBody.STRING?.Data is AsciiData says && says.Value.Length > 0)
                return FormattableString.Invariant($"label “{says.Value}”");

            if (row.Shape.Element.XY?.Data is Int4Data points && points.Values.Length >= 2)
            {
                var drawn = row.Drawn.Apply(points.Values[0], points.Values[1]);

                return FormattableString.Invariant($"{kind} at {drawn.X}, {drawn.Y}");
            }

            return kind;
        }

        ///<summary>A layer row's title: how many shapes it has, on which layer, in which cell.</summary>
        public static string Layer(Hierarchy.TreeRow row, string layerName)
        {
            string shapes = "shapes";

            if (row.Count == 1)
                shapes = "shape";

            return FormattableString.Invariant($"{row.Count} {shapes} on {layerName} in {row.Structure}");
        }

        ///<summary>What a twisty opens, for the row it is on: a placed cell opens to where it is placed, not to what is in it.</summary>
        public static string Fold(Hierarchy.TreeRow row)
        {
            string does = "Show";

            if (row.Open)
                does = "Hide";

            if (row.Kind == Hierarchy.TreeRowKind.Cell && row.Inside.Length > 0)
                return $"{does} where {row.Structure} is placed in {row.Inside}";

            if (row.Kind == Hierarchy.TreeRowKind.Placement)
                return $"{does} what this placement of {row.Structure} draws";

            return $"{does} what is in it";
        }
    }
}
