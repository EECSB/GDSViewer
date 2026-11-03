//
//Where the guided tour's two pieces go: the shade over the page, with a hole cut in it where the part a step is
//about shows through, and the card that says what that part is.
//
//**Measured here rather than in C#**, because where something is on screen is the browser's to know and changes
//without telling anybody: a window resized, a panel scrolled. So the tour hands over a selector, and this keeps
//both pieces where that element is for as long as the step lasts - resizes and scrolls included.
//
//**The hole is a clip-path, not a shadow.** The usual spotlight is an element the size of the hole with a shadow
//spread far enough to cover the window - and "far enough" is a number picked for a window nobody has measured.
//The shade is the window's own size whatever that is, and the hole is cut out of it.
//
//**A step whose part is not on the page says so** rather than spotlighting nothing: the 2D view's tools are not
//there over the 3D view, and the tour leaves that step out instead of pointing at an empty corner.
//

///The selector of the part being shown, or null for a step about the whole app, whose card sits in the middle.
let tourTarget = null;

///The tour's component while it is open, which the keys it answers to are passed to.
let tourHandle = null;

///The gap between the part and its card, and between either and the window's edge, in pixels.
const TOUR_GAP = 12;

///The part a selector names, if it is on the page and has a size - a part that is hidden has none.
function tourPart(selector) {
    if (selector == null)
        return null;

    const part = document.querySelector(selector);

    if (part == null)
        return null;

    const box = part.getBoundingClientRect();

    if (box.width === 0 && box.height === 0)
        return null;

    return part;
}

///
///Cuts the hole in the shade: everything dimmed but this rectangle. Round the window, then round the hole - the
///same ten points every time, so a change of step slides the one hole to the next rather than swapping them.
///
function tourCut(shade, left, top, width, height) {
    const right = left + width;
    const bottom = top + height;

    shade.style.clipPath = `polygon(evenodd, 0 0, 100% 0, 100% 100%, 0 100%, 0 0, ${left}px ${top}px, ${right}px ${top}px, ${right}px ${bottom}px, ${left}px ${bottom}px, ${left}px ${top}px)`;
}

///
///Cuts the hole round the part and puts the card beside it - under it when there is room, else over it, else
///inside. The card is only shown from here, so it never appears anywhere it has not been put.
///
function tourPlaceNow() {
    const shade = document.getElementById('tourShade');
    const card = document.getElementById('tourCard');

    if (shade == null || card == null)
        return;

    const part = tourPart(tourTarget);
    const width = window.innerWidth;
    const height = window.innerHeight;
    const cardBox = card.getBoundingClientRect();

    card.style.visibility = 'visible';

    if (part == null) {
        const left = Math.max(TOUR_GAP, (width - cardBox.width) / 2);
        const top = Math.max(TOUR_GAP, (height - cardBox.height) / 3);

        //A hole of no size under the middle of the card, so the whole page dims - and the next step's grows out of it.
        tourCut(shade, left + (cardBox.width / 2), top + (cardBox.height / 2), 0, 0);

        card.style.left = left + 'px';
        card.style.top = top + 'px';

        return;
    }

    const box = part.getBoundingClientRect();
    const pad = 4;

    tourCut(shade, box.left - pad, box.top - pad, box.width + (pad * 2), box.height + (pad * 2));

    let top = box.bottom + TOUR_GAP;

    if (top + cardBox.height > height - TOUR_GAP)
        top = box.top - TOUR_GAP - cardBox.height;

    //Neither under nor over: a part as tall as the window, like the drawing - the card goes inside it, at the top.
    if (top < TOUR_GAP)
        top = Math.max(TOUR_GAP, box.top + TOUR_GAP);

    let left = box.left + (box.width / 2) - (cardBox.width / 2);

    left = Math.min(Math.max(TOUR_GAP, left), width - TOUR_GAP - cardBox.width);

    card.style.left = Math.max(TOUR_GAP, left) + 'px';
    card.style.top = top + 'px';
}

///
///Every key is the tour's while it is open, wherever the focus is - caught on its way down, before anything on the
///page hears it. **The editor's keys are on the window too**, and without this the arrow that goes to the next
///step would also nudge whatever shape is chosen behind the shade, and Delete would delete it.
///
///Only the propagation stops: Enter and Space still press the card's buttons, which is the browser's own doing.
///
function onTourKey(event) {
    if (tourHandle == null)
        return;

    event.stopPropagation();

    if (event.key === 'Escape' || event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        event.preventDefault();
        tourHandle.invokeMethodAsync('OnTourKey', event.key);

        return;
    }

    if (event.key === 'Tab')
        tourKeepFocus(event);
}

///Tab goes round the card's own buttons rather than out to the page behind the shade.
function tourKeepFocus(event) {
    const card = document.getElementById('tourCard');

    if (card == null)
        return;

    const stops = Array.from(card.querySelectorAll('button:not([disabled])'));

    if (stops.length === 0)
        return;

    event.preventDefault();

    let next = stops.indexOf(document.activeElement);

    if (event.shiftKey)
        next = next - 1;
    else
        next = next + 1;

    if (next < 0)
        next = stops.length - 1;
    else if (next >= stops.length)
        next = 0;

    stops[next].focus();
}

window.tour = {
    ///The tour has opened: its keys, and the window that moves its parts, are watched until stop.
    start: function (handle) {
        tourHandle = handle;

        window.addEventListener('keydown', onTourKey, true);
        window.addEventListener('resize', tourPlaceNow);
        window.addEventListener('scroll', tourPlaceNow, true);
    },

    ///Which of these selectors name a part that is on the page now - a null one, about the whole app, always is.
    present: function (selectors) {
        return selectors.map(each => each == null || tourPart(each) != null);
    },

    ///
    ///Shows a step: true when its part is on the page, or it has none; false when it names one that is not here,
    ///which the tour takes as a step to go past.
    ///
    place: function (selector) {
        const part = tourPart(selector);

        if (selector != null && part == null)
            return false;

        tourTarget = selector;

        if (part != null)
            part.scrollIntoView({ block: 'nearest', inline: 'nearest' });

        tourPlaceNow();

        //Once more after the card has its new words laid out, since its size is what decides where it fits.
        requestAnimationFrame(tourPlaceNow);

        return true;
    },

    ///The tour is over: no keys to hold and nothing to follow round the window any more.
    stop: function () {
        tourTarget = null;
        tourHandle = null;

        window.removeEventListener('keydown', onTourKey, true);
        window.removeEventListener('resize', tourPlaceNow);
        window.removeEventListener('scroll', tourPlaceNow, true);
    }
};
