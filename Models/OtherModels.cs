using GdsII;
using Microsoft.AspNetCore.Components;

namespace GDSViewer.Models
{
    ///
    ///A view asking the shell to name a layer.
    ///
    ///A layer's name is not part of the file: it is the app's own label for a pair of numbers, kept for
    ///every file opened afterwards and written into a layermap rather than into the GDS. So a rename raised
    ///from the 2D view's cell tree goes back up rather than onto that view's undo stack, and lands in the
    ///one place that already does it - see applyLayerName in Pages/Viewer.
    ///
    public sealed record LayerRename(LayerKey Key, string Name);

    ///<summary>What the shell needs of a view: something to draw into, and its own toolbar controls.</summary>
    public interface IRenderable
    {
        //Declared, not defaulted. A default body here would let a view that forgets to implement Render
        //compile and then silently draw nothing, which is exactly what used to happen to Viewer2D.
        //
        //Both arguments are nullable on purpose: the shell calls this before a file is open, and a view
        //re-rendering itself for its own reasons - a slider moving - passes null to mean "what you
        //already have". Every implementation handles that.
        //
        //There used to be a third argument, a bool for whether labels were drawn. It had to ride along
        //rather than be a [Parameter] because a parameter is only pushed to a child when the parent
        //re-renders, so the view would draw with the previous value and catch up a render late - where
        //showLayers works because it is a list the shell mutates in place. Labels are per layer now, so
        //they are in that list too and the exception is gone.
        //
        //prepared is the flattened layout when the caller already has one, and null to mean "work it out".
        //
        //The shell flattens a file as it opens it, because what the flattener found - a reference loop, a
        //cell that is not in the file, a layout too large to draw all of - is something somebody has to be
        //told about whichever view they happen to be looking at. Handing that over rather than letting each
        //view flatten again is what keeps a file open to one pass instead of two, and it is why switching
        //from 2D to 3D and back no longer re-flattens: measured at 500 ms on a half-million-element layout.
        //
        Task Render(GDS? gds, List<CheckboxItem>? showLayers, FlattenedLayout? prepared = null);

        ///<summary>
        ///The controls this view contributes to the shell's toolbar. The shell renders them without
        ///knowing what they are, which is what keeps a view from needing a reference back to it.
        ///</summary>
        RenderFragment Toolbar { get; }

        ///<summary>
        ///This view's own controls - opacity, layer spacing, background - written into the session so they
        ///come back, and read out of it when one is restored.
        ///
        ///On the interface because each view owns its controls and the shell deliberately does not know
        ///what they are; the alternative was hoisting every slider into the shell so it could save them,
        ///which would undo the split that keeps a view's toolbar its own business. A view with nothing to
        ///remember, like the text one, leaves both of these empty.
        ///
        ///Applying is separate from rendering: the shell sets these before the first draw, so a restored
        ///opacity is what gets drawn rather than something that flickers to it afterwards.
        ///</summary>
        void WriteSettings(SavedSession session);

        Task ApplySettings(SavedSession session);
    }

    public class CheckboxItem
    {
        ///<summary>
        ///The layer/datatype pair this row toggles. A pair rather than a layer number because that is what
        ///identifies a layer - see <see cref="LayerKey"/> - so 65/20 and 65/16 get a row each.
        ///</summary>
        public LayerKey Id { get; set; }

        public required string Label { get; set; }

        ///<summary>Whether this layer is drawn at all - what the eye on its row says.</summary>
        public bool IsSelected { get; set; }

        ///
        ///Whether this layer is locked: drawn, and not to be touched.
        ///
        ///**A third state, between shown and hidden.** Hiding a layer takes it off the screen, which is the
        ///wrong answer when it is the thing you are working *against* - a via has to line up with the metal
        ///over it, and you cannot line up with something you cannot see. Locking leaves it in the picture,
        ///faded, and takes it out of everything that picks: a click passes through it, a band does not catch
        ///it, and Select All leaves it alone.
        ///
        ///**Any number of them, which is the point.** This began as one isolated layer - press a row and
        ///work on that alone - and one layer is not how a layout is edited: a via and its two metals are
        ///three layers being worked on together. Locking says which layers are *not* in hand, so what is
        ///left in hand can be as many as it needs to be.
        ///
        ///Off by default: a file opens with every layer live, so nothing is out of reach until somebody
        ///says so.
        ///
        public bool IsLocked { get; set; }

        ///<summary>
        ///Whether this layer's TEXT elements are drawn. Per layer rather than one switch for the file,
        ///because which labels are worth reading depends on the layer: the pin names on one metal layer
        ///can be the reason the view is open while every other layer's are noise.
        ///
        ///On by default, so a file opens showing what it says about itself.
        ///</summary>
        public bool ShowLabels { get; set; } = true;

        ///<summary>
        ///The pairs that are switched on, as a set.
        ///
        ///Built once per redraw rather than searching the list for each element. The bundled cells are
        ///small enough not to care, but the 2D view rebuilds its whole markup on every tick of the opacity
        ///slider - so a scan per element would be paid over and over, on a drag, at whatever size the file
        ///happens to be. Shared with the 3D view so both agree on what "visible" means.
        ///
        ///On this type rather than in the library, because it is about the shell's own row model. The
        ///library takes the set.
        ///</summary>
        public static IReadOnlySet<LayerKey> VisibleLayers(List<CheckboxItem> showLayers)
        {
            var visible = new HashSet<LayerKey>();

            foreach (var item in showLayers)
            {
                if (item.IsSelected)
                    visible.Add(item.Id);
            }

            return visible;
        }

        ///
        ///The pairs that can be worked on: drawn, and not locked.
        ///
        ///**What everything that picks is measured against**, where <see cref="VisibleLayers"/> is what
        ///everything that draws is measured against. The two were one question while the only way to take a
        ///layer out of reach was to hide it; a lock is the case where they differ, and keeping them apart is
        ///what lets a locked layer stay on screen while a click goes straight through it.
        ///
        ///A hidden layer is not editable either, which is why this is an intersection rather than the
        ///complement of the locked set: a shape nobody can see is not a shape anybody meant to choose.
        ///
        public static IReadOnlySet<LayerKey> EditableLayers(List<CheckboxItem> showLayers)
        {
            var editable = new HashSet<LayerKey>();

            foreach (var item in showLayers)
            {
                if (item.IsSelected && !item.IsLocked)
                    editable.Add(item.Id);
            }

            return editable;
        }

        ///
        ///The pairs that are locked, as a set, for the rule that fades them.
        ///
        ///Not intersected with what is visible: a hidden layer is not drawn, so whether it would have been
        ///faded is a question about nothing. Kept as what the rows say rather than as what is left over
        ///after the editable set, so a reader of either one does not have to hold the other in mind.
        ///
        public static IReadOnlySet<LayerKey> LockedLayers(List<CheckboxItem> showLayers)
        {
            var locked = new HashSet<LayerKey>();

            foreach (var item in showLayers)
            {
                if (item.IsLocked)
                    locked.Add(item.Id);
            }

            return locked;
        }

        ///<summary>
        ///The pairs whose labels are switched on, as a set. Built the same way and for the same reason as
        ///<see cref="VisibleLayers"/>.
        ///
        ///A layer that is switched off contributes nothing here even if its own label switch is on, so the
        ///two sets cannot disagree about a layer that is not being drawn at all. The renderers intersect
        ///them anyway; doing it here as well means the set says what it claims to on its own.
        ///</summary>
        public static IReadOnlySet<LayerKey> LabeledLayers(List<CheckboxItem> showLayers)
        {
            var labeled = new HashSet<LayerKey>();

            foreach (var item in showLayers)
            {
                if (item.IsSelected && item.ShowLabels)
                    labeled.Add(item.Id);
            }

            return labeled;
        }
    }

    public class ToolBarItem
    {
        public ToolBarItem(string displayText, string imagePath)
        {
            DisplayText = displayText;
            ImagePath = imagePath;
        }

        public string DisplayText { get; set; }
        public string ImagePath { get; set; }
    }
}
