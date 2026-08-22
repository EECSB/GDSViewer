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

    //The layer sidebar is one of the two edges the popup's room is measured between, so dragging it changes
    //that room. Cheap enough to do on the move: it is one rect while a popup is open and nothing when not.
    measurePopupRoom();
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

//
//How much room the popup under the toolbar has////////////////////////////////
//
//**Measured, because it cannot be worked out.** The Examples and History popups hang under their buttons
//and must not run past the right of the view. The room they have is the distance from the popup's own left
//edge to whatever is on that right - and the popup's left edge is wherever the toolbar happens to put its
//button, which moves with the cell tree being open, with the tree's dragged width, and with every control
//inserted ahead of the button.
//
//That was a hand-fitted line in the stylesheet - `80vw` less a constant - and it had been re-fitted three
//times: once when the layer sidebar stopped sizing itself, once when a New button went into the bar, and
//it still went wrong at the narrow end, where the bar wraps and the button moves somewhere the line never
//saw. The stylesheet says as much, and says what it needs instead: the popup has to be measured against
//the box it must stay inside rather than against the window.
//
//So this measures it. One number, written to a custom property the popup's rule reads, recomputed when the
//window changes, when a sidebar is dragged, and when a popup appears.
//

///A hair of daylight between the popup and whatever is to the right of it.
const POPUP_GUTTER = 12;

///
///The x the popup may not cross: the right of the view.
///
///**The view rather than the layer sidebar beside it.** The two are the same edge while that panel is open
///- measured, 886 against 886 - but the view is the thing the rule is about, and it is the one that stays
///right when the panel is closed and the canvas grows into the space. Reading the panel would then hand the
///popup the whole window.
///
///Falling back to the window for the views that have no wrapper, and for a bound that has somehow ended up
///left of the popup, which would give a negative room.
///
function popupRightBound(from) {
    const view = document.querySelector('.viewWrapper');

    if (view != null) {
        const box = view.getBoundingClientRect();

        if (box.width > 0 && box.right > from)
            return box.right;
    }

    return document.documentElement.clientWidth;
}

///
///The lowest the popup may reach: the foot of the view, or the window when there is no view.
///
///The downward twin of popupRightBound, and there for the same reason - a popup past the edge of the canvas
///is over the page rather than over the layout, which is not where a thing belonging to the toolbar goes.
///
function popupBottomBound() {
    const view = document.querySelector('.viewWrapper');

    if (view != null) {
        const box = view.getBoundingClientRect();

        if (box.height > 0)
            return box.bottom;
    }

    return document.documentElement.clientHeight;
}

///
///The widest the popup may become, matching the 72em cap in the stylesheet.
///
///Past this a file picker is a meter of white with a list of names down one edge, so the room stops being
///worth taking. Below it the popup takes whatever the view has.
///
const MOST_POPUP = 72 * 16;

///
///Writes the room to `--popup-room` and any leftward slide to `--popup-shift`, on the root rather than on
///the popup.
///
///Blazor owns the popup element and rewrites it on every render; an inline style set here would come and go
///with that. The root is nobody else's.
///
///**The slide is for the narrow end, and only for it.** The popup hangs under its button, which is where a
///popup belongs - it is how you can tell which button it came from. But that pins its left edge, and on a
///narrow window the room from there to the right of the view is less than the list and the picture want,
///while the view sits half empty to its *left*: at a 985px window, 410px of room inside a 607px view, with
///185 unused pixels on the other side of the popup.
///
///So when the room is less than the contents want, it slides left to make up the difference - never further
///than the left of the view, and never more than the difference. When the room is enough, the shift is zero
///and the popup stays under its button.
///
///**Overflow is the wrong trigger, which is what this tried first.** At that 985px window nothing overflowed:
///the list and the picture are both allowed to shrink, so they had, to 192px and to the picture's 160px
///floor. A popup whose picture has been squeezed to nothing "fits" perfectly.
///
///**And "narrower than it wants" is too eager**, which is what it tried second. The contents want about 53em
///and the room is under that on most windows, so the popup slid on nearly all of them - 221px from its
///button at 1280, where the picture was a healthy 295px and needed nothing. Hanging off the button is the
///point of this popup and app-launch.spec pins it.
///
///That was gated behind the picture reaching its floor for a while, so the popup would stay under its button
///on anything but the narrowest windows. It stayed under its button and left the room beside it empty, which
///is what was actually being complained about. Hanging off the button is worth something, but not the width:
///it slides whenever the room is short of what the contents want, and only that far.
///
function measurePopupRoom() {
    const popup = document.querySelector('.popupDiv.popupUnder');

    if (popup == null)
        return;

    const view = document.querySelector('.viewWrapper');
    const set = (name, value) => document.documentElement.style.setProperty(name, value + 'px');

    //
    //Measured with any previous slide taken off, so this is where the popup would sit on its own.
    //
    //Without that the shift compounds: the left edge read back is the shifted one, which makes the room look
    //larger, which is measured again on the next mutation from a position that already includes it.
    //
    set('--popup-shift', 0);

    const box = popup.getBoundingClientRect();
    const left = box.left;
    const bound = popupRightBound(left);
    const room = Math.max(0, bound - left - POPUP_GUTTER);

    set('--popup-room', room);

    //
    //**And the same question downward, which used to be a constant.**
    //
    //The stylesheet had `100vh` less 260 - the header, the bar above the popup and whatever sits below the
    //view, added up once on one window. A guess like that is wrong by a fixed number of pixels at every
    //other size, and it was wrong the mean way round: about forty short, so the popup stopped that far above
    //the foot of the canvas it is meant to reach. On a 620-tall window the Examples list showed six of its
    //897 rows with eighty-three pixels going spare underneath it.
    //
    //Measured, so it is right at every height rather than at the one it was taken on. The cap in the
    //stylesheet still applies: past about 760 a list that scrolls anyway has nothing more to show.
    //
    //The popup's own top is stable under this - it hangs at `top: 100%` of the button's column, so nothing
    //here moves it - which is why this needs none of the reset the sideways shift above does.
    //
    const tall = Math.max(0, popupBottomBound() - box.top - POPUP_GUTTER);

    set('--popup-tall', tall);

    //
    //**And what is left for the picker once the heading, the filter box and the padding have had theirs.**
    //
    //A max-height cannot make anything taller - it only stops it - so the room above went unused until the
    //box that actually decides the height was told about it. That box had a second constant of its own,
    //`100vh - 450` against the popup's `100vh - 260`: the same guess made twice, and the two have to stay
    //exactly 190 apart or the popup scrolls inside its own cap.
    //
    //That difference *is* the chrome, so it is measured rather than written down twice. It does not depend
    //on the picker's height - a heading and a filter box are the same size whatever is under them - so one
    //reading settles it and there is no second pass to converge.
    //
    const picker = popup.querySelector('.examplePicker');

    //
    //**Measured off scrollHeight, because a bounding rect here is a torn one.**
    //
    //The obvious reading - the popup's rect less the picker's - is the popup's *capped* height whenever the
    //cap is biting, while the picker in the same pass is still whatever it was before. Shrink the window and
    //that subtraction goes negative: a 1300px window left the picker at 640 while the popup had already been
    //clamped to 241, so the chrome came out at minus 398 and the picker was handed the whole 640 back.
    //
    //scrollHeight is the content, which no cap touches, so both terms move together and the picker's own
    //height cancels out of the subtraction whatever it currently is. It measures to the padding box, so the
    //transparent border the popup hangs from is added back - taken as the rect less clientHeight rather than
    //written down, since both of those are clamped alike and their difference is the border either way.
    //
    //Re-read rather than reusing the rect above, which was taken before --popup-room could change the width
    //and rewrap the contents.
    //
    if (picker != null) {
        const rect = popup.getBoundingClientRect();
        const border = rect.height - popup.clientHeight;
        const chrome = popup.scrollHeight + border - picker.getBoundingClientRect().height;

        set('--picker-tall', Math.max(0, tall - chrome));
    }

    if (view == null)
        return;

    //
    //A window narrow enough to take the picture away entirely leaves a popup that is only a list of names,
    //and a list of names has nothing to gain from being wider.
    //
    const picture = popup.querySelector('.examplePreviewFrame');

    if (picture == null || picture.getBoundingClientRect().width <= 0)
        return;

    const viewLeft = view.getBoundingClientRect().left;

    //The whole view, less a gutter each side, and never past the cap. Sliding cannot conjure room that is
    //not in the view, so this is the most any of it could ever be worth.
    const wanted = Math.min(MOST_POPUP, bound - viewLeft - (POPUP_GUTTER * 2));

    if (room >= wanted)
        return;

    const canShift = Math.max(0, left - viewLeft - POPUP_GUTTER);
    const shift = Math.min(canShift, wanted - room);

    if (shift <= 0)
        return;

    set('--popup-shift', shift);
    set('--popup-room', room + shift);
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

    window.addEventListener('resize', measurePopupRoom);

    //
    //And when a popup appears, which no resize announces.
    //
    //Watching for the element rather than being told about it by C#: the popups are opened from several
    //places and closed from more, and a call at each of them is a list that goes stale.
    //
    //**Held to one measurement a burst.** This watches the whole document, so every render Blazor does
    //arrives here - and a layout of twenty thousand elements is a great many of them. A rect is a forced
    //layout read, and one per mutation batch during that render is exactly the kind of cost this file was
    //written to avoid. Coalesced, the whole thing is one class lookup, which finds nothing at all unless a
    //popup is up.
    //
    //**A timeout rather than requestAnimationFrame**, which is the obvious choice and the wrong one: rAF
    //does not run at all while the page is not being rendered, so a measurement deferred into it on a hidden
    //tab is a measurement that never happens - and the flag guarding it stays raised, so every mutation
    //after that one is swallowed too until the page comes back. A timeout fires either way.
    //
    let pending = false;

    const watching = new MutationObserver(() => {
        if (pending)
            return;

        pending = true;

        setTimeout(() => {
            pending = false;

            measurePopupRoom();
        }, 0);
    });

    watching.observe(document.body, { childList: true, subtree: true });
}

startSidebars();
