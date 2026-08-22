using LibTessDotNet;

namespace GdsII.Cli
{
    ///<summary>
    ///Turns a flattened layout into solid geometry: every shape extruded to its layer's depth and lifted
    ///to its layer's place in the stack.
    ///
    ///This is the part the browser gets from three.js, which a console tool has no access to, so the same
    ///construction is done here - a triangulated cap at each end and a quad down every edge between them.
    ///The triangulation is the only piece worth a dependency: a GDSII boundary is often concave, and
    ///sky130 cells carry eight, twelve and fourteen-point outlines that a naive fan turns inside out.
    ///
    ///**The layout is not tipped over.** The 3D view rotates the stack by 1.5 radians to look at it, which
    ///is a property of the camera rather than of the layout; a file written here keeps the layout in its
    ///own X and Y with the layers stacked up Z, which is what whatever opens it will expect.
    ///</summary>
    public static class LayoutMesh
    {
        ///<summary>One layer's worth of solid, kept apart so each can carry its own name and color.</summary>
        public sealed class Part
        {
            public required LayerKey Layer { get; init; }
            public required string Name { get; init; }
            public required string Color { get; init; }

            ///<summary>Positions, three floats each, already in their place in the stack.</summary>
            public List<float> Vertices { get; } = new List<float>();

            ///<summary>Indices into <see cref="Vertices"/>, three per triangle.</summary>
            public List<int> Triangles { get; } = new List<int>();

            public int VertexCount
            {
                get { return Vertices.Count / 3; }
            }

            public int TriangleCount
            {
                get { return Triangles.Count / 3; }
            }
        }

        ///<summary>
        ///Builds one part per layer that has geometry on it, ordered the way the sidebar lists them.
        ///
        ///The layers come off the elements rather than being looked up, because the flattener hands out
        ///the same <see cref="Layer"/> instances the file's own layer table holds - so a spacing change
        ///applied before this is already reflected in the offsets read here.
        ///
        ///Labels are skipped. A TEXT element is an anchor and a string rather than an outline, and the 3D
        ///view draws it as a camera-facing billboard, which is not a thing a mesh file can hold. The
        ///browser's own exports drop them for the same reason.
        ///</summary>
        public static List<Part> Build(FlattenedLayout layout, double scale)
        {
            return Build(layout, scale, out _);
        }

        ///<summary>
        ///The same, reporting how many outlines were left out because they could not be made into a solid.
        ///Normally zero, and worth saying out loud when it is not: a shape quietly missing from an export
        ///is the kind of thing found much later by whoever opens the file.
        ///</summary>
        public static List<Part> Build(FlattenedLayout layout, double scale, out int skipped)
        {
            var parts = new Dictionary<LayerKey, Part>();

            //Counted before the merge, because the merge is where they disappear. Two points enclose no
            //area, so such an element contributes nothing to a union and nothing would say it had been
            //there - which is the one thing worth reporting about it.
            skipped = layout.Elements.Count(element => element.Text is null && element.Points.Count < 3);

            //Merged per layer first.
            //
            //Overlapping shapes on one layer extrude into solids that share a face, and a mesh with two
            //faces in the same place is not a solid at all: it is non-manifold, which is what a slicer
            //refuses and a mesh analysis tool reports as a defect. On screen the same thing is only a
            //flicker; in a file somebody prints or simulates, it matters more.
            foreach (var outline in Booleans.MergeByLayer(layout.Elements))
            {
                var layer = outline.Layer;

                if (!parts.TryGetValue(layer.Key, out var part))
                {
                    part = new Part
                    {
                        Layer = layer.Key,
                        Name = layer.DisplayName,
                        Color = layer.Color
                    };

                    parts[layer.Key] = part;
                }

                if (!append(part, outline, layer.Offset * scale, layer.Depth * scale, scale))
                    skipped++;
            }

            return parts
                .OrderBy(entry => entry.Key)
                .Select(entry => entry.Value)
                .ToList();
        }

        ///<summary>
        ///Extrudes one merged outline into the part: a cap at each end and a wall around every ring,
        ///including the rings of its holes. False when there was no solid to be had, which the caller
        ///counts rather than passes over in silence.
        ///</summary>
        private static bool append(Part part, Booleans.Outline outline, double bottom, double depth, double scale)
        {
            //The outer ring counter-clockwise and every hole clockwise. That is what makes one wall loop
            //work for both: a wall is built from its ring's own order, so a hole wound the other way gets
            //walls facing into the hole, which is the direction somebody standing in it would need to see
            //them from.
            var rings = new List<List<Element.Point>>();

            var boundary = prepare(outline.Boundary, counterClockwise: true);

            if (boundary.Count < 3)
                return false;

            rings.Add(boundary);

            foreach (var hole in outline.Holes)
            {
                var ring = prepare(hole, counterClockwise: false);

                if (ring.Count >= 3)
                    rings.Add(ring);
            }

            var capTriangles = triangulate(rings);

            //Nothing usable came back - a degenerate outline, every point on one line - so this shape is
            //left out rather than added as broken geometry.
            if (capTriangles.Count == 0)
                return false;

            double top = bottom + depth;

            var all = rings.SelectMany(ring => ring).ToList();

            int bottomStart = part.VertexCount;

            foreach (var point in all)
                addVertex(part, point.X * scale, point.Y * scale, bottom);

            int topStart = part.VertexCount;

            foreach (var point in all)
                addVertex(part, point.X * scale, point.Y * scale, top);

            //The bottom cap faces down, so its winding runs the other way to the top's.
            for (int i = 0; i < capTriangles.Count; i += 3)
            {
                addTriangle(part, bottomStart + capTriangles[i + 2], bottomStart + capTriangles[i + 1], bottomStart + capTriangles[i]);
                addTriangle(part, topStart + capTriangles[i], topStart + capTriangles[i + 1], topStart + capTriangles[i + 2]);
            }

            //And a wall down each edge, wrapping from a ring's last point back to its own first rather
            //than to the next ring's - which is what would happen if all of them were walked as one list.
            int at = 0;

            foreach (var ring in rings)
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    int here = at + i;
                    int next = at + ((i + 1) % ring.Count);

                    addTriangle(part, bottomStart + here, bottomStart + next, topStart + next);
                    addTriangle(part, bottomStart + here, topStart + next, topStart + here);
                }

                at += ring.Count;
            }

            return true;
        }

        ///<summary>
        ///Gets a ring into the shape the extrusion assumes: no repeated closing point, and running the way
        ///round it was asked for.
        ///</summary>
        private static List<Element.Point> prepare(List<Element.Point> points, bool counterClockwise)
        {
            var outline = new List<Element.Point>(points);

            //A GDSII boundary repeats its first point at the end to close the ring, which a triangulator
            //reads as a zero-length edge. Dropped rather than assumed absent, since a path's outline does
            //not carry one and a boundary always does.
            while (outline.Count > 1
                && outline[0].X == outline[^1].X
                && outline[0].Y == outline[^1].Y)
                outline.RemoveAt(outline.Count - 1);

            if (outline.Count < 3)
                return outline;

            //The tessellator hands back counter-clockwise caps whichever way the outline was written, but
            //the walls are built from the outline's own order - so unless the two are made to agree, a
            //clockwise boundary gets walls facing inward and the shape turns inside out under backface
            //culling. Nothing in the format says which way round a boundary runs, and files use both.
            if (signedArea(outline) < 0 == counterClockwise)
                outline.Reverse();

            return outline;
        }

        ///<summary>
        ///Twice the signed area by the shoelace formula, positive when the outline runs counter-clockwise.
        ///
        ///Measured relative to the first point and accumulated in double, because the coordinates are
        ///database units on an absolute grid: a shape a few hundred units across can sit a hundred million
        ///out from the origin, and the products of raw coordinates would then swamp the difference between
        ///them. Only the sign is wanted, so the shift costs nothing.
        ///</summary>
        private static double signedArea(List<Element.Point> outline)
        {
            double area = 0;

            var origin = outline[0];

            for (int i = 0; i < outline.Count; i++)
            {
                var here = outline[i];
                var next = outline[(i + 1) % outline.Count];

                double x1 = here.X - (double)origin.X;
                double y1 = here.Y - (double)origin.Y;
                double x2 = next.X - (double)origin.X;
                double y2 = next.Y - (double)origin.Y;

                area += (x1 * y2) - (x2 * y1);
            }

            return area;
        }

        ///<summary>
        ///Triangulates an outer ring and its holes together, returning indices into the rings laid end to
        ///end.
        ///
        ///One contour each. A tessellator takes holes as contours of their own, which is why the merge
        ///hands them over that way rather than as the keyhole a GDSII file has to write - a channel whose
        ///two edges lie on top of each other is exactly what this is worst at.
        ///
        ///Even-odd rather than non-zero winding, because these outlines come out of a layout rather than
        ///out of a drawing program: nothing guarantees which way round a boundary was written, and even-odd
        ///does not care - so a hole is a hole whichever way it runs.
        ///</summary>
        private static List<int> triangulate(List<List<Element.Point>> rings)
        {
            var tess = new Tess();

            int at = 0;

            foreach (var ring in rings)
            {
                var contour = new ContourVertex[ring.Count];

                for (int i = 0; i < ring.Count; i++)
                {
                    contour[i].Position = new Vec3(ring[i].X, ring[i].Y, 0);

                    //The index goes through as Data so the result can point back at the caller's own
                    //points rather than at ones the tessellator invented. Numbered across all the rings,
                    //since that is the order the vertices are written in.
                    contour[i].Data = at + i;
                }

                tess.AddContour(contour, ContourOrientation.Original);

                at += ring.Count;
            }

            var triangles = new List<int>();

            try
            {
                tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, combine);
            }
            catch
            {
                //A tessellator that cannot make sense of an outline costs that one shape, not the export.
                return triangles;
            }

            for (int i = 0; i < tess.ElementCount * 3; i++)
            {
                int index = tess.Elements[i];

                //Undefined means the tessellator dropped that triangle.
                if (index == Tess.Undef)
                    return new List<int>();

                //A vertex invented where edges cross has no original to point at - see combine below.
                if (tess.Vertices[index].Data is not int original)
                    return new List<int>();

                triangles.Add(original);
            }

            return triangles;
        }

        ///<summary>
        ///Called where edges cross and a vertex is needed that the outline does not contain. Returning
        ///null marks it as invented, which <see cref="triangulate"/> reads as "this outline cannot be
        ///expressed in its own points" and drops, rather than emitting geometry through a point that is
        ///not in the file.
        ///</summary>
        private static object? combine(Vec3 position, object[] data, float[] weights)
        {
            return null;
        }

        private static void addVertex(Part part, double x, double y, double z)
        {
            part.Vertices.Add((float)x);
            part.Vertices.Add((float)y);
            part.Vertices.Add((float)z);
        }

        private static void addTriangle(Part part, int a, int b, int c)
        {
            part.Triangles.Add(a);
            part.Triangles.Add(b);
            part.Triangles.Add(c);
        }
    }
}
