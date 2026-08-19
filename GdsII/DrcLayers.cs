namespace GdsII
{
    ///<summary>
    ///The layers a deck talks about, worked out against a layout: the drawn ones merged, and the derived
    ///ones computed from those.
    ///
    ///**This is the half of a design rule check that is not a measurement.** Real rules are not written
    ///against drawn layers - a transistor gate is `poly and diff` and nobody draws it, field poly is
    ///`poly not diff`, P+ diffusion is `diff and psdm` - so before anything can be measured, the layers the
    ///rules name have to exist. In sky130 the fourth rule of the second layer already needs one, which is
    ///why this is not an advanced feature to be added later.
    ///
    ///**A derivation may read another derivation**, so the order they are computed in is not the order they
    ///were written in. They are sorted by what they depend on, and a deck whose derivations depend on each
    ///other in a circle is reported rather than followed round.
    ///
    ///**Only what a rule actually reaches is computed.** The structure of every derivation is checked -
    ///cycles and missing names cost nothing to find and are worth telling the user about whether or not a
    ///rule uses them - but the geometry, which is a clipping pass over the whole layout, is worked out only
    ///for the layers some rule leads to. A deck carrying derivations for rules it does not yet have is a
    ///normal thing to write, and it should not cost a boolean over half a million shapes each.
    ///</summary>
    public sealed class DrcLayers
    {
        #region Building ********************************************************************

        private DrcLayers(DrcDeck deck, FlattenedLayout layout)
        {
            this.deck = deck;
            this.layout = layout;
        }

        private readonly DrcDeck deck;
        private readonly FlattenedLayout layout;

        private readonly Dictionary<string, List<List<Element.Point>>> geometry = new Dictionary<string, List<List<Element.Point>>>();
        private readonly HashSet<string> unresolved = new HashSet<string>();

        ///<summary>
        ///Works out every layer the deck's rules reach, against the geometry the layout holds.
        ///</summary>
        public static DrcLayers Resolve(DrcDeck deck, FlattenedLayout layout)
        {
            var layers = new DrcLayers(deck, layout);

            layers.run();

            return layers;
        }

        private void run()
        {
            var derivations = new Dictionary<string, DrcDerivation>();

            foreach (var derivation in deck.Derivations)
            {
                //A name declared twice never reaches here - the deck refuses the second - so the first wins
                //by already being present rather than by being chosen.
                if (!derivations.ContainsKey(derivation.Name))
                    derivations.Add(derivation.Name, derivation);
            }

            //Structure first, over all of them: a cycle is worth reporting whether or not a rule leads to it,
            //and finding one costs nothing but a walk of the names.
            var ordered = sortByDependency(derivations);

            var wanted = whatTheRulesReach(derivations);

            if (wanted.Count == 0)
                return;

            gatherDrawn(wanted);

            foreach (var derivation in ordered)
            {
                if (wanted.Contains(derivation.Name))
                    derive(derivation);
            }
        }

        #endregion **************************************************************************



        #region What is asked for ***********************************************************

        ///<summary>
        ///Every layer some rule leads to, following derivations down to the drawn layers they read.
        ///
        ///A rule's operands and the layer it is excepted inside, and then whatever those are made of. What
        ///is left out is a derivation nothing uses, which is the case this exists to avoid paying for.
        ///</summary>
        private HashSet<string> whatTheRulesReach(Dictionary<string, DrcDerivation> derivations)
        {
            var wanted = new HashSet<string>();

            foreach (var rule in deck.Rules)
            {
                foreach (string operand in rule.Operands)
                    reach(operand, derivations, wanted);

                if (rule.Except is string except)
                    reach(except, derivations, wanted);
            }

            return wanted;
        }

        private void reach(string name, Dictionary<string, DrcDerivation> derivations, HashSet<string> wanted)
        {
            //Every layer at once, which an off-grid rule names and no layer declares.
            if (name == DrcDeck.EveryLayer)
                return;

            //Already been here. Also what stops a cycle from walking forever - the cycle itself is reported
            //by the sort rather than here.
            if (!wanted.Add(name))
                return;

            if (!derivations.TryGetValue(name, out var derivation))
                return;

            foreach (string operand in derivation.Operands)
                reach(operand, derivations, wanted);
        }

        #endregion **************************************************************************



        #region Order ***********************************************************************

        private enum Mark
        {
            None,
            Visiting,
            Done
        }

        ///<summary>
        ///The derivations in an order where nothing is computed before what it reads.
        ///
        ///Depth-first, marking each name as it is entered and again as it is finished: meeting a name that
        ///is still being visited is a cycle, and the path held alongside is what lets the report name the
        ///way round rather than only that there was one. A name in a cycle is left out of the order and
        ///marked unresolvable, so a rule that reads it is reported rather than measured against nothing.
        ///</summary>
        private List<DrcDerivation> sortByDependency(Dictionary<string, DrcDerivation> derivations)
        {
            var ordered = new List<DrcDerivation>();
            var marks = new Dictionary<string, Mark>();
            var path = new List<string>();

            foreach (var derivation in deck.Derivations)
                visit(derivation.Name, derivations, marks, path, ordered);

            return ordered;
        }

        private void visit(
            string name,
            Dictionary<string, DrcDerivation> derivations,
            Dictionary<string, Mark> marks,
            List<string> path,
            List<DrcDerivation> ordered)
        {
            if (!derivations.TryGetValue(name, out var derivation))
            {
                //A drawn layer has nothing below it to visit and is finished. A name *nothing* declares is
                //a typo, which the deck has already reported - but it is marked unresolvable here as well,
                //so that the derivations reading it are carried along and a rule that would otherwise
                //measure against an empty layer can be named instead of coming back clean.
                if (name != DrcDeck.EveryLayer && !deck.Layers.ContainsKey(name))
                    unresolved.Add(name);

                return;
            }

            marks.TryGetValue(name, out Mark mark);

            if (mark == Mark.Done)
                return;

            if (mark == Mark.Visiting)
            {
                reportCycle(name, path);

                return;
            }

            marks[name] = Mark.Visiting;
            path.Add(name);

            foreach (string operand in derivation.Operands)
                visit(operand, derivations, marks, path, ordered);

            path.RemoveAt(path.Count - 1);
            marks[name] = Mark.Done;

            //A derivation caught in a cycle is not computable, and neither is anything that reads it. Left
            //out of the order rather than computed against nothing.
            if (!unresolved.Contains(name) && !readsSomethingUnresolved(derivation))
                ordered.Add(derivation);
            else
                unresolved.Add(name);
        }

        private bool readsSomethingUnresolved(DrcDerivation derivation)
        {
            foreach (string operand in derivation.Operands)
            {
                if (unresolved.Contains(operand))
                    return true;
            }

            return false;
        }

        private void reportCycle(string name, List<string> path)
        {
            int from = path.IndexOf(name);

            //The names from where the circle closes, and the one that closed it, so the report reads the
            //way round rather than as a set.
            var round = new List<string>(path.GetRange(from, path.Count - from))
            {
                name
            };

            foreach (string each in round)
                unresolved.Add(each);

            Problems.Add($"Derivation \"{name}\" depends on itself: {string.Join(" -> ", round)}.");
        }

        #endregion **************************************************************************



        #region Geometry ********************************************************************

        ///<summary>
        ///Every drawn layer the rules reach, merged, in one pass over the layout.
        ///
        ///**One pass rather than one per layer.** Walking the elements again for each of twenty-one layers
        ///is twenty-one passes over what can be half a million shapes, and the answer is the same. The
        ///grouping is done once and each layer's own shapes are merged out of it.
        ///
        ///Labels and open runs take no part, the same way <see cref="Booleans.MergeByLayer"/> leaves them
        ///out: a label is an anchor and a string, and a zero-width path is a centerline that encloses
        ///nothing. Either one unioned in would put area into a layer that has none there.
        ///</summary>
        private void gatherDrawn(HashSet<string> wanted)
        {
            var keys = new Dictionary<LayerKey, List<string>>();

            foreach (var declared in deck.Layers)
            {
                if (!wanted.Contains(declared.Key))
                    continue;

                if (!keys.TryGetValue(declared.Value, out var names))
                {
                    names = new List<string>();

                    keys.Add(declared.Value, names);
                }

                //Two names for one pair is a thing a deck may do, and both should get the same geometry
                //rather than the second getting none.
                names.Add(declared.Key);
            }

            if (keys.Count == 0)
                return;

            var shapesByKey = new Dictionary<LayerKey, List<IReadOnlyList<Element.Point>>>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is not null || element.IsOpen)
                    continue;

                if (!keys.ContainsKey(element.Layer.Key))
                    continue;

                if (!shapesByKey.TryGetValue(element.Layer.Key, out var shapes))
                {
                    shapes = new List<IReadOnlyList<Element.Point>>();

                    shapesByKey.Add(element.Layer.Key, shapes);
                }

                shapes.Add(element.Points);
            }

            foreach (var pair in keys)
            {
                //A layer the deck declares and the file does not carry is empty, which is not a problem: a
                //width check over nothing correctly finds nothing.
                if (!shapesByKey.TryGetValue(pair.Key, out var shapes))
                    shapes = new List<IReadOnlyList<Element.Point>>();

                var merged = Booleans.Merge(shapes);

                foreach (string name in pair.Value)
                    geometry[name] = merged;
            }
        }

        ///<summary>
        ///One derivation, folded left to right.
        ///
        ///**Step by step rather than in one call.** <see cref="Booleans.CombineAll"/> applies a single
        ///operation to many shapes, which is not what a derivation is: `poly and diff not psdm` changes
        ///operation halfway, and there is no precedence to reorder it by - it reads left to right because
        ///anything else is something a person has to hold in their head while reading a deck.
        ///</summary>
        private void derive(DrcDerivation derivation)
        {
            var result = geometryOf(derivation.First);

            foreach (var step in derivation.Rest)
            {
                var operand = geometryOf(step.Operand);

                result = Booleans.Combine(result, operand, step.Operation);
            }

            geometry[derivation.Name] = result;
        }

        ///<summary>
        ///What a name has been worked out to, or nothing when it has not.
        ///
        ///Empty rather than a throw for a name that could not be computed: the deck has already reported
        ///why, and <see cref="IsResolved"/> is how a caller tells "this layer is empty" from "this layer
        ///could not be worked out". Those are different answers and a report has to keep them apart.
        ///</summary>
        private List<List<Element.Point>> geometryOf(string name)
        {
            if (geometry.TryGetValue(name, out var shapes))
                return shapes;

            return new List<List<Element.Point>>();
        }

        #endregion **************************************************************************



        #region Reading it back *************************************************************

        ///<summary>What could not be worked out - a circle of derivations, and what reads one.</summary>
        public List<string> Problems { get; } = new List<string>();

        ///<summary>
        ///The shapes of a layer or a derivation, merged and hole-free.
        ///
        ///Empty in three cases, and they are not the same thing. A layer the file draws nothing on is
        ///empty and fine. A layer whose derivation could not be worked out is empty and is not -
        ///<see cref="IsResolved"/> is what tells those two apart. And a derivation no rule leads to is
        ///empty because it was never computed: this answers for the layers the deck's rules reach, since
        ///working out the rest would be a clipping pass over the whole layout for something nothing asked
        ///about.
        ///</summary>
        public IReadOnlyList<IReadOnlyList<Element.Point>> Of(string name)
        {
            return geometryOf(name);
        }

        ///<summary>
        ///Whether a name was worked out at all, as against having been worked out to nothing.
        ///
        ///**The distinction a report lives or dies on.** A layer that is empty because the file draws
        ///nothing on it has no violations, and saying so is correct. A layer that is empty because its
        ///derivation went round in a circle also has no violations, and saying so is a lie.
        ///</summary>
        public bool IsResolved(string name)
        {
            if (name == DrcDeck.EveryLayer)
                return true;

            if (unresolved.Contains(name))
                return false;

            return deck.Layers.ContainsKey(name) || namesDerivation(name);
        }

        private bool namesDerivation(string name)
        {
            foreach (var derivation in deck.Derivations)
            {
                if (derivation.Name == name)
                    return true;
            }

            return false;
        }

        ///<summary>
        ///Whether every layer the rules reach was worked out.
        ///
        ///The companion to <see cref="DrcDeck.AllRulesUnderstood"/>, and checked for the same reason: a
        ///report may not say "clean" over a layer it never managed to build.
        ///</summary>
        public bool AllLayersResolved
        {
            get { return Problems.Count == 0 && unresolved.Count == 0; }
        }

        ///<summary>
        ///The drawn layers a name is ultimately made of - itself when it is one, and what its derivation
        ///reads all the way down when it is not.
        ///
        ///**What lets a violation say which cell it came from.** A marker is a region a boolean produced
        ///and belongs to no element, so the way back into the hierarchy is through the elements that
        ///contributed to it - and those sit on the drawn layers underneath whatever the rule happened to
        ///name. A rule about a gate has to end up looking at poly and diff.
        ///</summary>
        public List<LayerKey> DrawnBehind(string name)
        {
            var keys = new List<LayerKey>();

            drawnBehind(name, keys, new HashSet<string>());

            return keys;
        }

        private void drawnBehind(string name, List<LayerKey> keys, HashSet<string> seen)
        {
            //Also what stops a circle from walking forever, which is worth having here as well as in the
            //sort: this is reachable on a deck whose cycle was reported and carried on from.
            if (!seen.Add(name))
                return;

            if (deck.Layers.TryGetValue(name, out var key))
            {
                if (!keys.Contains(key))
                    keys.Add(key);

                return;
            }

            foreach (var derivation in deck.Derivations)
            {
                if (derivation.Name != name)
                    continue;

                foreach (string operand in derivation.Operands)
                    drawnBehind(operand, keys, seen);
            }
        }

        ///<summary>The rules whose layers could not all be worked out, named.</summary>
        public List<string> RulesLeftUnmeasurable()
        {
            var left = new List<string>();

            foreach (var rule in deck.Rules)
            {
                if (!rulesLayersResolve(rule))
                    left.Add(rule.Id);
            }

            return left;
        }

        private bool rulesLayersResolve(DrcRule rule)
        {
            foreach (string operand in rule.Operands)
            {
                if (!IsResolved(operand))
                    return false;
            }

            if (rule.Except is string except && !IsResolved(except))
                return false;

            return true;
        }

        #endregion **************************************************************************
    }
}
