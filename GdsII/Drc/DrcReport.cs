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
    ///
    ///**Read as well as written.** <see cref="Read(string, GDS, FlattenedLayout)"/> takes a report KLayout
    ///or anything else wrote and places it on a layout, which is the other way round the same door.
    ///</summary>
    public static partial class DrcReport
    {
        ///
        ///The category a report puts what its check did not get to under: the layout the rules never saw, the rules
        ///that could not be measured, and what could not be read of the deck - <see cref="DrcResult.NotChecked"/>,
        ///<see cref="DrcResult.NotRun"/> and <see cref="DrcResult.Problems"/>, an item for each sentence, as its text.
        ///
        ///**In the report, not only beside it.** A check over part of IHP's SRAM macro found 141 faults that are not
        ///there, and a report of it held the 141 and nothing else - a list of faults to go and fix, to whoever opened it
        ///in KLayout's marker browser. The format has no field for a check that fell short, so it is said the two ways
        ///that browser shows without being asked: a category of its own, first in the file and so first in the
        ///browser's list, whose items' text is on their rows and whose description says they are not faults; and the
        ///report's description, which the browser's choice of report shows at its top, ending "not fully checked".
        ///
        ///**Told apart by what is in it as well as by its name.** An item here holds a sentence and nothing to draw,
        ///where a fault always holds its marker - so a rule somebody did name "Not checked" is still read back as the
        ///rule it is. See <see cref="DrcReportFile.NotChecked"/>.
        ///
        public const string NotCheckedCategory = "Not checked";

        ///<summary>What <see cref="NotCheckedCategory"/> says of itself in the marker browser, over every item in it.</summary>
        private const string NotCheckedDescription = "Not faults: what this check did not get to. The layout has not been fully checked, and nothing in this report says it is clean.";

        ///<summary>
        ///The whole report, as the XML KLayout reads.
        ///
        ///<paramref name="topCell"/> names the cell a violation is filed under when nothing more specific
        ///is known. A violation that carries its own <see cref="ElementSource"/> is filed under the cell it
        ///actually came from, which is the part a flat checker is not normally able to say.
        ///
        ///**In that cell's own coordinates, with a reference saying where the cell is.** A report gives an
        ///item's values in the coordinates of the cell it is filed under - KLayout's deep mode writes them
        ///that way, and places each cell through the transform its one reference carries. Filed under the
        ///cell with the top's coordinates, as this used to, the marker browser moved every one of them by
        ///the placement a second time: a fault in a cell placed ten microns over was drawn twenty over. A
        ///cell met in several places is told apart by variant, as KLayout does, so each fault is shown at the
        ///instance it was actually found in.
        ///</summary>
        public static string Write(DrcResult result, DrcDeck deck, GDS gds, string topCell, string description = "GDS Viewer")
        {
            return Write(result, deck, gds, topCell, description, new HashSet<string>());
        }

        ///<summary>
        ///The same, with the faults somebody waived - by <see cref="DrcViolation.Key"/> - tagged `waived`.
        ///
        ///**KLayout's own tag, declared the way it declares it**, so its marker browser shows them waived too, and
        ///a report read back here comes in with them waived - see <see cref="Read(string, GDS, FlattenedLayout)"/>.
        ///Declared only when one is waived, which is what KLayout writes for a database nobody has tagged.
        ///</summary>
        public static string Write(DrcResult result, DrcDeck deck, GDS gds, string topCell, string description, IReadOnlySet<string> waived)
        {
            double microns = DxfWriter.MicronsPerUnit(gds);

            var declared = new List<FiledCell>();
            var filed = filing(result, topCell, declared);

            var builder = new StringBuilder();

            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            builder.Append("<report-database>\n");
            builder.Append(" <description>").Append(escaped(describedAs(result, description))).Append("</description>\n");
            builder.Append(" <original-file/>\n");
            builder.Append(" <generator>gds drc</generator>\n");
            builder.Append(" <top-cell>").Append(escaped(topCell)).Append("</top-cell>\n");

            if (result.Violations.Any(violation => waived.Contains(violation.Key)))
                builder.Append(" <tags>\n  <tag>\n   <name>waived</name>\n   <description/>\n  </tag>\n </tags>\n");
            else
                builder.Append(" <tags>\n </tags>\n");

            appendCategories(builder, result, deck);
            appendCells(builder, declared, topCell, microns);
            appendItems(builder, result, filed, topCell, microns, waived);

            builder.Append("</report-database>\n");

            return builder.ToString();
        }

        ///<summary>The report's own description, said to fall short where the check did - see <see cref="NotCheckedCategory"/>.</summary>
        private static string describedAs(DrcResult result, string description)
        {
            if (result.Complete)
                return description;

            if (description.Length == 0)
                return "not fully checked";

            return $"{description} - not fully checked";
        }

        ///<summary>
        ///One category per rule that found something, with the deck's own wording as its description - after
        ///<see cref="NotCheckedCategory"/>, when the check did not get to everything.
        ///
        ///Only the rules that found something. A category per rule in the deck would fill the browser's
        ///list with thirty entries of which two matter, and KLayout's own writer does the same.
        ///</summary>
        private static void appendCategories(StringBuilder builder, DrcResult result, DrcDeck deck)
        {
            builder.Append(" <categories>\n");

            if (!result.Complete)
                appendCategory(builder, NotCheckedCategory, NotCheckedDescription);

            foreach (string id in ruleOrder(result))
                appendCategory(builder, id, descriptionOf(deck, id));

            builder.Append(" </categories>\n");
        }

        private static void appendCategory(StringBuilder builder, string name, string description)
        {
            builder.Append("  <category>\n");
            builder.Append("   <name>").Append(escaped(name)).Append("</name>\n");
            builder.Append("   <description>").Append(escaped(description)).Append("</description>\n");
            builder.Append("   <categories>\n   </categories>\n");
            builder.Append("  </category>\n");
        }

        ///<summary>
        ///Every cell a violation is filed under, which a report database has to declare before it uses.
        ///
        ///The top cell is always among them even when nothing is filed under it, because a report naming no
        ///cell at all is one KLayout opens onto nothing. Every other one carries the reference that says where
        ///it sits in the top, which is what puts its items back where they were found.
        ///</summary>
        private static void appendCells(StringBuilder builder, List<FiledCell> declared, string topCell, double microns)
        {
            builder.Append(" <cells>\n");

            appendCell(builder, topCell, "", null, topCell, microns);

            foreach (var cell in declared)
                appendCell(builder, cell.Name, cell.Variant, cell.Placement, topCell, microns);

            builder.Append(" </cells>\n");
        }

        private static void appendCell(StringBuilder builder, string name, string variant, Transform? placement, string topCell, double microns)
        {
            builder.Append("  <cell>\n");
            builder.Append("   <name>").Append(escaped(name)).Append("</name>\n");

            if (variant.Length == 0)
                builder.Append("   <variant/>\n");
            else
                builder.Append("   <variant>").Append(escaped(variant)).Append("</variant>\n");

            builder.Append("   <layout-name/>\n");
            builder.Append("   <references>\n");

            if (placement is Transform placing)
            {
                builder.Append("    <ref>\n");
                builder.Append("     <parent>").Append(escaped(topCell)).Append("</parent>\n");
                builder.Append("     <trans>").Append(transOf(placing, microns)).Append("</trans>\n");
                builder.Append("    </ref>\n");
            }

            builder.Append("   </references>\n");
            builder.Append("  </cell>\n");
        }

        private static void appendItems(StringBuilder builder, DrcResult result, List<FiledCell?> filed, string topCell, double microns, IReadOnlySet<string> waived)
        {
            builder.Append(" <items>\n");

            //Ahead of the faults, as its category is ahead of theirs: everything below is a count over what this says.
            foreach (string sentence in result.NotChecked.Concat(result.NotRun).Concat(result.Problems))
            {
                builder.Append("  <item>\n");
                builder.Append("   <tags/>\n");
                builder.Append("   <category>'").Append(escaped(NotCheckedCategory)).Append("'</category>\n");
                builder.Append("   <cell>").Append(escaped(topCell)).Append("</cell>\n");
                builder.Append("   <visited>false</visited>\n");
                builder.Append("   <multiplicity>1</multiplicity>\n");
                builder.Append("   <comment/>\n   <image/>\n");
                builder.Append("   <values>\n    <value>text: ");
                builder.Append(escaped(quotedText(sentence)));
                builder.Append("</value>\n   </values>\n");
                builder.Append("  </item>\n");
            }

            for (int i = 0; i < result.Violations.Count; i++)
            {
                var violation = result.Violations[i];

                if (violation.Marker.Count == 0)
                    continue;

                string cell = topCell;
                Transform? back = null;

                if (filed[i] is FiledCell under)
                {
                    cell = under.QualifiedName();
                    back = under.Placement.Inverse();
                }

                builder.Append("  <item>\n");

                if (waived.Contains(violation.Key))
                    builder.Append("   <tags>waived</tags>\n");
                else
                    builder.Append("   <tags/>\n");

                //Quoted inside the element, which is KLayout's own doing and not a mistake here. A name
                //written bare is read as a different category from the one the file declares.
                builder.Append("   <category>'").Append(escaped(violation.RuleId)).Append("'</category>\n");
                builder.Append("   <cell>").Append(escaped(cell)).Append("</cell>\n");
                builder.Append("   <visited>false</visited>\n");
                builder.Append("   <multiplicity>1</multiplicity>\n");
                builder.Append("   <comment/>\n   <image/>\n");
                builder.Append("   <values>\n    <value>");
                builder.Append(valueOf(violation, back, microns));
                builder.Append("</value>\n   </values>\n");
                builder.Append("  </item>\n");
            }

            builder.Append(" </items>\n");
        }

        ///<summary>
        ///A marker as one of KLayout's typed values, in the coordinates of the cell it is filed under.
        ///
        ///A region is a `polygon`. A point - which is what an off-grid fault is - is written as an `edge`
        ///of no length, since the format has no point of its own and an edge that begins where it ends is
        ///the honest way to say a coordinate with no extent. A marker of two points is the edge it is.
        ///
        ///<paramref name="back"/> takes the top's coordinates into the cell's; null for one filed under the
        ///top, whose coordinates are the top's already.
        ///</summary>
        private static string valueOf(DrcViolation violation, Transform? back, double microns)
        {
            var builder = new StringBuilder();

            if (violation.Marker.Count < 3)
            {
                builder.Append("edge: (");
                appendPoint(builder, violation.Marker[0], back, microns);
                builder.Append(';');
                appendPoint(builder, violation.Marker[^1], back, microns);
                builder.Append(')');

                return builder.ToString();
            }

            builder.Append("polygon: (");

            for (int i = 0; i < violation.Marker.Count; i++)
            {
                if (i > 0)
                    builder.Append(';');

                appendPoint(builder, violation.Marker[i], back, microns);
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
        ///
        ///Not rounded back onto the grid after <paramref name="back"/> has taken it into its cell: a cell
        ///placed at a quarter turn and a half-unit offset has its corners between grid points, and rounding
        ///them there would move the marker once it was placed again.
        ///</summary>
        private static void appendPoint(StringBuilder builder, Element.Point point, Transform? back, double microns)
        {
            double x = point.X;
            double y = point.Y;

            if (back is Transform into)
                (x, y) = into.ApplyTo(point.X, point.Y);

            builder.Append((x * microns).ToString("0.######", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append((y * microns).ToString("0.######", CultureInfo.InvariantCulture));
        }

        ///<summary>
        ///A placement as KLayout writes a transform: `r90 *1 10.5,-2.25`, or `m45 *1 0,0` for one that mirrors.
        ///
        ///The move in microns, like every other length in the file. A mirrored placement is written as the
        ///line it mirrors across, which is at half its turn - see transformOf, which reads these back.
        ///</summary>
        private static string transOf(Transform placement, double microns)
        {
            double angle = placement.AngleInDegrees % 360;

            if (angle < 0)
                angle += 360;

            string turn;

            if (placement.Mirrored)
                turn = "m" + numberText(angle / 2);
            else
                turn = "r" + numberText(angle);

            return $"{turn} *{numberText(placement.Scale)} {numberText(placement.Dx * microns)},{numberText(placement.Dy * microns)}";
        }

        private static string numberText(double value)
        {
            return value.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        ///<summary>
        ///The cell each violation is filed under - null for the top - with every other cell added to
        ///<paramref name="declared"/> once.
        ///
        ///A cell is one place it is drawn: the same cell placed twice holds two different faults, at two
        ///different places, and the report needs to say which. One found in a single place keeps its plain
        ///name; one found in several is numbered by variant in the order its places first turn up, which is
        ///what KLayout does with a cell it meets in more than one context.
        ///</summary>
        private static List<FiledCell?> filing(DrcResult result, string topCell, List<FiledCell> declared)
        {
            var filed = new List<FiledCell?>(result.Violations.Count);
            var byPlace = new Dictionary<(string, Transform), FiledCell>();

            foreach (var violation in result.Violations)
            {
                //A placement magnified to nothing has no way back into its cell, so its fault stays in the top's
                //coordinates and is filed there.
                if (violation.Source is not ElementSource source || source.Structure == topCell || source.Placement.Inverse() is null)
                {
                    filed.Add(null);

                    continue;
                }

                var key = (source.Structure, source.Placement);

                if (!byPlace.TryGetValue(key, out var cell))
                {
                    cell = new FiledCell { Name = source.Structure, Placement = source.Placement };

                    byPlace[key] = cell;
                    declared.Add(cell);
                }

                filed.Add(cell);
            }

            foreach (var group in declared.GroupBy(cell => cell.Name))
            {
                if (group.Count() < 2)
                    continue;

                int variant = 0;

                foreach (var cell in group)
                {
                    variant++;

                    cell.Variant = variant.ToString(CultureInfo.InvariantCulture);
                }
            }

            return filed;
        }

        ///<summary>A cell a report files violations under, at one of the places it is drawn.</summary>
        private sealed class FiledCell
        {
            public required string Name { get; init; }

            ///<summary>The transform from this cell's coordinates out to the top's.</summary>
            public required Transform Placement { get; init; }

            ///<summary>Empty for a cell met in one place, and its number among them for one met in several.</summary>
            public string Variant { get; set; } = "";

            public string QualifiedName()
            {
                if (Variant.Length == 0)
                    return Name;

                return $"{Name}:{Variant}";
            }
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

        private static string escaped(string text)
        {
            return WebUtility.HtmlEncode(text);
        }

        ///<summary>A text value's words as KLayout quotes them: in single quotes, with a quote, a backslash and a line end escaped.</summary>
        private static string quotedText(string text)
        {
            var builder = new StringBuilder("'");

            foreach (char character in text)
            {
                if (character == '\'' || character == '\\')
                    builder.Append('\\').Append(character);
                else if (character == '\n')
                    builder.Append("\\n");
                else if (character == '\r')
                    builder.Append("\\r");
                else if (character == '\t')
                    builder.Append("\\t");
                else
                    builder.Append(character);
            }

            return builder.Append('\'').ToString();
        }
    }
}
