const test = require('node:test');
const assert = require('node:assert');
const geometry = require('../../wwwroot/js/viewGeometry.js');

//The pure arithmetic behind the 2D view's pan and zoom, and the point a 3D label hangs from. These run
//under Node with the built-in test runner, so they need nothing installed - the browser layer is covered
//by the Playwright specs in e2e/.

test('panning moves the viewBox against the drag, so the layout follows the cursor', () => {
    const viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    //Dragging right by 100 pixels at 1 unit per pixel moves the window 100 units left.
    const panned = geometry.pannedOrigin(viewBox, { x: 0, y: 0 }, { x: 100, y: 50 }, 1);

    assert.strictEqual(panned.x, -100);
    assert.strictEqual(panned.y, -50);
});

test('panning scales with how many units a screen pixel is', () => {
    const viewBox = { x: 0, y: 0, width: 4000, height: 4000 };

    const panned = geometry.pannedOrigin(viewBox, { x: 0, y: 0 }, { x: 10, y: 0 }, 4);

    assert.strictEqual(panned.x, -40);
});

//
//The zoom is already in the pixel size, and is not counted a second time.
//
//It was: the pan damped its ratio by the zoom on top, so the same pixel size moved a zoomed-in view a fraction
//as far - right only when that ratio had been measured at the starting zoom, which a resize while zoomed in
//stopped being true. Two views at different zooms with the same pixel size now pan by the same amount.
//
test('a pixel moves the view by the units it is, whatever the zoom', () => {
    const wide = geometry.pannedOrigin({ x: 0, y: 0, width: 4000, height: 4000 }, { x: 0, y: 0 }, { x: 100, y: 0 }, 2);
    const zoomedIn = geometry.pannedOrigin({ x: 0, y: 0, width: 1000, height: 1000 }, { x: 0, y: 0 }, { x: 100, y: 0 }, 2);

    assert.strictEqual(wide.x, -200);
    assert.strictEqual(zoomedIn.x, -200);
});

test('panning starts from where the viewBox already is', () => {
    const viewBox = { x: -2000, y: -1000, width: 4000, height: 4000 };

    const panned = geometry.pannedOrigin(viewBox, { x: 0, y: 0 }, { x: 100, y: 100 }, 1);

    assert.strictEqual(panned.x, -2100);
    assert.strictEqual(panned.y, -1100);
});

//
//Where a layout point lands in an SVG of a given size, the way a browser fits a viewBox into one by default:
//scaled by whichever side is tighter, and centered along the other. The pinch arithmetic is checked against
//this rather than against its own formula, so a test cannot agree with a mistake by repeating it.
//
function fitted(box, size) {
    const perPixel = Math.max(box.width / size.width, box.height / size.height);

    return {
        perPixel: perPixel,
        left: (size.width - (box.width / perPixel)) / 2,
        top: (size.height - (box.height / perPixel)) / 2
    };
}

function screenOf(box, size, point) {
    const fit = fitted(box, size);

    return { x: ((point.x - box.x) / fit.perPixel) + fit.left, y: ((point.y - box.y) / fit.perPixel) + fit.top };
}

function layoutOf(box, size, spot) {
    const fit = fitted(box, size);

    return { x: box.x + ((spot.x - fit.left) * fit.perPixel), y: box.y + ((spot.y - fit.top) * fit.perPixel) };
}

function assertNear(actual, expected, message) {
    assert.ok(Math.abs(actual - expected) < 1e-6, `${message}: ${actual} is not ${expected}`);
}

//
//The picture moved during a gesture lands every point where the viewBox it stands in for would put it.
//
//Checked through the model above, at points across the box and beyond it - the picture carries the shapes
//drawn past the edge of the view as well - for a drag, a zoom about a point, and both at once, on a view
//wider than it is tall and on one taller than it is wide.
//
function assertShiftLands(applied, target, size) {
    const shift = geometry.viewBoxShift(applied, target, size.width, size.height);

    for (const spot of [{ x: 0, y: 0 }, { x: size.width, y: size.height }, { x: 137, y: 41 }, { x: -300, y: size.height + 200 }]) {
        const point = layoutOf(applied, size, spot);
        const wanted = screenOf(target, size, point);

        assertNear((spot.x * shift.scale) + shift.x, wanted.x, `x of ${JSON.stringify(spot)}`);
        assertNear((spot.y * shift.scale) + shift.y, wanted.y, `y of ${JSON.stringify(spot)}`);
    }

    return shift;
}

test('a picture left where it is needs no moving', () => {
    const box = { x: -2000, y: -1000, width: 4000, height: 4000 };

    assert.deepStrictEqual(geometry.viewBoxShift(box, box, 900, 500), { scale: 1, x: 0, y: 0 });
});

test('a drag moves the picture by the pixels it moved the view', () => {
    const size = { width: 900, height: 500 };
    const applied = { x: 0, y: 0, width: 2000, height: 2000 };

    //Four units a pixel, the tight side being the height: 120 units left is 30 pixels right.
    const shift = assertShiftLands(applied, { x: -120, y: 80, width: 2000, height: 2000 }, size);

    assert.strictEqual(shift.scale, 1);
    assertNear(shift.x, 30, 'x');
    assertNear(shift.y, -20, 'y');
});

test('a zoom scales the picture about the point the wheel was over', () => {
    const size = { width: 375, height: 568 };
    const applied = { x: -1500, y: -800, width: 3000, height: 3000 };
    const target = geometry.pinchedViewBox(applied, layoutOf(applied, size, { x: 200, y: 300 }), { x: 200, y: 300 }, { x: 200, y: 300 }, 100, 160, fitted(applied, size).perPixel);

    const shift = assertShiftLands(applied, target, size);

    assertNear(shift.scale, 1.6, 'scale');
});

test('a zoom and a drag at once are one scale and one shift', () => {
    assertShiftLands({ x: 10, y: 20, width: 5000, height: 5000 }, { x: 900, y: -350, width: 1250, height: 1250 }, { width: 1100, height: 640 });
});

test('spreading two fingers to twice the distance halves the view, about the point between them', () => {
    //A phone: taller than wide, so the width is the tight side.
    const size = { width: 375, height: 568 };
    const from = { x: -1500, y: -800, width: 3000, height: 3000 };
    const middle = { x: 200, y: 300 };
    const anchor = layoutOf(from, size, middle);

    const box = geometry.pinchedViewBox(from, anchor, middle, middle, 100, 200, fitted(from, size).perPixel);

    assertNear(box.width, 1500, 'width');
    assertNear(box.height, 1500, 'height');

    const landed = screenOf(box, size, anchor);

    assertNear(landed.x, middle.x, 'x');
    assertNear(landed.y, middle.y, 'y');
});

test('closing the fingers zooms out, and the point between them still stays put', () => {
    //Wider than tall this time, so the height is the tight side and the drawing is centered across.
    const size = { width: 900, height: 500 };
    const from = { x: 0, y: 0, width: 2000, height: 2000 };
    const middle = { x: 610, y: 140 };
    const anchor = layoutOf(from, size, middle);

    const box = geometry.pinchedViewBox(from, anchor, middle, middle, 240, 80, fitted(from, size).perPixel);

    assertNear(box.width, 6000, 'width');

    const landed = screenOf(box, size, anchor);

    assertNear(landed.x, middle.x, 'x');
    assertNear(landed.y, middle.y, 'y');
});

test('two fingers moved together without spreading drag the view exactly as far as they went', () => {
    const size = { width: 800, height: 500 };
    const from = { x: 0, y: 0, width: 4000, height: 4000 };
    const startMiddle = { x: 400, y: 250 };
    const middle = { x: 460, y: 160 };
    const anchor = layoutOf(from, size, startMiddle);

    const box = geometry.pinchedViewBox(from, anchor, startMiddle, middle, 120, 120, fitted(from, size).perPixel);

    assert.strictEqual(box.width, from.width);

    const landed = screenOf(box, size, anchor);

    assertNear(landed.x, middle.x, 'x');
    assertNear(landed.y, middle.y, 'y');
});

test('a pinch stops at the smallest view the wheel allows, rather than refusing the gesture', () => {
    const from = { x: 0, y: 0, width: 400, height: 400 };

    const box = geometry.pinchedViewBox(from, { x: 200, y: 200 }, { x: 0, y: 0 }, { x: 0, y: 0 }, 10, 1000, 1);

    assert.strictEqual(box.width, geometry.MINIMUM_SIZE);
    assert.strictEqual(box.height, geometry.MINIMUM_SIZE);
});

test('two fingers landing on the same spot do not collapse the view to nothing', () => {
    const from = { x: 0, y: 0, width: 4000, height: 4000 };

    const box = geometry.pinchedViewBox(from, { x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0, y: 0 }, 0, 50, 1);

    assert.ok(box.width >= geometry.MINIMUM_SIZE);
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

//The zoom buttons beside Center: a press is notches of the wheel, about the middle of the box rather than under
//a pointer, since a button has none over the drawing.
test('a press of the zoom buttons zooms about the middle, a notch at a time', () => {
    const zoomed = geometry.zoomedAboutMiddle({ x: 1000, y: 2000, width: 4000, height: 3000 }, 4);
    const factor = Math.pow(geometry.ZOOM_FACTOR, 4);

    assert.ok(Math.abs(zoomed.width - (4000 / factor)) < 1e-6);
    assert.ok(Math.abs(zoomed.height - (3000 / factor)) < 1e-6);

    //And the middle stays where it was.
    assert.ok(Math.abs((zoomed.x + (zoomed.width / 2)) - 3000) < 1e-6);
    assert.ok(Math.abs((zoomed.y + (zoomed.height / 2)) - 3500) < 1e-6);
});

test('a press out takes back a press in', () => {
    const start = { x: -500, y: 250, width: 8000, height: 8000 };
    const back = geometry.zoomedAboutMiddle(geometry.zoomedAboutMiddle(start, 4), -4);

    for (const key of ['x', 'y', 'width', 'height'])
        assert.ok(Math.abs(back[key] - start[key]) < 1e-6);
});

test('pressing in past the wheel\'s floor stops there, still about the middle', () => {
    const zoomed = geometry.zoomedAboutMiddle({ x: 0, y: 0, width: 400, height: 400 }, 40);

    assert.ok(zoomed.width >= geometry.MINIMUM_SIZE);
    assert.ok(Math.abs((zoomed.x + (zoomed.width / 2)) - 200) < 1e-6);
});

test('the 3D camera comes closer by the same notches, and goes back as far', () => {
    const closer = geometry.zoomedDistance(1000, 4);

    assert.ok(Math.abs(closer - (1000 / Math.pow(geometry.ZOOM_FACTOR, 4))) < 1e-9);
    assert.ok(Math.abs(geometry.zoomedDistance(closer, -4) - 1000) < 1e-9);
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
//is the label's top edge, which is 1 in a space whose Y runs up.
test('a label justified to the top hangs from its top edge', () => {
    assert.strictEqual(geometry.labelCenterY('Top'), 1);
    assert.strictEqual(geometry.labelCenterY('Middle'), 0.5);
    assert.strictEqual(geometry.labelCenterY('Bottom'), 0);
});

test('an unrecognized justification falls back to the middle rather than throwing', () => {
    assert.strictEqual(geometry.labelCenterX('sideways'), 0.5);
    assert.strictEqual(geometry.labelCenterY(undefined), 0.5);
});

//The 3D camera////////////////////////////////////

//
//Two layouts as the scene has them, measured in the browser: Mosfet.gds with its labels, the cell the app opens
//with, and tt_um_oscillating_bones, the smallest bundled design. Y is up the stack and Z is the layout's Y.
//
const MOSFET_BOX = { min: { x: -1350, y: -747, z: 0 }, max: { x: 1450, y: 1270, z: 1500 } };
const DESIGN_BOX = { min: { x: 530, y: -2418, z: 5000 }, max: { x: 153440, y: 3493, z: 225760 } };

//The 3D view's camera, and a window about the shape the specs run in.
const FOV = 100;
const ASPECT = 1.4;

///A box's eight corners.
function cornersOf(box) {
    const corners = [];

    for (const x of [box.min.x, box.max.x])
        for (const y of [box.min.y, box.max.y])
            for (const z of [box.min.z, box.max.z])
                corners.push({ x: x, y: y, z: z });

    return corners;
}

///
///Where a point lands for a camera at eye looking at target with Y up: x and y across the frame from -1 to 1,
///and its depth along the view. The arithmetic a perspective camera does, written out.
///
function seen(point, eye, target) {
    const forward = unit({ x: target.x - eye.x, y: target.y - eye.y, z: target.z - eye.z });
    const right = unit({ x: -forward.z, y: 0, z: forward.x });
    const up = {
        x: (right.y * forward.z) - (right.z * forward.y),
        y: (right.z * forward.x) - (right.x * forward.z),
        z: (right.x * forward.y) - (right.y * forward.x)
    };

    const to = { x: point.x - eye.x, y: point.y - eye.y, z: point.z - eye.z };
    const depth = dot(to, forward);
    const spread = Math.tan((FOV / 2) * (Math.PI / 180));

    return { x: dot(to, right) / (depth * spread * ASPECT), y: dot(to, up) / (depth * spread), depth: depth };
}

function dot(a, b) {
    return (a.x * b.x) + (a.y * b.y) + (a.z * b.z);
}

function unit(a) {
    const length = Math.hypot(a.x, a.y, a.z);

    return { x: a.x / length, y: a.y / length, z: a.z / length };
}

///
///How far out points land on each side of the frame, from the camera framedCamera gives them - and how many
///times deeper the deepest of them is than the nearest.
///
function framingOf(points) {
    const framed = geometry.framedCamera(points, FOV, ASPECT);
    const extent = { left: 9, right: -9, bottom: 9, top: -9, nearest: Infinity, deepest: 0 };

    for (const corner of points) {
        const landed = seen(corner, framed.position, framed.target);

        extent.left = Math.min(extent.left, landed.x);
        extent.right = Math.max(extent.right, landed.x);
        extent.bottom = Math.min(extent.bottom, landed.y);
        extent.top = Math.max(extent.top, landed.y);
        extent.nearest = Math.min(extent.nearest, landed.depth);
        extent.deepest = Math.max(extent.deepest, landed.depth);
    }

    extent.furthestOut = Math.max(-extent.left, extent.right, -extent.bottom, extent.top);
    extent.foreshortened = extent.deepest / extent.nearest;

    return extent;
}

//A standard cell one site wide, as the scene has it: narrow, deep, and as tall as a mapped stack.
const NARROW_CELL_BOX = { min: { x: 0, y: -900, z: 0 }, max: { x: 460, y: 1100, z: 2720 } };

test('the 3D view opens looking down at forty-five degrees, from the side the layout\'s Y points to', () => {
    for (const box of [MOSFET_BOX, DESIGN_BOX]) {
        const framed = geometry.framedCamera(cornersOf(box), FOV, ASPECT);
        const back = { x: framed.position.x - framed.target.x, y: framed.position.y - framed.target.y, z: framed.position.z - framed.target.z };

        //Straight on rather than turned, and on +Z - which is the layout's +Y, so its Y runs down the screen as
        //the 2D view draws it.
        assertNear(back.x, 0, 'turned');
        assert.ok(back.z > 0, `the camera is on the far side: ${back.z}`);

        assertNear(Math.atan2(back.y, back.z), geometry.OPENING_ELEVATION, 'elevation');
    }
});

test('every corner of a cell, a design and a narrow cell lands inside the frame', () => {
    for (const box of [MOSFET_BOX, DESIGN_BOX, NARROW_CELL_BOX]) {
        const extent = framingOf(cornersOf(box));

        assert.ok(extent.furthestOut <= (1 / 1.1) + 1e-9, `a corner lands ${extent.furthestOut} out`);
    }
});

//
//And the frame used: the corners furthest out sit a tenth in from its edge, the margin the 2D fit leaves. A speck
//in the middle of the window would pass the test above alone.
//
test('a cell comes as close as the frame allows, a tenth in from its edge', () => {
    const extent = framingOf(cornersOf(MOSFET_BOX));

    assertNear(extent.furthestOut, 1 / 1.1, 'how far out the outermost corner lands');
    assert.ok(extent.foreshortened < geometry.PERSPECTIVE_LIMIT, `foreshortened ${extent.foreshortened}`);
});

//
//**Unless that is close enough to make a giant of the near edge.** A cell one site wide fits the frame's width
//from very near, and seen from there its far side came out a twenty-third the scale of its near: the power rail
//along the near edge was most of the picture. It is held back to PERSPECTIVE_LIMIT.
//
test('a narrow cell stands back until its far side is a quarter the scale of its near', () => {
    const extent = framingOf(cornersOf(NARROW_CELL_BOX));

    assertNear(extent.foreshortened, geometry.PERSPECTIVE_LIMIT, 'how much deeper the far side is');
    assert.ok(extent.furthestOut < 1 / 1.1, `a corner lands ${extent.furthestOut} out`);
});

//
//Centered on screen, rather than aimed at the layout's middle. From an angle the near half of a layout looks
//bigger, and aimed at its middle a design sat in the lower two thirds of the window - its near edge at -0.91,
//its far edge at 0.30 - with the buttons along the foot over its edge.
//
test('the layout is in the middle of the picture, not just its middle in the middle', () => {
    for (const box of [MOSFET_BOX, DESIGN_BOX]) {
        const extent = framingOf(cornersOf(box));

        assertNear(extent.left + extent.right, 0, 'left and right');
        assertNear(extent.bottom + extent.top, 0, 'top and bottom');
    }

    //And a design fills most of the width, where aimed at its middle it filled two thirds.
    assert.ok(framingOf(cornersOf(DESIGN_BOX)).right > 0.8, `${framingOf(cornersOf(DESIGN_BOX)).right}`);
});

//
//**Framed on what is drawn, not on the box around it.** A layout whose widest part is at the back - Mosfet's
//pads are - has a box whose near corners hold nothing, and from above at an angle those are the widest thing
//in the picture. Here a wide bar across the back and a narrow block in front of it: framed on the two parts, the
//bar reaches the edges of the frame, and the camera comes closer than the box around both would let it.
//
test('a layout is framed on its parts, not on the empty corners of the box around them', () => {
    const bar = { min: { x: -5000, y: 0, z: 0 }, max: { x: 5000, y: 500, z: 1000 } };
    const block = { min: { x: -500, y: 0, z: 1000 }, max: { x: 500, y: 500, z: 4000 } };
    const around = { min: { x: -5000, y: 0, z: 0 }, max: { x: 5000, y: 500, z: 4000 } };

    const parts = [...cornersOf(bar), ...cornersOf(block)];
    const extent = framingOf(parts);

    assertNear(extent.furthestOut, 1 / 1.1, 'how far out the parts reach');

    const onParts = geometry.framedCamera(parts, FOV, ASPECT);
    const onBox = geometry.framedCamera(cornersOf(around), FOV, ASPECT);

    //Measured to the bar, which is what the picture is of.
    const barMiddle = { x: 0, y: 250, z: 500 };
    const fromParts = Math.hypot(onParts.position.x - barMiddle.x, onParts.position.y - barMiddle.y, onParts.position.z - barMiddle.z);
    const fromBox = Math.hypot(onBox.position.x - barMiddle.x, onBox.position.y - barMiddle.y, onBox.position.z - barMiddle.z);

    assert.ok(fromParts < fromBox * 0.9, `${fromParts} from the bar framed on the parts, ${fromBox} on the box`);
});

//
//What the view turns around is in the layout, on the line of sight - so pressing center and then orbiting
//turns the layout in place rather than swinging it around a point in the air.
//
test('what the view turns around is inside the layout, in the middle of the picture', () => {
    const tower = { min: { x: 0, y: -50000, z: 0 }, max: { x: 1000, y: 50000, z: 1000 } };

    for (const box of [MOSFET_BOX, DESIGN_BOX, tower]) {
        const framed = geometry.framedCamera(cornersOf(box), FOV, ASPECT);

        for (const axis of ['x', 'y', 'z']) {
            assert.ok(framed.target[axis] >= box.min[axis] - 1e-6, `${axis} ${framed.target[axis]} is under ${box.min[axis]}`);
            assert.ok(framed.target[axis] <= box.max[axis] + 1e-6, `${axis} ${framed.target[axis]} is over ${box.max[axis]}`);
        }
    }

    //For a flat layout that is the point of it at its middle height.
    const design = geometry.framedCamera(cornersOf(DESIGN_BOX), FOV, ASPECT);

    assertNear(design.target.y, (DESIGN_BOX.min.y + DESIGN_BOX.max.y) / 2, 'height');
});

test('a layout with no extent has nothing to frame, and neither does a camera that sees nothing', () => {
    const point = { min: { x: 5, y: 5, z: 5 }, max: { x: 5, y: 5, z: 5 } };

    assert.strictEqual(geometry.framedCamera(cornersOf(point), FOV, ASPECT), null);
    assert.strictEqual(geometry.framedCamera([], FOV, ASPECT), null);
    assert.strictEqual(geometry.framedCamera(cornersOf(MOSFET_BOX), 0, ASPECT), null);
    assert.strictEqual(geometry.framedCamera(cornersOf(MOSFET_BOX), FOV, 0), null);
});

//
//Why designs opened on nothing: the planes were 1 and 50,000, and the far corner of the smallest design is
//further than that from where the view frames it.
//
test('a design is framed from further than the far plane the view used to have', () => {
    const framed = geometry.framedCamera(cornersOf(DESIGN_BOX), FOV, ASPECT);
    const furthest = Math.max(...cornersOf(DESIGN_BOX).map(corner => seen(corner, framed.position, framed.target).depth));

    assert.ok(furthest > 50000, `the far corner is ${furthest} away`);

    const range = geometry.depthRange(DESIGN_BOX, framed.position, FOV, ASPECT);

    assert.ok(range.far >= furthest, `far ${range.far} is in front of the far corner at ${furthest}`);
});

//
//**Between the planes from wherever the camera is and whichever way it looks** - which is what depthRange is
//given no say in: it knows where the camera is and nothing about where it points. So the camera is put around
//the layout and turned towards each corner and the middle, and every point of the layout that lands in the
//frame has to be no nearer than the near plane and no further than the far.
//
test('every point of the layout in the frame is between the planes, whichever way the camera points', () => {
    const box = DESIGN_BOX;
    const middle = { x: (box.min.x + box.max.x) / 2, y: (box.min.y + box.max.y) / 2, z: (box.min.z + box.max.z) / 2 };
    //Where it opens, just over the surface - a little off the middle, so looking at the middle is not straight
    //down, which has no up - and out past a corner.
    const eyes = [
        geometry.framedCamera(cornersOf(box), FOV, ASPECT).position,
        { x: middle.x, y: box.max.y + 2000, z: middle.z + 1000 },
        { x: box.max.x + 30000, y: middle.y, z: box.min.z - 30000 }
    ];

    let checked = 0;

    for (const eye of eyes) {
        const range = geometry.depthRange(box, eye, FOV, ASPECT);

        for (const looking of [middle, ...cornersOf(box)]) {
            for (let i = 0; i <= 8; i++) {
                for (let j = 0; j <= 4; j++) {
                    for (let k = 0; k <= 8; k++) {
                        const point = {
                            x: box.min.x + ((box.max.x - box.min.x) * i / 8),
                            y: box.min.y + ((box.max.y - box.min.y) * j / 4),
                            z: box.min.z + ((box.max.z - box.min.z) * k / 8)
                        };

                        const landed = seen(point, eye, looking);

                        if (!(landed.depth > 0) || Math.abs(landed.x) > 1 || Math.abs(landed.y) > 1)
                            continue;

                        checked++;

                        assert.ok(landed.depth >= range.near, `${landed.depth} is in front of the near plane at ${range.near}`);
                        assert.ok(landed.depth <= range.far, `${landed.depth} is past the far plane at ${range.far}`);
                    }
                }
            }
        }
    }

    //Enough of them landed in a frame for the check to have meant something.
    assert.ok(checked > 1000, `only ${checked} points were in view`);
});

//
//From outside the layout the near plane comes right up to it, which is what keeps depth fine: the ratio of the
//two is about ten, where the old fixed pair had fifty thousand.
//
test('from outside the layout the near plane stands well off, for depth to spare', () => {
    const framed = geometry.framedCamera(cornersOf(DESIGN_BOX), FOV, ASPECT);
    const range = geometry.depthRange(DESIGN_BOX, framed.position, FOV, ASPECT);

    assert.ok(range.far / range.near < 20, `far over near is ${range.far / range.near}`);
});

test('from inside the layout the near plane comes down to a hundred-thousandth of the far', () => {
    const middle = { x: 50000, y: 0, z: 100000 };
    const range = geometry.depthRange(DESIGN_BOX, middle, FOV, ASPECT);

    assertNear(range.near * 100000, range.far, 'near');
});

test('a layout ten times the size gets planes ten times as far, from a camera ten times as far', () => {
    const scaled = {
        min: { x: DESIGN_BOX.min.x * 10, y: DESIGN_BOX.min.y * 10, z: DESIGN_BOX.min.z * 10 },
        max: { x: DESIGN_BOX.max.x * 10, y: DESIGN_BOX.max.y * 10, z: DESIGN_BOX.max.z * 10 }
    };

    const framed = geometry.framedCamera(cornersOf(DESIGN_BOX), FOV, ASPECT);
    const far = { x: framed.position.x * 10, y: framed.position.y * 10, z: framed.position.z * 10 };

    const range = geometry.depthRange(DESIGN_BOX, framed.position, FOV, ASPECT);
    const scaledRange = geometry.depthRange(scaled, far, FOV, ASPECT);

    assertNear(scaledRange.near / range.near, 10, 'near');
    assertNear(scaledRange.far / range.far, 10, 'far');
});

test('a camera on a box with no depth gets no planes rather than a pair of zeros', () => {
    const point = { min: { x: 5, y: 5, z: 5 }, max: { x: 5, y: 5, z: 5 } };

    assert.strictEqual(geometry.depthRange(point, { x: 5, y: 5, z: 5 }, FOV, ASPECT), null);
});

//
//The 3D view's draw distance: a share of the layout's width past its nearest point, with the haze starting most of the
//way there - so from any distance off the near side is drawn, and All is no limit at all.
//
test('the draw distance reaches a share of the layout\'s width past its nearest point, and All reaches everything', () => {
    //Twenty thousand along x above the box's middle: the nearest point of it is straight down, 18,500 below.
    const eye = { x: 50, y: 20000, z: 750 };
    const width = Math.max(MOSFET_BOX.max.x - MOSFET_BOX.min.x, MOSFET_BOX.max.z - MOSFET_BOX.min.z);
    const quarter = geometry.drawReach(MOSFET_BOX, eye, 0.25);
    const nearest = 20000 - MOSFET_BOX.max.y;

    assertNear(quarter.far, nearest + (width * 0.25), 'far');
    assertNear(quarter.hazeFrom, nearest + (width * 0.25 * 0.6), 'haze');

    assert.strictEqual(geometry.drawReach(MOSFET_BOX, eye, 1), null);

    //From inside the layout, where the nearest point is the camera's own.
    const inside = geometry.drawReach(MOSFET_BOX, { x: 0, y: 0, z: 750 }, 0.5);

    assertNear(inside.far, width * 0.5, 'inside');
});

//The orbit the example the app opens with always had, kept: within a percent of 10,000.
test('Admire circles Mosfet at the 10,000 units it always did', () => {
    assert.ok(Math.abs(geometry.admireReach(MOSFET_BOX) - 10000) < 100, `${geometry.admireReach(MOSFET_BOX)}`);
});

//And a design in proportion, rather than from 10,000 out of its middle, which is inside it.
test('Admire circles a design from outside it', () => {
    const reach = geometry.admireReach(DESIGN_BOX);
    const halfAcross = Math.hypot(DESIGN_BOX.max.x - DESIGN_BOX.min.x, DESIGN_BOX.max.z - DESIGN_BOX.min.z) / 2;

    assert.ok(reach > halfAcross * 2, `${reach} against a layout ${halfAcross} from its middle to a corner`);
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

//Reading the picture back. The 2D view writes a shape as its first corner and then steps - h and v along an
//edge that is level or upright, l along one that is not - and whatever reads the picture wants the corners.

test('a shape written as steps reads back as its corners', () => {
    const [ring] = geometry.subpaths('M1000,2000h400v-300h-400v300Z');

    assert.deepStrictEqual(ring.corners, [1000, 2000, 1400, 2000, 1400, 1700, 1000, 1700, 1000, 2000]);
    assert.strictEqual(ring.closed, true);
});

test('a slanted step moves both ways at once', () => {
    const [ring] = geometry.subpaths('M0,0l30,-40h10Z');

    assert.deepStrictEqual(ring.corners, [0, 0, 30, -40, 40, -40]);
});

//A corner given twice stays two corners. The nth corner read back has to be the element's nth, because the
//corner-drag preview and the specs count by it.
test('a step of nothing is still a corner', () => {
    const [ring] = geometry.subpaths('M5,5h0h10Z');

    assert.deepStrictEqual(ring.corners, [5, 5, 5, 5, 15, 5]);
});

test('each move starts a shape of its own, with or without a space before it', () => {
    const shapes = geometry.subpaths('M0,0h10v10ZM100,100v5 M-7,-7h-3');

    assert.deepStrictEqual(shapes.map(shape => shape.corners), [[0, 0, 10, 0, 10, 10], [100, 100, 100, 105], [-7, -7, -10, -7]]);
    assert.deepStrictEqual(shapes.map(shape => shape.closed), [true, false, false]);
});

//The picture as it was written before the steps, and the other straight ways SVG has of saying the same square.
test('every straight way of writing a square reads as the same corners', () => {
    const corners = [1, 1, 3, 1, 3, 3, 1, 3];

    for (const data of ['M1,1L3,1L3,3L1,3Z', 'M1 1 3 1 3 3 1 3z', 'm1 1 l2 0 l0 2 l-2 0 z', 'M1,1H3V3H1Z', 'M1,1h2v2h-2Z'])
        assert.deepStrictEqual(geometry.subpaths(data)[0].corners, corners, data);
});

//A run split off a path at its moves has lost its M, and still reads as starting with one.
test('a subpath without its move reads as though it had one', () => {
    assert.deepStrictEqual(geometry.subpaths('20,30h5')[0].corners, [20, 30, 25, 30]);
});

//Further apart than an int reaches, which is why the writer's steps are longs.
test('a step across the whole coordinate range lands exactly', () => {
    const [run] = geometry.subpaths('M-2000000000,0h4000000000');

    assert.deepStrictEqual(run.corners, [-2000000000, 0, 2000000000, 0]);
});

//The layout views write no curves, and a curve's numbers are not corners.
test('a curve is skipped rather than read as lines', () => {
    const [shape] = geometry.subpaths('M0,0C1,2 3,4 5,6L10,0');

    assert.deepStrictEqual(shape.corners, [0, 0, 10, 0]);
});

//
//**The corner-drag preview moves corners by where they are in the text**, so it has the steps written out as
//corners first: moved on its own, a step would carry every corner after it along.
//
test('a shape written as steps is written out again as its corners', () => {
    assert.strictEqual(geometry.absolutePath('M0,0h10v10h-10v-10ZM5,5l3,4'), 'M0,0L10,0L10,10L0,10L0,0Z M5,5L8,9');
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
        assert.strictEqual(
            geometry.position({ x: 120, y: -7 }, scale), '120, -7 units',
            `scale ${scale} should give units`);
    }
});

test('database units are whole numbers, which is what the file holds', () => {
    assert.strictEqual(geometry.position({ x: 120.4, y: -7.6 }, null), '120, -8 units');
});

//A picture's pixels as lit squares, which is how a logo becomes shapes. Read four bytes a pixel, the way a
//canvas hands them out.

function pixels(...colors) {
    return Uint8ClampedArray.from(colors.flat());
}

const BLACK = [0, 0, 0, 255];
const WHITE = [255, 255, 255, 255];

test('a picture is lit where it is dark, a row at a time from the top', () => {
    const rows = geometry.litRows(pixels(BLACK, WHITE, WHITE, BLACK), 2, 2, 'dark');

    assert.deepStrictEqual(rows, ['#.', '.#']);
});

test('read for its light side, it is lit where it is light instead', () => {
    const rows = geometry.litRows(pixels(BLACK, WHITE, WHITE, BLACK), 2, 2, 'light');

    assert.deepStrictEqual(rows, ['.#', '#.']);
});

//The background a transparent logo was cut out of is not part of it - whichever way round it is read.
test('a see-through pixel is never lit, either way round', () => {
    assert.deepStrictEqual(geometry.litRows(pixels([0, 0, 0, 0]), 1, 1, 'dark'), ['.']);
    assert.deepStrictEqual(geometry.litRows(pixels([255, 255, 255, 0]), 1, 1, 'light'), ['.']);
    assert.deepStrictEqual(geometry.litRows(pixels([255, 255, 255, 0]), 1, 1, 'opaque'), ['.']);
});

//A logo cut out of a transparent ground is its shape whatever its color - white included, which neither of the
//other two would draw against nothing.
test('read for what is not see-through, it is lit wherever there is something, whatever the color', () => {
    const rows = geometry.litRows(pixels(WHITE, [0, 0, 0, 0], [0, 0, 0, 0], BLACK), 2, 2, 'opaque');

    assert.deepStrictEqual(rows, ['#.', '.#']);
});

//Laid over the ground it is read against, so an edge that is mostly there counts and one that is barely
//there does not.
test('a faint edge counts for as much as it shows', () => {
    assert.deepStrictEqual(geometry.litRows(pixels([0, 0, 0, 200]), 1, 1, 'dark'), ['#']);
    assert.deepStrictEqual(geometry.litRows(pixels([0, 0, 0, 60]), 1, 1, 'dark'), ['.']);
});

//A plain average of the three channels makes pure green 85 out of 255, which is dark; the eye sees it as
//one of the lightest colors there is, and that is what the weighting says.
test('the shade is the one the eye sees, not the average of the channels', () => {
    assert.deepStrictEqual(geometry.litRows(pixels([0, 255, 0, 255]), 1, 1, 'dark'), ['.']);
    assert.deepStrictEqual(geometry.litRows(pixels([0, 0, 255, 255]), 1, 1, 'dark'), ['#']);
});

///A picture three by three: its border all one thing, and its middle another.
function framed(border, middle) {
    const all = [];

    for (let i = 0; i < 9; i++) {
        if (i === 4)
            all.push(middle);
        else
            all.push(border);
    }

    return pixels(...all);
}

//The border is the ground, so the shape is what it is not: a black logo on white, a white one on black, and
//anything at all on a ground that is see-through.
test('the side drawn is the one the picture\'s border is not', () => {
    assert.strictEqual(geometry.drawnSide(framed(WHITE, BLACK), 3, 3), 'dark');
    assert.strictEqual(geometry.drawnSide(framed(BLACK, WHITE), 3, 3), 'light');
    assert.strictEqual(geometry.drawnSide(framed([0, 0, 0, 0], WHITE), 3, 3), 'opaque');
});

//Mostly, not entirely: a logo that touches its edge here and there still leaves the ground in the majority.
test('a border mostly light still reads as a light ground', () => {
    const rgba = framed(WHITE, BLACK);

    //Two of the eight border pixels black.
    rgba.set(BLACK, 0);
    rgba.set(BLACK, 4 * 8);

    assert.strictEqual(geometry.drawnSide(rgba, 3, 3), 'dark');
});

//The frame rate behind Debug info in the settings: frames counted in the second before now, and the longest
//wait for one. requestAnimationFrame hands over a timestamp per frame shown, which is what these are.

test('the frame rate counts the frames shown in the last second', () => {
    const times = [];

    for (let time = 0; time <= 2000; time += 20)
        times.push(time);

    const rate = geometry.frameRate(times, 2000);

    assert.strictEqual(rate.fps, 50);
    assert.strictEqual(rate.longest, 20);
});

//The first wait in the second began with a frame before it, and that frame is where it is measured from.
test('the longest frame is the longest wait, measured from the frame before the second', () => {
    const rate = geometry.frameRate([100, 116, 132, 900], 1500);

    assert.strictEqual(rate.fps, 1);
    assert.strictEqual(rate.longest, 768);
});

//A page stuck on one long task shows no new frame at all - so the wait still going on is the longest one, or
//a freeze would read as nothing wrong for as long as it lasted.
test('a freeze still going on is the longest frame', () => {
    const rate = geometry.frameRate([0, 16, 32], 2032);

    assert.strictEqual(rate.fps, 0);
    assert.strictEqual(rate.longest, 2000);
});

test('before any frame there is no rate and no wait', () => {
    assert.deepStrictEqual(geometry.frameRate([], 500), { fps: 0, longest: 0 });
});

//A rate a display runs at, so the graph keeps its scale through a dropped frame and a page keeping up with its
//screen draws a flat line along the top.
test('the graph is scaled to a refresh rate at or above every sample', () => {
    assert.strictEqual(geometry.meterScale([]), 30);
    assert.strictEqual(geometry.meterScale([59, 60]), 60);
    assert.strictEqual(geometry.meterScale([61]), 90);
    assert.strictEqual(geometry.meterScale([144]), 144);
    assert.strictEqual(geometry.meterScale([170]), 240);
    assert.strictEqual(geometry.meterScale([300]), 300);
    assert.strictEqual(geometry.meterScale([301]), 360);
});

//Newest on the right edge, up the screen for more frames, and a history not yet full starts part way across.
test('the graph puts the newest sample on the right and more frames higher', () => {
    assert.strictEqual(geometry.meterPoints([60, 30], 4, 60), '2,0 3,30');
});

test('the graph keeps only the samples it has room for, inside its box', () => {
    assert.strictEqual(geometry.meterPoints([10, 20, -5, 100], 2, 60), '0,60 1,0');
});

//The 3D scene's own rate, off three's frame count read at intervals - see threeFrameStats in ThreeInterop.js.
test('the rate of the scene is the frames it drew over the time between the readings', () => {
    const readings = [{ time: 1000, frames: 100 }, { time: 1250, frames: 108 }, { time: 2000, frames: 132 }];

    assert.strictEqual(geometry.sceneRate(readings), 32);
});

test('the scene has no rate until there is a reading to measure against, or after it starts counting again', () => {
    assert.strictEqual(geometry.sceneRate([]), null);
    assert.strictEqual(geometry.sceneRate([{ time: 1000, frames: 100 }]), null);
    assert.strictEqual(geometry.sceneRate([{ time: 1000, frames: 100 }, { time: 1000, frames: 100 }]), null);
    assert.strictEqual(geometry.sceneRate([{ time: 1000, frames: 900 }, { time: 2000, frames: 30 }]), null);
});

test('a count is said in full, then in thousands, then in millions', () => {
    assert.strictEqual(geometry.shortCount(14), '14');
    assert.strictEqual(geometry.shortCount(9876), '9876');
    assert.strictEqual(geometry.shortCount(98300), '98.3 k');
    assert.strictEqual(geometry.shortCount(1240000), '1.24 M');
});
