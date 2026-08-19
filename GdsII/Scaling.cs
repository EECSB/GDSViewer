using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///
    ///Making geometry a size somebody asked for.
    ///
    ///**Separate from <see cref="Turning"/> because it is a different kind of operation.** Every turn there is
    ///exact: it maps whole numbers onto whole numbers about a whole-numbered point, and a shape comes out of
    ///it on the grid it went in on. A scale does not. Each corner moves by its own fraction of a unit and is
    ///rounded on its own, so a shape scaled and scaled back is not the shape that started - which is a real
    ///cost and the reason the control that uses this says so on screen rather than leaving it to be found out.
    ///
    ///It is still the only way to make two things the same size on purpose, which is why it exists.
    ///
    public static class Scaling
    {
        ///<summary>One point, scaled about another. Both in the same coordinates, whichever those are.</summary>
        public static (double X, double Y) Point(double x, double y, double byX, double byY, double aboutX, double aboutY)
        {
            return (aboutX + ((x - aboutX) * byX), aboutY + ((y - aboutY) * byY));
        }

        ///
        ///What to multiply a size by to make it another size, or null when there is no such number.
        ///
        ///Null for a size of zero, which is the case worth naming: a shape with no extent along an axis - a
        ///flat run, a path drawn straight - cannot be scaled into one that has extent, because there is
        ///nothing to multiply. Refusing is better than the alternatives, which are to leave it alone silently
        ///or to invent a size for it.
        ///
        public static double? Factor(long was, double wanted)
        {
            if (was <= 0 || wanted <= 0 || double.IsNaN(wanted) || double.IsInfinity(wanted))
                return null;

            return wanted / was;
        }

        ///
        ///One element's coordinates, scaled about a point in the *layout's* space rather than its own cell's.
        ///
        ///The same round trip <see cref="Turning.Coordinates"/> makes, and for the same reason: a cell placed
        ///at an angle draws its shapes turned, so growing one where it sits is not growing it along the cell's
        ///own axes. Each corner goes out into the layout, is scaled there, and comes back.
        ///
        ///Null for an element with no coordinates, or one in a cell whose placement cannot be undone.
        ///
        public static int[]? Coordinates(CellContext context, ElementModel model, double byX, double byY, double aboutX, double aboutY)
        {
            if (model.Element.XY?.Data is not Int4Data xy || xy.Values.Length < 2)
                return null;

            var scaled = new int[xy.Values.Length];

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
            {
                (double x, double y) = context.ToLayout(xy.Values[i], xy.Values[i + 1]);

                (double grownX, double grownY) = Point(x, y, byX, byY, aboutX, aboutY);

                if (context.ToLocal(grownX, grownY) is not (double localX, double localY))
                    return null;

                scaled[i] = (int)Math.Round(localX);
                scaled[(i) + 1] = (int)Math.Round(localY);
            }

            //An odd trailing value would be a malformed record; carried across rather than dropped, since an
            //edit is not the place to start deciding a file is wrong.
            if (xy.Values.Length % 2 != 0)
                scaled[^1] = xy.Values[^1];

            return scaled;
        }
    }
}
