//
//Dragging a sidebar wider or narrower.
//
//**Not through Blazor.** A drag is a pointer event every few milliseconds and each one would be a round trip
//to C# and a re-render of a panel holding a nine-hundred-row tree - which is a frame's work to move a border
//one pixel. This sets one CSS variable and nothing else, which is a style recalculation and a paint.
//
//**Delegated from the document rather than wired per handle.** Both panels come and go with a button, and a
//handle registered when one opened would be a listener on an element that no longer exists. One listener,
//asked on each press whether what was pressed is a grabber, survives every render Blazor does.
//
//Nothing here is saved. The width goes back to the panel's own when it is closed and opened again, which is
//what was asked for: a drag is for the layout in front of you now, where a session is for the next one.
//

///How narrow and how wide a sidebar may be dragged, in pixels.
const LEAST_SIDEBAR = 140;
const MOST_SIDEBAR = 720;

///What is being dragged, or null between drags.
let dragging = null;

///
///Starts a drag if the press landed on a grabber.
///
///The grabber says which panel it sizes and which way it grows through data attributes rather than by
///looking at where it sits: a handle on the left edge of a right-hand panel moves the opposite way from one
///on the right edge of a left-hand panel, and reading that off the DOM would be inferring what the markup
///can simply state.
///
function onSidebarDown(event) {
    const grabber = event.target.closest('[data-sidebar-grab]');

    if (grabber == null)
        return;

    const panel = document.getElementById(grabber.dataset.sidebarGrab);

    if (panel == null)
        return;

    //
    //Where the variable is written, which is not always the panel it sizes.
    //
    //The cell tree's width is declared on the wrapper around it, because the selection panel beside it steps
    //aside by the same number and can only read a variable from something it is inside. Setting it on the
    //panel would size the panel and leave the one next to it where it was.
    //
    let apply = panel;

    if (grabber.dataset.sidebarApply != null)
        apply = document.getElementById(grabber.dataset.sidebarApply) ?? panel;

    //Which way the panel grows as the pointer moves right: +1 for a panel on the left, -1 on the right.
    let towards = 1;

    if (grabber.dataset.sidebarTowards === 'left')
        towards = -1;

    dragging = {
        panel: apply,
        variable: grabber.dataset.sidebarVariable,

        towards: towards,

        from: event.clientX,
        was: panel.getBoundingClientRect().width
    };

    //So the drag keeps receiving moves when the pointer runs ahead of the border, which it always does.
    grabber.setPointerCapture(event.pointerId);

    //Or the browser starts selecting the text of the rows the pointer crosses.
    event.preventDefault();

    document.body.classList.add('sidebarResizing');
}

function onSidebarMove(event) {
    if (dragging == null)
        return;

    const wanted = dragging.was + ((event.clientX - dragging.from) * dragging.towards);
    const held = Math.min(MOST_SIDEBAR, Math.max(LEAST_SIDEBAR, wanted));

    //
    //The variable rather than the width.
    //
    //The panel's own rule reads it, and so does whatever else is measured against the panel - the selection
    //panel steps aside by exactly this number. Setting a width here would move the border and leave
    //everything that follows it where it was.
    //
    dragging.panel.style.setProperty(dragging.variable, held + 'px');
}

function onSidebarUp() {
    if (dragging == null)
        return;

    dragging = null;

    document.body.classList.remove('sidebarResizing');
}

///
///Forgets a dragged width, so the panel goes back to the one its own rule gives it.
///
///Called when a sidebar is shut or opened. The layer list does not strictly need it - Blazor takes that
///element out of the page and its inline style goes with it - but the cell tree's width is declared on the
///wrapper around it, and the wrapper is still there when the tree is not. Called for both, because a rule
///that holds for one panel and not the other is a rule nobody will remember.
///
function resetSidebar(applyTo, variable) {
    const target = document.getElementById(applyTo);

    if (target == null)
        return;

    target.style.removeProperty(variable);
}

///
///Wires the three listeners once.
///
///Called from the page rather than run on load, so that the order is the app's to decide and so this file
///does nothing at all if it is served to something that never opens a sidebar.
///
function startSidebars() {
    document.addEventListener('pointerdown', onSidebarDown);
    document.addEventListener('pointermove', onSidebarMove);
    document.addEventListener('pointerup', onSidebarUp);

    //A drag that ends because the pointer was taken away - a touch canceled, a window losing focus.
    document.addEventListener('pointercancel', onSidebarUp);
}

startSidebars();
