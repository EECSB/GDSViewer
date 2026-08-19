using System.Globalization;
using System.Net;
using System.Text;

namespace GdsII
{
    ///<summary>
    ///What a run found, written as KLayout's report database - a `.lyrdb` file.
    ///
    ///**The one format in this whole feature that somebody else defined.** There is no interchange format
    ///for design *rules*, which is why the deck is this app's own - but the *results* are a different
    ///matter: KLayout's marker browser reads this, and a fault found here can then be looked at in the tool
    ///the rest of the world uses. It is also the only way to hold this engine to an outside standard, since
    ///a report both tools can open is a report both tools can be made to disagree about.
    ///
    ///**The shape was learned from KLayout rather than guessed**, by running a deck through it and reading
    ///what came out. Two things about it would not have been guessed right: the coordinates are in
    ///**microns** where everything else here is database units, and a category is named in single quotes
    ///inside its own element - `&lt;category&gt;'met1.2'&lt;/category&gt;`.
    ///</summary>
    public static class DrcReport
    {
        ///<summary>
        ///The whole report, as the XML KLayout reads.
        ///
        ///<paramref name="topCell"/> names the cell a violation is filed under when nothing more specific
        ///is known. A violation that carries its own <see cref="ElementSource"/> is filed under the cell it
        ///actually came from, which is the part a flat checker is not normally able to say.
        ///</summary>
        public static string Write(DrcResult result, DrcDeck deck, GDS gds, string topCell, string description = "GDS Viewer")
        {
            double microns = DxfWriter.MicronsPerUnit(gds);

            var builder = new StringBuilder();

            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            builder.Append("<report-database>\n");
            builder.Append(" <description>").Append(escaped(description)).Append("</description>\n");
            builder.Append(" <original-file/>\n");
            builder.Append(" <generator>gds drc</generator>\n");
            builder.Append(" <top-cell>").Append(escaped(topCell)).Append("</top-cell>\n");
            builder.Append(" <tags>\n </tags>\n");

            appendCategories(builder, result, deck);
            appendCells(builder, result, topCell);
            appendItems(builder, result, topCell, microns);

            builder.Append("</report-database>\n");

            return builder.ToString();
        }

        ///<summary>
        ///One category per rule that found something, with the deck's own wording as its description.
        ///
        ///Only the rules that found something. A category per rule in the deck would fill the browser's
        ///list with thirty entries of which two matter, and KLayout's own writer does the same.
        ///</summary>
        private static void appendCategories(StringBuilder builder, DrcResult result, DrcDeck deck)
        {
            builder.Append(" <categories>\n");

            foreach (string id in ruleOrder(result))
            {
                builder.Append("  <category>\n");
                builder.Append("   <name>").Append(escaped(id)).Append("</name>\n");
                builder.Append("   <description>").Append(escaped(descriptionOf(deck, id))).Append("</description>\n");
                builder.Append("   <categories>\n   </categories>\n");
                builder.Append("  </category>\n");
            }

            builder.Append(" </categories>\n");
        }

        ///<summary>
        ///Every cell a violation is filed under, which a report database has to declare before it uses.
        ///
        ///The top cell is always among them even when nothing is filed under it, because a report naming no
        ///cell at all is one KLayout opens onto nothing.
        ///</summary>
        private static void appendCells(StringBuilder builder, DrcResult result, string topCell)
        {
            var cells = new List<string> { topCell };

            foreach (var violation in result.Violations)
            {
                string cell = cellOf(violation, topCell);

                if (!cells.Contains(cell))
                    cells.Add(cell);
            }

            builder.Append(" <cells>\n");

            foreach (string cell in cells)
            {
                builder.Append("  <cell>\n");
                builder.Append("   <name>").Append(escaped(cell)).Append("</name>\n");
                builder.Append("   <variant/>\n   <layout-name/>\n");
                builder.Append("   <references>\n   </references>\n");
                builder.Append("  </cell>\n");
            }

            builder.Append(" </cells>\n");
        }

        private static void appendItems(StringBuilder builder, DrcResult result, string topCell, double microns)
        {
            builder.Append(" <items>\n");

            foreach (var violation in result.Violations)
            {
                if (violation.Marker.Count == 0)
                    continue;

                builder.Append("  <item>\n");
                builder.Append("   <tags/>\n");

                //Quoted inside the element, which is KLayout's own doing and not a mistake here. A name
                //written bare is read as a different category from the one the file declares.
                builder.Append("   <category>'").Append(escaped(violation.RuleId)).Append("'</category>\n");
                builder.Append("   <cell>").Append(escaped(cellOf(violation, topCell))).Append("</cell>\n");
                builder.Append("   <visited>false</visited>\n");
                builder.Append("   <multiplicity>1</multiplicity>\n");
                builder.Append("   <comment/>\n   <image/>\n");
                builder.Append("   <values>\n    <value>");
                builder.Append(valueOf(violation, microns));
                builder.Append("</value>\n   </values>\n");
                builder.Append("  </item>\n");
            }

            builder.Append(" </items>\n");
        }

        ///<summary>
        ///A marker as one of KLayout's typed values.
        ///
        ///A region is a `polygon`. A point - which is what an off-grid fault is - is written as an `edge`
        ///of no length, since the format has no point of its own and an edge that begins where it ends is
        ///the honest way to say a coordinate with no extent.
        ///</summary>
        private static string valueOf(DrcViolation violation, double microns)
        {
            var builder = new StringBuilder();

            if (violation.Marker.Count < 3)
            {
                var at = violation.Marker[0];

                builder.Append("edge: (");
                appendPoint(builder, at, microns);
                builder.Append(';');
                appendPoint(builder, at, microns);
                builder.Append(')');

                return builder.ToString();
            }

            builder.Append("polygon: (");

            for (int i = 0; i < violation.Marker.Count; i++)
            {
                if (i > 0)
                    builder.Append(';');

                appendPoint(builder, violation.Marker[i], microns);
            }

            builder.Append(')');

            return builder.ToString();
        }

        ///<summary>
        ///One coordinate, in microns.
        ///
        ///**The one place this feature leaves database units**, because the format is somebody else's and
        ///it is written in microns. Invariant, like every other number here: a comma-decimal locale would
        ///put a comma where this format expects a decimal point, inside a list whose own separator is a
        ///semicolon - so KLayout would read a polygon of twice as many points, in the wrong places.
        ///</summary>
        private static void appendPoint(StringBuilder builder, Element.Point point, double microns)
        {
            builder.Append((point.X * microns).ToString("0.######", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append((point.Y * microns).ToString("0.######", CultureInfo.InvariantCulture));
        }

        ///<summary>The rules that found something, in the order they first did.</summary>
        private static List<string> ruleOrder(DrcResult result)
        {
            var order = new List<string>();

            foreach (var violation in result.Violations)
            {
                if (!order.Contains(violation.RuleId))
                    order.Add(violation.RuleId);
            }

            return order;
        }

        private static string descriptionOf(DrcDeck deck, string id)
        {
            foreach (var rule in deck.Rules)
            {
                if (rule.Id == id)
                    return rule.Description;
            }

            return "";
        }

        private static string cellOf(DrcViolation violation, string topCell)
        {
            if (violation.Source is ElementSource source)
                return source.Structure;

            return topCell;
        }

        private static string escaped(string text)
        {
            return WebUtility.HtmlEncode(text);
        }
    }
}
