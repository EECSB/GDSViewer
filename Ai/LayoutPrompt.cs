using System.Text;
using GdsII;

namespace GDSViewer.Ai
{
    ///
    ///What the model is told about the layout at the start of each message: what the app knows that no tool call
    ///would think to ask for.
    ///
    ///Gathered by the page, which is the only thing that knows what the person is looking at, and handed over as plain
    ///values so the prompt can be built - and tested - without one.
    ///
    public sealed class LayoutFacts
    {
        public string FileName { get; init; } = "";

        public double MicronsPerUnit { get; init; } = 1;

        ///<summary>The layers, as the layer list names them.</summary>
        public IReadOnlyList<string> Layers { get; init; } = Array.Empty<string>();

        ///<summary>The layers the person has locked: drawn, and not to be touched.</summary>
        public IReadOnlyList<string> Locked { get; init; } = Array.Empty<string>();

        public int CellCount { get; init; }

        public IReadOnlyList<string> TopCells { get; init; } = Array.Empty<string>();

        ///<summary>The cell the tools work in, or null when none can be told - no cell chosen and several tops.</summary>
        public string? WorkingCell { get; init; }

        ///<summary>The cell the person has opened for editing in the 2D view, or null when they are looking at the whole layout.</summary>
        public string? Editing { get; init; }

        ///<summary>The shapes picked out in the 2D view, by the numbers the tools use.</summary>
        public IReadOnlyList<int> Selected { get; init; } = Array.Empty<int>();

        ///<summary>The cell those shapes are in, when they are all in one.</summary>
        public string? SelectedIn { get; init; }

        ///<summary>What the 2D view shows, in microns, in the working cell's coordinates - or null when it is not on screen.</summary>
        public (double Left, double Bottom, double Right, double Top)? Shown { get; init; }

        ///<summary>Which view is open: 2D, 3D or text.</summary>
        public string View { get; init; } = "2D";

        public bool DeckLoaded { get; init; }

        ///<summary>Whether the look tool is on offer.</summary>
        public bool CanLook { get; init; }

        ///<summary>The PDK whose design rules read_rules and add_rules work on, or null when they are not on offer - see RuleTools.</summary>
        public string? RulesPdk { get; init; }

        ///<summary>Whether the app's example layouts are on offer to list_examples and read_example - see ExampleTools.</summary>
        public bool HasExamples { get; init; }

        ///<summary>The person's PDKs, by name, which use_pdk chooses from - see PdkTools. Empty where there are none to choose.</summary>
        public IReadOnlyList<string> PdkNames { get; init; } = Array.Empty<string>();

        ///<summary>The PDK in use, or null where there is none to say.</summary>
        public string? PdkInUse { get; init; }

        ///<summary>Whether the layout has no layers yet and waits for a PDK's - the empty layer list's Set PDK.</summary>
        public bool WaitsForPdk { get; init; }
    }

    ///
    ///The system prompt: who the model is talking to, the rules it keeps, the reach the approval mode gives it, and
    ///the layout as it stands.
    ///
    ///**Built again for every message**, since the cell, the selection and the view are whatever they are when the
    ///person presses Send. The tools' own conventions - units, shape numbers, undo - come from
    ///<see cref="AgentTools.Conventions"/>, which is written for exactly this.
    ///
    public static class LayoutPrompt
    {
        ///<summary>The most layers listed in the prompt; list_layers has the rest.</summary>
        public const int MostLayers = 40;

        public static string Build(LayoutFacts facts, ToolApprovalMode mode)
        {
            return Build(facts, mode, new HashSet<string>());
        }

        ///
        ///<param name="off">
        ///The tools switched off in the window's tool picker, which the model is told about by name - so a request that
        ///needs one is answered with which, rather than with the "I cannot make changes" GraphDBViewer saw from a model
        ///whose changing tools had been taken away without a word. See AiChat.ToolsOff.
        ///</param>
        ///
        public static string Build(LayoutFacts facts, ToolApprovalMode mode, IReadOnlySet<string> off)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("You are the assistant in GDS Viewer, a GDSII layout viewer and editor, talking with someone in a chat window beside their layout. You read and change the layout by calling tools.");
            prompt.AppendLine();
            prompt.AppendLine("Rules:");
            prompt.AppendLine("- Answer briefly and plainly. When a request could mean two different changes, ask which rather than guessing.");
            prompt.AppendLine("- Do what is asked with the tools rather than telling the person how they could do it. One call that makes the whole change is better than several small ones.");
            prompt.AppendLine("- Never guess where an existing shape is: find_shapes, measure and the other reading tools say.");
            prompt.AppendLine("- " + AgentTools.Conventions);
            prompt.AppendLine("- The person's 2D view draws y downward, so what they call the top of the screen is the smallest y and what they call above is a smaller y.");

            if (facts.CanLook && !off.Contains(LayoutToolRunner.LookName))
                prompt.AppendLine("- look draws a picture of the cell the same way the 2D view does. Use it to see how things are arranged; take numbers from the reading tools.");

            if (facts.RulesPdk is string pdk && !off.Contains(RuleTools.ReadName))
                prompt.AppendLine($"- Design rules are written into the PDK {pdk}: read_rules gives its deck, its layers and the deck's grammar, and add_rules adds lines to it. When the person describes rules - from words, a manual or a file - write them there rather than drawing anything.");

            if (facts.RulesPdk is string layersPdk && !off.Contains(LayerTools.ReadName))
                prompt.AppendLine($"- The PDK {layersPdk}'s layers are its layermap: read_layers gives it and its format, and set_layers adds, changes or takes out layers. When the person describes a PDK's layers - their numbers, names, colors, stack or roles - write them there.");

            if (facts.HasExamples && !off.Contains(ExampleTools.ReadName))
                prompt.AppendLine("- GDS Viewer ships about nine hundred sky130 example layouts, over two hundred gf180mcu ones and eighty-four ihp-sg13g2 ones - each PDK's standard cells and devices, and a few whole designs - which list_examples names with the PDK each is drawn in, and read_example reads beside this layout without changing it. Before drawing a cell, device or structure the layout's PDK has one of, read how an example drawn in that PDK draws it and follow its layers, sizes and spacing - never another PDK's.");

            //The Draw menu's Generator as a tool (2026-11-01, asked for): what it makes from a few numbers, put down whole.
            if (!off.Contains(AgentTools.PlaceStructureName))
                prompt.AppendLine("- An inductor, a capacitor, a resistor, a guard ring, a coupler, a filter, a resonator, an antenna, an alignment or test structure, pads, fill and the rest list_structures names are put down whole by place_structure, from their numbers, as the app's Generator makes them. Use it rather than drawing one of these shape by shape; a structure on more than one layer is its kinds put down at one point, each on its own layer.");

            //Told which PDKs there are and that a new layout waits for one (2026-11-01): it was told only that a layer comes
            //into being when drawn on, and drew a MOSFET "in sky130" on numbers it remembered, one gray layer at a time.
            if (facts.PdkInUse is string inUse && facts.PdkNames.Count > 0 && !off.Contains(PdkTools.UseName))
            {
                prompt.AppendLine($"- The person's PDKs are {string.Join(", ", facts.PdkNames)}, and {inUse} is in use. A PDK names a layout's layers and brings its design rules; use_pdk puts one in use and lays on whichever of its layers the layout lacks.");

                if (facts.WaitsForPdk)
                    prompt.AppendLine("- No PDK has laid its layers on this layout yet. When the person names a PDK, or asks for something drawn in one, call use_pdk before drawing anything: its layers come in named, colored and stacked, and you draw on them by name. When they ask for a device or a cell without naming a PDK, ask which to use rather than drawing on layer numbers from memory; numbers they give themselves are drawn on as given.");
            }

            appendMode(prompt, mode, readsOnly(off));

            if (off.Count > 0)
                prompt.AppendLine($"- The person has switched these tools off in this conversation: {string.Join(", ", off.Order(StringComparer.Ordinal))}. When a request needs one, say which, and that it can be switched back on in the window's tool picker.");

            appendLayout(prompt, facts);

            return prompt.ToString().TrimEnd();
        }

        ///<summary>Whether every tool that changes the layout is switched off - which no approval rung has anything to say about.</summary>
        private static bool readsOnly(IReadOnlySet<string> off)
        {
            foreach (var tool in AgentTools.All)
            {
                if (tool.Edits && !off.Contains(tool.Name))
                    return false;
            }

            return true;
        }

        ///
        ///The layout as it stands, the way the prompt says it - which is also how a model on the live link is told it,
        ///when it asks with describe_tab: it has no message of the person's to ride on.
        ///
        public static string Describe(LayoutFacts facts)
        {
            var said = new StringBuilder();

            appendLayout(said, facts);

            return said.ToString().Trim();
        }

        ///
        ///What the model is told about its own reach, which is not fixed: the same window runs it on a leash or off
        ///one. A model that thinks every call will be vetted writes as though nothing it asks for will land, and one
        ///that thinks nothing is watched writes as though everything is free - so the mode in force is stated.
        ///
        private static void appendMode(StringBuilder prompt, ToolApprovalMode mode, bool readsOnly)
        {
            if (mode == ToolApprovalMode.Ask)
            {
                prompt.AppendLine("- Every call you make is shown to the person and runs only once they approve it, reading ones included. Say in a few words what you are about to do before you call, and expect that they may decline.");
            }
            else if (mode == ToolApprovalMode.AutoRead)
            {
                prompt.AppendLine("- Calls that only read the layout run at once, and the person sees each one. A change is shown to them and made only once they approve it.");
            }
            else
            {
                prompt.AppendLine("- Every call runs at once, changes included, and the person sees each one.");
            }

            //Every changing tool switched off is the person's own choice of a conversation that only reads, so it is said
            //as that rather than as the reach the rung gives.
            if (readsOnly)
                prompt.AppendLine("- The person has switched off every tool that changes the layout, so this conversation only reads it: answer, and say what a change would be rather than making it.");
            else if (ApprovingToolRunner.NeedsApproval(mode, true))
                prompt.AppendLine("- Calling a tool that changes the layout is how you ask to make the change, so when the person wants something changed, call it. Never answer that you cannot make changes, and never tell them to make it themselves.");
            else
                prompt.AppendLine("- After changing something, say plainly what you changed.");
        }

        private static void appendLayout(StringBuilder prompt, LayoutFacts facts)
        {
            prompt.AppendLine();
            prompt.AppendLine("The layout:");

            if (facts.FileName.Length > 0)
                prompt.AppendLine($"- The file is {facts.FileName}.");

            prompt.AppendLine($"- One database unit is {LayoutPicture.Microns(facts.MicronsPerUnit)} µm.");

            if (facts.TopCells.Count > 0)
                prompt.AppendLine($"- It has {facts.CellCount} {plural(facts.CellCount, "cell")}; the top {plural(facts.TopCells.Count, "cell is", "cells are")} {string.Join(", ", facts.TopCells)}.");

            appendLayers(prompt, facts);

            if (facts.WorkingCell is string working)
                prompt.AppendLine($"- The tools work in {working}.");
            else
                prompt.AppendLine("- No cell is chosen for the tools, and there are several top cells: call open_cell before drawing anything.");

            if (facts.Editing is string open)
                prompt.AppendLine($"- The person has {open} open for editing in the 2D view.");
            else if (facts.View == "2D")
                prompt.AppendLine("- The person is looking at the whole layout in the 2D view.");
            else
                prompt.AppendLine($"- The person has the {facts.View} view open.");

            if (facts.Selected.Count == 0)
            {
                prompt.AppendLine("- Nothing is selected.");
            }
            else
            {
                string shapes = string.Join(", ", facts.Selected.Select(id => "#" + id));
                string where = "";

                if (facts.SelectedIn is string cell)
                    where = $" in {cell}";

                prompt.AppendLine($"- Selected in the 2D view{where}: {shapes}. \"This\", \"these\" and \"the selection\" mean them.");
            }

            if (facts.Shown is (double left, double bottom, double right, double top))
                prompt.AppendLine($"- The 2D view shows x {LayoutPicture.Microns(left)} to {LayoutPicture.Microns(right)} µm and y {LayoutPicture.Microns(bottom)} to {LayoutPicture.Microns(top)} µm. \"Here\" and \"on screen\" mean that area.");

            if (facts.DeckLoaded)
                prompt.AppendLine("- A rule deck is loaded, so each change is checked against the rules around it.");
        }

        private static void appendLayers(StringBuilder prompt, LayoutFacts facts)
        {
            if (facts.Layers.Count == 0)
            {
                prompt.AppendLine("- It has no layers yet. A layer comes into being when something is drawn on it, by name from the PDK or by number like 68/20.");

                return;
            }

            var named = facts.Layers.Take(MostLayers);
            string more = "";

            if (facts.Layers.Count > MostLayers)
                more = $", and {facts.Layers.Count - MostLayers} more that list_layers names";

            prompt.AppendLine($"- Its layers: {string.Join(", ", named)}{more}.");

            if (facts.Locked.Count > 0)
                prompt.AppendLine($"- The person has locked {string.Join(", ", facts.Locked)}: leave the shapes on those layers alone unless they ask.");
        }

        private static string plural(int count, string one)
        {
            return plural(count, one, one + "s");
        }

        private static string plural(int count, string one, string many)
        {
            if (count == 1)
                return one;

            return many;
        }
    }
}
