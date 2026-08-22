using System.Globalization;
using System.Text;

namespace GdsII
{
    ///<summary>
    ///Which geometric question a rule asks.
    ///
    ///A closed set on purpose. The alternative was an expression language like KLayout's, where a rule is
    ///a program - and the moment a deck can express something the engine cannot measure, the deck becomes
    ///a place to write a rule that silently does not run. A fixed vocabulary means an unsupported rule is
    ///refused *by name* at the door, which is what keeps a clean report honest.
    ///</summary>
    public enum DrcCheck
    {
        ///<summary>Narrower than the limit anywhere in the shape.</summary>
        Width,

        ///<summary>Two shapes closer than the limit. One layer against itself, or one against another.</summary>
        Space,

        ///<summary>A gap inside one shape narrower than the limit.</summary>
        Notch,

        ///<summary>
        ///The second layer does not surround the first by the limit, in every direction.
        ///
        ///**There is deliberately no `extension` beside this.** A deck writing one is refused by name, and
        ///the reason is that it would be this operation with the arguments the other way round - the two
        ///were the same six lines of code - while every extension rule a real deck carries is *directional*.
        ///An endcap is poly reaching past diffusion at the two ends of a channel and says nothing about its
        ///sides; measured in every direction it reports the sides, which on a transistor is the entire
        ///answer and all of it wrong. Direction needs edge pairs, so the rule is refused rather than
        ///approximated.
        ///</summary>
        Enclosure,

        ///<summary>A merged shape smaller than the limit, in square database units.</summary>
        Area,

        ///<summary>A hole in a merged shape smaller than the limit. Real decks state this separately.</summary>
        HoleArea,

        ///
        ///A window of the layout where the layer covers less than the limit, in **tenths of a percent**.
        ///
        ///**Not a distance and not an area**, which is why the unit has to be stated. A density is a ratio,
        ///and a deck holds whole numbers - so 300 is 30%, which is a real figure for a metal layer's
        ///minimum fill. Writing it as a fraction would need decimals in a format that deliberately has
        ///none.
        ///
        ///The window and the step come from the rule's own operands rather than being fixed, because a rule
        ///states both: `density met1 100000 50000 300` is a hundred-micron window stepped fifty microns,
        ///needing 30%. Stepping by less than the width is the point - a window stepped by its own width
        ///misses a sparse patch that straddles two of them.
        ///
        ///Minimum only. A maximum-density rule is the same sweep with the comparison turned round, and no
        ///bundled rule needs one yet, so it is not offered rather than being offered and untested.
        ///
        Density,

        ///
        ///A net carrying more metal than the gate it reaches can survive being charged by.
        ///
        ///**The one rule here that is about connectivity rather than geometry.** During manufacture a long
        ///run of metal collects charge from the plasma that etches it, and the charge leaves through
        ///whatever gate oxide the run is attached to - so what matters is not how big any shape is but the
        ///ratio of a *whole net's* metal to the gate area at the end of it. Two identical wires are fine or
        ///fatal depending on what else is on their net.
        ///
        ///Written `antenna &lt;metal&gt; &lt;gate&gt; &lt;ratio&gt;`, and the ratio is plain: 400 means
        ///four hundred to one.
        ///
        ///**Needs layer roles**, which is what tells one layer of metal from the via joining it to the next
        ///- and a GDSII file says neither. Without them nothing is connected to anything, every net is one
        ///shape, and the rule would pass a layout it never looked at. It is reported as a rule that did not
        ///run instead. See <see cref="LayerRole"/> and the layermap's seventh column.
        ///
        ///Nets reaching no gate at all are skipped rather than reported. A run of metal attached to no gate
        ///has no oxide to damage, and dividing by its absent area would make every dangling wire the worst
        ///antenna in the file.
        ///
        Antenna,

        ///<summary>
        ///A coordinate that is not a multiple of the manufacturing grid, which the rule's value carries.
        ///
        ///**The grid has to be stated, and cannot be recovered from the file.** <see cref="Grid.Of"/> reads
        ///back the coarsest grid every coordinate sits on, which is the greatest common divisor of them
        ///all - exactly the right answer for snapping, and circular for checking: one coordinate at 3 among
        ///a file of multiples of 5 drags that divisor to 1, and nothing is ever off a grid of 1. The stray
        ///coordinate defines the grid away. So the number is PDK data like every other value in a deck, and
        ///the deck states it: five, on sky130.
        ///</summary>
        OffGrid
    }

    ///<summary>One rule: what to measure, on what, and how much is allowed.</summary>
    public sealed class DrcRule
    {
        ///<summary>
        ///What the rule is called where it was written down - `met1.2`, `difftap.8`.
        ///
        ///**A label and never a check.** The checker keys on <see cref="Check"/> for what to measure and on
        ///this only for what to print, so an id transcribed wrongly misnames a violation and cannot change
        ///what was measured. That matters more than it sounds: the two sky130 documentation pages disagree
        ///about numbering, so some transcription is going to be wrong.
        ///</summary>
        public required string Id { get; init; }

        public required DrcCheck Check { get; init; }

        ///<summary>
        ///The layers the check runs on, in the order the rule names them. One for a width, two for an
        ///enclosure. The single operand of an <see cref="DrcCheck.OffGrid"/> rule may be
        ///<see cref="DrcDeck.EveryLayer"/>.
        ///</summary>
        public required IReadOnlyList<string> Operands { get; init; }

        ///<summary>
        ///The limit, in database units - or in *square* database units for the two area checks.
        ///
        ///Whole, because the layout is. Nothing is scaled on the way in: a deck for a file whose database
        ///unit is a nanometer is written in nanometers, which is the micron figure in the documentation
        ///times a thousand.
        ///
        ///For <see cref="DrcCheck.OffGrid"/> it is the manufacturing grid rather than a distance, and it is
        ///stated for the reason given there: a grid read back off the file is defined away by the very
        ///coordinates the check is looking for.
        ///</summary>
        public long Value { get; init; }

        public string Description { get; init; } = "";

        ///
        ///Which line of the deck this rule was read from, counting from one. Zero for a rule that came from
        ///somewhere other than text.
        ///
        ///**So that a rule can be taken back out.** A deck *is* its text and everything else here is derived
        ///from it, so removing a rule means removing its line and reading the deck again - not reaching into
        ///a parsed list the text would then disagree with. Matching by id instead would nearly work, and fail
        ///on a deck carrying the same id twice, which is exactly the deck somebody is in the middle of fixing.
        ///
        public int Line { get; init; }

        ///<summary>
        ///A layer inside which this rule does not apply, or null for one that always does.
        ///
        ///Real decks are full of these - sky130 exempts several rules inside a marker region - and it is
        ///the one qualifier of the three that costs nothing to honor: the violations are found as usual and
        ///then the exempt area is subtracted off them.
        ///</summary>
        public string? Except { get; init; }

        ///<summary>
        ///The square a <see cref="DrcCheck.Density"/> rule is measured over, in database units. Zero for
        ///every other check, which measures over nothing but the shapes themselves.
        ///</summary>
        public int Window { get; init; }

        ///<summary>
        ///How far that square moves between measurements, in database units.
        ///
        ///Less than the window, in any rule worth writing. A window stepped by its own width tiles the
        ///layout without overlapping, and a sparse patch straddling two of them is then half-counted in
        ///each and reported by neither.
        ///</summary>
        public int Step { get; init; }

        ///
        ///How the distance is measured, when the rule says.
        ///
        ///Null for a rule that does not, which is measured by sizing through <see cref="DrcChecks"/> - the
        ///square metric, reporting regions, and the route every rule took before there was another one.
        ///Naming a metric moves the rule onto the edge engine, which is what a rule qualified by edge
        ///direction needs.
        ///
        ///Written as a word after the value: `space poly 75 parallel`, `width met1 140 euclidean`.
        ///
        public DrcMetric? Metric { get; init; }
    }

    ///<summary>
    ///A layer computed from others, evaluated left to right.
    ///
    ///**Not an advanced feature, and not optional.** Real rules are not written against drawn layers: a
    ///transistor gate is `poly and diff` and nobody draws it, field poly is `poly not diff`, and P+
    ///diffusion is `diff and psdm`. In sky130 the fourth rule of the second layer already needs one.
    ///</summary>
    public sealed class DrcDerivation
    {
        public required string Name { get; init; }

        ///<summary>The layer the expression starts from.</summary>
        public required string First { get; init; }

        ///<summary>
        ///Each operation and the layer it applies, in order. Left to right with no precedence, because
        ///precedence between set operations is not something anybody reading a deck should have to hold in
        ///their head.
        ///</summary>
        public required IReadOnlyList<DrcStep> Rest { get; init; }

        ///<summary>Every layer this derivation reads, which is what a dependency graph is built from.</summary>
        public IEnumerable<string> Operands
        {
            get
            {
                yield return First;

                foreach (var step in Rest)
                    yield return step.Operand;
            }
        }
    }

    ///<summary>One operation of a derivation, and the layer it is applied to.</summary>
    public readonly record struct DrcStep(BooleanOperation Operation, string Operand);

    ///<summary>
    ///A design rule deck, read from text the user supplies.
    ///
    ///**Why this arrives as a file rather than being built in.** There is no interchange format for design
    ///rules: a foundry supporting three tools ships three separately maintained decks, KLayout's is a Ruby
    ///program and Magic's is entangled with its technology file, and nothing converts between them. So the
    ///format here is this app's own - and the deck stays somebody's file rather than something compiled in,
    ///for the same reason <see cref="LayerNames"/> does. A PDK's tables are its own licensed work, and one
    ///PDK's table does not belong inside a viewer that opens any layout.
    ///
    ///**The format.**
    ///<code>
    ///layer  met1 68/20
    ///derive gate = poly and diff
    ///rule   met1.2 space met1 140 "Met1 spacing"
    ///</code>
    ///Blank lines and `#` comments are skipped. Values are database units, or square ones for an area.
    ///
    ///**Read as far as it parses, like a layermap - but never quietly.** A line that cannot be read is
    ///recorded in <see cref="Problems"/>, and a rule naming a check this build cannot measure is recorded
    ///by name in <see cref="Refused"/>. Nothing is dropped in silence. A caller that reports "no violations"
    ///without consulting <see cref="AllRulesUnderstood"/> is reporting something it does not know.
    ///</summary>
    public class DrcDeck
    {
        #region Constants *******************************************************************

        ///<summary>
        ///How many bad lines are reported before the rest are counted rather than listed. The same reasoning
        ///as <see cref="LayerNames"/>: a file with the wrong delimiter throws one per line, and a thousand of
        ///them say nothing the first few did not.
        ///</summary>
        private const int MaximumReportedProblems = 5;

        ///<summary>The operand of a rule that applies to everything drawn rather than to a named layer.</summary>
        public const string EveryLayer = "*";

        #endregion **************************************************************************



        #region Properties ******************************************************************

        ///<summary>The drawn layers the deck names, by the name it gave them.</summary>
        public Dictionary<string, LayerKey> Layers { get; } = new Dictionary<string, LayerKey>();

        ///<summary>The computed layers, in the order they were written.</summary>
        public List<DrcDerivation> Derivations { get; } = new List<DrcDerivation>();

        ///<summary>The rules, in the order they were written.</summary>
        public List<DrcRule> Rules { get; } = new List<DrcRule>();

        ///<summary>What could not be read. A deck is read as far as it can be.</summary>
        public List<string> Problems { get; } = new List<string>();

        ///<summary>
        ///The rules this build cannot measure, named.
        ///
        ///**Kept apart from <see cref="Problems"/> because it means something different.** A problem is a
        ///line somebody typed wrongly; this is a rule that is written correctly and asks for a check that
        ///does not exist here. The distinction is the whole point - a report that says "clean" while three
        ///rules never ran is worse than no report, so the rules that did not run have to be nameable.
        ///</summary>
        public List<string> Refused { get; } = new List<string>();

        ///<summary>
        ///Whether every rule in the deck was understood - nothing unread and nothing refused.
        ///
        ///What a report checks before it is allowed to use the word "clean".
        ///</summary>
        public bool AllRulesUnderstood
        {
            get { return Problems.Count == 0 && Refused.Count == 0; }
        }

        ///<summary>How many layers, derived layers and rules the deck defines between them.</summary>
        public int Count
        {
            get { return Layers.Count + Derivations.Count + Rules.Count; }
        }

        #endregion **************************************************************************



        #region Reading *********************************************************************

        ///<summary>
        ///Reads a deck, keeping every line that parses.
        ///
        ///Deliberately not all-or-nothing, the same way a layermap is not: a deck with one bad line is still
        ///worth the rules that are good. What is different from a layermap is that the failures are
        ///load-bearing rather than cosmetic, which is why they are separated into two lists and why
        ///<see cref="AllRulesUnderstood"/> exists.
        ///</summary>
        public static DrcDeck Parse(string text)
        {
            var deck = new DrcDeck();

            if (string.IsNullOrWhiteSpace(text))
                return deck;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
                deck.readLine(lines[i], i + 1);

            deck.checkNamesResolve();

            return deck;
        }

        private void readLine(string line, int lineNumber)
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                return;

            var words = tokenize(trimmed);

            if (words.Count == 0)
                return;

            string keyword = words[0].Text.ToLowerInvariant();

            if (keyword == "layer")
            {
                readLayer(words, lineNumber);

                return;
            }

            if (keyword == "derive")
            {
                readDerivation(words, lineNumber);

                return;
            }

            if (keyword == "rule")
            {
                readRule(words, lineNumber);

                return;
            }

            report($"Line {lineNumber} starts with \"{words[0].Text}\", where a deck line is layer, derive or rule.");
        }

        ///<summary>
        ///One word of a line, and whether it arrived in quotes.
        ///
        ///**The quoting is carried rather than discarded**, which is what tells a description from an
        ///operand. Guessing instead - taking anything with a space in it - reads a one-word description as
        ///a layer name, and then the rule is rejected for naming a layer that does not exist. That failure
        ///names the wrong thing entirely, so the reader would go looking at their layer list.
        ///</summary>
        private readonly record struct Word(string Text, bool Quoted);

        ///<summary>
        ///Splits a line into words, keeping a quoted run together.
        ///
        ///Only the description is ever quoted, but taking quotes generally rather than special-casing the
        ///last field means a description holding the word `rule` cannot be mistaken for the start of one.
        ///</summary>
        private static List<Word> tokenize(string line)
        {
            var words = new List<Word>();
            var word = new StringBuilder();

            bool quoted = false;
            bool wasQuoted = false;

            foreach (char character in line)
            {
                if (character == '"')
                {
                    quoted = !quoted;

                    //An empty pair of quotes is still a description, and an empty one is a thing somebody
                    //writes. Remembered here because the builder cannot tell it from a word never started.
                    wasQuoted = true;

                    continue;
                }

                if (!quoted && char.IsWhiteSpace(character))
                {
                    if (word.Length > 0 || wasQuoted)
                        words.Add(new Word(word.ToString(), wasQuoted));

                    word.Clear();
                    wasQuoted = false;

                    continue;
                }

                word.Append(character);
            }

            if (word.Length > 0 || wasQuoted)
                words.Add(new Word(word.ToString(), wasQuoted));

            return words;
        }

        #endregion **************************************************************************



        #region Layers **********************************************************************

        ///<summary>`layer met1 68/20` - a name for a pair the file actually carries.</summary>
        private void readLayer(List<Word> words, int lineNumber)
        {
            if (words.Count != 3)
            {
                report($"Line {lineNumber} has {words.Count} word(s) where a layer is 3: layer <name> <number>/<datatype>.");

                return;
            }

            string name = words[1].Text;

            if (!tryParsePair(words[2].Text, out LayerKey key))
            {
                report($"Line {lineNumber}: \"{words[2].Text}\" is not a layer/datatype pair.");

                return;
            }

            if (isDeclared(name))
            {
                report($"Line {lineNumber} names \"{name}\" a second time.");

                return;
            }

            Layers[name] = key;
        }

        ///<summary>
        ///`68/20` into a pair.
        ///
        ///Invariant, and for the same reason the layermap parses invariantly: these are field numbers out of
        ///a data file rather than prose, and a comma-decimal locale would read them fine while being the
        ///wrong thing to have asked.
        ///</summary>
        private static bool tryParsePair(string field, out LayerKey key)
        {
            key = default;

            string[] halves = field.Split('/');

            if (halves.Length != 2)
                return false;

            if (!short.TryParse(halves[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short number))
                return false;

            if (!short.TryParse(halves[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short dataType))
                return false;

            key = new LayerKey(number, dataType);

            return true;
        }

        #endregion **************************************************************************



        #region Derivations *****************************************************************

        ///<summary>
        ///`derive gate = poly and diff`, left to right.
        ///
        ///The words are `derive`, the name, `=`, the first layer, and then an operation and a layer at a
        ///time - so the operations sit at even positions from four onwards and their operands just after.
        ///</summary>
        private void readDerivation(List<Word> words, int lineNumber)
        {
            if (words.Count < 4)
            {
                report($"Line {lineNumber} is too short for a derivation: derive <name> = <layer> [and|or|not|xor <layer>]...");

                return;
            }

            string name = words[1].Text;

            if (words[2].Text != "=")
            {
                report($"Line {lineNumber} has \"{words[2].Text}\" where a derivation needs = after its name.");

                return;
            }

            if (isDeclared(name))
            {
                report($"Line {lineNumber} names \"{name}\" a second time.");

                return;
            }

            var rest = new List<DrcStep>();

            for (int i = 4; i < words.Count; i += 2)
            {
                if (!tryParseOperation(words[i].Text, out BooleanOperation operation))
                {
                    report($"Line {lineNumber}: \"{words[i].Text}\" is not one of and, or, not, xor.");

                    return;
                }

                //An operation is the last thing on the line when somebody stopped mid-thought. Caught here
                //rather than by counting, so the message can name the operation that was left dangling.
                if (i + 1 >= words.Count)
                {
                    report($"Line {lineNumber} ends with \"{words[i].Text}\" and no layer after it.");

                    return;
                }

                rest.Add(new DrcStep(operation, words[i + 1].Text));
            }

            Derivations.Add(new DrcDerivation
            {
                Name = name,
                First = words[3].Text,
                Rest = rest
            });
        }

        private static bool tryParseOperation(string word, out BooleanOperation operation)
        {
            operation = default;

            switch (word.ToLowerInvariant())
            {
                case "and":
                    operation = BooleanOperation.And;

                    return true;

                case "or":
                    operation = BooleanOperation.Or;

                    return true;

                case "not":
                    operation = BooleanOperation.Not;

                    return true;

                case "xor":
                    operation = BooleanOperation.Xor;

                    return true;
            }

            return false;
        }

        private bool namesDerivation(string name)
        {
            foreach (var derivation in Derivations)
            {
                if (derivation.Name == name)
                    return true;
            }

            return false;
        }

        #endregion **************************************************************************



        #region Rules ***********************************************************************

        ///<summary>`rule met1.2 space met1 140 "Met1 spacing"`.</summary>
        private void readRule(List<Word> words, int lineNumber)
        {
            if (words.Count < 4)
            {
                report($"Line {lineNumber} is too short for a rule: rule <id> <check> <layer>... <value> \"<description>\".");

                return;
            }

            string id = words[1].Text;

            //Refused by name rather than skipped. This is the one failure the whole format exists to make
            //loud: a rule asking for a check this build cannot measure is a rule that will not run, and a
            //report that does not say so is claiming to have looked.
            if (!tryParseCheck(words[2].Text, out DrcCheck check))
            {
                Refused.Add($"{id}: \"{words[2].Text}\" is not a check this build can measure.");

                return;
            }

            var fields = new List<Word>(words.GetRange(3, words.Count - 3));

            //The description is the quoted field, wherever it sits. Taken off first so what is left is
            //operands, a modifier and a value.
            string description = takeDescription(fields);

            if (!tryTakeExcept(fields, id, lineNumber, out string? except))
                return;

            DrcMetric? metric = takeMetric(fields);

            if (!tryTakeNumber(fields, "window", id, lineNumber, out int window))
                return;

            if (!tryTakeNumber(fields, "step", id, lineNumber, out int step))
                return;

            if (!tryTakeValue(fields, id, lineNumber, out long value))
                return;

            //A density rule is measured over a square, and without one there is nothing to measure. Refused
            //rather than defaulted: a window somebody did not choose is a number this made up, and every
            //answer it produced would be about that number.
            if (check == DrcCheck.Density && (window <= 0 || step <= 0))
            {
                report($"Line {lineNumber}: rule {id} is a density and needs window and step, as in: density met1 300 window 100000 step 50000.");

                return;
            }

            if (!operandCountFits(check, fields.Count))
            {
                report($"Line {lineNumber}: rule {id} names {fields.Count} layer(s), where {check} takes {expectedOperands(check)}.");

                return;
            }

            //A metric only means something to the edge engine, and the edge engine does width and one-layer
            //spacing. Refused rather than measured the other way: the point of naming a metric is that the
            //answer differs, so quietly giving the sizing answer is the one outcome nobody asked for.
            if (metric is not null && !Drc.HasEdgeForm(check, fields.Count))
            {
                Refused.Add($"{id}: {check} cannot be measured by a named metric - only width and single-layer spacing can.");

                return;
            }

            Rules.Add(new DrcRule
            {
                Id = id,
                Check = check,
                Operands = fields.Select(field => field.Text).ToList(),
                Value = value,
                Description = description,
                Except = except,
                Window = window,
                Step = step,
                Metric = metric,
                Line = lineNumber
            });
        }

        ///<summary>Pulls the quoted field out, leaving the operands and the value behind.</summary>
        private static string takeDescription(List<Word> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (!fields[i].Quoted)
                    continue;

                string description = fields[i].Text;

                fields.RemoveAt(i);

                return description;
            }

            return "";
        }

        ///<summary>
        ///Pulls the `except` modifier and the layer after it out of the middle of a rule's fields.
        ///
        ///Taken out rather than parsed in place because it can sit anywhere a writer finds readable, and a
        ///modifier that has to come last is a modifier somebody puts second and loses. False when the line
        ///said `except` and named nothing, which is a rule that would otherwise be kept with its exemption
        ///quietly dropped - and an exemption dropped turns a passing layout into a failing one.
        ///</summary>
        private bool tryTakeExcept(List<Word> fields, string id, int lineNumber, out string? except)
        {
            except = null;

            for (int i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Text, "except", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 >= fields.Count)
                {
                    report($"Line {lineNumber}: rule {id} says except with no layer after it.");

                    return false;
                }

                except = fields[i + 1].Text;

                fields.RemoveRange(i, 2);

                return true;
            }

            return true;
        }

        ///<summary>
        ///Pulls the metric out of a rule's fields, if it names one.
        ///
        ///`parallel` rather than `projection` as the word a deck writes, because that is what a rule manual
        ///says: sky130's poly.4 reads "parallel edges only", and a deck should be able to be transcribed in
        ///the words it was written in. Both are accepted.
        ///
        ///Naming no metric is not a fault - it means the rule is measured by sizing, which is what every
        ///rule did before there was a second engine.
        ///</summary>
        private static DrcMetric? takeMetric(List<Word> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                DrcMetric? found = fields[i].Text.ToLowerInvariant() switch
                {
                    "euclidean" => DrcMetric.Euclidean,
                    "euclidian" => DrcMetric.Euclidean,
                    "square" => DrcMetric.Square,
                    "projection" => DrcMetric.Projection,
                    "parallel" => DrcMetric.Projection,
                    _ => null
                };

                if (found is null)
                    continue;

                fields.RemoveAt(i);

                return found;
            }

            return null;
        }

        ///<summary>
        ///Pulls a named number - `window 100000` - out of the middle of a rule's fields.
        ///
        ///The same shape as <see cref="tryTakeExcept"/>, and taken out for the same reason: a modifier that
        ///has to come in a fixed place is a modifier somebody writes in the wrong one. False only when the
        ///name is there and what follows it is not a number, since a missing modifier is not a fault - most
        ///checks have no window at all.
        ///</summary>
        private bool tryTakeNumber(List<Word> fields, string name, string id, int lineNumber, out int value)
        {
            value = 0;

            for (int i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Text, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 >= fields.Count)
                {
                    report($"Line {lineNumber}: rule {id} says {name} with no number after it.");

                    return false;
                }

                if (!int.TryParse(fields[i + 1].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
                {
                    report($"Line {lineNumber}: \"{fields[i + 1].Text}\" is not a {name} for rule {id}.");

                    return false;
                }

                fields.RemoveRange(i, 2);

                return true;
            }

            return true;
        }

        ///<summary>The limit, off the end of what is left once the description and the modifier are gone.</summary>
        private bool tryTakeValue(List<Word> fields, string id, int lineNumber, out long value)
        {
            value = 0;

            if (fields.Count == 0)
            {
                report($"Line {lineNumber}: rule {id} needs a value.");

                return false;
            }

            if (!long.TryParse(fields[^1].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                report($"Line {lineNumber}: \"{fields[^1].Text}\" is not a value for rule {id}.");

                return false;
            }

            //A limit of zero forbids nothing and a negative one is not a distance. Either is a typed number
            //rather than a rule, and running it would report every shape or none of them.
            if (value <= 0)
            {
                report($"Line {lineNumber}: rule {id} has a value of {value}, which measures nothing.");

                return false;
            }

            fields.RemoveAt(fields.Count - 1);

            return true;
        }

        private static bool tryParseCheck(string word, out DrcCheck check)
        {
            return Enum.TryParse(word, ignoreCase: true, out check) && Enum.IsDefined(check);
        }

        private static bool operandCountFits(DrcCheck check, int count)
        {
            //Spacing is the one that takes either: one layer against itself, or one against another.
            if (check == DrcCheck.Space)
                return count == 1 || count == 2;

            if (check == DrcCheck.Enclosure || check == DrcCheck.Antenna)
                return count == 2;

            return count == 1;
        }

        private static string expectedOperands(DrcCheck check)
        {
            if (check == DrcCheck.Space)
                return "1 or 2";

            if (check == DrcCheck.Enclosure || check == DrcCheck.Antenna)
                return "2";

            return "1";
        }

        #endregion **************************************************************************



        #region Resolving *******************************************************************

        ///<summary>
        ///Checks that every layer a derivation or a rule names was declared somewhere.
        ///
        ///**The commonest way a deck is wrong.** A typo in a layer name is not a syntax error - the line
        ///parses perfectly and names something that does not exist - so without this it would reach the
        ///engine and find nothing, which looks exactly like a layer with no violations on it. Run after the
        ///whole file is read rather than per line, so a derivation may name one written below it.
        ///</summary>
        private void checkNamesResolve()
        {
            foreach (var derivation in Derivations)
            {
                foreach (string operand in derivation.Operands)
                {
                    if (!isDeclared(operand))
                        report($"Derivation \"{derivation.Name}\" reads \"{operand}\", which no layer or derivation declares.");
                }
            }

            foreach (var rule in Rules)
            {
                foreach (string operand in rule.Operands)
                {
                    //Every layer at once, which an off-grid rule takes and nothing declares.
                    if (operand == EveryLayer && rule.Check == DrcCheck.OffGrid)
                        continue;

                    if (!isDeclared(operand))
                        report($"Rule {rule.Id} reads \"{operand}\", which no layer or derivation declares.");
                }

                if (rule.Except is string except && !isDeclared(except))
                    report($"Rule {rule.Id} is excepted inside \"{except}\", which no layer or derivation declares.");
            }
        }

        private bool isDeclared(string name)
        {
            return Layers.ContainsKey(name) || namesDerivation(name);
        }

        #endregion **************************************************************************



        #region Problems ********************************************************************

        private void report(string problem)
        {
            if (Problems.Count < MaximumReportedProblems)
                Problems.Add(problem);
        }

        #endregion **************************************************************************
    }
}
