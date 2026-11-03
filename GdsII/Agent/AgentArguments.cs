using System.Globalization;
using System.Text.Json;

namespace GdsII
{
    ///
    ///The arguments of one tool call, read against what the tool says it takes.
    ///
    ///**Strict about names, forgiving about spelling.** An argument the tool does not take is refused, with
    ///the list of the ones it does - a model that wrote `diameter` where the tool takes `radius` would
    ///otherwise get a circle of the default size and never learn why. But a number that arrives as the string
    ///"1.5", or a shape number written "#12", is what was meant, and refusing it would be refusing the call
    ///over punctuation.
    ///
    internal sealed class AgentArguments
    {
        private readonly AgentTool tool;
        private readonly Dictionary<string, JsonElement> given = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        internal AgentArguments(AgentTool tool, JsonElement arguments)
        {
            this.tool = tool;

            //No arguments at all is how a provider sends a call to a tool that takes none.
            bool none = arguments.ValueKind == JsonValueKind.Undefined || arguments.ValueKind == JsonValueKind.Null;

            if (!none && arguments.ValueKind != JsonValueKind.Object)
                throw new AgentProblem($"The arguments to {tool.Name} have to be a JSON object, like {{\"name\": value}}.");

            if (!none)
            {
                foreach (var property in arguments.EnumerateObject())
                {
                    if (parameter(property.Name) is null)
                        throw new AgentProblem($"{tool.Name} does not take \"{property.Name}\". {accepted()}");

                    //A null is an argument left out, which is how some models write "use the default".
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        continue;

                    given[property.Name] = property.Value;
                }
            }

            foreach (var expected in tool.Parameters)
            {
                if (expected.Required && !given.ContainsKey(expected.Name))
                    throw new AgentProblem($"{tool.Name} needs \"{expected.Name}\": {expected.Description}");
            }
        }

        ///<summary>Whether the call gave this argument at all.</summary>
        public bool Has(string name)
        {
            return given.ContainsKey(name);
        }

        public double Number(string name)
        {
            return OptionalNumber(name) ?? throw missing(name);
        }

        public double? OptionalNumber(string name)
        {
            if (!given.TryGetValue(name, out var value))
                return null;

            if (numberIn(value) is double number)
                return number;

            throw new AgentProblem($"\"{name}\" has to be a number, and {describe(value)} is not one.");
        }

        public int Integer(string name)
        {
            return OptionalInteger(name) ?? throw missing(name);
        }

        public int? OptionalInteger(string name)
        {
            if (!given.TryGetValue(name, out var value))
                return null;

            if (wholeIn(value) is int whole)
                return whole;

            throw new AgentProblem($"\"{name}\" has to be a whole number, and {describe(value)} is not one.");
        }

        public string Text(string name)
        {
            return OptionalText(name) ?? throw missing(name);
        }

        ///<summary>The text given, checked against the tool's own list of choices when it has one.</summary>
        public string? OptionalText(string name)
        {
            if (!given.TryGetValue(name, out var value))
                return null;

            string text;

            if (value.ValueKind == JsonValueKind.String)
                text = value.GetString() ?? "";
            else if (value.ValueKind == JsonValueKind.Number)
                text = value.GetRawText();
            else
                throw new AgentProblem($"\"{name}\" has to be text, and {describe(value)} is not.");

            if (parameter(name)?.Choices is IReadOnlyList<string> choices)
            {
                foreach (string choice in choices)
                {
                    if (string.Equals(choice, text.Trim(), StringComparison.OrdinalIgnoreCase))
                        return choice;
                }

                throw new AgentProblem($"\"{name}\" has to be one of {string.Join(", ", choices)}, and \"{text}\" is not.");
            }

            return text;
        }

        public bool Flag(string name, bool otherwise)
        {
            if (!given.TryGetValue(name, out var value))
                return otherwise;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool said))
                return said;

            throw new AgentProblem($"\"{name}\" has to be true or false, and {describe(value)} is neither.");
        }

        public (double X, double Y) Point(string name)
        {
            return OptionalPoint(name) ?? throw missing(name);
        }

        public (double X, double Y)? OptionalPoint(string name)
        {
            if (!given.TryGetValue(name, out var value))
                return null;

            if (pointIn(value) is (double X, double Y) point)
                return point;

            throw new AgentProblem($"\"{name}\" has to be a point, [x, y] in microns, and {describe(value)} is not one.");
        }

        public List<(double X, double Y)> Points(string name)
        {
            if (!given.TryGetValue(name, out var value))
                throw missing(name);

            if (value.ValueKind != JsonValueKind.Array)
                throw new AgentProblem($"\"{name}\" has to be a list of points, [[x, y], [x, y], ...] in microns.");

            var points = new List<(double X, double Y)>();
            int at = 0;

            foreach (var item in value.EnumerateArray())
            {
                if (pointIn(item) is not (double X, double Y) point)
                    throw new AgentProblem($"Item {at} of \"{name}\" has to be a point, [x, y] in microns, and {describe(item)} is not one.");

                points.Add(point);
                at++;
            }

            int fewest = parameter(name)?.Fewest ?? 0;

            if (points.Count < fewest)
                throw new AgentProblem($"\"{name}\" needs at least {fewest} points, and has {points.Count}.");

            return points;
        }

        ///<summary>[left, bottom, right, top], put the right way round whichever way it was written.</summary>
        public (double Left, double Bottom, double Right, double Top)? OptionalBox(string name)
        {
            if (!given.TryGetValue(name, out var value))
                return null;

            var numbers = new List<double>();

            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 4)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (numberIn(item) is double number)
                        numbers.Add(number);
                }
            }

            if (numbers.Count != 4)
                throw new AgentProblem($"\"{name}\" has to be a box, [left, bottom, right, top] in microns, and {describe(value)} is not one.");

            return (
                Math.Min(numbers[0], numbers[2]),
                Math.Min(numbers[1], numbers[3]),
                Math.Max(numbers[0], numbers[2]),
                Math.Max(numbers[1], numbers[3]));
        }

        ///<summary>Shape numbers, each once, in the order they were given.</summary>
        public List<int> Shapes(string name)
        {
            if (!given.TryGetValue(name, out var value))
                throw missing(name);

            var numbers = new List<int>();

            //One number on its own is a list of one, which is what a model asking about one shape writes.
            if (value.ValueKind != JsonValueKind.Array)
            {
                if (shapeIn(value) is not int one)
                    throw new AgentProblem($"\"{name}\" has to be a list of shape numbers, like [3, 7], and {describe(value)} is not.");

                numbers.Add(one);

                return numbers;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (shapeIn(item) is not int number)
                    throw new AgentProblem($"\"{name}\" has to be a list of shape numbers, like [3, 7], and {describe(item)} is not one.");

                if (!numbers.Contains(number))
                    numbers.Add(number);
            }

            int fewest = Math.Max(1, parameter(name)?.Fewest ?? 1);

            if (numbers.Count < fewest)
                throw new AgentProblem($"\"{name}\" needs at least {fewest} shapes, and has {numbers.Count}.");

            return numbers;
        }

        ///
        ///Numbers by name - a structure's, whose names are the structure's to say: "radius=700, ring=11", or the same as a
        ///JSON object, which is what a model may send where the schema says text. Empty when the argument was left out.
        ///
        ///**A unit after a number is taken as said**: "0.5 µm", "60°" and "60 deg" are the numbers they look like, since
        ///each name's unit is fixed and a model that writes it out is being careful, not asking for another.
        ///
        public Dictionary<string, double> Pairs(string name)
        {
            var pairs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (!given.TryGetValue(name, out var value))
                return pairs;

            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (numberIn(property.Value) is not double number)
                        throw new AgentProblem($"\"{property.Name}\" in \"{name}\" has to be a number, and {describe(property.Value)} is not one.");

                    pairs[property.Name.Trim()] = number;
                }

                return pairs;
            }

            if (value.ValueKind != JsonValueKind.String)
                throw new AgentProblem($"\"{name}\" has to be name=value pairs, like \"radius=700, ring=11\", and {describe(value)} is not.");

            foreach (string pair in (value.GetString() ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int split = pair.IndexOfAny(new[] { '=', ':' });
                string written = "";

                if (split > 0)
                    written = unitless(pair[(split + 1)..]);

                if (split <= 0
                    || !double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    || !double.IsFinite(number))
                    throw new AgentProblem($"\"{pair}\" in \"{name}\" is not a name and a number, like radius=700.");

                pairs[pair[..split].Trim()] = number;
            }

            return pairs;
        }

        ///<summary>A number as written with its unit after it, without the unit.</summary>
        private static string unitless(string written)
        {
            string number = written.Trim();

            foreach (string unit in new[] { "µm", "μm", "um", "°", "degrees", "deg" })
            {
                if (number.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                    return number[..^unit.Length].Trim();
            }

            return number;
        }

        private AgentParameter? parameter(string name)
        {
            foreach (var each in tool.Parameters)
            {
                if (each.Name == name)
                    return each;
            }

            return null;
        }

        private string accepted()
        {
            if (tool.Parameters.Count == 0)
                return "It takes no arguments.";

            return $"It takes {string.Join(", ", tool.Parameters.Select(each => each.Name))}.";
        }

        private AgentProblem missing(string name)
        {
            string about = parameter(name)?.Description ?? "";

            return new AgentProblem($"{tool.Name} needs \"{name}\". {about}".TrimEnd());
        }

        private static double? numberIn(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number))
                return number;

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && double.IsFinite(parsed))
                return parsed;

            return null;
        }

        private static int? wholeIn(JsonElement value)
        {
            if (numberIn(value) is not double number)
                return null;

            if (number != Math.Floor(number) || number < int.MinValue || number > int.MaxValue)
                return null;

            return (int)number;
        }

        ///<summary>A shape number, written as a number or as "#12" - the way every answer here writes one.</summary>
        private static int? shapeIn(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string text = (value.GetString() ?? "").Trim().TrimStart('#');

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    return parsed;

                return null;
            }

            return wholeIn(value);
        }

        ///<summary>[x, y], or an object with an x and a y - the one other way a model is likely to write a point.</summary>
        private static (double X, double Y)? pointIn(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2)
            {
                if (numberIn(value[0]) is double x && numberIn(value[1]) is double y)
                    return (x, y);

                return null;
            }

            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("x", out var across)
                && value.TryGetProperty("y", out var up))
            {
                if (numberIn(across) is double x && numberIn(up) is double y)
                    return (x, y);
            }

            return null;
        }

        ///<summary>What was sent, short enough to quote back.</summary>
        private static string describe(JsonElement value)
        {
            string raw = value.GetRawText();

            if (raw.Length > 60)
                raw = raw.Substring(0, 57) + "...";

            return raw;
        }
    }
}
