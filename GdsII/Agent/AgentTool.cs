using System.Text.Json.Nodes;

namespace GdsII
{
    ///
    ///The kinds of value a tool takes, which are what its JSON schema is written from.
    ///
    ///**A handful, chosen for how a layout is described.** A point is two numbers rather than an object with an
    ///x and a y, because that is how a model writes one and how a person reads one - `[1.5, 0]` - and a box is
    ///four. Shapes are the numbers this session hands out, never coordinates: a model that has to say *which*
    ///shape by where it is will pick the wrong one of two that overlap.
    ///
    public enum AgentValue
    {
        Number,
        Integer,
        Text,
        Flag,

        ///<summary>[x, y], in microns.</summary>
        Point,

        ///<summary>[[x, y], ...], in microns.</summary>
        Points,

        ///<summary>[left, bottom, right, top], in microns.</summary>
        Box,

        ///<summary>Shape numbers, as <see cref="AgentSession.IdOf(GDS.ElementModel)"/> hands them out.</summary>
        Shapes
    }

    ///
    ///One argument a tool takes: its name, what it is, and what the model is told about it.
    ///
    ///**The schema and the reading both come from this**, so the two cannot disagree. A schema written out by
    ///hand beside code that reads the arguments is two descriptions of one thing, and the day they part the
    ///model is told one shape and refused for sending it.
    ///
    public sealed class AgentParameter
    {
        public required string Name { get; init; }

        public required AgentValue Value { get; init; }

        public required string Description { get; init; }

        public bool Required { get; init; }

        ///<summary>The only values a text argument may take, or null for any.</summary>
        public IReadOnlyList<string>? Choices { get; init; }

        ///<summary>The fewest items a list takes - points along a path, corners of a polygon, shapes to combine.</summary>
        public int Fewest { get; init; }

        ///
        ///This argument as JSON Schema.
        ///
        ///**Only the keywords every provider takes**: type, description, enum, items, minItems, maxItems. Google's
        ///function calling reads a subset of OpenAPI and refuses some of the rest outright - `additionalProperties`,
        ///`oneOf`, `default` - so a schema that uses them works with one model and is a hard error with another.
        ///Anything finer is checked when the call arrives, where the answer can say what was wrong.
        ///
        public JsonObject Schema()
        {
            var schema = new JsonObject();

            if (Value == AgentValue.Number)
            {
                schema["type"] = "number";
            }
            else if (Value == AgentValue.Integer)
            {
                schema["type"] = "integer";
            }
            else if (Value == AgentValue.Text)
            {
                schema["type"] = "string";
            }
            else if (Value == AgentValue.Flag)
            {
                schema["type"] = "boolean";
            }
            else if (Value == AgentValue.Point)
            {
                schema["type"] = "array";
                schema["items"] = new JsonObject { ["type"] = "number" };
                schema["minItems"] = 2;
                schema["maxItems"] = 2;
            }
            else if (Value == AgentValue.Points)
            {
                schema["type"] = "array";
                schema["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "number" },
                    ["minItems"] = 2,
                    ["maxItems"] = 2
                };

                if (Fewest > 0)
                    schema["minItems"] = Fewest;
            }
            else if (Value == AgentValue.Box)
            {
                schema["type"] = "array";
                schema["items"] = new JsonObject { ["type"] = "number" };
                schema["minItems"] = 4;
                schema["maxItems"] = 4;
            }
            else
            {
                schema["type"] = "array";
                schema["items"] = new JsonObject { ["type"] = "integer" };
                schema["minItems"] = Math.Max(1, Fewest);
            }

            schema["description"] = Description;

            if (Choices is not null)
            {
                var choices = new JsonArray();

                foreach (string choice in Choices)
                    choices.Add(choice);

                schema["enum"] = choices;
            }

            return schema;
        }
    }

    ///
    ///One thing an assistant can do to a layout, or ask about it.
    ///
    ///Named, described and given a schema the way every model provider's tool calling wants one, and run
    ///through <see cref="AgentTools.Call(AgentSession, string, string)"/>. The description is written for the
    ///model rather than for a person: it is the only thing the model knows about the tool.
    ///
    public sealed class AgentTool
    {
        private readonly Func<AgentArguments, AgentSession, AgentResult> run;

        internal AgentTool(
            string name,
            string description,
            bool edits,
            IReadOnlyList<AgentParameter> parameters,
            Func<AgentArguments, AgentSession, AgentResult> run)
        {
            Name = name;
            Description = description;
            Edits = edits;
            Parameters = parameters;

            this.run = run;
        }

        ///<summary>What the model calls it: lower case and underscores, which every provider accepts.</summary>
        public string Name { get; }

        public string Description { get; }

        ///<summary>Whether calling it changes the layout - and so is a step on the undo history.</summary>
        public bool Edits { get; }

        public IReadOnlyList<AgentParameter> Parameters { get; }

        ///
        ///The schema of everything it takes, as one JSON object - what Anthropic calls `input_schema`, OpenAI
        ///`parameters` and MCP `inputSchema`.
        ///
        ///A new object each time, so a caller that adds a provider's own keyword to it cannot change what the
        ///next caller is given.
        ///
        public JsonObject InputSchema()
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var parameter in Parameters)
            {
                properties[parameter.Name] = parameter.Schema();

                if (parameter.Required)
                    required.Add(parameter.Name);
            }

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
                schema["required"] = required;

            return schema;
        }

        internal AgentResult Run(AgentArguments arguments, AgentSession session)
        {
            return run(arguments, session);
        }
    }

    ///
    ///What a tool call comes back with: the text the model reads, and what the host needs to know about the
    ///layout afterwards.
    ///
    public sealed class AgentResult
    {
        ///<summary>What goes back to the model - a short summary, or what was wrong.</summary>
        public required string Text { get; init; }

        ///
        ///Whether the call was refused. The text then says why, in terms the model can act on: the layer that
        ///does not exist and the ones that do, the shape number nobody handed out, the corner where a polygon
        ///crosses itself. The layout is untouched.
        ///
        public bool IsError { get; init; }

        ///<summary>The step this call put on the undo history, or null when it added none.</summary>
        public LayoutEdit? Edit { get; init; }

        ///
        ///Whether the layout is different afterwards, and has to be drawn again.
        ///
        ///Not the same as <see cref="Edit"/> being set: taking back the assistant's last change alters the layout
        ///and adds no step, since it is an undo of one.
        ///
        public bool Changed { get; init; }

        ///<summary>The numbers of the shapes this call added, in the order it added them.</summary>
        public IReadOnlyList<int> Made { get; init; } = Array.Empty<int>();
    }

    ///
    ///Something wrong with a call, said the way the model should hear it.
    ///
    ///Thrown anywhere between reading the arguments and building the edit, and turned into an error result
    ///rather than let out: a refused call is an answer the model needs, not a failure of the host.
    ///
    internal sealed class AgentProblem : Exception
    {
        public AgentProblem(string message) : base(message)
        {
        }
    }
}
