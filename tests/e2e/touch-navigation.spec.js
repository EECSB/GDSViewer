//Moving around the 2D drawing with fingers: one to drag it, two to zoom and drag at once.
//
//Through the browser's real touch input rather than through dispatched PointerEvents. A synthetic event is
//delivered to the page as it was built, so it cannot show what a real one runs into - the browser deciding a
//drag is a scroll, taking it over, and canceling the pointer the view was following. That is the failure
//being pinned here, and only Input.dispatchTouchEvent goes through the part of the browser that makes it.
//
//The measure throughout is the point under the finger. A drag or a pinch is right when the part of the layout
//a finger went down on is still under that finger when it comes up - which is what makes a drawing feel held
//rather than slid, and what a speed or an anchor being slightly off would break.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    svgCounts,
    viewSettled,
    MOSFET,
    MOSFET_POLYGONS
} = require('./helpers');

const PHONE = { width: 375, height: 812 };

//How far a point may land from where the finger is, in screen pixels. Rounding in the matrix and in the
//viewBox attribute, not slack for a gesture that is roughly right.
const NEAR = 2;

///The layout point under a spot on the screen, through the SVG's own matrix.
async function layoutAt(page, spot) {
    return page.evaluate((spot) => {
        const svg = document.getElementById('gdsSVG');
        const point = svg.createSVGPoint();

        point.x = spot.x;
        point.y = spot.y;

        const layout = point.matrixTransform(svg.getScreenCTM().inverse());

        return { x: layout.x, y: layout.y };
    }, spot);
}

///Where a layout point is on the screen now.
async function screenOf(page, layout) {
    return page.evaluate((layout) => {
        const svg = document.getElementById('gdsSVG');
        const point = svg.createSVGPoint();

        point.x = layout.x;
        point.y = layout.y;

        const screen = point.matrixTransform(svg.getScreenCTM());

        return { x: screen.x, y: screen.y };
    }, layout);
}

async function viewBoxOf(page) {
    const text = await page.locator('#gdsSVG').getAttribute('viewBox');

    return text.trim().split(/\s+/).map(Number);
}

///The middle of the drawing, which nothing floats over - the bars are at its top and its corners.
async function middleOfDrawing(page) {
    const box = await page.locator('#gdsSVG').boundingBox();

    return { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };
}

///
///Opens the drawing and waits for the view to stop moving.
///
///The fit lands after the shapes do, so a gesture started the moment they are counted can be measured against
///a matrix that is about to change underneath it. Two reads a beat apart that agree are what settled means.
///
async function openSettled(page) {
    await gotoExample(page, MOSFET, '2d');

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await expect.poll(async () => {
        const first = await page.locator('#gdsSVG').getAttribute('viewBox');

        await page.waitForTimeout(250);

        return first === await page.locator('#gdsSVG').getAttribute('viewBox');
    }, { timeout: 15000 }).toBe(true);
}

///
///Fingers, through the browser's own touch input.
///
///A touchStart or a touchMove lists every finger that is down, and the browser works out against the event
///before which ones pressed or moved - so a second finger landing is a touchStart listing both.
///
///**A touchEnd is the other way round: it lists the fingers that lift**, and an empty one lifts them all.
///Listing the finger that stays, which is what the symmetry suggests, lifts that one instead - and its next
///move then arrives as a new finger landing, which turns the drag being tested into a pinch against the
///finger that was meant to have gone.
///
async function fingers(page) {
    const client = await page.context().newCDPSession(page);

    return async function (type, points) {
        await client.send('Input.dispatchTouchEvent', {
            type: type,
            touchPoints: points.map((point, index) => ({ x: point.x, y: point.y, id: point.id ?? index }))
        });
    };
}

///Where a finger is partway along, in steps, so a drag arrives the way a hand makes it and not as one jump.
function along(from, to, step, steps) {
    return {
        x: from.x + ((to.x - from.x) * step / steps),
        y: from.y + ((to.y - from.y) * step / steps)
    };
}

const STEPS = 8;

async function expectUnder(page, layout, spot) {
    const now = await screenOf(page, layout);

    expect(Math.abs(now.x - spot.x)).toBeLessThanOrEqual(NEAR);
    expect(Math.abs(now.y - spot.y)).toBeLessThanOrEqual(NEAR);
}

test.describe('on a touch screen', () => {
    test.use({ viewport: PHONE, isMobile: true, hasTouch: true });

    ///
    ///The page scrolls on a phone - that is how its sidebars are reached - so a drag the browser was allowed
    ///to keep would move the page and leave the drawing where it was. It must reach the view instead.
    ///
    test('one finger drags the drawing, and the drawing stays under it', async ({ page }) => {
        await openSettled(page);

        const touch = await fingers(page);
        const from = await middleOfDrawing(page);
        const to = { x: from.x + 80, y: from.y + 120 };
        const held = await layoutAt(page, from);

        await touch('touchStart', [from]);

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [along(from, to, step, STEPS)]);

        await touch('touchEnd', []);

        await expectUnder(page, held, to);
        expect(await page.evaluate(() => window.scrollY)).toBe(0);
    });

    ///
    ///Spread to twice the distance, so the view is half as wide - and zoomed about the fingers rather than
    ///about a corner, so the point between them when they landed is still between them.
    ///
    test('two fingers spread apart zoom in about the point between them', async ({ page }) => {
        await openSettled(page);

        const touch = await fingers(page);
        const middle = await middleOfDrawing(page);
        const held = await layoutAt(page, middle);
        const [, , widthBefore] = await viewBoxOf(page);

        const leftFrom = { x: middle.x - 50, y: middle.y, id: 0 };
        const rightFrom = { x: middle.x + 50, y: middle.y, id: 1 };
        const leftTo = { x: middle.x - 100, y: middle.y, id: 0 };
        const rightTo = { x: middle.x + 100, y: middle.y, id: 1 };

        await touch('touchStart', [leftFrom]);
        await touch('touchStart', [leftFrom, rightFrom]);

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [{ ...along(leftFrom, leftTo, step, STEPS), id: 0 }, { ...along(rightFrom, rightTo, step, STEPS), id: 1 }]);

        await touch('touchEnd', []);

        //The pinch scales the picture, and the viewBox follows once the fingers are off.
        await viewSettled(page);

        const [, , widthAfter] = await viewBoxOf(page);

        expect(widthAfter / widthBefore).toBeCloseTo(0.5, 2);
        await expectUnder(page, held, middle);
    });

    ///A pinch that does not change the distance is a drag, and the drawing goes with the pair of fingers.
    test('two fingers moved together drag the drawing with them', async ({ page }) => {
        await openSettled(page);

        const touch = await fingers(page);
        const middle = await middleOfDrawing(page);
        const held = await layoutAt(page, middle);
        const [, , widthBefore] = await viewBoxOf(page);

        const shift = { x: 60, y: -90 };
        const leftFrom = { x: middle.x - 60, y: middle.y, id: 0 };
        const rightFrom = { x: middle.x + 60, y: middle.y, id: 1 };
        const leftTo = { x: leftFrom.x + shift.x, y: leftFrom.y + shift.y, id: 0 };
        const rightTo = { x: rightFrom.x + shift.x, y: rightFrom.y + shift.y, id: 1 };

        await touch('touchStart', [leftFrom]);
        await touch('touchStart', [leftFrom, rightFrom]);

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [{ ...along(leftFrom, leftTo, step, STEPS), id: 0 }, { ...along(rightFrom, rightTo, step, STEPS), id: 1 }]);

        await touch('touchEnd', []);

        await viewSettled(page);

        const [, , widthAfter] = await viewBoxOf(page);

        expect(widthAfter).toBeCloseTo(widthBefore, 3);
        await expectUnder(page, held, { x: middle.x + shift.x, y: middle.y + shift.y });
    });

    ///
    ///Hands do not lift both fingers at once. The one left down carries on dragging from wherever the pinch
    ///left the drawing - not from where that finger first landed, which would throw the view back.
    ///
    test('lifting one finger of a pinch leaves the other dragging, without a jump', async ({ page }) => {
        await openSettled(page);

        const touch = await fingers(page);
        const middle = await middleOfDrawing(page);

        const leftFrom = { x: middle.x - 50, y: middle.y, id: 0 };
        const rightFrom = { x: middle.x + 50, y: middle.y, id: 1 };
        const leftSpread = { x: middle.x - 90, y: middle.y + 20, id: 0 };
        const rightSpread = { x: middle.x + 90, y: middle.y + 20, id: 1 };

        await touch('touchStart', [leftFrom]);
        await touch('touchStart', [leftFrom, rightFrom]);

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [{ ...along(leftFrom, leftSpread, step, STEPS), id: 0 }, { ...along(rightFrom, rightSpread, step, STEPS), id: 1 }]);

        //The right finger comes up - named, since a touchEnd lists the fingers that lift. What is under the
        //left one now is what it should keep hold of.
        await touch('touchEnd', [rightSpread]);

        const held = await layoutAt(page, leftSpread);
        const leftTo = { x: leftSpread.x + 40, y: leftSpread.y - 70, id: 0 };

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [{ ...along(leftSpread, leftTo, step, STEPS), id: 0 }]);

        await touch('touchEnd', []);

        await expectUnder(page, held, leftTo);
    });

    ///The same floor the wheel stops at - below it a viewBox walks towards nothing and the view stops drawing.
    test('a pinch stops zooming in where the wheel does', async ({ page }) => {
        await openSettled(page);

        const touch = await fingers(page);
        const middle = await middleOfDrawing(page);

        const leftFrom = { x: middle.x - 4, y: middle.y, id: 0 };
        const rightFrom = { x: middle.x + 4, y: middle.y, id: 1 };
        const leftTo = { x: 2, y: middle.y, id: 0 };
        const rightTo = { x: PHONE.width - 2, y: middle.y, id: 1 };

        await touch('touchStart', [leftFrom]);
        await touch('touchStart', [leftFrom, rightFrom]);

        for (let step = 1; step <= STEPS; step++)
            await touch('touchMove', [{ ...along(leftFrom, leftTo, step, STEPS), id: 0 }, { ...along(rightFrom, rightTo, step, STEPS), id: 1 }]);

        await touch('touchEnd', []);

        await viewSettled(page);

        const [, , width, height] = await viewBoxOf(page);
        const minimum = await page.evaluate(() => window.viewGeometry.MINIMUM_SIZE);

        expect(Math.min(width, height)).toBeGreaterThanOrEqual(minimum - 0.001);
    });
});

///
///The same promise for a mouse, where it is easier to break without noticing: a pan that is merely a little
///fast still looks like a pan.
///
///Zoomed in and then resized, because those are the two things that change how many layout units a pixel is
///- and a drag worked out from a number measured before either of them would carry the layout past the
///pointer, or leave it behind.
///
test('a mouse drag keeps the drawing under the pointer, zoomed in and after the window changes size', async ({ page }) => {
    await openSettled(page);

    const middle = await middleOfDrawing(page);

    await page.mouse.move(middle.x, middle.y);

    for (let notch = 0; notch < 4; notch++)
        await page.mouse.wheel(0, -100);

    await page.setViewportSize({ width: 1150, height: 820 });

    await expect.poll(async () => {
        const first = await page.locator('#gdsSVG').boundingBox();

        await page.waitForTimeout(250);

        const second = await page.locator('#gdsSVG').boundingBox();

        return first.width === second.width && first.height === second.height;
    }, { timeout: 15000 }).toBe(true);

    const from = await middleOfDrawing(page);
    const to = { x: from.x + 100, y: from.y + 60 };
    const held = await layoutAt(page, from);

    await page.mouse.move(from.x, from.y);
    await page.mouse.down();

    for (let step = 1; step <= STEPS; step++) {
        const at = along(from, to, step, STEPS);

        await page.mouse.move(at.x, at.y);
    }

    await page.mouse.up();

    await expectUnder(page, held, to);
});
