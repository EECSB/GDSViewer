using System.Globalization;
using System.Numerics;
using System.Text;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace GdsII.Cli
{
    ///<summary>
    ///Writes an extruded layout out in the formats the 3D view's own download offers: STL, OBJ and glTF,
    ///plus GLB, which is the same glTF in one binary file.
    ///
    ///STL and OBJ are written here rather than through a library. Both are a list of triangles with a
    ///header - a few dozen lines each - and a dependency for that would be more to keep current than to
    ///write. glTF is not in that class: it is JSON alongside a binary buffer with accessors, byte strides
    ///and alignment rules, and SharpGLTF already gets those right.
    ///</summary>
    public static class ModelWriters
    {
        #region STL *************************************************************************

        ///<summary>
        ///Binary STL: an 80-byte header, a triangle count, then fifty bytes per triangle.
        ///
        ///The format carries no colors and no parts, so every layer lands in one undivided heap of
        ///triangles. That is STL rather than this writer - it is why the other two formats exist.
        ///</summary>
        public static void WriteBinaryStl(IReadOnlyList<LayoutMesh.Part> parts, Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            var header = new byte[80];
            byte[] note = Encoding.ASCII.GetBytes(Banner);

            Array.Copy(note, header, Math.Min(note.Length, header.Length));

            writer.Write(header);
            writer.Write((uint)parts.Sum(part => part.TriangleCount));

            foreach (var part in parts)
            {
                for (int i = 0; i < part.Triangles.Count; i += 3)
                {
                    var a = vertexOf(part, part.Triangles[i]);
                    var b = vertexOf(part, part.Triangles[i + 1]);
                    var c = vertexOf(part, part.Triangles[i + 2]);

                    writeVector(writer, normalOf(a, b, c));
                    writeVector(writer, a);
                    writeVector(writer, b);
                    writeVector(writer, c);

                    //The attribute byte count, which the format has never found a use for.
                    writer.Write((ushort)0);
                }
            }
        }

        ///<summary>
        ///The text form of the same thing, which is what the 3D view's download button produces. Around
        ///six times the size for identical geometry, so it is offered rather than assumed.
        ///</summary>
        public static void WriteAsciiStl(IReadOnlyList<LayoutMesh.Part> parts, TextWriter writer)
        {
            writer.Write("solid exported\n");

            foreach (var part in parts)
            {
                for (int i = 0; i < part.Triangles.Count; i += 3)
                {
                    var a = vertexOf(part, part.Triangles[i]);
                    var b = vertexOf(part, part.Triangles[i + 1]);
                    var c = vertexOf(part, part.Triangles[i + 2]);

                    var normal = normalOf(a, b, c);

                    writer.Write(FormattableString.Invariant($"facet normal {normal.X} {normal.Y} {normal.Z}\n"));
                    writer.Write("\touter loop\n");
                    writer.Write(FormattableString.Invariant($"\t\tvertex {a.X} {a.Y} {a.Z}\n"));
                    writer.Write(FormattableString.Invariant($"\t\tvertex {b.X} {b.Y} {b.Z}\n"));
                    writer.Write(FormattableString.Invariant($"\t\tvertex {c.X} {c.Y} {c.Z}\n"));
                    writer.Write("\tendloop\n");
                    writer.Write("endfacet\n");
                }
            }

            writer.Write("endsolid exported\n");
        }

        private static void writeVector(BinaryWriter writer, Vector3 vector)
        {
            writer.Write(vector.X);
            writer.Write(vector.Y);
            writer.Write(vector.Z);
        }

        ///<summary>
        ///The face normal, or zero for a degenerate triangle. Most readers recompute it from the winding
        ///anyway, but a NaN out of normalizing a zero-length cross product is the kind of thing that makes
        ///a whole file fail to open rather than one triangle look wrong.
        ///</summary>
        private static Vector3 normalOf(Vector3 a, Vector3 b, Vector3 c)
        {
            var cross = Vector3.Cross(b - a, c - a);

            if (cross.LengthSquared() <= float.Epsilon)
                return Vector3.Zero;

            return Vector3.Normalize(cross);
        }

        #endregion **************************************************************************



        #region OBJ *************************************************************************

        ///<summary>
        ///Wavefront OBJ, one object per layer so the parts stay separable, with a face normal per triangle
        ///and optionally a companion .mtl carrying the layer colors.
        ///
        ///Indices are one-based and count across the whole file rather than restarting per object, which
        ///is the format's rule and the usual reason a hand-written OBJ opens with everything jumbled.
        ///</summary>
        public static void WriteObj(IReadOnlyList<LayoutMesh.Part> parts, TextWriter writer, string? materialLibrary)
        {
            writer.Write($"#{Banner}\n");

            if (materialLibrary is not null)
                writer.Write($"mtllib {materialLibrary}\n");

            int verticesWritten = 0;
            int normalsWritten = 0;

            foreach (var part in parts)
            {
                writer.Write($"\no {objName(part)}\n");

                if (materialLibrary is not null)
                    writer.Write($"usemtl {objName(part)}\n");

                for (int i = 0; i < part.Vertices.Count; i += 3)
                    writer.Write(FormattableString.Invariant($"v {part.Vertices[i]} {part.Vertices[i + 1]} {part.Vertices[i + 2]}\n"));

                for (int i = 0; i < part.Triangles.Count; i += 3)
                {
                    var normal = normalOf(
                        vertexOf(part, part.Triangles[i]),
                        vertexOf(part, part.Triangles[i + 1]),
                        vertexOf(part, part.Triangles[i + 2]));

                    writer.Write(FormattableString.Invariant($"vn {normal.X} {normal.Y} {normal.Z}\n"));
                }

                for (int i = 0; i < part.Triangles.Count; i += 3)
                {
                    //One-based, offset past everything written before this object, and one normal per
                    //triangle shared by its three corners - which is what makes the faces read as flat.
                    int a = verticesWritten + part.Triangles[i] + 1;
                    int b = verticesWritten + part.Triangles[i + 1] + 1;
                    int c = verticesWritten + part.Triangles[i + 2] + 1;
                    int normal = normalsWritten + (i / 3) + 1;

                    writer.Write($"f {a}//{normal} {b}//{normal} {c}//{normal}\n");
                }

                verticesWritten += part.VertexCount;
                normalsWritten += part.TriangleCount;
            }
        }

        ///<summary>The material file OBJ points at, which is the only way colors survive that format.</summary>
        public static void WriteMtl(IReadOnlyList<LayoutMesh.Part> parts, TextWriter writer)
        {
            writer.Write($"#{Banner}\n");

            foreach (var part in parts)
            {
                var color = parseColor(part.Color);

                writer.Write($"\nnewmtl {objName(part)}\n");
                writer.Write(FormattableString.Invariant($"Kd {color.X} {color.Y} {color.Z}\n"));
                writer.Write("Ka 0 0 0\n");
                writer.Write("d 1\n");
                writer.Write("illum 1\n");
            }
        }

        ///<summary>
        ///A name OBJ can carry. The format splits its lines on whitespace, so a named layer - "diff.drawing
        ///(65/20)" - would otherwise arrive as two tokens and readers disagree about which is the name.
        ///</summary>
        private static string objName(LayoutMesh.Part part)
        {
            return part.Name.Replace(' ', '_');
        }

        #endregion **************************************************************************



        #region glTF ************************************************************************

        ///<summary>
        ///glTF, or GLB when the path says so. One mesh per layer, each with its own color, which is what
        ///makes this the most faithful of the three - STL loses the layers and OBJ needs a second file to
        ///keep their colors.
        ///
        ///No normals are written. glTF says a primitive without them is flat shaded, with the normal taken
        ///from each triangle's own winding, and that is exactly right for extruded geometry - supplying
        ///per-vertex normals would mean splitting every vertex the caps and the walls share.
        ///</summary>
        public static void WriteGltf(IReadOnlyList<LayoutMesh.Part> parts, string path)
        {
            var scene = new SceneBuilder();

            foreach (var part in parts)
            {
                var color = parseColor(part.Color);

                var material = new MaterialBuilder(part.Name)
                    .WithDoubleSide(false)
                    .WithMetallicRoughnessShader()
                    .WithBaseColor(new Vector4(color, 1));

                var mesh = new MeshBuilder<VertexPosition>(part.Name);
                var primitive = mesh.UsePrimitive(material);

                for (int i = 0; i < part.Triangles.Count; i += 3)
                {
                    primitive.AddTriangle(
                        new VertexPosition(vertexOf(part, part.Triangles[i])),
                        new VertexPosition(vertexOf(part, part.Triangles[i + 1])),
                        new VertexPosition(vertexOf(part, part.Triangles[i + 2])));
                }

                scene.AddRigidMesh(mesh, Matrix4x4.Identity);
            }

            var model = scene.ToGltf2();

            if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                model.SaveGLB(path);

                return;
            }

            //Indented, the way the 3D view's own download writes it - a .gltf is JSON that someone may
            //well open, and the geometry is in the buffer beside it either way.
            model.SaveGLTF(path, new WriteSettings { JsonIndented = true });
        }

        #endregion **************************************************************************



        #region Shared **********************************************************************

        private const string Banner = "Written by gds, the GdsII command-line tool";

        private static Vector3 vertexOf(LayoutMesh.Part part, int index)
        {
            int at = index * 3;

            return new Vector3(part.Vertices[at], part.Vertices[at + 1], part.Vertices[at + 2]);
        }

        ///<summary>
        ///Reads the "#rrggbb" the palette and the layermaps write. Anything else comes back mid gray
        ///rather than throwing: a color is a presentation detail, and an export that fails outright
        ///because one layer was given an unusual color would be a poor trade.
        ///</summary>
        private static Vector3 parseColor(string color)
        {
            string text = color.TrimStart('#');

            if (text.Length != 6
                || !int.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int red)
                || !int.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int green)
                || !int.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int blue))
                return new Vector3(0.5f, 0.5f, 0.5f);

            return new Vector3(red / 255f, green / 255f, blue / 255f);
        }

        #endregion **************************************************************************
    }
}
