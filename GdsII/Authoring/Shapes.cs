namespace GdsII
{
    ///
    ///Outlines to draw with, for code that is building a layout rather than reading one.
    ///
    ///**A layout format has no curves.** GDSII stores polygons and polylines and nothing else, so a circle
    ///is a many-sided polygon and the side count is a decision somebody has to make - too few and it is
    ///visibly a hexagon, too many and every file that carries it is larger for a difference no process can
    ///hold. That decision is the argument these take rather than something they make quietly.
    ///
    ///**Everything here is a list of corners, not an element.** The corners are what varies; putting one on
    ///a layer is <see cref="AddElement"/> and is the same call whichever shape it was. Keeping the two apart
    ///means a shape can be measured, moved, combined with <see cref="Booleans"/> or fed to another builder
    ///without having been put in a file first.
    ///
    ///Coordinates are database units, whole numbers, because that is what a GDSII file holds. The arithmetic
    ///runs in double and rounds once at the end, so a circle's corners are as near its true ones as a whole
    ///number gets rather than drifting as they accumulate.
    ///
    public static class Shapes
    {
        ///<summary>
        ///How many corners a curve gets when nobody says. Sixty-four is a hair under 0.12% off a true circle
        ///at the middle of a side, which is finer than any process this draws for and small enough that a
        ///file full of them is not remarked on.
        ///</summary>
        public const int DefaultVertices = 64;

        ///<summary>The fewest corners a closed shape can have and still enclose anything.</summary>
        public const int FewestVertices = 3;

        ///
        ///A rectangle, **centered on the point**, like everything else here.
        ///
        ///Centered rather than cornered because the shapes beside it are: a circle at (0,0) is a circle
        ///around (0,0), and a rectangle at (0,0) that quietly meant "corner here" would be the one call in
        ///this class that has to be looked up. <see cref="Between"/> is the corner-to-corner form for a
        ///caller who has two corners rather than a center and a size.
        ///
        ///An odd width or height cannot be split evenly about a whole-numbered center, so the extra unit
        ///goes on the high side - which keeps the shape the size that was asked for rather than a unit short.
        ///
        public static List<Element.Point> Rectangle(int centerX, int centerY, int width, int height)
        {
            int left = centerX - (width / 2);
            int bottom = centerY - (height / 2);

            return Between(left, bottom, left + width, bottom + height);
        }

        ///<summary>
        ///A rectangle from one corner to the other, whichever way round the two are given.
        ///
        ///Counter-clockwise, which is the winding every other shape here uses - so a set of them combines
        ///with <see cref="Booleans"/> without one of them behaving as a hole.
        ///</summary>
        public static List<Element.Point> Between(int x1, int y1, int x2, int y2)
        {
            int left = Math.Min(x1, x2);
            int right = Math.Max(x1, x2);
            int bottom = Math.Min(y1, y2);
            int top = Math.Max(y1, y2);

            return new List<Element.Point>
            {
                new Element.Point(left, bottom),
                new Element.Point(right, bottom),
                new Element.Point(right, top),
                new Element.Point(left, top)
            };
        }

        ///
        ///A circle around the point, as a polygon of <paramref name="vertices"/> corners.
        ///
        ///**The corners sit on the circle rather than outside it**, so the polygon is inscribed and its
        ///edges run inside the radius - which is the conservative reading for a layout, where a shape
        ///bulging past the radius fails the spacing rule the radius was chosen to satisfy.
        ///
        ///**To within a database unit, because a database unit is what a file holds.** A corner at 45° on a
        ///radius of 500 is at 353.553, and the nearest whole coordinate is 354 - which is 0.63 units further
        ///out than the circle. Rounding inward instead would guarantee the stronger claim and would bias
        ///every corner of every shape towards the middle, shrinking it systematically for a sub-nanometer
        ///artefact nothing measures. Rounding to nearest is what every other tool does, so a circle drawn
        ///here and one drawn elsewhere have their corners in the same places.
        ///
        public static List<Element.Point> Circle(int centerX, int centerY, int radius, int vertices = DefaultVertices)
        {
            return Ellipse(centerX, centerY, radius, radius, vertices);
        }

        ///
        ///An ellipse around the point, with a radius along each axis.
        ///
        ///Stepped by angle rather than by arc length, which spaces the corners evenly round a circle and
        ///unevenly round a flat ellipse - denser at the ends of the minor axis, where the curve is
        ///straightest, and sparser at the ends of the major axis, where it is not. It is the wrong way
        ///round for accuracy per corner and it is what every tool does, so a shape drawn here and the same
        ///shape drawn elsewhere have their corners in the same places.
        ///
        public static List<Element.Point> Ellipse(int centerX, int centerY, int radiusX, int radiusY, int vertices = DefaultVertices)
        {
            var points = new List<Element.Point>();

            if (radiusX <= 0 || radiusY <= 0)
                return points;

            int corners = Math.Max(FewestVertices, vertices);

            for (int i = 0; i < corners; i++)
            {
                double angle = 2 * Math.PI * i / corners;

                points.Add(new Element.Point(
                    (int)Math.Round(centerX + (radiusX * Math.Cos(angle))),
                    (int)Math.Round(centerY + (radiusY * Math.Sin(angle)))));
            }

            return points;
        }

        ///
        ///A regular polygon around the point, with a corner at <paramref name="turnDegrees"/> from the x axis.
        ///
        ///The same walk a circle takes, said as what it is. A hexagon and a "circle with six sides" are the
        ///same list, and a caller who means the first should not have to write the second - naming it is the
        ///whole of what this adds, and the turn is here because which way up a hexagon sits is usually the
        ///point of drawing one.
        ///
        public static List<Element.Point> RegularPolygon(int centerX, int centerY, int radius, int sides, double turnDegrees = 0)
        {
            var points = new List<Element.Point>();

            if (radius <= 0)
                return points;

            int corners = Math.Max(FewestVertices, sides);
            double turn = turnDegrees * Math.PI / 180;

            for (int i = 0; i < corners; i++)
            {
                double angle = turn + (2 * Math.PI * i / corners);

                points.Add(new Element.Point(
                    (int)Math.Round(centerX + (radius * Math.Cos(angle))),
                    (int)Math.Round(centerY + (radius * Math.Sin(angle)))));
            }

            return points;
        }

        ///
        ///A ring: the outside as one loop and the inside as the other.
        ///
        ///**Two loops, because GDSII has no hole.** The format stores a boundary and nothing else, so a
        ///shape with a hole in it is either two elements or one keyhole - a channel whose two edges lie on
        ///top of each other. This hands back both loops and leaves that choice to the caller;
        ///<see cref="Booleans.Combine"/> with <see cref="BooleanOperation.Not"/> is what cuts one from the
        ///other where the keyhole is wanted.
        ///
        public static (List<Element.Point> Outside, List<Element.Point> Inside) Ring(
            int centerX,
            int centerY,
            int outerRadius,
            int innerRadius,
            int vertices = DefaultVertices)
        {
            return (
                Circle(centerX, centerY, outerRadius, vertices),
                Circle(centerX, centerY, innerRadius, vertices));
        }
    }
}
