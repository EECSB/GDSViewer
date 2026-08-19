using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///The eight ways a shape can be turned or mirrored without leaving the grid it sits on.
    ///
    ///**Named by what happens to the coordinates, not by a direction.** Which way "clockwise" looks depends
    ///on which way the view draws Y, and a layout format and a screen do not agree about that - so a name
    ///borrowed from one of them is a name that is wrong in the other. Whoever draws the button decides what
    ///to call it; this only says what the numbers do.
    ///
    ///Only quarters, and only mirrors about the axes. Every one of these maps whole numbers onto whole
    ///numbers about a whole-numbered point, so a shape comes out of it exactly on the grid it went in on -
    ///where a turn of some other angle rounds every corner, moves them each by a different amount, and
    ///leaves geometry no mask shop would take.
    ///</summary>
    public enum Turn
    {
        ///<summary>(x, y) becomes (-y, x) about the pivot, which is a quarter turn the way the angles run.</summary>
        Quarter,

        ///<summary>(x, y) becomes (y, -x): the other three quarters.</summary>
        ThreeQuarters,

        ///<summary>X is reflected about the pivot and Y is left where it was.</summary>
        FlipX,

        ///<summary>Y is reflected about the pivot and X is left where it was.</summary>
        FlipY
    }

    ///<summary>
    ///Turning and mirroring geometry about a point in the layout's own coordinates.
    ///</summary>
    public static class Turning
    {
        ///<summary>One point, turned about another. Both in the same coordinates, whichever those are.</summary>
        public static (double X, double Y) Point(double x, double y, Turn turn, double pivotX, double pivotY)
        {
            double dx = x - pivotX;
            double dy = y - pivotY;

            if (turn == Turn.Quarter)
                return (pivotX - dy, pivotY + dx);

            if (turn == Turn.ThreeQuarters)
                return (pivotX + dy, pivotY - dx);

            if (turn == Turn.FlipX)
                return (pivotX - dx, y);

            return (x, pivotY - dy);
        }

        ///
        ///The same turn as a transform, so it can be composed with a placement rather than applied to points.
        ///
        ///What <see cref="Point"/> does to a corner, this does to a whole coordinate frame - which is what
        ///turning a *placed cell* needs, since a placement is a transform and turning it is composition
        ///rather than arithmetic on the shapes it draws.
        ///
        public static Transform About(Turn turn, double pivotX, double pivotY)
        {
            double xx = 1;
            double xy = 0;
            double yx = 0;
            double yy = 1;

            if (turn == Turn.Quarter)
            {
                xx = 0;
                xy = -1;
                yx = 1;
                yy = 0;
            }
            else if (turn == Turn.ThreeQuarters)
            {
                xx = 0;
                xy = 1;
                yx = -1;
                yy = 0;
            }
            else if (turn == Turn.FlipX)
            {
                xx = -1;
            }
            else
            {
                yy = -1;
            }

            //Turned about the pivot rather than the origin, which is the pivot moved out, turned, and put back.
            double dx = pivotX - ((xx * pivotX) + (xy * pivotY));
            double dy = pivotY - ((yx * pivotX) + (yy * pivotY));

            return new Transform(xx, xy, yx, yy, dx, dy);
        }

        ///
        ///One element's coordinates, turned about a point in the *layout's* space rather than its own cell's.
        ///
        ///**Out through the placement and back again.** A cell placed at a quarter turn draws its shapes
        ///sideways, so turning them where they sit comes out as a different quarter on screen - and on a cell
        ///placed mirrored it comes out as the opposite direction from the one that was asked for. Each corner
        ///goes out into the layout, turns there, and comes back, which is the route a drawn corner already
        ///takes.
        ///
        ///Exact wherever it can be: for a cell placed square, every value in the round trip is a whole number
        ///and a quarter turn about a whole-numbered point maps whole numbers onto whole numbers, so nothing
        ///is rounded at all. A cell placed at some angle nobody chose rounds, as everything here does.
        ///
        ///Null for an element with no coordinates to turn, or one in a cell whose placement cannot be undone.
        ///
        public static int[]? Coordinates(CellContext context, ElementModel model, Turn turn, double pivotX, double pivotY)
        {
            if (model.Element.XY?.Data is not Int4Data xy || xy.Values.Length < 2)
                return null;

            var moved = new int[xy.Values.Length];

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
            {
                (double x, double y) = context.ToLayout(xy.Values[i], xy.Values[i + 1]);

                (double turnedX, double turnedY) = Point(x, y, turn, pivotX, pivotY);

                if (context.ToLocal(turnedX, turnedY) is not (double localX, double localY))
                    return null;

                moved[i] = (int)Math.Round(localX);
                moved[i + 1] = (int)Math.Round(localY);
            }

            //An odd trailing value would be a malformed record; carried across rather than dropped, since an
            //edit is not the place to start deciding a file is wrong.
            if (xy.Values.Length % 2 != 0)
                moved[^1] = xy.Values[^1];

            return moved;
        }
    }
}
