//We select the SVG into the page
let svg;
let ratio;

const initialViewBox = {
    x: -2000,
    y: - 1000,
    width: 4000,
    height: 4000
};

//We save the original values from the viewBox
let viewBox = {
    x: initialViewBox.x,
    y: initialViewBox.y,
    width: initialViewBox.width,
    height: initialViewBox.height
};

//The distances calculated from the pointer will be stored here
var newViewBox = {
    x: 0,
    y: 0
};

//This variable will be used later for move events to check if pointer is down or not
let isPointerDown = false;
let isFirstClick = true;

//Watches the SVG for a change of size. Kept so the one from the last visit to this view can be dropped
//before another is started. See registerSVGEvents.
let svgResizeObserver = null;

//The size the grid and the ruler were last drawn across, so the observer above can tell a real resize from
//being woken by its own drawing. See updateRatio.
let lastDrawnWidth = 0;
let lastDrawnHeight = 0;

//This variable will contain the original coordinates when the user start pressing the mouse or touching the screen
let pointerOrigin = {
    x: 0,
    y: 0
};


//Called on every first render of the 2D view, so it runs again each time that view is returned to.
//Everything below has to be safe to repeat: the listeners on the SVG go away with the element Blazor
//replaced, but anything hung on window does not.
function registerSVGEvents(view) {
    //By id rather than the first svg in the document: the QR code and any inline icon are svg elements
    //too, and whichever appears first would otherwise get the pan and zoom handlers instead.
    svg = document.getElementById('gdsSVG');

    if (svg == null)
        return;

    keys.view = view;

    //
    //The middle button belongs to the view, not to the browser.
    //
    //A middle press starts scroll-anywhere in Chrome and pastes the primary selection on Linux, and neither
    //is stoppable from `pointerdown` - the first is driven by `mousedown` and the second by `auxclick`. Both
    //have to be refused here or a middle drag pans the layout with a drifting scroll cursor over it.
    //
    svg.addEventListener('mousedown', refuseMiddleDefault);
    svg.addEventListener('auxclick', refuseMiddleDefault);

    if (window.PointerEvent) {
        //If browser supports pointer events.
        svg.addEventListener('pointerdown', onPointerDown);
        svg.addEventListener('pointerup', onPointerUp);
        svg.addEventListener('pointerleave', onPointerLeave);
        svg.addEventListener('pointermove', onPointerMove);
    } else {
        //Else add mouse events listeners ...
        svg.addEventListener('mousedown', onPointerDown);
        svg.addEventListener('mouseup', onPointerUp);
        svg.addEventListener('mouseleave', onPointerUp);
        svg.addEventListener('mousemove', onPointerMove);

        //.. and touch events listeners.
        svg.addEventListener('touchstart', onPointerDown);
        svg.addEventListener('touchend', onPointerUp);
        svg.addEventListener('touchmove', onPointerMove);
    }

    //A fresh view starts in pan mode, which is what the component's own field says too - so the two
    //cannot disagree about which tool is active after leaving the view and coming back to it.
    ruler.active = false;
    clearRuler();

    //The chosen node belongs to the SVG Blazor has just replaced, so the reference to it is stale.
    selection.active = false;
    selection.chosen = null;
    drawing.active = false;
    drawing.points = [];

    //Hung on window rather than on the SVG, which cannot take focus - so leaving the view does not take it
    //with the element, and a second visit would otherwise add a second one.
    window.removeEventListener('keydown', onEditorKey);
    window.addEventListener('keydown', onEditorKey);

    //A polygon is finished by clicking its first corner, by Enter, or by this - between the three, somebody
    //who has not been told will find one.
    svg.addEventListener('dblclick', function (event) {
        if (drawing.active && (drawing.mode === 'poly' || drawing.mode === 'path')) {
            finishShape();

            return;
        }

        retypeLabelUnder(event);
    });

    //Calculate the ratio based on the viewBox width and the SVG width.
    updateRatio();

    //The SVG's own box, rather than the window.
    //
    //The view is sized by what the layout leaves it now, so the SVG can change width while the window sits
    //still - the sidebar taking more room, a control above it wrapping. A window listener hears none of
    //that, and the ratio stays measured against a width the SVG no longer has, which drags the drawing at
    //the wrong speed. This still covers the window, since a narrower window is a narrower SVG.
    //
    //Disconnected first because this runs on every visit to the view, on the element Blazor has just
    //made - the observer from last time is watching one that is no longer in the document.
    if (svgResizeObserver != null)
        svgResizeObserver.disconnect();

    svgResizeObserver = new ResizeObserver(updateRatio);
    svgResizeObserver.observe(svg);

    const wrapper = document.getElementById("svgWrapper");

    if (wrapper == null)
        return;

    //Add scrool event for zoom.
    wrapper.addEventListener("wheel", function (e) {
        e.preventDefault();

        //Sized by viewGeometry, which also refuses to go below a minimum: each notch subtracts a fixed
        //amount, so enough of them walked the width through zero and negative, which is not a viewBox a
        //browser accepts - the view just stopped drawing.
        const zoomed = window.viewGeometry.zoomedSize(viewBox, e.deltaY);

        //
        //**Anchored under the pointer.** Only the size used to change, which leaves the top-left corner of
        //the view where it was and pulls everything towards it - so zooming in walked off into the empty
        //margin beside the layout rather than towards whatever was being looked at. A fixed step made that
        //barely perceptible; a proportional one makes it the whole of the gesture.
        //
        const under = layoutPointFromEvent(e);
        const scale = zoomed.width / viewBox.width;

        viewBox.width = zoomed.width;
        viewBox.height = zoomed.height;

        if (under != null) {
            newViewBox.x = under.x - ((under.x - newViewBox.x) * scale);
            newViewBox.y = under.y - ((under.y - newViewBox.y) * scale);

            viewBox.x = newViewBox.x;
            viewBox.y = newViewBox.y;
        }

        var viewBoxString = `${newViewBox.x} ${newViewBox.y} ${viewBox.width} ${viewBox.height}`;

        //Apply new viewBox coordinates.
        svg.setAttribute('viewBox', viewBoxString);

        //The measurement is drawn in the layout's coordinates, so it pans and zooms with the geometry -
        //but its line width and lettering are sized against how much layout is on screen, and that is
        //exactly what has just changed.
        drawRuler();

        //And the grid is only drawn across what is on screen, which has just become somewhere else.
        drawGrid();

        //The one gesture that changes how many pixels a layout unit is, which is what a stipple's size is
        //held against. Pan does not call this: it moves the window without resizing it.
        scalePatterns();

        //A wheel has no "up", so the only end to a zoom is it stopping.
        reportViewBoxWhenSettled();
    });
}

//How many viewBox units one screen pixel is, which is what turns a drag in pixels into a pan in the
//layout's own coordinates. Recomputed on resize because the SVG's on-screen width changes with it.
function updateRatio() {
    if (svg == null)
        return;

    const width = svg.getBoundingClientRect().width;

    //A hidden or not-yet-laid-out SVG measures zero, and dividing by it would make every later drag NaN.
    if (width === 0)
        return;

    ratio = viewBox.width / width;

    //
    //And the two things drawn across *what is on screen* are drawn again, because that is what just moved.
    //
    //The viewBox does not change when the window does - the geometry rescales itself and needs nothing -
    //but the grid and the ruler are explicit paths spanning the visible area, and a wider window shows
    //layout they were never drawn across. This was written once before and taken out again for being
    //unprovable: every spec that resized happened to switch the grid on afterwards, and that switch is what
    //redrew it. With the grid on out of the box there is no such switch, and two specs now fail without it.
    //
    //**Only when the box really changed.** This runs from a ResizeObserver watching the SVG and both of
    //these draw *into* that SVG, so redrawing unconditionally is an observer that wakes itself.
    //
    const height = svg.getBoundingClientRect().height;

    if (width === lastDrawnWidth && height === lastDrawnHeight)
        return;

    lastDrawnWidth = width;
    lastDrawnHeight = height;

    drawGrid();
    drawRuler();

    //The box changed size, so a layout unit is a different number of pixels than it was.
    scalePatterns();
}

function getPointFromEvent(event) {
    var point = { x: 0, y: 0 };
    
    if (event.targetTouches) {
        //If event is triggered by a touch, get the position of the first finger like so:
        point.x = event.targetTouches[0].clientX;
        point.y = event.targetTouches[0].clientY;
    } else { 
        //Else get the mouse position like so:
        point.x = event.clientX;
        point.y = event.clientY;
    }

    return point;
}

///
///Whether the middle button is being held, which pans whatever tool is in hand.
///
///Panning is not really one of the tools. It is how you get to the part of the layout you want to use a
///tool *on*, and going up to the toolbar to move the view and again to come back is an interruption in the
///middle of the thing you were actually doing. Every layout editor gives it to the middle button for that
///reason.
///
let middlePanning = false;

///<summary>Stops the browser doing its own thing with a middle press. See where this is hung up.</summary>
function refuseMiddleDefault(event) {
    if (event.button === 1)
        event.preventDefault();
}

///Moves the view by however far the pointer has come since the press.
function panWith(event) {
    //Prevent user from making a selection on the page.
    event.preventDefault();

    const pointerPosition = getPointFromEvent(event);

    //ratio converts a pixel into layout units at the current size; viewGeometry also damps by the zoom,
    //so a drag moves what is under the cursor by the same amount however far in it is.
    const panned = window.viewGeometry.pannedOrigin(viewBox, initialViewBox.height, pointerOrigin, pointerPosition, ratio);

    newViewBox.x = panned.x;
    newViewBox.y = panned.y;

    svg.setAttribute('viewBox', `${newViewBox.x} ${newViewBox.y} ${viewBox.width} ${viewBox.height}`);

    //Drawn only across what is on screen, which is what has just moved.
    drawGrid();
}

function onPointerMove(event) {
    //Before anything else, and whatever the tool is doing: where the pointer is does not depend on what
    //it is being used for.
    rememberPointerOverView(event);
    showCursorAt(event);

    //Asked before the tool is, because that is the whole point of it: the middle button pans through
    //whatever else is going on.
    if (middlePanning) {
        panWith(event);

        return;
    }

    //Carrying a cell takes the pointer over completely, before any tool: the thing being carried is not a
    //tool's business, and whichever one is in hand it is still following the cursor until it is put down.
    if (carrying.active) {
        onCarryMove(event);

        return;
    }

    //Measuring takes the pointer over completely: a drag that both panned and measured would move the
    //thing being measured while it was being measured.
    if (ruler.active) {
        onRulerMove(event);

        return;
    }

    //Selecting leaves the view still. Panning while picking would mean a click that started on one shape
    //could finish on another.
    if (selection.active) {
        onSelectMove(event);

        return;
    }

    if (drawing.active) {
        onDrawMove(event);

        return;
    }

    //Avoids a jump of the image on the first move by centering it.
    if (isFirstClick) {
        isFirstClick = false;

        return;
    }

    if (!isPointerDown)
        return;

    panWith(event);
}

function onPointerDown(event) {
    //
    //**The right button raises the menu and does nothing else.**
    //
    //Asked before anything, including the capture, because otherwise it falls through to whichever tool is
    //in hand: a right-click with Select ran the hit test, so raising a menu over a shape first changed what
    //was chosen, and a right-click on the background cleared the very selection the menu was about to offer
    //actions for. The menu itself is Blazor's, off the contextmenu event, which arrives after this one.
    //
    if (event.button === 2)
        return;

    //
    //**The view keeps the pointer for the whole gesture.**
    //
    //Without this a drag is only a drag for as long as the browser feels like sending the events to this
    //element. Leaving the view, or the browser deciding the gesture was really a text selection, takes them
    //elsewhere - and a corner drag that loses its middle finishes wherever the sequence picked up again,
    //which looks like the shape jumping across the layout rather than like events going missing.
    //
    //Capturing also suppresses the boundary events until the release, so a drag that goes past the edge of
    //the view carries on and ends where the button actually came up.
    //
    if (svg != null && svg.setPointerCapture != null && event.pointerId != null) {
        try {
            svg.setPointerCapture(event.pointerId);
        }
        catch { }
    }

    //
    //**The middle button pans, whatever tool is in hand.**
    //
    //Asked before the tool is, so it works in the middle of one rather than instead of it. The default has
    //to go with it: a middle press on a page is the browser's scroll-anywhere gesture, and that would put a
    //drifting cursor over a view that is already being dragged.
    //
    if (event.button === 1) {
        event.preventDefault();

        middlePanning = true;

        const from = getPointFromEvent(event);

        pointerOrigin.x = from.x;
        pointerOrigin.y = from.y;

        return;
    }

    //Before the tools, for the reason above: a press while something is being carried puts it down, whatever
    //was in hand when it was picked up.
    if (carrying.active) {
        onCarryDown(event);

        return;
    }

    if (ruler.active) {
        onRulerClick(event);

        return;
    }

    if (selection.active) {
        onSelectClick(event);

        return;
    }

    if (drawing.active) {
        onDrawDown(event);

        return;
    }

    isPointerDown = true;

    //Get the starting click/touchdown on the start of the drag.
    var pointerPosition = getPointFromEvent(event);
    pointerOrigin.x = pointerPosition.x;
    pointerOrigin.y = pointerPosition.y;
}

///
///The pointer leaving the view, which is only the end of a gesture when the view is not holding it.
///
///Leaving used to finish a drag outright, and that was the safety net for a release the view would never
///hear about. With the pointer captured the release is guaranteed to arrive, so treating the boundary as
///the end cuts a drag short at the edge - a corner dragged out and brought back stopped where it crossed,
///which looked like the drag being ignored. The net is still there for a browser that will not capture.
///
function onPointerLeave(event) {
    if (svg != null && svg.hasPointerCapture != null && event.pointerId != null && svg.hasPointerCapture(event.pointerId))
        return;

    onPointerUp(event);
}

function onPointerUp(event) {
    //The press was never passed on, so the release must not be either - the same rule the middle button is
    //held to a few lines down, and for the same reason.
    if (event.button === 2)
        return;

    //Handed back before anything else runs, so an early return below cannot leave the view holding it.
    if (svg != null && svg.releasePointerCapture != null && event.pointerId != null && svg.hasPointerCapture(event.pointerId)) {
        try {
            svg.releasePointerCapture(event.pointerId);
        }
        catch { }
    }

    //
    //Ended before the tool is asked, and it keeps the view where the drag left it.
    //
    //The tool was never told the press happened, so it must not be told about the release either - a Select
    //that heard only the second half of a gesture would read it as a click on wherever the pan finished.
    //
    if (middlePanning) {
        middlePanning = false;

        viewBox.x = newViewBox.x;
        viewBox.y = newViewBox.y;

        return;
    }

    if (selection.active) {
        onSelectRelease(event);

        return;
    }

    if (drawing.active) {
        onDrawUp(event);

        return;
    }

    if (ruler.active)
        return;

    isPointerDown = false;

    //Save the new viewBox coordinates based on the last pointer position.
    viewBox.x = newViewBox.x;
    viewBox.y = newViewBox.y;

    //The end of a pan, which is where C# gets told what is on screen - see reportViewBox.
    reportViewBoxWhenSettled();
}

//Selection//////////////////////////////////////

//The view this reports back to, and which shape is currently picked out.
let selection = {
    active: false,
    view: null,

    //Whether a chosen shape wears the handles that reshape it. False for the Move tool - see startSelecting.
    handles: true,

    //What C# last said is chosen, so a press can tell "take hold of this" from "go into it".
    chosen: [],

    //The already-chosen shape a press landed on, waiting to hear whether it becomes a drag or a click.
    pressedOnChosen: -1,

    //Where a drag started, in the layout's coordinates. Null when nothing is being dragged.
    draggingFrom: null,

    //Which corner is being dragged, or -1 when the whole shape is.
    draggingCorner: -1,

    //Where a rubber band started, when the drag began on the background rather than on a shape.
    bandFrom: null
};

const SELECTED_CLASS = 'shapeSelected';

///
///Switches selecting on. view is a DotNetObjectReference back to the component that asked.
///
///`withHandles` is what tells Select from Move. The handles that reshape a chosen shape sit on top of it
///and have to be tested before it, or a click meant for one would re-select the shape underneath and take
///the handles away - which means a chosen shape wears a ring of corners that all catch a drag. Move is the
///same picking with none of them, so a drag takes hold of the shape itself wherever it lands.
///
function startSelecting(view, withHandles = true) {
    selection.active = true;
    selection.view = view;
    selection.handles = withHandles === true;

    if (!selection.handles)
        clearVertexHandles();

    if (svg != null)
        svg.style.cursor = 'pointer';
}

function stopSelecting() {
    selection.active = false;
    selection.view = null;

    clearSelection();

    if (svg != null)
        svg.style.cursor = '';
}

function clearSelection() {
    if (svg != null)
        showSelection(null);

    selection.draggingFrom = null;
    selection.draggingCorner = -1;
    selection.pressedOnChosen = -1;

    dropLift();
    clearBand();
    clearVertexHandles();
}

///
///Picks out whatever the pointer landed on, and tells the view which element of the layout it was.
///
///**C# does the hit test now.** The geometry is one path per layer rather than a node per shape - see
///SvgWriter - so there is nothing per shape for the event to name, and the layout has to be asked instead.
///That is the better place for it anyway: while a cell is being edited, the shapes of that cell win over
///whatever is drawn on top of them, which is what clicking through a faded context means.
///
///Asked synchronously, because the answer decides at pointer-down whether the drag that may follow is a
///shape being moved or a band being pulled.
///
///Labels are still their own nodes and are still hit-tested by the browser: there are a few thousand at
///worst against hundreds of thousands of shapes, and a name is a box of text rather than an outline.
///
function onSelectClick(event) {
    const target = event.target;

    if (target == null || target.closest == null)
        return;

    //A handle first, because it sits on top of the shape it belongs to and a click meant for it would
    //otherwise re-select that shape and take the handles away under the pointer. None of them exist under
    //the Move tool, which is the whole of what that tool is.
    let handle = null;

    if (selection.handles)
        handle = target.closest('[data-corner]');

    if (handle != null) {
        selection.draggingCorner = parseInt(handle.getAttribute('data-corner'), 10);
        selection.draggingFrom = layoutPointFromEvent(event);

        return;
    }

    //Either modifier, because which one adds to a selection is a habit rather than a rule and the two
    //are the same gesture to whoever is holding one down.
    const adding = event.ctrlKey === true || event.metaKey === true || event.shiftKey === true;

    const point = layoutPointFromEvent(event);

    //A label, which still carries its own number - and is handed over rather than acted on, because a
    //name's box is far larger than the anchor it hangs from and would otherwise swallow every click meant
    //for a shape drawn over it. Which of the two a click means is one rule, in one place: Picking.
    const label = target.closest('[data-element]');

    let named = -1;

    if (label != null)
        named = parseInt(label.getAttribute('data-element'), 10);

    let index = -1;

    //
    //**Asked where the pointer is, not where it snaps to.**
    //
    //Snapping decides where a point *goes*; it has no business deciding what was clicked. The pitch is a
    //micron by default and this file's shapes are a few tenths across, so the nearest crossing to the
    //middle of a shape is usually outside it - with snapping on, clicking a shape selected nothing at all.
    //
    //Only the hit test. The drag origin below stays snapped, which is what keeps a move a whole number of
    //steps: a distance between two snapped points is a multiple of the pitch.
    //
    const under = rawLayoutPoint(event) ?? point;

    if (selection.view != null)
        index = selection.view.invokeMethod('HitTest', under.x, under.y, named);
    else
        index = named;

    //
    //**Pressing on what is already chosen says nothing yet.**
    //
    //A second click on a shape descends into its cell - see descendsOnClick - and a drag also starts with a
    //press on a shape that is already chosen. Reporting the press immediately would descend halfway through
    //taking hold of a placement, which is the one gesture that has to reach the placement rather than what
    //is inside it. So it waits: the release decides, because only the release knows whether the pointer
    //moved.
    //
    if (!adding && index >= 0 && selection.chosen.length === 1 && selection.chosen[0] === index) {
        selection.pressedOnChosen = index;
        selection.draggingFrom = point;

        return;
    }

    if (index < 0) {
        //On the background: a drag from here is a rubber band, and a click that goes nowhere clears.
        selection.bandFrom = point;

        return;
    }

    selection.draggingFrom = point;

    notifySelection(index, adding);
}

function notifySelection(index, adding) {
    if (selection.view == null)
        return;

    selection.view.invokeMethodAsync('OnElementSelected', index, adding === true);
}

///
///Marks exactly the shapes C# says are chosen.
///
///Driven from there rather than kept here, because which shapes a rubber band caught is a question about
///the layout and not about the DOM - and two places both believing they know the answer is how a
///selection ends up showing one thing and acting on another.
///
function showSelection(indexes, outlines) {
    //Remembered, because a press has to tell "take hold of what is already chosen" from "choose this".
    if (indexes == null)
        selection.chosen = [];
    else
        selection.chosen = [...indexes];

    for (const node of svg.querySelectorAll('.' + SELECTED_CLASS))
        node.classList.remove(SELECTED_CLASS);

    const existing = document.getElementById(CHOSEN_ID);

    if (existing != null)
        existing.remove();

    if (indexes == null || indexes.length === 0)
        return;

    const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');

    group.setAttribute('id', CHOSEN_ID);

    //Over the layout and under the handles, which are appended after it.
    svg.appendChild(group);

    for (let i = 0; i < indexes.length; i++) {
        //A label is still a node of its own and can simply be marked. Only the geometry moved into the
        //merged paths, and only the geometry needs drawing again.
        const label = svg.querySelector(':scope > text[data-element="' + indexes[i] + '"]');

        if (label != null) {
            label.classList.add(SELECTED_CLASS);

            continue;
        }

        if (outlines == null || outlines[i] == null)
            continue;

        const outline = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');

        outline.setAttribute('class', SELECTED_CLASS);
        outline.setAttribute(ELEMENT_ATTRIBUTE, indexes[i]);
        outline.setAttribute('points', outlines[i]);

        //The shape underneath keeps its own fill; this draws the outline over it and nothing else.
        outline.setAttribute('fill', 'none');

        group.appendChild(outline);
    }
}

///The group the highlights live in, so putting the next selection up is one node removed and one added.
const CHOSEN_ID = 'chosenShapes';

///What a node carries to say which element of the layout it is - a label, or a highlight drawn over one.
const ELEMENT_ATTRIBUTE = 'data-element';

///And what a merged path carries: the elements its subpaths came from, which is how a shape is found in it.
const ELEMENTS_ATTRIBUTE = 'data-elements';

///
///Finishes a drag, and reports how far it went in the layout's coordinates.
///
///Reported rather than applied here: the distance still has to be brought into the cell being edited,
///which is C#'s business - a cell placed at a quarter turn draws a shape sideways, so a drag to the right
///on screen is a drag upwards in the cell, and only the transform knows that.
///
function onSelectRelease(event) {
    if (selection.bandFrom != null) {
        finishBand(event);

        return;
    }

    if (selection.draggingFrom == null)
        return;

    const from = selection.draggingFrom;
    const corner = selection.draggingCorner;
    const to = layoutPointFromEvent(event);

    selection.draggingFrom = null;
    selection.draggingCorner = -1;

    //What was lifted for the preview goes back. C# is about to redraw the whole picture with the edit in
    //it, so this only has to leave the markup the shape it was found in.
    dropLift();

    if (to == null || selection.view == null)
        return;

    const dx = to.x - from.x;
    const dy = to.y - from.y;

    const pressedOnChosen = selection.pressedOnChosen;

    selection.pressedOnChosen = -1;

    //A click is not a drag. Below a unit there is nothing to move anything by, and treating every click
    //as a zero-length move would fill the undo stack with nothing.
    if (Math.abs(dx) < 1 && Math.abs(dy) < 1) {
        //It was a press on what was already chosen, and it did not move: that is the click that goes into
        //the cell. Reported now rather than on the way down, so a drag never descends - see onSelectClick.
        if (pressedOnChosen >= 0)
            notifySelection(pressedOnChosen, false);

        return;
    }

    if (corner >= 0) {
        selection.view.invokeMethodAsync('OnCornerDragged', corner, dx, dy);

        return;
    }

    selection.view.invokeMethodAsync('OnElementDragged', dx, dy);
}

//The rubber band////////////////////////////////

const BAND_ID = 'selectBand';

///
///Where the drag began on the background rather than on a shape, so it is a box rather than a move.
///
///Which shapes it caught is C#'s answer, not this one: the geometry is over there, and asking here would
///mean the DOM deciding something about the layout that the model would then have to be told.
///
///
///A pointer move while the select tool has hold of something: a band being pulled, or a shape being moved.
///
function onSelectMove(event) {
    if (selection.bandFrom != null) {
        onBandMove(event);

        return;
    }

    if (selection.draggingFrom != null)
        onDragMove(event);
}

///
///**The shape follows the pointer.**
///
///It used to sit still until the button came up and then jump to where the pointer had got to, because the
///drag was reported once, on release. What is drawn is Blazor's markup and C# owns it, so moving the shape
///properly would mean rebuilding the whole picture per frame - which at twenty thousand shapes is a third
///of a second each.
///
///So the shapes being moved are lifted out of the picture on the first movement and put in a group of
///their own, and that group is translated. One attribute a frame, no geometry rebuilt, nothing crossing
///into C#. The drop reports the distance exactly as it always did, so it is still one edit and one step on
///the undo stack - and the redraw that follows puts the real shapes back where the preview was.
///
function onDragMove(event) {
    const to = layoutPointFromEvent(event);

    if (to == null || svg == null)
        return;

    const dx = to.x - selection.draggingFrom.x;
    const dy = to.y - selection.draggingFrom.y;

    //A press that has not really moved is still a click - see onSelectRelease - so nothing is lifted for it.
    if (Math.abs(dx) < 1 && Math.abs(dy) < 1)
        return;

    if (document.getElementById(DRAG_ID) == null)
        liftForDrag();

    const lifted = document.getElementById(DRAG_ID);

    if (lifted == null)
        return;

    if (selection.draggingCorner >= 0)
        reshapeLifted(selection.draggingCorner, dx, dy);
    else
        lifted.setAttribute('transform', `translate(${dx} ${dy})`);
}

///The group the shapes being moved live in while the pointer has hold of them.
const DRAG_ID = 'draggingShapes';

///
///Takes the chosen shapes out of the picture and into a group that can be moved on its own.
///
///Out of it rather than over it: leaving them where they are would drag a copy across a stationary
///original, which reads as two shapes rather than one being moved. A layer's path is rewritten once, here,
///to drop the subpaths being lifted - and each lifted shape is redrawn as a path of its own carrying the
///same layer class, so it keeps the fill, the stroke and the opacity it had.
///
function liftForDrag() {
    if (svg == null)
        return;

    const chosen = new Set(selection.chosen);

    if (chosen.size === 0)
        return;

    const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');

    group.setAttribute('id', DRAG_ID);
    group.setAttribute('pointer-events', 'none');

    for (const path of [...svg.querySelectorAll(':scope > path[' + ELEMENTS_ATTRIBUTE + ']')]) {
        const elements = path.getAttribute(ELEMENTS_ATTRIBUTE).trim().split(/\s+/).map(Number);
        const runs = path.getAttribute('d').split('M').filter(run => run.length > 0).map(run => 'M' + run);

        const staying = [];
        const stayingElements = [];
        const going = [];

        for (let i = 0; i < runs.length; i++) {
            if (chosen.has(elements[i])) {
                going.push(runs[i]);
            }
            else {
                staying.push(runs[i]);
                stayingElements.push(elements[i]);
            }
        }

        if (going.length === 0)
            continue;

        const moving = document.createElementNS('http://www.w3.org/2000/svg', 'path');

        moving.setAttribute('class', path.getAttribute('class'));
        moving.setAttribute('fill-rule', 'nonzero');
        moving.setAttribute('d', going.join(' '));

        group.appendChild(moving);

        //What is left of the layer. A layer whose every shape is being moved keeps its node with nothing
        //in it, so the redraw afterwards has the same shape of markup to replace.
        path.setAttribute('d', staying.join(' '));
        path.setAttribute(ELEMENTS_ATTRIBUTE, stayingElements.join(' '));
    }

    //
    //**A label is a node, so the node itself goes in.**
    //
    //The geometry had to be cut out of a layer's path because a shape is a subpath rather than a node of
    //its own; a label never stopped being one. Borrowed rather than copied, and handed back on the drop.
    //
    for (const label of [...svg.querySelectorAll(':scope > text[' + ELEMENT_ATTRIBUTE + ']')]) {
        if (chosen.has(Number(label.getAttribute(ELEMENT_ATTRIBUTE))))
            group.appendChild(label);
    }

    //The highlight travels with what it is highlighting, or it would be left behind on empty ground.
    const highlight = document.getElementById(CHOSEN_ID);

    if (highlight != null)
        group.appendChild(highlight);

    //
    //**And so do the handles, for a drag that moves the whole shape.**
    //
    //They are the corners of the thing being moved. Leaving them behind draws the shape in one place and
    //its corners in another, which reads as the shape not having moved at all.
    //
    //Not for a corner drag: there exactly one corner is going anywhere, and carrying the group would move
    //the ones that are staying put. reshapeLifted moves that one.
    //
    if (selection.draggingCorner < 0) {
        const handles = document.getElementById(HANDLES_ID);

        if (handles != null)
            group.appendChild(handles);
    }

    svg.appendChild(group);
}

///
///Moves one corner of the lifted shape, for a handle being dragged.
///
///The whole shape cannot simply be translated here: a corner drag reshapes, so the preview has to reshape
///too or it would say the wrong thing about what the release is going to do.
///
function reshapeLifted(corner, dx, dy) {
    const lifted = document.getElementById(DRAG_ID);

    if (lifted == null)
        return;

    const moving = cornersMovingWith(corner);

    for (const shape of lifted.querySelectorAll('path, polygon')) {
        let original = shape.dataset.originalGeometry;

        if (original == null && shape.tagName === 'polygon')
            original = shape.getAttribute('points');
        else if (original == null)
            original = shape.getAttribute('d');

        shape.dataset.originalGeometry = original;

        //Every coordinate pair, with the ones being dragged shifted. The geometry drawn for a shape can
        //hold more corners than the file does - a path draws an outline around a centerline - so a handle
        //that is past the end of what is drawn simply moves nothing here, and the release still reports it.
        let at = -1;

        const moved = original.replace(/(-?[\d.]+),(-?[\d.]+)/g, (whole, x, y) => {
            at++;

            if (!moving.includes(at))
                return whole;

            return `${Number(x) + dx},${Number(y) + dy}`;
        });

        if (shape.tagName === 'polygon')
            shape.setAttribute('points', moved);
        else
            shape.setAttribute('d', moved);
    }

    dragHandle(moving, dx, dy);
}

///
///Every corner that a drag of this one moves.
///
///**A ring's opening corner is written twice.** GDSII closes a boundary by repeating its first point at
///the end, so those two are one corner stored as two - and dragging either has to move both, or the
///outline opens into a hook. `MoveVertex` applies exactly that rule to the file; the preview applies it
///here, or it draws a shape the release is not going to make.
///
///It is also the reason a dot looked stuck. The two sit exactly on top of each other, the press takes the
///one on top, and moving only that leaves its twin behind on the corner you thought you were dragging.
///
///Asked of where the handles *started*, not where they are: one of the pair may already have been moved by
///an earlier frame of this same drag, and asking then answers no. Same trap MoveVertex documents.
///
function cornersMovingWith(corner) {
    const handles = [...document.querySelectorAll('#' + HANDLES_ID + ' [data-corner]')];

    if (handles.length < 2 || (corner !== 0 && corner !== handles.length - 1))
        return [corner];

    if (startedAt(handles[0]) !== startedAt(handles[handles.length - 1]))
        return [corner];

    return [0, handles.length - 1];
}

///Where a handle was before this drag began, as one comparable string.
function startedAt(handle) {
    const x = handle.dataset.originalX ?? handle.getAttribute('cx');
    const y = handle.dataset.originalY ?? handle.getAttribute('cy');

    return x + ',' + y;
}

///
///Takes the handles being pulled along with the corners they are handles for.
///
///Those only. The rest are corners of the same shape that are not going anywhere, and moving them would
///say the shape was being translated when it is being reshaped.
///
///Measured from where each started rather than from where it is, like the geometry: every frame's offset
///is from the press, and adding each step to the last would drift.
///
function dragHandle(corners, dx, dy) {
    for (const corner of corners) {
        const handle = document.querySelector('#' + HANDLES_ID + ' [data-corner="' + corner + '"]');

        if (handle == null)
            continue;

        const fromX = handle.dataset.originalX ?? handle.getAttribute('cx');
        const fromY = handle.dataset.originalY ?? handle.getAttribute('cy');

        handle.dataset.originalX = fromX;
        handle.dataset.originalY = fromY;

        handle.setAttribute('cx', Number(fromX) + dx);
        handle.setAttribute('cy', Number(fromY) + dy);
    }
}

///Puts the picture back to one node a layer. The redraw that follows a drop draws the real thing again.
function dropLift() {
    const lifted = document.getElementById(DRAG_ID);

    if (lifted == null)
        return;

    //Everything borrowed goes back before the group does, or removing the group would take it with it. The
    //layers' own paths are not put back the same way: what was cut out of them is a run of text in a `d`
    //rather than a node, and the redraw that follows a drop writes all of it again anyway.
    if (svg != null) {
        for (const label of [...lifted.querySelectorAll('text[' + ELEMENT_ATTRIBUTE + ']')])
            svg.appendChild(label);

        const highlight = document.getElementById(CHOSEN_ID);

        if (highlight != null)
            svg.appendChild(highlight);

        const handles = document.getElementById(HANDLES_ID);

        if (handles != null)
            svg.appendChild(handles);
    }

    lifted.remove();
}

function onBandMove(event) {
    if (selection.bandFrom == null)
        return;

    const to = layoutPointFromEvent(event);

    if (to == null || svg == null)
        return;

    let band = document.getElementById(BAND_ID);

    if (band == null) {
        band = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        band.setAttribute('id', BAND_ID);
        band.setAttribute('pointer-events', 'none');
        band.setAttribute('fill', 'rgba(71, 129, 255, 0.15)');
        band.setAttribute('stroke', '#4781ff');
        band.setAttribute('stroke-dasharray', '5 4');
        band.setAttribute('vector-effect', 'non-scaling-stroke');
        band.setAttribute('stroke-width', '1.5');

        svg.appendChild(band);
    }

    band.setAttribute('x', Math.min(selection.bandFrom.x, to.x));
    band.setAttribute('y', Math.min(selection.bandFrom.y, to.y));
    band.setAttribute('width', Math.abs(to.x - selection.bandFrom.x));
    band.setAttribute('height', Math.abs(to.y - selection.bandFrom.y));
}

function finishBand(event) {
    const from = selection.bandFrom;
    const to = layoutPointFromEvent(event);

    selection.bandFrom = null;

    clearBand();

    if (to == null || selection.view == null)
        return;

    const adding = event.ctrlKey === true || event.metaKey === true || event.shiftKey === true;

    //A click on the background, rather than a box dragged across it, clears - which is what every other
    //tool does with a click on nothing and so what is expected here.
    if (Math.abs(to.x - from.x) < 1 && Math.abs(to.y - from.y) < 1) {
        if (!adding)
            notifySelection(-1, false);

        return;
    }

    selection.view.invokeMethodAsync('OnBandSelected', from.x, from.y, to.x, to.y, adding);
}

function clearBand() {
    const band = document.getElementById(BAND_ID);

    if (band != null)
        band.remove();
}

//Retyping a label where it is/////////////////////

///
///Which view to tell about a label being retyped.
///
///Its own handle rather than the selection's or the drawing's, because retyping is neither: a label is
///double-clicked with whatever tool is in hand, including none of them.
///
let retyping = { view: null };

function startRetyping(view) {
    retyping.view = view;

    //Hung on the document rather than on the box, which does not exist yet and is replaced whenever the
    //picture is rebuilt. Removed first because this runs on every visit to the view.
    document.removeEventListener('pointerdown', onDismissRetype);
    document.addEventListener('pointerdown', onDismissRetype);
}

///
///A press anywhere but the box keeps what is in it and closes it.
///
///**This is what blur used to do, and blur was wrong.** The picture is rebuilt on every edit, and a rebuild
///takes the box's element with it - which fires a blur nobody asked for. Committing on that closed the box
///a few milliseconds after it opened, every time, with the caret still in it.
///
///A pointer going down somewhere else is a person deciding to stop, which is the thing that was meant.
///
function onDismissRetype(event) {
    if (retyping.view == null)
        return;

    const box = document.getElementById(RETYPE_BOX_ID);

    if (box == null || event.target === box)
        return;

    retyping.view.invokeMethodAsync('OnRetypeDismissed');
}

const RETYPE_BOX_ID = 'canvasLabelText';

//The bar's menus/////////////////////////////////

///
///The panels that hang off a button in the toolbar and go away on a press somewhere else.
///
///One table rather than a pair of near-identical handlers: they differ only in which element is the panel,
///which is the button that opens it, and what to call - and a second copy of the logic is a second place
///for "is this press outside?" to be got subtly wrong.
///
///
///Each entry is a panel, the button that opens it, and who to tell when it should shut.
///
///Registered rather than listed, because they do not all belong to the same component: the shapes and the
///grid are the 2D view's, and the view picker is the shell's - and a table written here would have to name
///a handle that only one of them can hold.
///
let barMenus = [];

///
///Hangs one up. The listener goes on once, however many register.
///
///One listener rather than one per opening, for the same reason the retype box has one: a listener added
///when a panel opens has to be taken away when it closes, by every path that closes it - and the one that
///gets missed leaves a handler running over a panel that is no longer there.
///
function registerBarMenu(view, menu, opener, closed) {
    //Replaced rather than added twice, so a component that renders again does not stack up handles.
    barMenus = barMenus.filter(one => one.menu !== menu);

    barMenus.push({ view: view, menu: menu, opener: opener, closed: closed });

    if (barMenus.length === 1)
        document.addEventListener('pointerdown', onDismissBarMenus);
}

///
///A press anywhere but a panel or the button that opens it puts that panel away.
///
///The opener is not "outside": pressing it is how the panel is asked for, and having that close the one the
///same press opens would make it impossible to reach with the panel already up.
///
///Nothing is prevented here. These sit over the toolbar rather than over the layout, so a press meant for
///the canvas is a press on the canvas - it does what it was going to do, and closes these on the way past,
///which is what dismissing on a click means.
///
function onDismissBarMenus(event) {
    for (const one of barMenus) {
        const menu = document.getElementById(one.menu);

        if (menu == null || menu.contains(event.target))
            continue;

        const opener = document.getElementById(one.opener);

        if (opener != null && opener.contains(event.target))
            continue;

        one.view.invokeMethodAsync(one.closed);
    }
}

///
///Keeps the keyboard in the box across a rebuild of the picture.
///
///Only when it has been lost, and without selecting: re-selecting on every redraw would wipe out what was
///being typed the moment anything else on the page changed.
///
function keepFocusIn(box) {
    if (box == null || document.activeElement === box)
        return;

    box.focus();
}

///
///A double-click on a label asks the view to open a box over it.
///
///Double rather than single, because a single click has to go on meaning "choose this", which is what
///moving one and deleting one both start with. A label placed by the Draw tool opens its box without
///being asked, so the common case is still click and type.
///
function retypeLabelUnder(event) {
    if (retyping.view == null || event == null)
        return;

    //
    //**What is under the pointer, not what the event named.**
    //
    //The first click of the pair chooses the label, and choosing anything rebuilds the picture - so the
    //text node the second click lands on is a different node from the one the first did. When a
    //double-click's two clicks have different targets the browser dispatches it on their common ancestor,
    //which here is the whole `<svg>`. The event's own target is the one thing it cannot be.
    //
    for (const node of document.elementsFromPoint(event.clientX, event.clientY)) {
        if (node.closest == null)
            continue;

        const label = node.closest('text[' + ELEMENT_ATTRIBUTE + ']');

        if (label == null)
            continue;

        retyping.view.invokeMethodAsync('OnLabelRetyped', Number(label.getAttribute(ELEMENT_ATTRIBUTE)));

        return;
    }
}

///
///Where a label is drawn, in pixels from the corner of the view rather than from the corner of the window.
///
///From the view, because the box that opens over it is positioned inside `#svgWrapper` - and the wrapper
///is not at the window's origin once there is anything above or beside it.
///
///Null when there is no such label, which is the honest answer while the picture is being rebuilt.
///
async function labelBox(index) {
    const wrapper = document.getElementById('svgWrapper');

    if (wrapper == null)
        return null;

    //
    //**Waits for the label to be drawn.**
    //
    //This is asked straight after the edit that made it, and the markup is Blazor's - it goes into the page
    //on a schedule of its own. Waiting here rather than in C# keeps the whole gesture inside one event,
    //where StateHasChanged is reliable; asking from a render callback instead put the answer in a
    //continuation whose re-render was sometimes never flushed, and the box simply never appeared.
    //
    //Frames rather than a fixed delay, so it costs one frame in the ordinary case and still finds the label
    //on a layout big enough to take several.
    //
    for (let tries = 0; tries < 30; tries++) {
        const label = document.querySelector('#gdsSVG text[' + ELEMENT_ATTRIBUTE + '="' + index + '"]');

        if (label != null) {
            const box = label.getBoundingClientRect();
            const frame = wrapper.getBoundingClientRect();

            return {
                x: box.x - frame.x,
                y: box.y - frame.y,
                width: box.width,
                height: box.height
            };
        }

        await new Promise(resolve => requestAnimationFrame(resolve));
    }

    return null;
}

///
///Gives a box the keyboard with everything in it selected.
///
///Selected rather than merely focused, because a label goes down already saying "label" - so the first
///keystroke has to replace that rather than append to it. ElementReference.FocusAsync can do the focus
///half and not this half.
///
function selectAllIn(box) {
    if (box == null)
        return;

    box.focus();

    if (box.select != null)
        box.select();
}

//Vertex handles/////////////////////////////////

const HANDLES_ID = 'vertexHandles';

///
///Puts a grab handle on every corner of the chosen shape.
///
///Called from C# once it has decided the shape is one this cell may change - the corners come over
///already in the layout's coordinates, because the view draws in those and the cell's own would need the
///placement applied to each of them here.
///
///Sized against the current viewBox, like the ruler, so a handle stays the same size on screen at any
///zoom rather than growing into the shape it is a handle for.
///
function showVertexHandles(corners) {
    clearVertexHandles();

    if (svg == null || corners == null || corners.length === 0)
        return;

    const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    group.setAttribute('id', HANDLES_ID);

    const radius = (viewBox.width / 1000) * 7;

    for (let i = 0; i < corners.length; i++) {
        const handle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');

        handle.setAttribute('class', 'vertexHandle');
        handle.setAttribute('data-corner', i);
        handle.setAttribute('cx', corners[i].x);
        handle.setAttribute('cy', corners[i].y);
        handle.setAttribute('r', radius);

        group.appendChild(handle);
    }

    svg.appendChild(group);
}

function clearVertexHandles() {
    const group = document.getElementById(HANDLES_ID);

    if (group != null)
        group.remove();
}

//Drawing////////////////////////////////////////

let drawing = {
    active: false,
    view: null,

    //'rect' and 'ellipse' are dragged out corner to corner, 'poly' and 'path' are clicked point by point,
    //and 'text' is one click.
    mode: 'rect',

    //Where a drag began.
    from: null,

    //The corners of a polygon so far, in the layout's coordinates.
    points: [],

    //Where the pointer is, for the shape that follows it.
    at: null,

    //How many sides stand in for an ellipse. A layout format has no curves; see viewGeometry.
    segments: 64,

    //Whether the modifier that squares an ellipse off into a circle is down.
    circle: false,

    //How wide a path is drawn, in the layout's own units. Drawn at that width rather than as a hairline,
    //because the width is a real dimension of the thing being made and is the whole reason to draw a path
    //instead of a polygon - a preview that ignored it would be a preview of a different shape.
    width: 0
};

const DRAW_ID = 'drawPreview';

function startDrawing(view, mode, segments, width) {
    drawing.active = true;
    drawing.view = view;

    setDrawMode(mode, segments, width);

    if (svg != null)
        svg.style.cursor = 'crosshair';
}

///Switches between the shapes, dropping whatever was half-drawn in the one before.
function setDrawMode(mode, segments, width) {
    if (mode === 'poly' || mode === 'ellipse' || mode === 'text' || mode === 'path')
        drawing.mode = mode;
    else
        drawing.mode = 'rect';

    if (segments > 0)
        drawing.segments = segments;

    if (width >= 0)
        drawing.width = width;

    clearDrawing();
}

///
///How wide the path being drawn is, changed while one is half drawn.
///
///Kept apart from setDrawMode because that clears whatever is half drawn: typing a width four corners into a
///route should widen the route, not throw it away and start again.
///
function setPathWidth(width) {
    if (width >= 0)
        drawing.width = width;

    if (drawing.active && drawing.mode === 'path')
        drawPreview();
}

function stopDrawing() {
    drawing.active = false;
    drawing.view = null;

    clearDrawing();

    if (svg != null)
        svg.style.cursor = '';
}

function clearDrawing() {
    drawing.from = null;
    drawing.points = [];
    drawing.at = null;
    drawing.circle = false;

    const preview = document.getElementById(DRAW_ID);

    if (preview != null)
        preview.remove();
}

function onDrawDown(event) {
    if (drawing.mode === 'poly' || drawing.mode === 'path') {
        onOutlineClick(event);

        return;
    }

    //A label is one click, and where it goes is all C# needs - what it says is typed afterwards, into a box
    //that opens over the label once it has been drawn. See OnLabelPlaced.
    if (drawing.mode === 'text') {
        const point = layoutPointFromEvent(event);

        if (point != null && drawing.view != null)
            drawing.view.invokeMethodAsync('OnLabelPlaced', point.x, point.y);

        return;
    }

    drawing.from = layoutPointFromEvent(event);
}

function onDrawMove(event) {
    //Nothing follows the pointer for a label: it lands where it is clicked, at a size this view fixes.
    if (drawing.mode === 'text')
        return;

    if (drawing.mode === 'poly' || drawing.mode === 'path') {
        if (drawing.points.length === 0)
            return;

        drawing.at = layoutPointFromEvent(event);
        drawPreview();

        return;
    }

    if (drawing.from == null)
        return;

    drawing.at = layoutPointFromEvent(event);
    drawing.circle = event.shiftKey === true;

    drawPreview();
}

///
///Finishes a rectangle and hands its two opposite corners over in the layout's coordinates.
///
///Two corners rather than four: the shape is axis-aligned in the *layout*, and turning that into an
///outline in a cell that may be rotated is C#'s job - four corners built here would be four corners in
///the wrong space.
///
function onDrawUp(event) {
    //A polygon, a path and a label are clicked rather than dragged, so releasing the button ends nothing.
    if (drawing.mode === 'poly' || drawing.mode === 'path' || drawing.mode === 'text')
        return;

    if (drawing.from == null)
        return;

    const from = drawing.from;
    const to = layoutPointFromEvent(event);
    const circle = event.shiftKey === true;
    const segments = drawing.segments;
    const ellipse = drawing.mode === 'ellipse';

    clearDrawing();

    if (to == null || drawing.view == null)
        return;

    //
    //**A click fills the square it landed in.**
    //
    //Dragging a rectangle out is the way to draw one of any size, and it stays that way - but the common
    //thing on a grid is one square, and dragging across a single cell is a fiddly way to ask for it. A
    //click asked for nothing at all before: below a unit of travel there is no rectangle, and the gesture
    //was dropped.
    //
    //From the *unsnapped* point, because a snapped one sits on a corner shared by four squares and cannot
    //say which was meant. Only with a grid to land on - without one there is no square to fill and a click
    //still means nothing.
    //
    if (Math.abs(to.x - from.x) < 1 || Math.abs(to.y - from.y) < 1) {
        const square = clickedSquare(event);

        if (square != null && !ellipse)
            drawing.view.invokeMethodAsync('OnRectangleDrawn', square.x, square.y, square.x + grid.pitch, square.y + grid.pitch);

        return;
    }

    if (ellipse) {
        //Handed over as the corners it came out as, down the same path a clicked polygon takes. The shape
        //on screen and the shape in the file are then the same points rather than two answers worked out
        //twice from the same box.
        handOver(window.viewGeometry.ellipseCorners(from, to, segments, circle));

        return;
    }

    drawing.view.invokeMethodAsync('OnRectangleDrawn', from.x, from.y, to.x, to.y);
}

///
///The corner of the grid square the pointer is inside, or null when there is no grid.
///
///Floored rather than rounded: rounding gives the nearest crossing, and a crossing is the corner of four
///squares. Which one was clicked is decided by which side of each line the pointer fell, which is what
///flooring answers.
///
function clickedSquare(event) {
    //Only while snapping to the grid. That is the mode where a square is a thing the layout is being worked
    //to, and it is what makes the size of what appears predictable - with the grid off there is no cell to
    //mean, and a click that dropped a pitch-sized shape somewhere would be a surprise rather than a
    //shortcut. See the drawing spec, which still holds that a click draws nothing without it.
    if (!grid.snap || grid.pitch <= 0)
        return null;

    const point = rawLayoutPoint(event);

    if (point == null)
        return null;

    return {
        x: Math.floor(point.x / grid.pitch) * grid.pitch,
        y: Math.floor(point.y / grid.pitch) * grid.pitch
    };
}

///
///One point of an outline, or the click that ends it.
///
///**A polygon closes on its first corner and a path ends on its last.** A ring and an open run are
///finished by different gestures because they are different shapes: clicking back onto the start of a wire
///means a wire that goes back where it came from, which is a route somebody may well want. Enter and a
///double-click end either; between the three, somebody who has never used this before will find one.
///
function onOutlineClick(event) {
    const point = layoutPointFromEvent(event);

    if (point == null)
        return;

    //Near enough to mean it, measured on screen rather than in the layout - the same number of pixels is
    //the same gesture at any zoom.
    if (drawing.mode === 'path') {
        if (drawing.points.length >= 2 && within(point, drawing.points[drawing.points.length - 1])) {
            finishShape();

            return;
        }
    }
    else if (drawing.points.length >= 3 && within(point, drawing.points[0])) {
        finishShape();

        return;
    }

    //A corner on top of the one before it is a zero-length edge, which is what the second half of a
    //double-click would otherwise leave behind.
    if (drawing.points.length > 0 && within(point, drawing.points[drawing.points.length - 1]))
        return;

    drawing.points.push(point);
    drawing.at = point;

    drawPreview();
}

///How close two points are on screen, in layout units - the handle radius, which is what looks like "on it".
function within(a, b) {
    const near = unitsPerPixel() * 9;

    return Math.abs(a.x - b.x) <= near && Math.abs(a.y - b.y) <= near;
}

///
///The keys a half-drawn outline answers to, which are only live while one is being clicked out.
///
///Taken before anything else the keyboard does, because all three are keys the editor uses for something
///else: Escape lets go of a selection, Backspace deletes one, and both would be the wrong thing to do to
///somebody who is four corners into an outline.
///
function onOutlineKey(event) {
    if (event.key === 'Enter') {
        event.preventDefault();
        finishShape();

        return true;
    }

    if (event.key === 'Escape') {
        event.preventDefault();
        clearDrawing();

        return true;
    }

    if (event.key === 'Backspace') {
        //One corner at a time, which is the only way to recover from a misplaced click without starting
        //the whole outline again.
        event.preventDefault();
        drawing.points.pop();
        drawPreview();

        return true;
    }

    return false;
}

function finishShape() {
    const points = drawing.points;
    const path = drawing.mode === 'path';

    clearDrawing();

    handOver(points, path);
}

///
///Hands an outline over, in the layout's coordinates, as one flat run of x and y.
///
///**Two points is a path and three is a polygon**, which is the one place the two differ on the way over: a
///run between two points is a perfectly good wire, and an outline through two corners has no area.
///
function handOver(points, path) {
    let fewest = 3;

    if (path === true)
        fewest = 2;

    if (points == null || points.length < fewest || drawing.view == null)
        return;

    const flat = [];

    for (const point of points) {
        flat.push(point.x);
        flat.push(point.y);
    }

    if (path === true) {
        drawing.view.invokeMethodAsync('OnPathDrawn', flat);

        return;
    }

    drawing.view.invokeMethodAsync('OnPolygonDrawn', flat);
}

///
///What the shape being drawn looks like so far.
///
///One element rather than two, swapped when the tool changes: a rectangle is a rect and a polygon is a
///polygon, and leaving the other one in the document would leave the last thing drawn on screen.
///
function drawPreview() {
    if (svg == null)
        return;

    if (drawing.mode === 'poly' || drawing.mode === 'path')
        drawPolygonPreview();
    else if (drawing.mode === 'ellipse')
        drawEllipsePreview();
    else
        drawRectanglePreview();
}

///
///The polygon that is about to be added, not an SVG ellipse standing in for it.
///
///A real <ellipse> would be smoother than the shape that lands in the file, which at a dozen sides is the
///difference between a circle and something visibly not one - and the whole reason the side count is a
///control is so somebody can see what they are choosing.
///
function drawEllipsePreview() {
    if (drawing.from == null || drawing.at == null)
        return;

    const corners = window.viewGeometry.ellipseCorners(drawing.from, drawing.at, drawing.segments, drawing.circle);

    const preview = previewNode('polygon');

    preview.setAttribute('points', corners.map(corner => `${corner.x},${corner.y}`).join(' '));
}

function drawRectanglePreview() {
    if (drawing.from == null || drawing.at == null)
        return;

    const preview = previewNode('rect');

    preview.setAttribute('x', Math.min(drawing.from.x, drawing.at.x));
    preview.setAttribute('y', Math.min(drawing.from.y, drawing.at.y));
    preview.setAttribute('width', Math.abs(drawing.at.x - drawing.from.x));
    preview.setAttribute('height', Math.abs(drawing.at.y - drawing.from.y));
}

function drawPolygonPreview() {
    if (drawing.points.length === 0) {
        const stale = document.getElementById(DRAW_ID);

        if (stale != null)
            stale.remove();

        return;
    }

    const group = previewNode('g');

    while (group.firstChild != null)
        group.removeChild(group.firstChild);

    //The corners placed so far, plus wherever the pointer is - so the outline shows the shape it would
    //close into rather than only the edges already fixed.
    const corners = drawing.points.slice();

    if (drawing.at != null)
        corners.push(drawing.at);

    const path = drawing.mode === 'path';

    let kind = 'polygon';

    if (path)
        kind = 'polyline';

    const outline = document.createElementNS('http://www.w3.org/2000/svg', kind);
    outline.setAttribute('points', corners.map(point => `${point.x},${point.y}`).join(' '));

    if (path) {
        //**At its real width**, in the layout's own units, which is what makes this a preview of the thing
        //being drawn rather than of the line down the middle of it. A width of zero has no thickness to
        //show, so it falls back to a hairline - which is also how a reader draws one.
        outline.setAttribute('fill', 'none');
        outline.setAttribute('stroke', 'rgba(216, 27, 96, 0.55)');
        outline.setAttribute('stroke-linejoin', 'round');

        if (drawing.width > 0) {
            outline.setAttribute('stroke-width', drawing.width);
        }
        else {
            outline.setAttribute('vector-effect', 'non-scaling-stroke');
            outline.setAttribute('stroke-width', '2');
        }
    }
    else {
        outline.setAttribute('fill', 'rgba(216, 27, 96, 0.25)');
        outline.setAttribute('stroke', '#d81b60');
        outline.setAttribute('vector-effect', 'non-scaling-stroke');
        outline.setAttribute('stroke-width', '2');
    }

    group.appendChild(outline);

    //A handle on every corner, the first larger - because clicking it is what closes the ring, and a
    //target somebody cannot see is a gesture they will not find.
    const radius = unitsPerPixel() * 4;

    for (let i = 0; i < drawing.points.length; i++) {
        const handle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');

        handle.setAttribute('cx', drawing.points[i].x);
        handle.setAttribute('cy', drawing.points[i].y);
        handle.setAttribute('fill', '#d81b60');

        //The larger handle marks the point that ends the shape when it is clicked again: the first corner
        //for a ring, the last one placed for an open run.
        let ending = i === 0;

        if (drawing.mode === 'path')
            ending = i === drawing.points.length - 1;

        if (ending)
            handle.setAttribute('r', radius * 1.8);
        else
            handle.setAttribute('r', radius);

        group.appendChild(handle);
    }
}

//Carrying a cell/////////////////////////////////

const CARRY_ID = 'carriedCell';

///
///A cell picked up out of the library and following the pointer until it is put down.
///
///**The drawing is built once, in C#, and moved here.** A cell is however many hundred shapes, and rebuilding
///that markup through the component on every pointer move is a round trip and a re-render per pixel. What
///moves instead is a transform on the group holding it, which is one attribute - the same trick the drag
///already uses to move the chosen shapes without redrawing them.
///
///Turning is the same attribute. A quarter turn is a rotate() in front of the translate rather than new
///geometry, and the count is handed to C# when the cell lands so the record written says the same thing the
///picture said.
///
let carrying = {
    active: false,
    view: null,

    ///<summary>Quarter turns to the right, which is the way this view draws them - see turnChosen.</summary>
    quarters: 0,

    mirrored: false,

    ///Where the pointer is, in the layout's coordinates.
    at: null,

    ///
    ///The cell's markup, kept so it can be put back.
    ///
    ///Blazor owns what is inside the SVG and replaces it wholesale on every render, which takes anything
    ///appended here with it - the grid is put back after each render for the same reason. Holding the markup
    ///means putting it back costs nothing but a string this side of the wire, rather than a round trip to
    ///the component for a cell it already sent once.
    ///
    markup: '',

    ///
    ///The middle of the cell's own shapes, which is what the pointer holds it by.
    ///
    ///**Not its origin.** A cell's origin is wherever the file says, and grouping shapes into one keeps
    ///their coordinates - so a cell made out of something drawn two thousand units from the origin has an
    ///origin two thousand units from anything in it. Carried by that point, the cell hangs off the side of
    ///the screen while the cursor holds an empty patch of nothing.
    ///
    middle: { x: 0, y: 0 }
};

///
///Picks a cell up. The markup is the cell drawn by SvgWriter, so it carries the layers' own colors.
///
///Not a ghost outline: what is being placed is the cell, and the thing that says which cell is its own
///shapes on their own layers. An outline would say a rectangle of about this size is coming.
///
function startCarrying(view, markup, middleX, middleY) {
    carrying.active = true;
    carrying.view = view;
    carrying.quarters = 0;
    carrying.mirrored = false;
    carrying.at = null;
    carrying.markup = markup;
    carrying.middle = { x: middleX, y: middleY };

    carryNode().innerHTML = markup;

    if (svg != null)
        svg.style.cursor = 'copy';
}

///
///Puts the carried cell back into the picture after a render has taken it out.
///
///Called after every one, like the grid, and for the same reason: what is inside the SVG belongs to Blazor
///and is replaced whole. Cheap when there is nothing to do, which is almost always - one lookup.
///
function restoreCarried() {
    if (!carrying.active || svg == null)
        return;

    if (document.getElementById(CARRY_ID) != null)
        return;

    carryNode().innerHTML = carrying.markup;

    placeCarried();
}

function stopCarrying() {
    carrying.active = false;
    carrying.view = null;
    carrying.at = null;
    carrying.markup = '';

    const carried = document.getElementById(CARRY_ID);

    if (carried != null)
        carried.remove();

    if (svg != null)
        svg.style.cursor = '';
}

///A quarter to the right, on whatever is being carried. Ctrl+R, and nothing at all when nothing is.
function turnCarried() {
    if (!carrying.active)
        return;

    carrying.quarters = (carrying.quarters + 1) % 4;

    placeCarried();
}

function mirrorCarried() {
    if (!carrying.active)
        return;

    carrying.mirrored = !carrying.mirrored;

    placeCarried();
}

function onCarryMove(event) {
    carrying.at = layoutPointFromEvent(event);

    placeCarried();
}

///Puts it down where it is, and tells C# where that was and which way up.
function onCarryDown(event) {
    const point = layoutPointFromEvent(event);

    if (point == null || carrying.view == null)
        return;

    const origin = carriedOriginAt(point);

    carrying.view.invokeMethodAsync('OnCellDropped', origin.x, origin.y, carrying.quarters * 90, carrying.mirrored);
}

///
///The transform that moves and turns it.
///
///Read right to left, which is the order SVG applies them: the cell is mirrored about its own origin, then
///turned about it, and only then moved to the pointer. Any other order turns it about the pointer instead,
///which swings the cell around the cursor rather than spinning it on the spot.
///
function placeCarried() {
    const group = document.getElementById(CARRY_ID);

    if (group == null || carrying.at == null)
        return;

    let transform = 'translate(' + carrying.at.x + ' ' + carrying.at.y + ')';

    if (carrying.quarters !== 0)
        transform += ' rotate(' + (carrying.quarters * 90) + ')';

    //Top to bottom, which is what the format's reflection is - see Hierarchy.PlacementRecords. Before the
    //turn, in the order the format applies them, which is why it is written after it here.
    if (carrying.mirrored)
        transform += ' scale(1 -1)';

    //And last of all, which means first: the cell is moved so that its middle is the point everything else
    //happens about. Held by the middle, it turns on the spot under the cursor.
    transform += ' translate(' + (-carrying.middle.x) + ' ' + (-carrying.middle.y) + ')';

    group.setAttribute('transform', transform);
}

///
///Where the cell's *origin* ends up, which is what a placement record stores.
///
///The picture is held by the middle; the file is written from the origin. Both are the same transform, so
///this applies it to (0, 0) rather than working the offset out a second way - the two disagreeing is how a
///cell comes to be drawn in one place and written in another.
///
function carriedOriginAt(point) {
    let x = -carrying.middle.x;
    let y = -carrying.middle.y;

    if (carrying.mirrored)
        y = -y;

    //A quarter to the right is (x, y) to (-y, x), which is what SVG's rotate does in a Y-down space.
    for (let turn = 0; turn < (carrying.quarters % 4); turn++) {
        const across = -y;

        y = x;
        x = across;
    }

    return { x: point.x + x, y: point.y + y };
}

///
///The keys that belong to the thing in hand, and whether this was one of them.
///
///Ctrl+R for a quarter turn, which is the pair every layout editor uses - and Ctrl rather than a bare R
///because R alone is a letter, and the single letters here are the tools.
///
///Escape puts it back. Answered here rather than through OnShortcut so it cannot also clear the selection
///on the way past: putting down what you are carrying and throwing away what was chosen are two things, and
///one press should not be both.
///
function onCarryKey(event) {
    const held = event.ctrlKey === true || event.metaKey === true;

    if (held && event.key.toLowerCase() === 'r') {
        event.preventDefault();

        turnCarried();

        return true;
    }

    if (held && event.key.toLowerCase() === 'm') {
        event.preventDefault();

        mirrorCarried();

        return true;
    }

    if (event.key === 'Escape') {
        event.preventDefault();

        //Held before stopping, which clears it: C# has to be told that the cell was put back, or the panel
        //goes on saying one is in hand.
        const view = carrying.view;

        stopCarrying();

        if (view != null)
            view.invokeMethodAsync('OnCarryStopped');

        return true;
    }

    return false;
}

function carryNode() {
    let group = document.getElementById(CARRY_ID);

    if (group != null)
        return group;

    group = document.createElementNS('http://www.w3.org/2000/svg', 'g');

    group.setAttribute('id', CARRY_ID);
    group.setAttribute('pointer-events', 'none');
    group.setAttribute('opacity', '0.7');

    svg.appendChild(group);

    return group;
}

///The preview element of the right kind, replacing one of the wrong kind if that is what is there.
function previewNode(kind) {
    let preview = document.getElementById(DRAW_ID);

    if (preview != null && preview.tagName === kind)
        return preview;

    if (preview != null)
        preview.remove();

    preview = document.createElementNS('http://www.w3.org/2000/svg', kind);
    preview.setAttribute('id', DRAW_ID);
    preview.setAttribute('pointer-events', 'none');

    //A group is the polygon tool's, which styles the outline it holds rather than itself.
    if (kind !== 'g') {
        preview.setAttribute('fill', 'rgba(216, 27, 96, 0.25)');
        preview.setAttribute('stroke', '#d81b60');
        preview.setAttribute('vector-effect', 'non-scaling-stroke');
        preview.setAttribute('stroke-width', '2');
    }

    svg.appendChild(preview);

    return preview;
}

//The keyboard///////////////////////////////////

//What the shortcuts report back through. One handle for the whole view rather than one per tool, because
//the keyboard is not a tool - it is another way to press the buttons that are already there.
let keys = {
    view: null
};

///
///Every shortcut the 2D editor answers to.
///
///**Nothing happens while something is being typed into.** There is a box for what a label says, one for the
///grid pitch and four for an array, and a "d" typed into any of them must be a letter rather than the Draw
///tool. Checked against whatever has focus rather than against a list of ids, so a box added later is
///covered without anybody remembering to come back here.
///
///**And nothing happens once the view has gone.** This listener is hung on the window so that the SVG - which
///cannot take focus - can still have a keyboard, and the window outlives the view. Without the check, Ctrl+Z
///in the text editor would undo a shape instead of a line of typing.
///
function onEditorKey(event) {
    if (document.getElementById('gdsSVG') == null || keys.view == null)
        return;

    if (isTyping(document.activeElement))
        return;

    //
    //**Escape abandons whatever is being drawn, whichever shape it is.**
    //
    //It used to reach only a half-clicked outline, because Enter and Backspace only mean anything to one
    //of those and all three were gated together. But a rectangle dragged out is just as much a thing in
    //progress, and the way out of it was to finish it and undo - which is two steps and leaves a shape in
    //the file in between.
    //
    //clearDrawing forgets where the drag began, and the release checks for that before drawing anything,
    //so letting go afterwards adds nothing.
    //
    if (drawing.active && event.key === 'Escape') {
        event.preventDefault();
        clearDrawing();

        return;
    }

    //Enter and Backspace belong to a half-drawn outline, which is the only shape that has corners so far.
    if (drawing.active && (drawing.mode === 'poly' || drawing.mode === 'path') && onOutlineKey(event))
        return;

    //Before the nudge and before the shortcuts: while a cell is being carried these keys are about the thing
    //in hand rather than about whatever is chosen underneath it.
    if (carrying.active && onCarryKey(event))
        return;

    if (nudge(event))
        return;

    const what = shortcutFor(event);

    if (what == null)
        return;

    event.preventDefault();

    keys.view.invokeMethodAsync('OnShortcut', what);
}

///Whether the keyboard belongs to something being written in rather than to the layout.
function isTyping(element) {
    if (element == null)
        return false;

    if (element.isContentEditable)
        return true;

    return ['INPUT', 'TEXTAREA', 'SELECT'].includes(element.tagName);
}

///
///An arrow key, as a distance in the layout's coordinates.
///
///One grid pitch a press, ten with shift. The pitch rather than one database unit, because a unit is a
///nanometer on most files and pressing an arrow forty thousand times is not an editor - and because a nudge
///by the pitch lands on the grid, which is where the shape probably wants to be anyway.
///
///Down the screen is a larger Y here, so the arrow that says down moves the shape down.
///
function nudge(event) {
    const steps = {
        ArrowLeft: [-1, 0],
        ArrowRight: [1, 0],
        ArrowUp: [0, -1],
        ArrowDown: [0, 1]
    };

    const step = steps[event.key];

    if (step == null || event.ctrlKey || event.metaKey || event.altKey)
        return false;

    event.preventDefault();

    let by = grid.pitch;

    if (by <= 0)
        by = 1;

    if (event.shiftKey)
        by = by * 10;

    //Down the same path a drag takes, so the distance is brought into the cell the same way.
    keys.view.invokeMethodAsync('OnElementDragged', step[0] * by, step[1] * by);

    return true;
}

///
///What a key press means, or null for one this does not answer to.
///
///Both of the redo pairs, because which one somebody reaches for is a habit rather than a rule - and the
///same for Delete and Backspace.
///
function shortcutFor(event) {
    const held = event.ctrlKey === true || event.metaKey === true;
    const key = event.key.toLowerCase();

    if (held) {
        if (key === 'z' && event.shiftKey)
            return 'redo';

        if (key === 'z')
            return 'undo';

        if (key === 'y')
            return 'redo';

        if (key === 'c')
            return 'copy';

        if (key === 'x')
            return 'cut';

        if (key === 'v')
            return 'paste';

        if (key === 'a')
            return 'all';

        return null;
    }

    if (event.key === 'Delete' || event.key === 'Backspace')
        return 'delete';

    if (event.key === 'Escape')
        return 'none';

    //The tools, and the grid. Single letters, which are free of the browser's own shortcuts and are only
    //reached at all when nothing is being typed into.
    //
    //v for Move, which breaks the first-letter pattern the rest of these follow: m is Measure and was here
    //first. Every editor that has a move tool - Figma, Illustrator, Photoshop - puts it on v, so the hand
    //that reaches for it will already be reaching there.
    //
    const letters = { p: 'pan', m: 'measure', s: 'select', v: 'move', d: 'draw', g: 'grid' };

    return letters[key] ?? null;
}

//Where the pointer is///////////////////////////

//How many microns one database unit is, or null when the file's UNITS say nothing usable.
let scaleMicronsPerUnit = null;

///
///How much of the layout is on screen, told to C# once a gesture has finished.
///
///**On settle, never per frame.** Panning is a viewBox attribute and nothing else - no round trip and no
///rebuild - which is exactly why it is smooth. Telling C# on every pointer move would put a rebuild of the
///whole markup in the middle of the one interaction that had none.
///
///**Grown by a margin before it is sent**, so an ordinary pan moves nothing new into view and costs
///nothing at all; only a pan that leaves the margin behind is worth a rebuild. Half a viewport each way,
///which is a drag most of the way across the window.
///
const VIEW_MARGIN = 0.5;

let viewSettling = null;

function reportViewBox() {
    if (keys.view == null)
        return;

    const shown = shownArea();

    if (shown == null)
        return;

    const marginX = shown.width * VIEW_MARGIN;
    const marginY = shown.height * VIEW_MARGIN;

    keys.view.invokeMethodAsync(
        'OnViewBoxChanged',
        shown.x - marginX,
        shown.y - marginY,
        shown.x + shown.width + marginX,
        shown.y + shown.height + marginY,
        unitsPerPixel());
}

///
///And where it is looking, exactly, so the session can put it back.
///
///**Not the box reportViewBox sends.** That one is the *shown* area grown by half a viewport each way,
///which is right for deciding what is worth drawing and wrong for coming back to - restoring it would zoom
///out by the margin every time, and again on the visit after that. This is the viewBox itself.
///
///Told on settle for the same reason as the other, and separately because that one gives up early on a
///layout small enough not to need culling - which is most files, and all of them should come back where
///they were left.
///
///
///**On its own timer, and a much longer one.** The 140ms below is the culling report's, and it is short
///because what it decides is what gets drawn - waiting longer is a visible wait. Nothing is waiting on
///this: it ends in a session being written, and a session written a second late is a session written.
///
///The difference matters because the pointer-up that calls this is not only a pan. Any release that is not
///a selection, a draw or a ruler lands there, so an ordinary click on the layout arrives here too - and at
///140ms that put a store write behind a large share of the clicks in the app. Two specs elsewhere started
///failing on it, a different one each run, which is what contention looks like rather than a bug.
///
let viewSettlingForSession = null;

function reportViewSettledWhenStill() {
    if (viewSettlingForSession != null)
        clearTimeout(viewSettlingForSession);

    viewSettlingForSession = setTimeout(function () {
        viewSettlingForSession = null;

        if (keys.view == null)
            return;

        const box = currentViewBox();

        if (box == null)
            return;

        keys.view.invokeMethodAsync('OnViewSettled', box.x, box.y, box.width, box.height);
    }, 1000);
}

///Waits for the gesture to stop before asking for that, so a drag is one report rather than a hundred.
function reportViewBoxWhenSettled() {
    if (viewSettling != null)
        clearTimeout(viewSettling);

    viewSettling = setTimeout(function () {
        viewSettling = null;

        reportViewBox();
    }, 140);

    reportViewSettledWhenStill();
}

///
///Puts the view back where a session says it was left, in place of framing the drawing.
///
///Takes the attribute as it is written rather than four arguments, because that is the shape the session
///holds and splitting it at the C# end would mean writing the same parse twice.
///
///Refused for anything that is not four numbers, or whose size is not positive: a viewBox of zero width is
///not one a browser accepts, and what it does with one is stop drawing. The caller frames the drawing when
///this refuses, which is what it would have done anyway.
///
function applyViewBox(text) {
    if (svg == null || typeof text !== 'string')
        return false;

    const parts = text.trim().split(/\s+/).map(Number);

    if (parts.length !== 4 || parts.some(one => !isFinite(one)))
        return false;

    const [x, y, width, height] = parts;

    if (!(width > 0) || !(height > 0))
        return false;

    viewBox.x = x;
    viewBox.y = y;
    viewBox.width = width;
    viewBox.height = height;

    newViewBox.x = x;
    newViewBox.y = y;

    svg.setAttribute('viewBox', `${x} ${y} ${width} ${height}`);

    //Both are drawn across whatever is on screen, which has just become somewhere else. Same as the fit.
    drawRuler();
    drawGrid();

    //And the stipples, which are held at a size on screen rather than in the layout.
    scalePatterns();

    //And so has what is worth drawing at all.
    reportViewBox();

    return true;
}

///
///Frames the whole layout, which is what a file being opened should show.
///
///**The starting viewBox is a guess and always was.** It is a fixed window a few thousand units across,
///which happens to fit a standard cell and nothing else - a die, a package drawing, anything that arrived
///as a DXF opens somewhere off the edge of it, and the only way to find the layout is to guess which way
///to pan. Reading the drawn bounds and fitting to them costs one pass over what is already on screen.
///
///A tenth of a margin, so nothing sits on the edge of the view. Squared off to the wider of the two axes
///because the viewBox keeps its aspect ratio: fitting each axis on its own would show one of them right
///and crop the other.
///
///Refused for a layout with nothing in it, or one whose bounds have no size - a viewBox of zero width is
///not one a browser accepts, and what it does instead is stop drawing.
///
///
///The bounds of the layout, which is not the bounds of the SVG.
///
///`svg.getBBox()` was what this used, and it measures every child - including the grid, which is drawn
///across whatever is on screen rather than around the geometry. So with the grid switched on when a file
///opened, the fit framed the *grid*: measured on the bundled cell, a box of 40,000 units square against a
///layout 2,800 by 1,500, giving a viewBox of 22,000 where 3,080 was wanted. The layout came up seven times
///too small in the middle of an empty view, and clicking a shape hit nothing, because the shapes were not
///where anything expected them.
///
///It reads as a bug about the grid and is a bug about the fit. The grid did nothing wrong; it was measured
///when it should not have been, and so were the ruler, the rubber band and the rest of the furniture. Those
///are drawn *over* the layout and none of them says anything about how big it is.
///
function drawnBounds() {
    //Built here rather than held as a constant: every one of these is declared further down the file, and a
    //list assembled at the top would be reading them before they exist. This runs once per file opened.
    const furniture = [GRID_ID, RULER_ID, CHOSEN_ID, BAND_ID, DRAG_ID, HANDLES_ID, DRAW_ID, SNAP_MARK_ID, CARRY_ID];

    let left = Infinity;
    let top = Infinity;
    let right = -Infinity;
    let bottom = -Infinity;

    for (const node of svg.children) {
        if (furniture.includes(node.id))
            continue;

        //A <style> has no box, and asking one for a box is not free of consequences in every browser.
        if (typeof node.getBBox !== 'function')
            continue;

        const box = node.getBBox();

        if (!(box.width > 0) && !(box.height > 0))
            continue;

        left = Math.min(left, box.x);
        top = Math.min(top, box.y);
        right = Math.max(right, box.x + box.width);
        bottom = Math.max(bottom, box.y + box.height);
    }

    if (!isFinite(left) || !isFinite(top))
        return null;

    return { x: left, y: top, width: right - left, height: bottom - top };
}

function fitToDrawing() {
    if (svg == null)
        return;

    let box = null;

    try {
        box = drawnBounds();
    }
    catch {
        return;
    }

    if (box == null || !(box.width > 0) || !(box.height > 0))
        return;

    const size = Math.max(box.width, box.height) * 1.1;

    viewBox.x = box.x + (box.width / 2) - (size / 2);
    viewBox.y = box.y + (box.height / 2) - (size / 2);
    viewBox.width = size;
    viewBox.height = size;

    newViewBox.x = viewBox.x;
    newViewBox.y = viewBox.y;

    svg.setAttribute('viewBox', `${viewBox.x} ${viewBox.y} ${viewBox.width} ${viewBox.height}`);

    //Both are drawn across whatever is on screen, which has just become somewhere else entirely.
    drawRuler();
    drawGrid();

    //And the stipples, which are held at a size on screen rather than in the layout.
    scalePatterns();

    //And so has what is worth drawing at all.
    reportViewBox();

    //
    //**No settle report from here**, deliberately.
    //
    //The frame a file opens on is where you are looking until you move it - but it is also exactly what a
    //reopen would work out for itself, so recording it saves nothing and costs a session write on every
    //file that is opened. Two specs elsewhere went flaky on that write the first time this reported, which
    //is the cheaper half of the lesson.
    //
    //The effect is that a file nobody has moved carries no box, and comes back framed. Which is right.
    //
}

function setScale(micronsPerUnit) {
    scaleMicronsPerUnit = micronsPerUnit;
}

///
///Writes the pointer's position into the readout, straight into the element.
///
///**Not through interop.** This fires on every movement of the pointer, and a call into C# per pixel of
///travel to set a string is a round trip per pixel - the one place in this view where doing it the tidy way
///would be felt. Blazor does not own that element's text, so nothing fights over it.
///
function showCursorAt(event) {
    const readout = document.getElementById('cursorAt');

    if (readout == null || svg == null)
        return;

    //Where the pointer actually is, not where it would be snapped to: this says where you are pointing.
    if (svg.getScreenCTM == null)
        return;

    const matrix = svg.getScreenCTM();

    if (matrix == null)
        return;

    const screen = getPointFromEvent(event);

    const point = svg.createSVGPoint();
    point.x = screen.x;
    point.y = screen.y;

    const layout = point.matrixTransform(matrix.inverse());

    readout.textContent = window.viewGeometry.position(layout, scaleMicronsPerUnit);
}

///
///Where the pointer last was over the view, in window pixels, or null before it has ever been there.
///
///Recorded in the move handler rather than asked for when wanted, because there is nothing to ask: the
///browser will tell you where a pointer *event* happened and has no answer at all for "where is the cursor
///now". Something that wants to act at the cursor without an event of its own - Ctrl+V - has to have been
///keeping track.
///
///This is the last position **over the SVG**, which is the useful one and not an accident of where the
///listener happens to be. The pointer travelling off the view onto a panel, or onto the menu it just opened
///with the right button, leaves this at the last place in the drawing it was - which is where somebody
///pressing Paste on that menu means.
///
let lastPointerOverView = null;

function rememberPointerOverView(event) {
    lastPointerOverView = getPointFromEvent(event);
}

///The last pointer position as a place in the layout, or null if there has not been one.
function pointerLayoutPoint() {
    if (lastPointerOverView == null)
        return null;

    return layoutPointAt(lastPointerOverView.x, lastPointerOverView.y);
}

//The grid////////////////////////////////////////

//pitch is in the layout's own coordinates - database units - because that is what everything here is in.
//Turning a real distance on the chip into one of these is C#'s job; it is the only side that has read the
//file's UNITS record.
let grid = {
    show: false,
    snap: false,
    pitch: 0,

    //Whether the pointer is taken to the corners and edges of what is already drawn.
    toShapes: false
};

const GRID_ID = 'gridOverlay';

///Below this many pixels apart, a line is not a grid line - it is a shade of gray over the whole layout.
const GRID_LEAST_PIXELS = 5;

///
///Sets the grid up, and is safe to call on every render.
///
///Called after each one on purpose: Blazor owns the markup inside the SVG and replaces it wholesale, which
///takes anything added here with it. Rebuilding on demand is cheaper than trying to work out whether it
///survived.
///
function applyGrid(show, snap, pitch, toShapes) {
    grid.show = show === true;
    grid.snap = snap === true;
    grid.pitch = 0;

    if (pitch > 0)
        grid.pitch = pitch;
    grid.toShapes = toShapes === true;

    //Called after every render, which is exactly when the markup may have been replaced - so this is
    //where the geometry index learns that what it holds is a list of shapes no longer there.
    drawnCount++;

    drawGrid();

    //And the same reasoning for the fill patterns: a render replaces the <pattern> elements with fresh ones
    //carrying no transform, so this is the one call that cannot take the shortcut below.
    scalePatterns(true);
}

///
///How big one repeat of a layer's fill pattern should look, in pixels on the screen.
///
///A stipple is a texture rather than a thing in the layout, so it belongs to the eye rather than to the
///coordinates - which is what KLayout does, and what makes a hatch still readable when you have zoomed
///into a single via. Left in layout units it would be a wall of solid color at the fit and four enormous
///stripes across a via.
///
///
///The size a pattern is held at when its layer has not been given one of its own. Must match
///Layer.DefaultPatternPixels, which is the value the settings popup shows in the box before anything is
///typed - two sides of the same number, and the only way to keep them together is to say so.
///
const WANTED_PATTERN_PIXELS = 9;

///
///Where a layer that *has* been given a size carries it. Written by SvgWriter.appendPattern; see
///SvgWriter.PatternPixelsAttribute, which is the C# end of this name.
///
const PATTERN_PIXELS_ATTRIBUTE = 'data-pixels';

///
///What the last rescale was for, so panning does not repeat it.
///
///Every place that changes the viewBox calls this, and most of them are pans - which move the window
///without resizing it, so the pattern's screen size has not changed and there is nothing to do. Guarding
///on the ratio rather than counting call sites keeps that decision in one place.
///
let lastPatternRatio = 0;

///
///Holds every fill pattern at one size on screen, whatever the zoom is.
///
///The tile is written in layout units by SvgWriter - a fraction of the layout's own extent, since a
///database unit is not a length - and that is the right size for a picture with no viewer, like a
///downloaded SVG. Here there is a viewer, and it can say how many pixels a unit currently is.
///
///`patternTransform` rather than rewriting the width and height: it scales the motif's stroke widths along
///with the tile, so the lines stay one weight relative to the pattern instead of thinning out as you zoom.
///
function scalePatterns(force) {
    if (svg == null)
        return;

    const patterns = svg.querySelectorAll('pattern.layerFill');

    if (patterns.length === 0)
        return;

    const across = svg.getBoundingClientRect().width;

    if (!(across > 0) || !(viewBox.width > 0))
        return;

    const ratio = viewBox.width / across;

    //A thousandth, which is far finer than a pattern can show and coarse enough that a pan's rounding
    //does not count as a change.
    if (force !== true && Math.abs(ratio - lastPatternRatio) < ratio / 1000)
        return;

    lastPatternRatio = ratio;

    for (const pattern of patterns) {
        const tile = Number(pattern.getAttribute('width'));

        if (!(tile > 0))
            continue;

        //Per pattern, because the size is per layer: a layer given a coarser hatch is coarser at every
        //zoom, which is the whole of what that setting means. Absent is the usual size, so a picture where
        //nobody changed anything carries no such attributes at all.
        let wanted = Number(pattern.getAttribute(PATTERN_PIXELS_ATTRIBUTE));

        if (!(wanted > 0))
            wanted = WANTED_PATTERN_PIXELS;

        pattern.setAttribute('patternTransform', `scale(${(wanted * ratio) / tile})`);
    }
}

///
///Two nested patterns: a line every pitch, and a heavier one every tenth.
///
///The tenth is what makes a grid readable at a glance - counting nine identical squares to find out where
///you are is not reading. Each is dropped as it gets too fine to be anything but a wash of color, so
///zooming out ends with the heavy lines alone and then with nothing, rather than with a gray rectangle.
///
function drawGrid() {
    const existing = document.getElementById(GRID_ID);

    if (svg == null || !grid.show || grid.pitch <= 0) {
        if (existing != null)
            existing.remove();

        return;
    }

    const shown = shownArea();
    const perPixel = unitsPerPixel();

    if (shown == null || perPixel <= 0)
        return;

    const minor = grid.pitch / perPixel;
    const major = minor * 10;

    if (major < GRID_LEAST_PIXELS) {
        if (existing != null)
            existing.remove();

        return;
    }

    let group = existing;

    if (group == null) {
        group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        group.setAttribute('id', GRID_ID);
        group.setAttribute('pointer-events', 'none');

        //First child, so the layout is drawn over the grid rather than under it.
        svg.insertBefore(group, svg.firstChild);
    }

    while (group.firstChild != null)
        group.removeChild(group.firstChild);

    const step = grid.pitch;
    const wide = step * 10;

    if (minor >= GRID_LEAST_PIXELS)
        group.appendChild(gridLines(shown, step, perPixel * 0.6, 0.35));

    group.appendChild(gridLines(shown, wide, perPixel, 0.6));
}

///
///One set of lines, as a single path - one node for the whole grid rather than one per line.
///
///Snapped outwards to whole steps *of its own spacing*, so the lines sit still as the view moves rather
///than sliding with it. Each set gets its own bounds rather than sharing the coarser one's: at a zoom where
///the whole view fits inside one heavy square, sharing would draw the fine lines across ten times the
///ground that is on screen.
///
function gridLines(shown, step, thickness, opacity) {
    const left = Math.floor(shown.x / step) * step;
    const top = Math.floor(shown.y / step) * step;
    const right = Math.ceil((shown.x + shown.width) / step) * step;
    const bottom = Math.ceil((shown.y + shown.height) / step) * step;

    const parts = [];

    for (let x = left; x <= right; x += step)
        parts.push(`M${x} ${top}V${bottom}`);

    for (let y = top; y <= bottom; y += step)
        parts.push(`M${left} ${y}H${right}`);

    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');

    path.setAttribute('class', 'gridLine');
    path.setAttribute('d', parts.join(''));
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke-width', thickness);
    path.setAttribute('opacity', opacity);

    return path;
}

///
///What the SVG is actually showing, read off the element rather than off the pan bookkeeping.
///
///The two are not the same thing. viewBox holds the size and the origin as of the last pointer-up, and
///newViewBox holds the origin mid-drag - and before anything has been panned at all, neither holds the
///origin the markup started with. The element knows, because it is what is being drawn.
///
function currentViewBox() {
    if (svg == null || svg.viewBox == null || svg.viewBox.baseVal == null)
        return null;

    const box = svg.viewBox.baseVal;

    if (box.width === 0)
        return null;

    return { x: box.x, y: box.y, width: box.width, height: box.height };
}

///
///What is actually on screen, in the layout's own coordinates.
///
///**Not the viewBox.** The SVG sets no `preserveAspectRatio`, so it takes the default of `xMidYMid meet`:
///a viewBox that is not the element's shape is scaled to fit inside it and centered, and the element then
///shows *more* layout than the viewBox names. The box here is square and the view is wide, so there is a
///band of layout down each side that the viewBox says nothing about - which is why the grid stopped short
///of the window with empty ground either side of it.
///
///Read off the browser's own matrix rather than worked out from the two aspect ratios, because that matrix
///*is* the answer to how the box was fitted. Anything else is a second implementation of the same rule,
///and the two would eventually disagree.
///
function shownArea() {
    let matrix = null;

    if (svg != null && svg.getScreenCTM != null)
        matrix = svg.getScreenCTM();

    if (matrix == null)
        return currentViewBox();

    const rect = svg.getBoundingClientRect();
    const inverse = matrix.inverse();

    const corner = (x, y) => {
        const point = svg.createSVGPoint();

        point.x = x;
        point.y = y;

        return point.matrixTransform(inverse);
    };

    const topLeft = corner(rect.left, rect.top);
    const bottomRight = corner(rect.right, rect.bottom);

    if (bottomRight.x - topLeft.x === 0)
        return currentViewBox();

    return {
        x: topLeft.x,
        y: topLeft.y,
        width: bottomRight.x - topLeft.x,
        height: bottomRight.y - topLeft.y
    };
}

///How many layout units one screen pixel is, which is what sizes anything that has to look the same at
///any zoom - a handle, a grid line, the distance that counts as "on top of" something.
///
///**Off the browser's matrix, not the viewBox against the element's width.**
///
///With `meet`, the scale is set by whichever axis is the tighter fit - the height, for a square box in a
///wide view - so dividing the box's *width* by the element's answers about the looser one. Measured at the
///proportions the app opens at, that was 2.8 units a pixel where the truth is 4.08: understated by nearly
///half, in the number that sizes the snap radius, the vertex handles, and both level-of-detail thresholds.
///
///
///The right edge of the view, in the same client coordinates a click reports.
///
///For placing a popup that has to stay on the canvas when the thing that opens it does not. The layer
///settings are reached from a gear in the sidebar, so a position worked out from the pointer alone lands
///partly over the list it came from - a third of it, measured.
///
///Zero when there is no view, which the caller reads as "nothing to clamp to" and places from the pointer
///the way it always did.
///
function viewRightEdge() {
    const view = document.querySelector('.viewWrapper');

    if (view == null)
        return 0;

    return view.getBoundingClientRect().right;
}

function unitsPerPixel() {
    let matrix = null;

    if (svg != null && svg.getScreenCTM != null)
        matrix = svg.getScreenCTM();

    if (matrix == null || matrix.a === 0)
        return 0;

    return 1 / matrix.a;
}

//Ruler//////////////////////////////////////////

//What the ruler is doing. from and to are in the layout's own coordinates, not in pixels.
let ruler = {
    active: false,
    micronsPerUnit: null,
    from: null,
    to: null,

    //A finished measurement stays on screen until the next click starts another, so it can be read
    //rather than having to be held.
    frozen: false
};

const RULER_ID = 'rulerOverlay';

///
///Turns a pointer event into a point in the layout's coordinates.
///
///Through the SVG's own screen matrix rather than by repeating the pan and zoom arithmetic: getScreenCTM
///already accounts for the viewBox, the element's size on screen and anything CSS has done to it, and
///inverting it is the one conversion that cannot drift out of agreement with what is drawn.
///
///**Snapping happens here and nowhere else.** Every tool that asks where the pointer is asks through this,
///so a shape's preview and the numbers that go into the file are the same numbers by construction rather
///than by two pieces of code agreeing. It also makes a drag come out as a whole number of steps without
///anything having to arrange that: a distance between two snapped points is a multiple of the pitch, so a
///shape that was on the grid stays on it and one that never was keeps the offset it had.
///
function layoutPointFromEvent(event) {
    if (svg == null || svg.getScreenCTM == null)
        return null;

    const matrix = svg.getScreenCTM();

    if (matrix == null)
        return null;

    const screen = getPointFromEvent(event);

    const point = svg.createSVGPoint();
    point.x = screen.x;
    point.y = screen.y;

    const layout = point.matrixTransform(matrix.inverse());

    return snapped({ x: layout.x, y: layout.y });
}

///
///How big the window is, for anything that has to decide which way to open.
///
///CSS can hold a panel inside the window - max-width and overflow both do - but it cannot ask whether there
///is room to the right of a point and put the panel on the other side if there is not. That is a comparison
///against a number only the window knows.
///
function windowSize() {
    return { width: window.innerWidth, height: window.innerHeight };
}

///
///Where a point on the screen is in the layout's own coordinates.
///
///The same conversion as rawLayoutPoint, from a pair of window coordinates rather than from an event -
///which is what a Blazor handler has to work with. MouseEventArgs carries where the pointer was and nothing
///about the matrix that turns that into a place in the file, and the matrix is only knowable here.
///
function layoutPointAt(clientX, clientY) {
    if (svg == null || svg.getScreenCTM == null)
        return null;

    const matrix = svg.getScreenCTM();

    if (matrix == null)
        return null;

    const point = svg.createSVGPoint();

    point.x = clientX;
    point.y = clientY;

    const layout = point.matrixTransform(matrix.inverse());

    return { x: layout.x, y: layout.y };
}

///
///Where the pointer is, before anything snaps it.
///
///Wanted by the one thing that cares which grid square was clicked rather than which crossing was nearest:
///a snapped point sits on a corner shared by four squares and cannot say which of them was meant.
///
function rawLayoutPoint(event) {
    if (svg == null || svg.getScreenCTM == null)
        return null;

    const matrix = svg.getScreenCTM();

    if (matrix == null)
        return null;

    const screen = getPointFromEvent(event);
    const point = svg.createSVGPoint();

    point.x = screen.x;
    point.y = screen.y;

    const layout = point.matrixTransform(matrix.inverse());

    return { x: layout.x, y: layout.y };
}

///
///Wherever the pointer should be taken to mean, rather than where it literally is.
///
///**Geometry before the grid.** Somebody holding the pointer over the corner of a shape means that corner:
///the whole reason to snap to one is to butt a new shape against it exactly, and rounding to the nearest
///grid crossing afterwards would put it a fraction off with nothing to say why. The grid is what happens
///when nothing is near.
///
function snapped(point) {
    let onto = null;

    if (grid.toShapes)
        onto = nearestGeometry(point);
    const crossing = onGrid(point);

    //
    //**The nearer of the two wins, rather than shapes always.**
    //
    //Both switches can be on at once, and shapes used to take everything within ten pixels whatever the
    //grid said - so a grid line directly under the pointer lost to an edge nine pixels away, and a corner
    //landed a third of a square off the line it was aimed at. Measured on the bundled cell that is 225 to
    //400 units on a pitch of 1000, which is close enough to look like the grid being wrong rather than
    //like the other switch winning.
    //
    if (onto != null && (crossing == null || nearer(point, onto) <= nearer(point, crossing))) {
        markSnap(onto);

        return onto;
    }

    markSnap(null);

    if (crossing == null)
        return point;

    return crossing;
}

///The nearest crossing of the grid, or null when there is no grid to land on.
function onGrid(point) {
    if (!grid.snap || grid.pitch <= 0)
        return null;

    return {
        x: Math.round(point.x / grid.pitch) * grid.pitch,
        y: Math.round(point.y / grid.pitch) * grid.pitch
    };
}

///How far apart two points are, squared - which is all a comparison needs.
function nearer(from, to) {
    return ((to.x - from.x) * (to.x - from.x)) + ((to.y - from.y) * (to.y - from.y));
}

///
///The nearest corner or edge of anything already drawn, within a few pixels of the pointer.
///
///The rule itself - a corner beating an edge it lies on - is in viewGeometry, which has no DOM and is
///tested under Node. What is here is the two things only a browser knows: how far a few pixels is in the
///layout's own units, and where the shapes are.
///
function nearestGeometry(point) {
    const near = unitsPerPixel() * SNAP_PIXELS;

    return window.viewGeometry.nearestSnap(geometryIndex(), point, near);
}

///How near the pointer has to be, on screen, for a corner or an edge to be what it meant.
const SNAP_PIXELS = 10;

///
///Every corner of every shape drawn, with the corner each one runs to, as one flat array.
///
///**Built once per redraw, not read off the DOM per pointer move.** Asking the document for the points of
///every polygon on every move is a string parse per shape per pixel of travel; a typed array of numbers is
///scanned in a fraction of a millisecond. It is rebuilt when the markup changes, which is what the counter
///below notices - Blazor replaces the whole SVG rather than editing it, so a stale index is not a wrong
///index, it is an index of shapes that are no longer there.
///
let geometry = {
    corners: null,
    builtFor: -1
};

///Bumped every time the view's markup is replaced; see applyGrid.
let drawnCount = 0;

function geometryIndex() {
    if (geometry.builtFor === drawnCount)
        return geometry.corners;

    if (svg == null)
        return null;

    const found = [];

    //
    //**A subpath at a time, because the picture is one path per layer.**
    //
    //Split on the move that starts each shape, so an edge is never invented between the last corner of one
    //shape and the first of the next - which is what reading the whole path's coordinates as one run would
    //do, and it would offer a snap along a line that is not drawn anywhere.
    //
    for (const shape of svg.querySelectorAll(':scope > path[' + ELEMENTS_ATTRIBUTE + ']')) {
        for (const run of shape.getAttribute('d').split('M')) {
            if (run.length === 0)
                continue;

            const numbers = run.replace(/[LZ]/g, ' ').trim().split(/[\s,]+/).map(Number);

            for (let i = 0; i + 1 < numbers.length; i += 2) {
                const nextIsThere = i + 3 < numbers.length;

                found.push(numbers[i], numbers[i + 1]);

                if (nextIsThere)
                    found.push(numbers[i + 2], numbers[i + 3]);
                else
                    found.push(NaN, NaN);
            }
        }
    }

    geometry.corners = Float64Array.from(found);
    geometry.builtFor = drawnCount;

    return geometry.corners;
}

const SNAP_MARK_ID = 'snapMark';

///
///A ring where the pointer has been taken to, or nothing when it has been left where it is.
///
///Silent snapping is the thing that makes snapping feel broken: a corner lands somewhere it was not put and
///there is nothing on screen to say why.
///
function markSnap(at) {
    const showing = document.getElementById(SNAP_MARK_ID);

    if (at == null) {
        if (showing != null)
            showing.remove();

        return;
    }

    let mark = showing;

    if (mark == null) {
        mark = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        mark.setAttribute('id', SNAP_MARK_ID);
        mark.setAttribute('pointer-events', 'none');
        mark.setAttribute('fill', 'none');
        mark.setAttribute('stroke', '#00a0b0');
        mark.setAttribute('vector-effect', 'non-scaling-stroke');
        mark.setAttribute('stroke-width', '2');

        svg.appendChild(mark);
    }

    mark.setAttribute('cx', at.x);
    mark.setAttribute('cy', at.y);
    mark.setAttribute('r', unitsPerPixel() * 5);
}

///Switches the ruler on. micronsPerUnit comes from the file's UNITS record, or null when it says nothing.
function startRuler(micronsPerUnit) {
    ruler.active = true;
    ruler.micronsPerUnit = micronsPerUnit;

    clearRuler();

    if (svg != null)
        svg.style.cursor = 'crosshair';
}

function stopRuler() {
    ruler.active = false;

    clearRuler();

    if (svg != null)
        svg.style.cursor = '';
}

function clearRuler() {
    ruler.from = null;
    ruler.to = null;
    ruler.frozen = false;

    drawRuler();
}

function onRulerClick(event) {
    const point = layoutPointFromEvent(event);

    if (point == null)
        return;

    //Three states in one handler: nothing measured, measuring, and measured. A click moves to the next.
    if (ruler.from == null || ruler.frozen) {
        ruler.from = point;
        ruler.to = point;
        ruler.frozen = false;
    }
    else {
        ruler.to = point;
        ruler.frozen = true;
    }

    drawRuler();
}

function onRulerMove(event) {
    if (ruler.from == null || ruler.frozen)
        return;

    const point = layoutPointFromEvent(event);

    if (point == null)
        return;

    ruler.to = point;

    drawRuler();
}

///
///Draws the line, its end ticks and the reading, into a group this owns inside the geometry's own SVG.
///
///Inside it rather than in a second SVG on top, so the measurement pans and zooms with the layout without
///anything having to keep two viewBoxes in step. The cost is that the stroke and the text would scale with
///the zoom, so both are sized against the current viewBox each time - which is why this is called from the
///zoom handler as well as from the pointer ones.
///
function drawRuler() {
    if (svg == null)
        return;

    let group = document.getElementById(RULER_ID);

    if (ruler.from == null || ruler.to == null) {
        if (group != null)
            group.remove();

        return;
    }

    if (group == null) {
        group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        group.setAttribute('id', RULER_ID);

        //A measurement is not part of the layout, so it must not answer a hit test meant for one.
        group.setAttribute('pointer-events', 'none');

        svg.appendChild(group);
    }

    //Everything is sized against how much layout is on screen, so the ruler looks the same at any zoom.
    const scale = viewBox.width / 1000;

    const reading = window.viewGeometry.measurement(ruler.from, ruler.to, ruler.micronsPerUnit);

    const midX = (ruler.from.x + ruler.to.x) / 2;
    const midY = (ruler.from.y + ruler.to.y) / 2;

    //The line twice: a thick white one under a thinner colored one, which is what keeps it visible over
    //both a pale layer and a dark one without having to know which it is over.
    group.innerHTML =
        rulerLine(ruler.from, ruler.to, '#ffffff', 7 * scale)
        + rulerLine(ruler.from, ruler.to, '#d81b60', 3 * scale)
        + rulerTick(ruler.from, scale)
        + rulerTick(ruler.to, scale)
        + rulerText(midX, midY - (14 * scale), reading.label, 18 * scale)
        + rulerText(midX, midY + (16 * scale), 'dx ' + Math.round(reading.dx) + '   dy ' + Math.round(reading.dy), 14 * scale);
}

function rulerLine(from, to, color, width) {
    return '<line x1="' + from.x + '" y1="' + from.y + '" x2="' + to.x + '" y2="' + to.y
        + '" stroke="' + color + '" stroke-width="' + width + '" stroke-linecap="round"/>';
}

function rulerTick(at, scale) {
    return '<circle cx="' + at.x + '" cy="' + at.y + '" r="' + (6 * scale)
        + '" fill="#d81b60" stroke="#ffffff" stroke-width="' + (3 * scale) + '"/>';
}

///The reading, haloed in white so it stays legible over whatever geometry it lands on.
function rulerText(x, y, text, size) {
    return '<text x="' + x + '" y="' + y + '" fill="#d81b60" font-size="' + size
        + '" font-family="sans-serif" font-weight="600" text-anchor="middle"'
        + ' style="paint-order: stroke; stroke: #ffffff; stroke-width: ' + (size / 3.6) + 'px;">'
        + text.replace(/&/g, '&amp;').replace(/</g, '&lt;') + '</text>';
}

///
///Saves the layout as an image.
///
///**The markup is handed over rather than taken off the screen.** What is drawn is only what is on screen
///once a layout is large enough to be culled, so a copy of the view is a copy of that - see downloadSvg in
///Viewer2DSvg for the measurement. C# builds the whole layout instead, which also means nothing has to be
///stripped: the grid, the ruler, the handles, the band, the drawing preview and the selection are things
///somebody is doing to the layout rather than things it contains, and none of them are in what arrives.
///
///The viewBox is read off the screen, so the file frames whatever was being looked at.
///
function downloadSvg(filename, markup) {
    const svgElement = document.getElementById("gdsSVG");

    if (svgElement == null)
        return;

    const wrapper = document.createElementNS('http://www.w3.org/2000/svg', 'svg');

    wrapper.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    wrapper.setAttribute('viewBox', svgElement.getAttribute('viewBox'));
    wrapper.innerHTML = markup;

    const svgContent = wrapper.outerHTML;

    const link = document.createElement("a");
    link.href = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svgContent);
    link.download = filename;
    link.style.display = "none";

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}




function BlazorDownloadFile(filename, contentType, content) {
    //Create the URL
    const file = new File([content], filename, { type: contentType });
    const exportUrl = URL.createObjectURL(file);

    //Create the <a> element and click on it
    const a = document.createElement("a");
    document.body.appendChild(a);
    a.href = exportUrl;
    a.download = filename;
    a.target = "_self";
    a.click();

    //We don't need to keep the object URL, let's release the memory
    //On older versions of Safari, it seems you need to comment this line...
    URL.revokeObjectURL(exportUrl);
}

function applyStyleForElement(data) {
    document.getElementById(data.id).style[data.attrib] = data.value;
}

function applyStyleForElementClass(data) {
    for (let classReference of document.getElementsByClassName(data.className)) {
        classReference.style[data.attrib] = data.value;
    }
}

function setInnerHTMLForElement(data) {
    document.getElementById(data.id).innerHTML = data.value;
}