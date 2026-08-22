namespace GdsII
{
    ///<summary>
    ///An axis-aligned box around some geometry, in database units.
    ///
    ///The one thing every consumer asks for first and the library did not answer: how big is this, and
    ///where is it. Fitting a view to a layout, placing one cell beside another, deciding whether a shape is
    ///on screen - all of them start here.
    ///
    ///**Long rather than int for the sizes.** The corners are coordinates and fit where a coordinate fits,
    ///but a layout can span most of the signed 32-bit range, so a width is a subtraction that overflows
    ///exactly when it matters. The corners stay int because that is what a GDSII coordinate is and a box
    ///that could not be written back into a file would be the wrong type to hand out.
    ///
    ///Empty is a real state rather than a zero box: a layer with nothing on it has no extent, and a box at
    ///the origin would put it somewhere.
    ///</summary>
    public readonly record struct Bounds
    {
        public Bounds(int left, int bottom, int right, int top)
        {
            Left = Math.Min(left, right);
            Bottom = Math.Min(bottom, top);
            Right = Math.Max(left, right);
            Top = Math.Max(bottom, top);

            IsEmpty = false;
        }

        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Top { get; }

        ///<summary>
        ///True for the box around nothing at all, which is what a default-constructed one is.
        ///
        ///Worth a flag rather than a convention like "right below left", because every method here has to
        ///agree on what nothing means and a convention is something each of them has to remember.
        ///</summary>
        public bool IsEmpty { get; private init; }

        ///<summary>The box around nothing. Union with anything gives that thing back.</summary>
        public static Bounds Empty
        {
            get { return new Bounds { IsEmpty = true }; }
        }

        public long Width
        {
            get { return (long)Right - Left; }
        }

        public long Height
        {
            get { return (long)Top - Bottom; }
        }

        ///<summary>
        ///The area of the box, not of what is in it - a layout's extent rather than how much of it is
        ///covered. <see cref="Measure"/> answers the other question.
        ///</summary>
        public long Area
        {
            get { return Width * Height; }
        }

        ///<summary>
        ///The middle, rounded towards negative infinity so it lands on the grid rather than between it.
        ///
        ///Integer division would round towards zero, which puts the center of a box on one side of the
        ///origin a unit away from where the same box centered on the other side would put it.
        ///</summary>
        public Element.Point Center
        {
            get
            {
                return new Element.Point(
                    (int)divideDown((long)Left + Right, 2),
                    (int)divideDown((long)Bottom + Top, 2));
            }
        }

        ///<summary>The smallest box holding every one of these points, or empty when there are none.</summary>
        public static Bounds Of(IEnumerable<Element.Point> points)
        {
            bool any = false;

            int left = int.MaxValue;
            int bottom = int.MaxValue;
            int right = int.MinValue;
            int top = int.MinValue;

            foreach (var point in points)
            {
                any = true;

                left = Math.Min(left, point.X);
                bottom = Math.Min(bottom, point.Y);
                right = Math.Max(right, point.X);
                top = Math.Max(top, point.Y);
            }

            if (!any)
                return Empty;

            return new Bounds(left, bottom, right, top);
        }

        ///<summary>The smallest box holding both.</summary>
        public Bounds Union(Bounds other)
        {
            if (IsEmpty)
                return other;

            if (other.IsEmpty)
                return this;

            return new Bounds(
                Math.Min(Left, other.Left),
                Math.Min(Bottom, other.Bottom),
                Math.Max(Right, other.Right),
                Math.Max(Top, other.Top));
        }

        ///<summary>
        ///Whether the two overlap at all, touching included.
        ///
        ///Touching counts because these are used to decide what to draw, and a shape whose edge is exactly
        ///on the edge of the view is on screen. Nothing intersects an empty box.
        ///</summary>
        public bool Intersects(Bounds other)
        {
            if (IsEmpty || other.IsEmpty)
                return false;

            return Left <= other.Right
                && Right >= other.Left
                && Bottom <= other.Top
                && Top >= other.Bottom;
        }

        ///<summary>Whether the point is inside or on the edge.</summary>
        public bool Contains(Element.Point point)
        {
            if (IsEmpty)
                return false;

            return point.X >= Left
                && point.X <= Right
                && point.Y >= Bottom
                && point.Y <= Top;
        }

        ///<summary>Whether the other box is wholly inside this one.</summary>
        public bool Contains(Bounds other)
        {
            if (IsEmpty || other.IsEmpty)
                return false;

            return other.Left >= Left
                && other.Right <= Right
                && other.Bottom >= Bottom
                && other.Top <= Top;
        }

        ///<summary>
        ///The same box with every edge moved out by this much, or in for a negative amount.
        ///
        ///Clamped to the coordinate range rather than wrapping: growing a box that already reaches the edge
        ///of what a coordinate can hold should give the edge, not the other side of it.
        ///</summary>
        public Bounds Grown(int by)
        {
            if (IsEmpty)
                return this;

            long left = (long)Left - by;
            long bottom = (long)Bottom - by;
            long right = (long)Right + by;
            long top = (long)Top + by;

            //Shrunk past nothing is nothing, rather than a box turned inside out.
            if (left > right || bottom > top)
                return Empty;

            return new Bounds(clamp(left), clamp(bottom), clamp(right), clamp(top));
        }

        ///<summary>
        ///Invariant, because these are coordinates rather than prose.
        ///
        ///A layout's coordinates are routinely negative and a culture is free to write a negative with
        ///something that is not a minus - which is not a formatting preference here but a wrong number,
        ///and it is what the hostile-culture test caught this printing as "(!1350, 0)".
        ///</summary>
        public override string ToString()
        {
            if (IsEmpty)
                return "empty";

            return FormattableString.Invariant($"({Left}, {Bottom}) to ({Right}, {Top})");
        }

        private static int clamp(long value)
        {
            return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        }

        private static long divideDown(long value, long by)
        {
            long result = value / by;

            if (value % by != 0 && value < 0)
                return result - 1;

            return result;
        }
    }
}
