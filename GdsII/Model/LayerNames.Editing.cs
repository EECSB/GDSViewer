using System.Text;

namespace GdsII
{
    ///
    ///A mapping changed a row at a time and written back out - for a PDK, whose layers are a mapping kept as text and
    ///edited from the layer sidebar or the app's PDK manager.
    ///
    public partial class LayerNames
    {
        ///
        ///This mapping as text that <see cref="Parse"/> reads back as the same mapping: a row for every pair it says
        ///anything about, in pair order, with only what each row says.
        ///
        ///**Through the writer a session's names go through** - see <see cref="Named"/> - by laying each row on a layer
        ///of its own and writing that, so the two cannot write a row differently. What is kept is what the rows say, not
        ///how they were laid out: comments and blank lines are not, and neither is a row that said nothing.
        ///
        public string Write()
        {
            var builder = new StringBuilder();

            builder.Append(HeaderRow);

            foreach (var key in Pairs)
            {
                var layer = new Layer(key, "");

                ApplyTo(new Dictionary<LayerKey, Layer> { [key] = layer });

                appendRow(builder, key, layer, layer.Name ?? "", everyColumn: false);
            }

            return builder.ToString();
        }

        ///
        ///Makes the row for <paramref name="key"/> say what <paramref name="layer"/> says: every column chosen on it, the
        ///way <see cref="Named"/> writes a layer, and no row at all for a layer nothing was said about. For a PDK taking
        ///in an edit made to one of a file's layers - which, on a file the PDK names, carries the PDK's row and the edit.
        ///
        public void SetRow(LayerKey key, Layer layer)
        {
            Remove(key);

            if (!layer.WasSaid)
                return;

            var builder = new StringBuilder();

            appendRow(builder, key, layer, layer.Name ?? "", everyColumn: false);

            MergeFrom(Parse(builder.ToString()));
        }

        ///<summary>Drops everything this mapping says about <paramref name="key"/>.</summary>
        public void Remove(LayerKey key)
        {
            Names.Remove(key);
            Colors.Remove(key);
            Stack.Remove(key);
            Roles.Remove(key);
            Fills.Remove(key);
            PatternColors.Remove(key);
            PatternSizes.Remove(key);
            Textures.Remove(key);
            TextureSizes.Remove(key);
        }

        ///
        ///Lays <paramref name="other"/> over this mapping column by column: whatever it says about a pair replaces what
        ///this says, and whatever it leaves out stays - the way <see cref="ApplyTo"/> lays a mapping over a file's layers.
        ///For a layermap imported into a PDK, which may well name a layer without saying how high it is.
        ///
        public void MergeFrom(LayerNames other)
        {
            copy(other.Names, Names);
            copy(other.Colors, Colors);
            copy(other.Stack, Stack);
            copy(other.Roles, Roles);
            copy(other.Fills, Fills);
            copy(other.PatternColors, PatternColors);
            copy(other.PatternSizes, PatternSizes);
            copy(other.Textures, Textures);
            copy(other.TextureSizes, TextureSizes);
        }

        private static void copy<T>(Dictionary<LayerKey, T> from, Dictionary<LayerKey, T> into)
        {
            foreach (var entry in from)
                into[entry.Key] = entry.Value;
        }

        ///
        ///Whether this mapping is for the technology a file with these layers was drawn in, judged by them: at least half
        ///are rows of it. A file with no layers yet fits any - a new layout, which is drawn in whatever the PDK is.
        ///
        ///**Half, not one.** A pair in common is a coincidence two processes can share, and labels from one laid over a
        ///layout from the other read as right when they are wrong - worse than numbers. **And not all**, since a real
        ///file carries area ids and markers no layermap lists, the bundled designs among them.
        ///
        public bool Fits(IEnumerable<LayerKey> layers)
        {
            var rows = new HashSet<LayerKey>(Pairs);
            int count = 0;
            int named = 0;

            foreach (var key in layers)
            {
                count++;

                if (rows.Contains(key))
                    named++;
            }

            return count == 0 || named * 2 >= count;
        }
    }
}
