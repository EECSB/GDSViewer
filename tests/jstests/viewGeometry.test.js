const test = require('node:test');
const assert = require('node:assert');
const geometry = require('../../wwwroot/js/viewGeometry.js');

//The pure arithmetic behind the 2D view's pan and zoom, and the point a 3D label hangs from. These run
//under Node with the built-in test runner, so they need nothing installed - the browser layer is covered
//by the Playwright specs in e2e/.

test('panning moves the viewBox against the drag, so the layout follows the cursor', () => {
    const viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    //Dragging right by 100 pixels at 1 unit per pixel moves the window 100 units left.
    const panned = geometry.pannedOrigin(viewBox, 4000, { x: 0, y: 0 }, { x: 100, y: 50 }, 1);

    assert.strictEqual(panned.x, -100);
    assert.strictEqual(panned.y, -50);
});

test('panning scales with the ratio, which is units per screen pixel', () => {
    const viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    const panned = geometry.pannedOrigin(viewBox, 4000, { x: 0, y: 0 }, { x: 10, y: 0 }, 4);

    assert.strictEqual(panned.x, -40);
});

test('panning is damped by the zoom, so a drag moves the same distance however far in it is', () => {
    //Zoomed in to a quarter of the initial height, a pixel covers a quarter as much layout.
    const zoomedIn = { x: 0, y: 0, width: 1000, height: 1000 };

    const panned = geometry.pannedOrigin(zoomedIn, 4000, { x: 0, y: 0 }, { x: 100, y: 0 }, 1);

    assert.strictEqual(panned.x, -25);
});

test('panning starts from where the viewBox already is', () => {
    const viewBox = { x: -2000, y: -1000, width: 4000, height: 4000 };

    const panned = geometry.pannedOrigin(viewBox, 4000, { x: 0, y: 0 }, { x: 100, y: 100 }, 1);

    assert.strictEqual(panned.x, -2100);
    assert.strictEqual(panned.y, -1100);
});

test('scrolling down zooms out and scrolling up zooms in', () => {
    const viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    //A proportion of what is on screen rather than a fixed number of units, so a notch means the same
    //thing on a standard cell and on a die. Compared as reals: a factor divides, and 4000 / 1.125 is not a
    //number either side of this computes bit for bit the same way.
    assert.ok(Math.abs(geometry.zoomedSize(viewBox, 120).width - (4000 * geometry.ZOOM_FACTOR)) < 1e-6);
    assert.ok(Math.abs(geometry.zoomedSize(viewBox, -120).width - (4000 / geometry.ZOOM_FACTOR)) < 1e-6);
});

test('zooming keeps the box square when it started square', () => {
    const zoomed = geometry.zoomedSize({ x: 0, y: 0, width: 4000, height: 4000 }, -120);

    assert.strictEqual(zoomed.width, zoomed.height);
});

//Without a floor, enough notches walk the width down towards zero - and a viewBox that small is one the
//browser draws nothing in, with nothing to say why.
test('zooming in stops at the minimum instead of collapsing through zero', () => {
    const atMinimum = { x: 0, y: 0, width: geometry.MINIMUM_SIZE, height: geometry.MINIMUM_SIZE };

    const zoomed = geometry.zoomedSize(atMinimum, -120);

    assert.strictEqual(zoomed.width, geometry.MINIMUM_SIZE);
    assert.strictEqual(zoomed.height, geometry.MINIMUM_SIZE);
});

test('zooming in never returns a size below the minimum, however many notches', () => {
    let viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    for (let notch = 0; notch < 100; notch++) {
        const zoomed = geometry.zoomedSize(viewBox, -120);

        viewBox = { x: 0, y: 0, width: zoomed.width, height: zoomed.height };
    }

    assert.ok(viewBox.width >= geometry.MINIMUM_SIZE, `width fell to ${viewBox.width}`);
    assert.ok(viewBox.height >= geometry.MINIMUM_SIZE, `height fell to ${viewBox.height}`);
});

test('zooming out is not capped', () => {
    let viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    for (let notch = 0; notch < 10; notch++) {
        const zoomed = geometry.zoomedSize(viewBox, 120);

        viewBox = { x: 0, y: 0, width: zoomed.width, height: zoomed.height };
    }

    assert.strictEqual(Math.round(viewBox.width), Math.round(4000 * Math.pow(geometry.ZOOM_FACTOR, 10)));
});

test('a label hangs from the point its horizontal justification names', () => {
    assert.strictEqual(geometry.labelCenterX('Left'), 0);
    assert.strictEqual(geometry.labelCenterX('Center'), 0.5);
    assert.strictEqual(geometry.labelCenterX('Right'), 1);
});

//Inverted against the enum name on purpose: "Top" means the text hangs below its anchor, so the anchor
//is the sprite's top edge, which is 1 in a space whose Y runs up.
test('a label justified to the top hangs from its top edge', () => {
    assert.strictEqual(geometry.labelCenterY('Top'), 1);
    assert.strictEqual(geometry.labelCenterY('Middle'), 0.5);
    assert.strictEqual(geometry.labelCenterY('Bottom'), 0);
});

test('an unrecognized justification falls back to the middle rather than throwing', () => {
    assert.strictEqual(geometry.labelCenterX('sideways'), 0.5);
    assert.strictEqual(geometry.labelCenterY(undefined), 0.5);
});

//The ruler///////////////////////////////////////

test('a measurement is the distance between two layout points', () => {
    const reading = geometry.measurement({ x: 0, y: 0 }, { x: 300, y: 400 }, null);

    assert.strictEqual(reading.dx, 300);
    assert.strictEqual(reading.dy, 400);
    assert.strictEqual(reading.distance, 500);
});

test('a measurement in the other direction is the same distance and the opposite deltas', () => {
    const forward = geometry.measurement({ x: 100, y: 200 }, { x: 400, y: 600 }, null);
    const back = geometry.measurement({ x: 400, y: 600 }, { x: 100, y: 200 }, null);

    assert.strictEqual(forward.distance, back.distance);
    assert.strictEqual(forward.dx, -back.dx);
    assert.strictEqual(forward.dy, -back.dy);
});

test('measuring a point against itself is zero rather than a division by anything', () => {
    const reading = geometry.measurement({ x: 50, y: 50 }, { x: 50, y: 50 }, 0.001);

    assert.strictEqual(reading.distance, 0);
    assert.strictEqual(reading.microns, 0);
});

//
//The deltas are reported the file's way round, not the picture's.
//
//This view maps GDSII upward Y straight onto SVG downward Y, so the drawing is flipped: a point that
//looks higher on screen has a smaller Y in the file. A measurement that agreed with the picture would
//disagree with every coordinate in the text view and in the download, which is the worse of the two.
//
test('dy follows the file rather than the screen', () => {
    const reading = geometry.measurement({ x: 0, y: 1000 }, { x: 0, y: 400 }, null);

    assert.strictEqual(reading.dy, -600);
});

test('microns come from the scale the file gives', () => {
    //A nanometer grid, which is what every bundled sample uses: a thousand units is one micron.
    const reading = geometry.measurement({ x: 0, y: 0 }, { x: 1000, y: 0 }, 0.001);

    assert.strictEqual(reading.microns, 1);
    assert.match(reading.label, /1000\.00 units/);
    assert.match(reading.label, /1\.0000 µm/);
});

//An invented scale is worse than none, because a number with a unit on it gets quoted.
test('a file with no usable scale is measured in database units alone', () => {
    for (const scale of [null, undefined, 0, -1, NaN, Infinity]) {
        const reading = geometry.measurement({ x: 0, y: 0 }, { x: 250, y: 0 }, scale);

        assert.strictEqual(reading.microns, null, `scale ${scale} should give no microns`);
        assert.strictEqual(reading.label, '250.00 units');
    }
});

//Four decimals on the microns, because a database unit is usually a nanometer and three would round a
//single-unit measurement away to nothing at all.
test('one database unit still reads as a number of microns', () => {
    const reading = geometry.measurement({ x: 0, y: 0 }, { x: 1, y: 0 }, 0.001);

    assert.match(reading.label, /0\.0010 µm/);
});

//The polygon that stands in for a circle. A layout format has no curves, so how round a round thing is
//comes down to how many sides somebody asked for - which makes it arithmetic worth checking rather than
//something to eyeball in a browser.

test('an ellipse has the corners it was asked for, on the box it was given', () => {
    const corners = geometry.ellipseCorners({ x: 0, y: 0 }, { x: 200, y: 100 }, 8, false);

    assert.strictEqual(corners.length, 8);

    //Every corner is on the ellipse, which is the one thing that makes it that ellipse and not another.
    for (const corner of corners) {
        const x = (corner.x - 100) / 100;
        const y = (corner.y - 50) / 50;

        assert.ok(Math.abs((x * x) + (y * y) - 1) < 1e-9, `${corner.x},${corner.y} is off the ellipse`);
    }
});

test('it fills the box exactly, in both directions', () => {
    const corners = geometry.ellipseCorners({ x: -40, y: 10 }, { x: 60, y: 310 }, 64, false);

    const xs = corners.map(corner => corner.x);
    const ys = corners.map(corner => corner.y);

    assert.ok(Math.abs(Math.min(...xs) - -40) < 1e-9);
    assert.ok(Math.abs(Math.max(...xs) - 60) < 1e-9);
    assert.ok(Math.abs(Math.min(...ys) - 10) < 1e-9);
    assert.ok(Math.abs(Math.max(...ys) - 310) < 1e-9);
});

//Dragged either way, because a box dragged up and to the left is the same box.
test('a drag in any direction gives the same shape', () => {
    const forwards = geometry.ellipseCorners({ x: 0, y: 0 }, { x: 200, y: 100 }, 16, false);
    const backwards = geometry.ellipseCorners({ x: 200, y: 100 }, { x: 0, y: 0 }, 16, false);

    for (let i = 0; i < forwards.length; i++) {
        assert.ok(Math.abs(forwards[i].x - backwards[i].x) < 1e-9);
        assert.ok(Math.abs(forwards[i].y - backwards[i].y) < 1e-9);
    }
});

test('asking for a circle squares the box off, away from where the drag started', () => {
    //Wider than tall, so the height is what grows.
    const box = geometry.drawnBox({ x: 10, y: 20 }, { x: 210, y: 70 }, true);

    assert.deepStrictEqual(box.from, { x: 10, y: 20 });
    assert.deepStrictEqual(box.to, { x: 210, y: 220 });
});

test('and squares it off the way the drag went, not always down and right', () => {
    const box = geometry.drawnBox({ x: 0, y: 0 }, { x: -200, y: -50 }, true);

    assert.deepStrictEqual(box.to, { x: -200, y: -200 });
});

test('a circle really is round, whatever the box was', () => {
    const corners = geometry.ellipseCorners({ x: 0, y: 0 }, { x: 300, y: 40 }, 32, true);

    const radii = corners.map(corner =>
        Math.hypot(corner.x - 150, corner.y - 150));

    for (const radius of radii)
        assert.ok(Math.abs(radius - 150) < 1e-9, `radius ${radius} is not 150`);
});

//Fewer than three corners is not a shape, whatever was typed into the box.
test('there is a floor on how few sides a shape can have', () => {
    assert.strictEqual(geometry.ellipseCorners({ x: 0, y: 0 }, { x: 10, y: 10 }, 1, false).length, 3);
    assert.strictEqual(geometry.ellipseCorners({ x: 0, y: 0 }, { x: 10, y: 10 }, 0, false).length, 3);
});

//What the toolbar tells somebody before they have drawn anything, so it has to be size-independent.
test('the error a side makes falls as the sides are added', () => {
    assert.ok(geometry.segmentError(64) < geometry.segmentError(16));

    //A square standing in for a circle misses by the better part of a third of the radius.
    assert.ok(Math.abs(geometry.segmentError(4) - 0.29289) < 1e-4);

    //And sixty-four sides is within about a tenth of a percent, which is the default for that reason.
    assert.ok(geometry.segmentError(64) < 0.0013);
});

//Snapping to what is already drawn. The corners come in flat - four numbers each, where the corner is and
//where the edge leaving it goes - because this is asked on every movement of the pointer and an array of
//objects would allocate one per corner per ask.

///A square from (0,0) to (100,100), as the flat run the search takes.
function square() {
    return [
        0, 0, 100, 0,
        100, 0, 100, 100,
        100, 100, 0, 100,
        0, 100, 0, 0
    ];
}

test('a corner near the pointer is what it snaps to', () => {
    const onto = geometry.nearestSnap(square(), { x: 3, y: 4 }, 20);

    assert.deepStrictEqual(onto, { x: 0, y: 0 });
});

test('a point on an edge snaps onto that edge', () => {
    const onto = geometry.nearestSnap(square(), { x: 50, y: 6 }, 20);

    assert.deepStrictEqual(onto, { x: 50, y: 0 });
});

//
//**A corner beats an edge, even a nearer one.**
//
//Every corner is also a point on two edges, so a search comparing distances alone answers with the edge at
//every corner - by a fraction, and always. Here the pointer is a hair inside the bottom edge and close to
//the left one, so the edge is nearer than the corner and the corner is still the answer.
//
test('a corner wins over an edge that is nearer', () => {
    const onto = geometry.nearestSnap(square(), { x: 4, y: 1 }, 20);

    assert.deepStrictEqual(onto, { x: 0, y: 0 });
});

test('nothing within reach snaps to nothing', () => {
    assert.strictEqual(geometry.nearestSnap(square(), { x: 50, y: 50 }, 20), null);
    assert.strictEqual(geometry.nearestSnap(square(), { x: -400, y: -400 }, 20), null);
});

test('a reach of nothing snaps to nothing', () => {
    assert.strictEqual(geometry.nearestSnap(square(), { x: 0, y: 0 }, 0), null);
    assert.strictEqual(geometry.nearestSnap(null, { x: 0, y: 0 }, 20), null);
});

//A corner that ends an outline has no edge leaving it, and carries NaN rather than a wrong one.
test('a corner with no edge after it is still a corner', () => {
    const lonely = [7, 7, NaN, NaN];

    assert.deepStrictEqual(geometry.nearestSnap(lonely, { x: 9, y: 9 }, 20), { x: 7, y: 7 });
});

test('the nearest of several corners is the one chosen', () => {
    const onto = geometry.nearestSnap(square(), { x: 96, y: 97 }, 20);

    assert.deepStrictEqual(onto, { x: 100, y: 100 });
});

//The nearest place on the edge, not the nearest place on the line through it - which for a pointer out
//beyond the end of a segment is somewhere the shape never reaches.
test('a point past the end of an edge takes its end rather than running off the line', () => {
    const onto = geometry.alongEdge({ x: 500, y: 0 }, 0, 0, 100, 0);

    assert.deepStrictEqual(onto, { x: 100, y: 0 });
});

test('and a point before its start takes the start', () => {
    const onto = geometry.alongEdge({ x: -500, y: 0 }, 0, 0, 100, 0);

    assert.deepStrictEqual(onto, { x: 0, y: 0 });
});

test('an edge of no length is its own answer', () => {
    const onto = geometry.alongEdge({ x: 5, y: 5 }, 3, 3, 3, 3);

    assert.deepStrictEqual(onto, { x: 3, y: 3 });
});

//Where the pointer is, as a line to read.

test('a position is given in microns when the file says what a unit is', () => {
    assert.strictEqual(geometry.position({ x: 1500, y: -2000 }, 0.001), '1.5000, -2.0000 µm');
});

//Four decimals, because a database unit is usually a nanometer and three would round a position on the
//grid to one that is not on it.
test('one database unit is still a number of microns', () => {
    assert.strictEqual(geometry.position({ x: 1, y: 0 }, 0.001), '0.0010, 0.0000 µm');
});

//An invented scale is worse than none: a number with a unit on it gets quoted.
test('with no scale it reads in database units', () => {
    for (const scale of [null, undefined, 0, -1, NaN]) {
        assert.strictEqual(geometry.position({ x: 120, y: -7 }, scale), '120, -7 units',
            `scale ${scale} should give units`);
    }
});

test('database units are whole numbers, which is what the file holds', () => {
    assert.strictEqual(geometry.position({ x: 120.4, y: -7.6 }, null), '120, -8 units');
});
