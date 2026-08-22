//Drawing a round thing into a format that has no curves.
//
//The arithmetic - where the corners go, what squaring a box off into a circle means, how far a side falls
//inside the curve - is pure and covered under Node in jstests/viewGeometry.test.js.
//
//What is only checkable here is that the shape which reaches the file is the one that was on screen: the
//preview is built from the same corner list that gets handed over, and the point of a test in a browser is
//to catch the day that stops being true.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapePoints, shapeBox, snapToGrid, chooseShape, openShapeSettings, setShapeSetting } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCellAndDraw(page);
});

///Into a cell, since there is nowhere to put a new shape until something has said which cell it goes in.
async function enterCellAndDraw(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await page.locator('#drawTool').click();
    await chooseShape(page, '#ellipseShape');
}

///Drags a box out, optionally holding the modifier that squares it off.
async function dragBox(page, from, to, modifier) {
    const view = await page.locator('#gdsSVG').boundingBox();

    if (modifier)
        await page.keyboard.down(modifier);

    await page.mouse.move(view.x + from[0], view.y + from[1]);
    await page.mouse.down();
    await page.mouse.move(view.x + to[0], view.y + to[1], { steps: 8 });
    await page.mouse.up();

    if (modifier)
        await page.keyboard.up(modifier);
}

///The corners of the last polygon drawn, as numbers.
async function lastShape(page) {
    const points = await shapePoints(page, -1);

    return points.trim().split(/\s+/).map(pair => {
        const [x, y] = pair.split(',').map(Number);

        return { x: x, y: y };
    });
}

test.describe('the tool', () => {
    ///Pointed at first: choosing a shape closes the menu, so reading the mark means asking for it again.
    test('is one of the shapes, alongside rectangle and polygon', async ({ page }) => {
        await page.locator('#drawTool').hover();

        await expect(page.locator('#ellipseShape')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#rectangleShape')).not.toHaveClass(/shapePickOn/);
    });

    ///It is not a tool, so it must not count towards the one tool that is lit.
    test('choosing it leaves Draw as the tool that is on', async ({ page }) => {
        await expect(page.locator('#toolGroup .toolButton.toolButtonOn')).toHaveCount(1);
        await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/);
    });

    ///
    ///**This asked whether the toolbar carried the box**, which it did while Ellipse was in hand and did not
    ///for any other shape. It hangs off Ellipse's own row in the picker now, so the question became which row.
    ///
    ///Checked in both directions: a panel on every row would satisfy "Ellipse has one" just as well.
    ///
    test('the side count hangs off this shape and no other', async ({ page }) => {
        await expect(page.locator('#ellipseSides')).toHaveCount(0);

        await openShapeSettings(page, '#ellipseShape');

        await expect(page.locator('#ellipseSides')).toBeVisible();

        await expect(page.locator('#rectangleShape ~ .shapePickPanel')).toHaveCount(0);
        await expect(page.locator('#polygonShape ~ .shapePickPanel')).toHaveCount(0);
    });

    ///A count chosen before anything is drawn can only honestly be described as a share of the radius.
    test('it says how far a side falls inside the curve', async ({ page }) => {
        //The one beside the side count, by name. It was the last .gridUnit in the bar; there are two panels
        //now and the path's µm is the later of them, so "last" would read the wrong span.
        const howClose = page.locator('#ellipseSides ~ .gridUnit');

        await openShapeSettings(page, '#ellipseShape');

        await expect(howClose).toHaveAttribute('title', /0\.12 % of the radius/);

        await setShapeSetting(page, '#ellipseShape', '#ellipseSides', 8);

        await openShapeSettings(page, '#ellipseShape');

        await expect(howClose).toHaveAttribute('title', /7\.61 % of the radius/);
    });

    test('the hint says what the drag will do', async ({ page }) => {
        await expect(page.locator('#drawHint')).toContainText('hold Shift for a circle');
        await expect(page.locator('#drawHint')).toContainText('64 sides');
    });
});

test.describe('what is drawn', () => {
    test('a drag adds a shape with the sides that were asked for', async ({ page }) => {
        await setShapeSetting(page, '#ellipseShape', '#ellipseSides', 16);

        const before = await shapeCount(page);

        await dragBox(page, [140, 140], [340, 260]);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        //Sixteen, plus the repeat that closes the ring.
        expect(await lastShape(page)).toHaveLength(17);
    });

    ///
    ///**Every corner is on the ellipse the box describes.**
    ///
    ///The shape is checked against its own bounding box rather than against numbers worked out here, because
    ///the drag is in screen pixels and the file is in database units - so what can be asserted is that the
    ///thing in the file is an ellipse, not which one.
    ///
    test('the corners lie on an ellipse', async ({ page }) => {
        const before = await shapeCount(page);

        await dragBox(page, [140, 140], [360, 280]);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const corners = await lastShape(page);

        const xs = corners.map(corner => corner.x);
        const ys = corners.map(corner => corner.y);

        const centerX = (Math.min(...xs) + Math.max(...xs)) / 2;
        const centerY = (Math.min(...ys) + Math.max(...ys)) / 2;
        const radiusX = (Math.max(...xs) - Math.min(...xs)) / 2;
        const radiusY = (Math.max(...ys) - Math.min(...ys)) / 2;

        //Not round: a drag wider than it is tall makes an ellipse, which is the whole point of the tool.
        expect(Math.abs(radiusX - radiusY)).toBeGreaterThan(radiusY * 0.1);

        for (const corner of corners) {
            const x = (corner.x - centerX) / radiusX;
            const y = (corner.y - centerY) / radiusY;

            //Loose, because the file holds whole numbers and a curve does not.
            expect(Math.abs((x * x) + (y * y) - 1)).toBeLessThan(0.01);
        }
    });

    test('holding shift makes it round however the box was dragged', async ({ page }) => {
        const before = await shapeCount(page);

        //Twice as wide as it is tall, so anything but a circle would show.
        await dragBox(page, [140, 160], [380, 280], 'Shift');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const corners = await lastShape(page);

        const xs = corners.map(corner => corner.x);
        const ys = corners.map(corner => corner.y);

        const width = Math.max(...xs) - Math.min(...xs);
        const height = Math.max(...ys) - Math.min(...ys);

        expect(Math.abs(width - height)).toBeLessThan(width * 0.02);
    });

    ///
    ///**No side has zero length, whatever the rounding did on the way in.**
    ///
    ///The dropping itself is covered in LayoutEditTests against a hand-made outline. What this adds is that
    ///the case really arises from the tool: many sides on a small shape puts several pairs of corners on the
    ///same pair of whole numbers, because the file holds integers and a curve does not.
    ///
    test('no side has zero length', async ({ page }) => {
        //Off, because this drags twelve pixels on purpose and a grid step is a micron - snapped, both ends
        //land on the same crossing and there is no ellipse at all to inspect.
        await snapToGrid(page, false);

        await setShapeSetting(page, '#ellipseShape', '#ellipseSides', 512);

        const before = await shapeCount(page);

        await dragBox(page, [200, 200], [212, 212]);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const corners = await lastShape(page);

        for (let i = 1; i < corners.length; i++)
            expect(corners[i]).not.toEqual(corners[i - 1]);

        //And corners really were lost to the rounding, or the dropping was never exercised.
        expect(corners.length).toBeLessThan(513);
    });

    ///
    ///A click, not a one-pixel drag. A pixel is several database units at any normal zoom, so a pixel of
    ///movement is a real if tiny shape - the same threshold the rectangle tool has always used.
    ///
    test('a click that goes nowhere puts nothing in the file', async ({ page }) => {
        const before = await shapeCount(page);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 200, view.y + 200);
        await page.mouse.down();
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
        await expect(page.locator('#undoEdit')).toHaveCount(0);
    });
});

test.describe('the preview', () => {
    ///
    ///**The polygon that is about to be added, not a smooth ellipse standing in for it.**
    ///
    ///A real <ellipse> element would look better than what lands in the file, which at a dozen sides is the
    ///difference between a circle and something visibly not one - and the side count is a control precisely
    ///so somebody can see what they are choosing.
    ///
    test('shows the sides that will be written, not a smooth curve', async ({ page }) => {
        await setShapeSetting(page, '#ellipseShape', '#ellipseSides', 12);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 140, view.y + 140);
        await page.mouse.down();
        await page.mouse.move(view.x + 340, view.y + 280, { steps: 6 });

        await expect(page.locator('#drawPreview')).toHaveCount(1);

        const kind = await page.locator('#drawPreview').evaluate(node => node.tagName);
        const corners = await page.locator('#drawPreview').getAttribute('points');

        expect(kind).toBe('polygon');
        expect(corners.trim().split(/\s+/)).toHaveLength(12);

        await page.mouse.up();
    });

    test('and goes when the shape is finished', async ({ page }) => {
        await dragBox(page, [140, 140], [340, 260]);

        await expect(page.locator('#drawPreview')).toHaveCount(0);
    });
});

test.describe('afterwards', () => {
    test('it is one step on the undo stack', async ({ page }) => {
        const before = await shapeCount(page);

        await dragBox(page, [140, 140], [340, 260]);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    ///How round a round thing should be is a decision about the work, so it comes back. Which shape the
    ///hand is drawing right now does not, for the same reason the tool does not.
    test('the side count survives a reload and the shape does not', async ({ page }) => {
        await setShapeSetting(page, '#ellipseShape', '#ellipseSides', 24);

        await page.goto('/');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        await enterCellAndDraw(page);

        await openShapeSettings(page, '#ellipseShape');

        await expect(page.locator('#ellipseSides')).toHaveValue('24');
    });
});
