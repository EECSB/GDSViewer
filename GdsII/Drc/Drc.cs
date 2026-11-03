namespace GdsII
{
    ///<summary>
    ///One thing wrong with a layout, and enough about it to find it again.
    ///</summary>
    public sealed class DrcViolation
    {
        ///<summary>The rule that was broken, as the deck named it - `met1.2`, `difftap.8`.</summary>
        public required string RuleId { get; init; }

        ///<summary>What the rule says, as the deck worded it. Empty when the deck gave no description.</summary>
        public required string Description { get; init; }

        public required DrcCheck Check { get; init; }

        ///<summary>The limit that was not met, in database units - or square ones for an area.</summary>
        public required long Limit { get; init; }

        ///<summary>
        ///The ground the violation covers, which is what gets drawn over the layout.
        ///
        ///A region for every check but one. An <see cref="DrcCheck.OffGrid"/> violation is a single point,
        ///because a corner in the wrong place has no area to it - anything drawing these has to expect one.
        ///</summary>
        public required List<Element.Point> Marker { get; init; }

        ///<summary>
        ///The box around the marker, worked out once and kept.
        ///
        ///What a list sorts and zooms by. Degenerate for an off-grid point, which is correct rather than a
        ///failure to measure.
        ///</summary>
        public Bounds Bounds
        {
            get
            {
                box ??= Bounds.Of(Marker);

                return box.Value;
            }
        }

        private Bounds? box;

        ///<summary>
        ///Where in the hierarchy this came from, or null when nothing could be attributed.
        ///
        ///**The thing a flat checker is not supposed to be able to tell you.** A violation is found on
        ///flattened geometry, where a shape on screen may be one of a thousand instances of a cell - and
        ///moving it means changing a coordinate in that cell rather than the one being looked at. This is
        ///the way back, and it is only possible because the flattener kept it; see
        ///<see cref="ElementSource"/>.
        ///
        ///Null is normal rather than exceptional. A spacing violation sits in empty ground between two
        ///cells and belongs to neither more than the other, and a marker that no element's own geometry
        ///reaches gets nothing rather than a guess.
        ///</summary>
        public ElementSource? Source { get; init; }

        ///<summary>
        ///What makes this the same fault as one found before: the rule, and exactly the ground it covers.
        ///
        ///**What a waiver is kept by.** A waiver is somebody having looked at a fault and let it stand, and a
        ///check run again tomorrow finds the same rule broken on the same ground - the same fault, still
        ///waived. Moved by a unit, or measured differently because something beside it changed, it is a
        ///different fault and comes back to be looked at, which is the direction for a waiver to fail in: a
        ///fault nobody has seen is never hidden by one somebody saw before.
        ///
        ///The box rather than the outline, which a merge can start at any corner of. Written invariant, since
        ///it is stored and compared as text.
        ///</summary>
        public string Key => FormattableString.Invariant($"{RuleId}@{Bounds.Left},{Bounds.Bottom},{Bounds.Right},{Bounds.Top}");

        public override string ToString()
        {
            return $"{RuleId} at {Bounds}";
        }
    }

    ///<summary>
    ///What a run of a deck over a layout found, and what it did not manage to look at.
    ///
    ///**The second half is the point.** A list of violations on its own invites the reading "nothing here,
    ///so the layout is fine", and that reading is only safe when every rule actually ran. A deck may hold a
    ///check this build cannot measure, a derivation that goes round in a circle, or a line that did not
    ///parse - and each of those is a rule that quietly measured nothing. <see cref="Clean"/> is false while
    ///any of them stands, whatever the violation count says.
    ///</summary>
    public sealed class DrcResult
    {
        public List<DrcViolation> Violations { get; } = new List<DrcViolation>();

        ///<summary>
        ///The rules that did not run, each leading with its own id so a report can name them.
        ///</summary>
        public List<string> NotRun { get; } = new List<string>();

        ///<summary>
        ///Everything the deck and the layer resolution reported - lines that did not parse, and circles of
        ///derivations. Not rule-scoped, unlike <see cref="NotRun"/>.
        ///</summary>
        public List<string> Problems { get; } = new List<string>();

        ///
        ///What of the layout the rules never saw, and why - empty when they saw all of it.
        ///
        ///**The other way a check measures less than it was asked to.** <see cref="NotRun"/> and <see cref="Problems"/>
        ///are about the deck, a rule that could not be measured; this is about the layout. A flatten that stopped at
        ///its ceiling hands over the first part of the file and no more, and a cell that contains itself has its
        ///nesting cut short - either way what is missing was not checked, and where it would have met what is there,
        ///the rest of a wire or the metal over a via, a fault is found that is not in the file. IHP's 1024x16 SRAM
        ///macro, stopped at the app's ceiling with 1,500,532 of its 2,639,671 elements, gave 138 M3.b spacings and 3
        ///V2.c enclosures that way, and nothing said it had been checked in part.
        ///
        public List<string> NotChecked { get; } = new List<string>();

        ///<summary>Whether every rule in the deck was read, understood and measured, over the whole layout.</summary>
        public bool Complete
        {
            get { return NotRun.Count == 0 && Problems.Count == 0 && NotChecked.Count == 0; }
        }

        ///<summary>
        ///Whether the layout may be called clean: nothing found, and nothing skipped on the way.
        ///
        ///**Never true while anything was skipped**, however few violations came back. A report that says
        ///clean over a deck whose rules did not all run is making a claim nobody checked.
        ///</summary>
        public bool Clean
        {
            get { return Violations.Count == 0 && Complete; }
        }

        ///<summary>The violations grouped by the rule that found them, in the order the deck lists them.</summary>
        public List<IGrouping<string, DrcViolation>> ByRule()
        {
            return ByRule(Violations);
        }

        ///
        ///The same for any run of violations - the ones left after an area or the waivers have been taken out,
        ///say - grouped by rule in the order the first of each was found, which is the deck's. One grouping,
        ///since the CLI's report and the assistant's answer each had one of their own beside this.
        ///
        public static List<IGrouping<string, DrcViolation>> ByRule(IEnumerable<DrcViolation> violations)
        {
            return violations.GroupBy(violation => violation.RuleId).ToList();
        }
    }

    ///<summary>
    ///Runs a deck over a layout.
    ///
    ///The fourth phase of the plan in `docs/DRC.md`, and the piece that ties the other three together:
    ///<see cref="DrcDeck"/> reads the rules, <see cref="DrcLayers"/> works out the layers they name, and
    ///<see cref="DrcChecks"/> measures. What is added here is the bookkeeping - which rules ran, what the
    ///violations are called, and how a region on flattened geometry finds its way back to a cell.
    ///</summary>
    public static class Drc
    {
        ///<summary>
        ///Checks a layout against a deck.
        ///
        ///Everything a rule needs is in the deck and the layout, the manufacturing grid included - an
        ///off-grid rule states it rather than reading it back off the file, for the reason given on
        ///<see cref="DrcCheck.OffGrid"/>.
        ///
        ///<paramref name="switchedOff"/> names rules by id that are not to run at all - somebody has put them
        ///aside, the way a layer is hidden. Left out rather than reported as not run: a rule nobody asked for
        ///is not a gap in the answer, where one that was asked for and could not be measured is.
        ///</summary>
        public static DrcResult Check(DrcDeck deck, FlattenedLayout layout, IReadOnlySet<string>? switchedOff = null)
        {
            var result = new DrcResult();

            //What the layout is missing, said before anything is measured over it - see DrcResult.NotChecked.
            result.NotChecked.AddRange(NotCheckedOf(layout));

            foreach (string refused in deck.Refused)
                result.NotRun.Add(refused);

            foreach (string problem in deck.Problems)
                result.Problems.Add(problem);

            var layers = DrcLayers.Resolve(deck, layout);

            foreach (string problem in layers.Problems)
                result.Problems.Add(problem);

            var unmeasurable = new HashSet<string>(layers.RulesLeftUnmeasurable());

            foreach (var rule in deck.Rules)
            {
                if (switchedOff is not null && switchedOff.Contains(rule.Id))
                    continue;

                run(rule, deck, layers, layout, unmeasurable, result);
            }

            return result;
        }

        ///
        ///What a check over this layout cannot see of the file it came from, a sentence each - see
        ///<see cref="DrcResult.NotChecked"/>. Empty for a layout flattened whole.
        ///
        ///**Asked of the flatten, not of a window cut out of it.** A check over an area is handed the elements within
        ///reach of the area, and their count is not where the flatten stopped - said as one, it put the stop at however
        ///many shapes the window held. A caller that checks a window adds this for the layout the window was cut from.
        ///
        public static List<string> NotCheckedOf(FlattenedLayout layout)
        {
            var missing = new List<string>();

            if (layout.Stopped)
            {
                missing.Add(FormattableString.Invariant(
                    $"Only the first {layout.Elements.Count:N0} elements of the layout were checked: flattening it stopped there, so the rest was not, and a fault where the two meet may be nothing but the rest missing."));
            }

            if (layout.DepthLimitReached)
                missing.Add("A cell in this file contains itself, so its nesting was cut short and what lies past the cut was not checked.");

            return missing;
        }

        private static void run(
            DrcRule rule,
            DrcDeck deck,
            DrcLayers layers,
            FlattenedLayout layout,
            HashSet<string> unmeasurable,
            DrcResult result)
        {
            if (unmeasurable.Contains(rule.Id))
            {
                result.NotRun.Add($"{rule.Id}: a layer it reads could not be worked out.");

                return;
            }

            if (rule.Check == DrcCheck.OffGrid)
            {
                offGrid(rule, deck, layout, result);

                return;
            }

            if (rule.Check == DrcCheck.Antenna)
            {
                antenna(rule, layers, layout, result);

                return;
            }

            List<List<Element.Point>> regions;

            if (rule.Metric is DrcMetric named)
                regions = byEdges(rule, layers, named);
            else
                regions = measure(rule, layers);

            if (rule.Except is string except)
                regions = DrcChecks.Outside(regions, layers.RingsOf(except));

            if (regions.Count == 0)
                return;

            var found = candidatesFor(rule, layers, layout);

            //Indexed once per rule rather than per violation. Null for a rule whose layers nothing is drawn
            //on, which is a real case - a deck is written for a process and a cell uses some of it.
            CandidateGrid? candidates = null;

            if (found.Count > 0)
                candidates = CandidateGrid.Of(found);

            foreach (var region in regions)
            {
                result.Violations.Add(new DrcViolation
                {
                    RuleId = rule.Id,
                    Description = rule.Description,
                    Check = rule.Check,
                    Limit = rule.Value,
                    Marker = region,
                    Source = sourceOf(Bounds.Of(region), region, candidates)
                });
            }
        }

        ///<summary>
        ///A rule that named a metric, measured by the edge engine instead of by sizing.
        ///
        ///**Only width and spacing on one layer, which is what the edge engine does.** A rule naming a
        ///metric for a check that has no edge form is refused rather than quietly measured the other way -
        ///the whole point of naming a metric is that the answer differs, so silently giving the sizing
        ///answer would be the one outcome nobody asked for.
        ///
        ///The marker is the ground between the two edges: the part of each that is too close to the other, as
        ///KLayout reports a pair. Two parts alongside each other make a real quadrilateral; two that meet at a
        ///point close to a triangle, and a part that is a single point leaves a line with no area - correct, and
        ///something anything drawing these has to expect.
        ///</summary>
        private static List<List<Element.Point>> byEdges(DrcRule rule, DrcLayers layers, DrcMetric metric)
        {
            var first = layers.RingsOf(rule.Operands[0]);

            var pairs = rule.Check switch
            {
                DrcCheck.Width => DrcEdges.Width(first, rule.Value, metric),
                DrcCheck.Space when rule.Operands.Count == 1 => DrcEdges.Space(first, rule.Value, metric),
                _ => null
            };

            if (pairs is null)
                throw new ArgumentOutOfRangeException(nameof(rule), rule.Check, "No edge form for this check.");

            var markers = new List<List<Element.Point>>();

            foreach (var pair in pairs)
                markers.Add(pair.Marker());

            return markers;
        }

        ///<summary>Whether a check can be measured by the edge engine at all.</summary>
        public static bool HasEdgeForm(DrcCheck check, int operands)
        {
            if (check == DrcCheck.Width)
                return operands == 1;

            if (check == DrcCheck.Space)
                return operands == 1;

            return false;
        }

        ///<summary>
        ///A rule measured by sizing, over its layers as rings - every hole still a hole, which is what keeps a
        ///layer that merges into one outline with thousands of them from taking minutes. See
        ///<see cref="DrcLayers.RingsOf"/>.
        ///</summary>
        private static List<List<Element.Point>> measure(DrcRule rule, DrcLayers layers)
        {
            var first = layers.RingsOf(rule.Operands[0]);

            switch (rule.Check)
            {
                case DrcCheck.Width:
                    return DrcChecks.Width(first, rule.Value);

                case DrcCheck.Space:
                    if (rule.Operands.Count == 1)
                        return DrcChecks.Space(first, rule.Value);

                    return DrcChecks.Space(first, layers.RingsOf(rule.Operands[1]), rule.Value);

                case DrcCheck.Notch:
                    return DrcChecks.Notch(first, rule.Value);

                case DrcCheck.Enclosure:
                    return DrcChecks.Enclosure(first, layers.RingsOf(rule.Operands[1]), rule.Value);

                case DrcCheck.Area:
                    return DrcChecks.Area(first, rule.Value);

                case DrcCheck.HoleArea:
                    return DrcChecks.HoleArea(first, rule.Value);

                case DrcCheck.Density:
                    return DrcChecks.Density(first, rule.Window, rule.Step, rule.Value);
            }

            //Off-grid is handled before this and every other check is above, so reaching here means a check
            //was added to the enum and not to the runner. Empty rather than a throw would be the silent
            //skip this whole design exists to prevent.
            throw new ArgumentOutOfRangeException(nameof(rule), rule.Check, "No measurement for this check.");
        }

        ///<summary>
        ///The off-grid rule, whose violations are points rather than regions.
        ///
        ///Walked here rather than through <see cref="DrcChecks.OffGrid"/>, which answers with points alone.
        ///The element is in hand while walking, so the violation can name the cell it is in for nothing -
        ///and an off-grid corner is the one fault where knowing the cell is most of the fix, since the
        ///coordinate to move is inside it.
        ///
        ///**Only the layers the deck declares**, which is what `*` means here and is the difference between
        ///a useful rule and a noisy one. Run over every element in the file instead, this reported a
        ///signed-off sky130 standard cell for a contact-sized square sitting three nanometers off the grid -
        ///on layer 122/16, which is `pwell.pin`. A pin is an annotation saying where a net may be reached;
        ///nothing on a mask comes from it, and where it sits is nobody's manufacturing problem. The deck
        ///names the layers that are made, so taking the set from the deck is both the narrower answer and
        ///the correct one.
        ///</summary>
        private static void offGrid(DrcRule rule, DrcDeck deck, FlattenedLayout layout, DrcResult result)
        {
            int grid = (int)rule.Value;

            var keys = new HashSet<LayerKey>(deck.GriddedBy(rule));

            foreach (var element in layout.Elements)
            {
                if (!DrcChecks.Checkable(element) || !keys.Contains(element.Layer.Key))
                    continue;

                foreach (var point in element.Points)
                {
                    if (!DrcChecks.IsOffGrid(point, grid))
                        continue;

                    result.Violations.Add(new DrcViolation
                    {
                        RuleId = rule.Id,
                        Description = rule.Description,
                        Check = rule.Check,
                        Limit = rule.Value,
                        Marker = new List<Element.Point> { point },
                        Source = element.Source
                    });
                }
            }
        }

        ///<summary>
        ///The antenna rule, which asks about whole nets rather than about shapes.
        ///
        ///**Refused rather than run when no layer has a role**, and that is the important line in here. A
        ///GDSII file does not say which of its numbers are metal and which are the vias between them, so
        ///without roles nothing is joined to anything: every net is one shape, every ratio is tiny, and the
        ///rule passes a layout it never looked at. Reported as a rule that did not run instead, which is the
        ///same treatment a check this build cannot measure gets and for the same reason.
        ///
        ///One violation per net rather than per shape, because the fault is a property of the net - two
        ///identical wires are fine or fatal depending on what else is attached to them. The marker is the
        ///largest piece of that net's metal, which is the part somebody will recognize.
        ///</summary>
        private static void antenna(DrcRule rule, DrcLayers layers, FlattenedLayout layout, DrcResult result)
        {
            if (!Nets.AnyRolesSet(layout.Elements.Select(element => element.Layer).Distinct()))
            {
                result.NotRun.Add($"{rule.Id}: no layer has a role, so nothing is connected and a net cannot be traced.");

                return;
            }

            var gate = layers.RingsOf(rule.Operands[1]);

            if (gate.Count == 0)
                return;

            var metalKeys = new HashSet<LayerKey>(layers.DrawnBehind(rule.Operands[0]));

            if (metalKeys.Count == 0)
                return;

            foreach (var net in Nets.All(layout))
            {
                var metal = new List<IReadOnlyList<Element.Point>>();
                var everything = new List<IReadOnlyList<Element.Point>>();

                foreach (int index in net)
                {
                    var element = layout.Elements[index];

                    everything.Add(element.Points);

                    if (metalKeys.Contains(element.Layer.Key))
                        metal.Add(element.Points);
                }

                if (metal.Count == 0)
                    continue;

                var merged = Booleans.Merge(metal);

                double metalArea = 0;

                foreach (var piece in merged)
                    metalArea += Measure.AreaOf(piece);

                //The gate this net actually reaches, rather than every gate in the file.
                double gateArea = 0;

                foreach (var piece in Booleans.Combine(gate, Booleans.AsRings(everything), BooleanOperation.And))
                {
                    gateArea += Measure.AreaOf(piece.Boundary);

                    foreach (var hole in piece.Holes)
                        gateArea -= Measure.AreaOf(hole);
                }

                //A net reaching no gate has no oxide to damage. Dividing by nothing would make every
                //dangling wire the worst antenna in the file.
                if (gateArea <= 0 || metalArea <= 0)
                    continue;

                if (metalArea / gateArea <= rule.Value)
                    continue;

                var largest = merged[0];

                foreach (var piece in merged)
                {
                    if (Measure.AreaOf(piece) > Measure.AreaOf(largest))
                        largest = piece;
                }

                result.Violations.Add(new DrcViolation
                {
                    RuleId = rule.Id,
                    Description = rule.Description,
                    Check = rule.Check,
                    Limit = rule.Value,
                    Marker = largest
                });
            }
        }

        #region Finding the way back ********************************************************

        ///<summary>One element of the layout with its extent, worked out once rather than per violation.</summary>
        private readonly record struct Candidate(Element Element, Bounds Bounds);

        ///
        ///The candidates bucketed by where they are, so a marker only looks at what is near it.
        ///
        ///**Because attribution was the whole cost of a large run.** Measured on a generated layout of
        ///320,000 elements, a width check that found nothing took 22.7 seconds and the same check finding
        ///188,742 violations took 478 - and the difference is not the checking, which had already happened.
        ///It was this: every violation scanning every candidate, which at 188,742 by 96,000 is eighteen
        ///billion box comparisons for an answer that is always within a few hundred units of the marker.
        ///
        ///A uniform grid rather than anything cleverer. Layout is spread fairly evenly over its own extent
        ///by nature - that is what a chip is - so the case a quadtree exists to handle is not the case here,
        ///and a grid is an array index instead of a walk.
        ///
        ///A candidate sits in every bucket its extent touches, so a long wire is found from anywhere along
        ///it. That means a query can meet the same one twice, which is what <see cref="seen"/> is for: a
        ///stamp per candidate compared against the query's own number, so nothing is allocated per lookup.
        ///
        private sealed class CandidateGrid
        {
            ///<summary>How many candidates a bucket is aimed at holding. Small enough to cut, large enough not to sprawl.</summary>
            private const int PerBucket = 4;

            ///<summary>A ceiling on the grid's side, so a huge layout cannot ask for a huge array.</summary>
            private const int MostCells = 512;

            private readonly List<Candidate> all;
            private readonly List<int>[] buckets;
            private readonly int[] seen;
            private readonly Bounds extent;
            private readonly int side;

            private int query;

            private CandidateGrid(List<Candidate> all, Bounds extent, int side)
            {
                this.all = all;
                this.extent = extent;
                this.side = side;

                buckets = new List<int>[side * side];
                seen = new int[all.Count];

                for (int i = 0; i < buckets.Length; i++)
                    buckets[i] = new List<int>();

                for (int i = 0; i < all.Count; i++)
                    place(i);
            }

            public static CandidateGrid Of(List<Candidate> candidates)
            {
                var extent = Bounds.Empty;

                foreach (var candidate in candidates)
                    extent = extent.Union(candidate.Bounds);

                int side = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, candidates.Count / (double)PerBucket)));

                return new CandidateGrid(candidates, extent, Math.Clamp(side, 1, MostCells));
            }

            private void place(int index)
            {
                var box = all[index].Bounds;

                columnsFor(box, out int left, out int right, out int bottom, out int top);

                for (int y = bottom; y <= top; y++)
                {
                    for (int x = left; x <= right; x++)
                        buckets[(y * side) + x].Add(index);
                }
            }

            ///<summary>Which buckets a box covers, clamped to the grid.</summary>
            private void columnsFor(Bounds box, out int left, out int right, out int bottom, out int top)
            {
                left = column(box.Left, extent.Left, extent.Width);
                right = column(box.Right, extent.Left, extent.Width);
                bottom = column(box.Bottom, extent.Bottom, extent.Height);
                top = column(box.Top, extent.Bottom, extent.Height);
            }

            private int column(long at, long from, long across)
            {
                if (across <= 0)
                    return 0;

                //Long arithmetic before the divide: a coordinate times the side is past a million times the
                //coordinate, and a die measured in nanometers is already large.
                long index = (at - from) * side / across;

                return (int)Math.Clamp(index, 0, side - 1);
            }

            ///<summary>Everything whose extent could reach the box, each offered once.</summary>
            public IEnumerable<Candidate> Near(Bounds box)
            {
                query++;

                columnsFor(box, out int left, out int right, out int bottom, out int top);

                for (int y = bottom; y <= top; y++)
                {
                    for (int x = left; x <= right; x++)
                    {
                        foreach (int index in buckets[(y * side) + x])
                        {
                            if (seen[index] == query)
                                continue;

                            seen[index] = query;

                            if (all[index].Bounds.Intersects(box))
                                yield return all[index];
                        }
                    }
                }
            }
        }

        ///<summary>
        ///The elements a violation of this rule could have come from: those on the drawn layers underneath
        ///whatever the rule named, with a source to offer.
        ///
        ///Gathered once per rule. Done per violation it would be a walk of the layout for each marker, and
        ///a rule that finds a thousand of them would walk it a thousand times.
        ///</summary>
        private static List<Candidate> candidatesFor(DrcRule rule, DrcLayers layers, FlattenedLayout layout)
        {
            var keys = new HashSet<LayerKey>();

            foreach (string operand in rule.Operands)
            {
                foreach (var key in layers.DrawnBehind(operand))
                    keys.Add(key);
            }

            var candidates = new List<Candidate>();

            if (keys.Count == 0)
                return candidates;

            foreach (var element in layout.Elements)
            {
                if (element.Source is null || element.Text is not null)
                    continue;

                if (!keys.Contains(element.Layer.Key))
                    continue;

                candidates.Add(new Candidate(element, Bounds.Of(element.Points)));
            }

            return candidates;
        }

        ///<summary>
        ///Which element a marker came from, or null when none of them can be said to own it.
        ///
        ///**Two stages, and the second is what makes it worth trusting.** The box test rejects nearly
        ///everything for nothing, and on its own would name an element whose extent covers the marker while
        ///its geometry is nowhere near it - an L-shaped cell reported for a violation in the corner it does
        ///not occupy. So the survivors are then actually intersected, and the first whose own geometry
        ///reaches the marker is the answer.
        ///
        ///Grown by a unit before either test, because a spacing violation lies in the empty ground *between*
        ///two shapes and touches neither. The same unit a traced net uses to decide two shapes abut, for the
        ///same reason: coordinates are whole, so anything that close was meant to be touching.
        ///
        ///Null when nothing reaches it, which is honest. A marker in open ground between two cells belongs
        ///to neither more than the other, and a report is better off saying so than naming whichever came
        ///first in the list.
        ///</summary>
        private static ElementSource? sourceOf(Bounds marker, List<Element.Point> region, CandidateGrid? candidates)
        {
            if (candidates is null)
                return null;

            var near = candidates.Near(marker.Grown(1)).ToList();

            if (near.Count == 0)
                return null;

            //The clipping pass is left until something is actually within reach, which on a marker in open
            //ground is never - and a spacing rule finds a great many of those.
            var grown = Booleans.Grow(new[] { region }, 1);

            foreach (var candidate in near)
            {
                if (Booleans.Combine(grown, new[] { candidate.Element.Points }, BooleanOperation.And).Count > 0)
                    return candidate.Element.Source;
            }

            return null;
        }

        #endregion **************************************************************************
    }
}
