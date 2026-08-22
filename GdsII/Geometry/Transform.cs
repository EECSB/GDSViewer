namespace GdsII
{
    ///<summary>
    ///A 2D affine transform, applied as x' = XX*x + XY*y + Dx and y' = YX*x + YY*y + Dy.
    ///
    ///GDSII places one structure inside another with a reflection, a magnification, a rotation and a
    ///translation. Those compose as nesting deepens, and keeping them as separate fields would mean
    ///working out how a reflection interacts with a parent's rotation at every level. A matrix composes
    ///by multiplication instead, so nesting is just one operation however deep it goes.
    ///</summary>
    ///<remarks>
    ///A record struct for the value equality: two placements are the same placement when their six numbers
    ///are, and comparing them is how one instance of a cell is told from another. ValueType's own equality
    ///would give the same answer by reflecting over the fields and boxing to do it, which is a strange
    ///price to pay for something asked once per drawn shape.
    ///</remarks>
    public readonly record struct Transform
    {
        #region Constructors ****************************************************************

        public Transform(double xx, double xy, double yx, double yy, double dx, double dy)
        {
            XX = xx;
            XY = xy;
            YX = yx;
            YY = yy;
            Dx = dx;
            Dy = dy;
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        public double XX { get; }
        public double XY { get; }
        public double YX { get; }
        public double YY { get; }
        public double Dx { get; }
        public double Dy { get; }

        public static Transform Identity
        {
            get { return new Transform(1, 0, 0, 1, 0, 0); }
        }

        ///<summary>
        ///The magnification this transform applies, read off the length of the transformed x axis. A GDSII
        ///placement is a similarity - uniform scale, rotation and an optional reflection - so a single
        ///number describes it. Used to divide out a parent's contribution when a child's magnification is
        ///marked absolute.
        ///</summary>
        public double Scale => Math.Sqrt((XX * XX) + (YX * YX));

        ///<summary>The rotation this transform applies, in degrees, for the same reason as Scale.</summary>
        public double AngleInDegrees => Math.Atan2(YX, XX) * 180.0 / Math.PI;

        ///<summary>
        ///Whether this turns the plane over, which is what a placement's reflection bit says.
        ///
        ///The sign of the determinant, because a reflection composed with any amount of rotation is still a
        ///reflection and no amount of rotation is ever one. That is what makes the three fields readable back
        ///off a composed matrix at all: <see cref="AngleInDegrees"/> gives the same answer either way round,
        ///so this is the only thing the rotation cannot be told apart from.
        ///</summary>
        public bool Mirrored => ((XX * YY) - (XY * YX)) < 0;

        #endregion **************************************************************************



        #region Methods *********************************************************************

        ///<summary>
        ///Builds the transform GDSII describes for a placement: reflect about the X axis first, then
        ///magnify, then rotate counterclockwise, then translate to the reference point. Rolling that
        ///order into the matrix once is what lets callers ignore it afterwards.
        ///</summary>
        public static Transform ForPlacement(bool reflectAboutX, double magnification, double angleInDegrees, double dx, double dy)
        {
            double radians = angleInDegrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            //A reflection about the X axis is a -1 on the y scale, which then rides through the rotation.
            double reflection = 1;

            if (reflectAboutX)
                reflection = -1;

            return new Transform(
                magnification * cos,
                -reflection * magnification * sin,
                magnification * sin,
                reflection * magnification * cos,
                dx,
                dy);
        }

        public static Transform ForTranslation(double dx, double dy)
        {
            return new Transform(1, 0, 0, 1, dx, dy);
        }

        ///<summary>
        ///Returns the transform equivalent to applying this one and then <paramref name="outer"/> - the
        ///order nesting needs, where a child's own placement happens inside its parent's.
        ///</summary>
        public Transform Then(Transform outer)
        {
            return new Transform(
                (outer.XX * XX) + (outer.XY * YX),
                (outer.XX * XY) + (outer.XY * YY),
                (outer.YX * XX) + (outer.YY * YX),
                (outer.YX * XY) + (outer.YY * YY),
                (outer.XX * Dx) + (outer.XY * Dy) + outer.Dx,
                (outer.YX * Dx) + (outer.YY * Dy) + outer.Dy);
        }

        ///<summary>Applies the transform, rounding back to the integer grid GDSII coordinates live on.</summary>
        public Element.Point Apply(int x, int y)
        {
            (double transformedX, double transformedY) = ApplyTo(x, y);

            return new Element.Point((int)Math.Round(transformedX), (int)Math.Round(transformedY));
        }

        ///<summary>
        ///The same without the rounding.
        ///
        ///**Because a round trip has to survive.** Editing a placed cell means taking a point in the
        ///layout, bringing it back through <see cref="Inverse"/> into the cell's own coordinates, changing
        ///it and sending it forward again - and rounding at each step turns a unit of error into two. The
        ///editor rounds once, when it writes.
        ///</summary>
        public (double X, double Y) ApplyTo(double x, double y)
        {
            return ((XX * x) + (XY * y) + Dx, (YX * x) + (YY * y) + Dy);
        }

        ///<summary>
        ///The transform that undoes this one, or null when there is nothing to undo it to.
        ///
        ///What makes editing through a placement possible: a click arrives in the top-level coordinates the
        ///layout is drawn in, and the cell it lands in holds its geometry in its own. Without this the
        ///editor could show where a shape is but not say which coordinate in the file put it there.
        ///
        ///Null only for a singular one, which in practice means a placement magnified by zero - everything
        ///inside it collapses to a point, so there is no way back out to say where in the cell a click was.
        ///Real files do carry a zero magnification occasionally, so it is a case and not an assertion.
        ///</summary>
        public Transform? Inverse()
        {
            double determinant = (XX * YY) - (XY * YX);

            if (determinant == 0 || double.IsNaN(determinant) || double.IsInfinity(determinant))
                return null;

            double xx = YY / determinant;
            double xy = -XY / determinant;
            double yx = -YX / determinant;
            double yy = XX / determinant;

            //Forward is p' = M p + D, so back is p = M⁻¹p' - M⁻¹D - which makes the inverse's own
            //translation the negated image of this one's.
            return new Transform(
                xx,
                xy,
                yx,
                yy,
                -((xx * Dx) + (xy * Dy)),
                -((yx * Dx) + (yy * Dy)));
        }

        #endregion **************************************************************************
    }
}
