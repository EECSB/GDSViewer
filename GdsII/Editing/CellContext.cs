namespace GdsII
{
    ///<summary>
    ///Which cell is being edited, and through which of its placements it is being looked at.
    ///
    ///**Editing a hierarchical layout means editing one structure at a time.** A shape on screen may be
    ///one of a thousand instances of a cell; moving it moves all thousand, because there is one cell and
    ///the instances are references to it. So before anything can be changed there has to be an answer to
    ///"which cell am I in", and that is this.
    ///
    ///Two things, not one. <see cref="Structure"/> is what an edit changes - every instance of it moves.
    ///<see cref="Placement"/> is the instance being looked through, which is what turns a click in the
    ///layout's coordinates into a coordinate in the cell. Conflating them is how an editor ends up
    ///changing the right cell by the wrong amount.
    ///
    ///Null means no cell is being edited and the whole layout is simply being looked at, which is where
    ///the view starts. Descending is <see cref="At"/>; coming back out is <see cref="Up"/>, or letting go
    ///of the context entirely.
    ///</summary>
    public sealed class CellContext
    {
        private CellContext(IReadOnlyList<PlacementLevel> levels)
        {
            Levels = levels;
        }

        ///<summary>
        ///Every structure between the top and the one being edited, outermost first, each with the
        ///transform out to the layout's coordinates. Never empty.
        ///</summary>
        public IReadOnlyList<PlacementLevel> Levels { get; }

        ///<summary>The cell being edited. An edit here changes every instance of it.</summary>
        public string Structure
        {
            get { return Levels[^1].Structure; }
        }

        ///<summary>
        ///The instance being looked through: this cell's own coordinates out to the layout's.
        ///
        ///Which instance matters for reading a click and for nothing else - the change itself lands on the
        ///cell, so every other instance of it moves too.
        ///</summary>
        public Transform Placement
        {
            get { return Levels[^1].Placement; }
        }

        public IReadOnlyList<string> Path
        {
            get { return Levels.Select(level => level.Structure).ToList(); }
        }

        ///<summary>How many placements deep. Zero for a top-level structure, which is edited directly.</summary>
        public int Depth
        {
            get { return Levels.Count - 1; }
        }

        public bool IsTop
        {
            get { return Levels.Count == 1; }
        }

        ///<summary>
        ///The context for the cell a shape belongs to, complete with every level it was reached through.
        ///
        ///Which is the gesture: click a shape, and edit the cell that shape is in.
        ///</summary>
        public static CellContext At(ElementSource source)
        {
            return new CellContext(source.Ancestry);
        }

        ///
        ///The context for a cell reached by name rather than by clicking something in it.
        ///
        ///One level and no transform, which is what a cell looked at directly is: its own coordinates are
        ///the layout's, because nothing is placing it. That is exactly true for a cell nothing references,
        ///and it is the only honest answer for one that is placed several times - there is no single place
        ///it sits, and picking one of them would be inventing an answer to a question nobody asked.
        ///
        ///**The one way into a cell with nothing in it.** Every other route starts from a shape, and an
        ///empty cell has none - so before this there was no way to open one and draw the first shape into it.
        ///
        public static CellContext Of(string structure)
        {
            return new CellContext(new List<PlacementLevel> { new PlacementLevel(structure, Transform.Identity) });
        }

        ///<summary>
        ///One level out, or null at the top - where the way out is to stop editing rather than to go
        ///further up.
        ///</summary>
        public CellContext? Up()
        {
            if (IsTop)
                return null;

            return new CellContext(Levels.Take(Levels.Count - 1).ToList());
        }

        ///<summary>
        ///The context that many levels in, for a breadcrumb whose earlier entries are clickable. Clamped
        ///rather than throwing, because the chain a breadcrumb was drawn from may have gone away.
        ///</summary>
        public CellContext To(int depth)
        {
            int take = Math.Clamp(depth + 1, 1, Levels.Count);

            return new CellContext(Levels.Take(take).ToList());
        }

        ///<summary>
        ///Whether an edit here would change this shape.
        ///
        ///**By structure, not by instance.** Every instance of the cell being edited is affected, and a
        ///view that highlighted only the one clicked through would be telling a comfortable lie - the
        ///others move too, and somebody should be able to see them move.
        ///</summary>
        public bool Holds(Element element)
        {
            return element.Source is ElementSource source && source.Structure == Structure;
        }

        ///<summary>
        ///Whether this shape is the one instance being looked through, rather than merely one of the cell's.
        ///
        ///The difference is worth drawing: these are the shapes a click will land on, and the others are
        ///the ones that will move with them.
        ///</summary>
        public bool IsLookingThrough(Element element)
        {
            if (element.Source is not ElementSource source)
                return false;

            return source.Structure == Structure && source.Placement.Equals(Placement);
        }

        ///<summary>
        ///A point in the layout's coordinates, brought into the cell being edited. Null when the placement
        ///cannot be undone, which means a magnification of zero.
        ///</summary>
        public (double X, double Y)? ToLocal(double x, double y)
        {
            if (Placement.Inverse() is not Transform back)
                return null;

            return back.ApplyTo(x, y);
        }

        ///<summary>A point in the cell's coordinates, out to the layout's.</summary>
        public (double X, double Y) ToLayout(double x, double y)
        {
            return Placement.ApplyTo(x, y);
        }

        public override string ToString()
        {
            return string.Join(" > ", Path);
        }
    }
}
