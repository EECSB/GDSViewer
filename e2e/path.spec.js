//Drawing a wire, and changing how wide it is.
//
//What the records hold, what order they come out in, and what the width draws as are covered in PathTests -
//including the case that decides the design, a path with no WIDTH record being given one.
//
//What is only checkable here is the gesture and the two controls: that a run of clicks ends on the last point
//rather than the first, that the width typed in the toolbar is the width that lands in the file, and that
//the width on the selection panel is a different number meaning a different thing.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapePoints, shapeBox, snapToGrid, chooseShape, setShapeSetting, openShapeSettings } = require('./helpers');

const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCell(page);

    //
    //Snapping off, which is how every test in this file was written and what they still mean.
    //
    //It is on out of the box now. At the default pitch of a micron, and this view fitted at roughly seven
    //database units to the pixel, a gesture of a few dozen pixels is a fraction of one grid step - so two
    //clicks meant to be apart land on the same crossing and the shape, path or reading collapses. These
    //are about the tools rather than about the grid, so the grid is taken out of them.
    //
    await snapToGrid(page, false);
});

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

async function usePath(page, microns) {
    await page.locator('#drawTool').click();
    await chooseShape(page, '#pathShape');

    //After choosing, because the width lives on the Path row in the picker now rather than in the toolbar,
    //and choosing closes the picker. See setShapeSetting.
    if (microns !== undefined)
        await setShapeSetting(page, '#pathShape', '#pathWidth', microns);
}

///Clicks a two-segment run, ending it by clicking the last point again.
async function clickARun(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    const corners = [
        { x: view.x + 150, y: view.y + 150 },
        { x: view.x + 320, y: view.y + 150 },
        { x: view.x + 320, y: view.y + 300 }
    ];

    for (const corner of corners)
        await page.mouse.click(corner.x, corner.y);

    //Again on the last, which is what ends an open run.
    await page.mouse.click(corners[2].x, corners[2].y);
}

///How tall the last shape drawn is, in layout units.
async function heightOfLast(page) {
    const points = await shapePoints(page, -1);

    const numbers = points.trim().split(/[\s,]+/).map(Number);
    const ys = numbers.filter((_, at) => at % 2 === 1);

    return Math.max(...ys) - Math.min(...ys);
}

test.describe('the tool', () => {
    test('is offered beside the other shapes', async ({ page }) => {
        await page.locator('#drawTool').click();

        await expect(page.locator('#pathShape')).toBeVisible();
    });

    ///
    ///A width belongs to a path and to nothing else, so it hangs off the Path row and no other.
    ///
    ///**This asked whether the toolbar carried them**, which it did while the shape was Path and did not
    ///otherwise. They are on the row now, so the question is which row - and the answer is checked in both
    ///directions, because a panel on every row would satisfy "Path has one" just as well.
    ///
    test('the width and the ends hang off the path row and no other', async ({ page }) => {
        await page.locator('#drawTool').click();

        //Nowhere in the bar, whichever shape is in hand.
        await chooseShape(page, '#pathShape');

        await expect(page.locator('#pathWidth')).toHaveCount(0);

        //And on Path's own row, where hovering it is what opens them.
        await openShapeSettings(page, '#pathShape');

        await expect(page.locator('#pathWidth')).toBeVisible();
        await expect(page.locator('#pathEnds')).toBeVisible();

        //A shape with nothing to set has no panel to open.
        await expect(page.locator('#rectangleShape ~ .shapePickPanel')).toHaveCount(0);
        await expect(page.locator('#polygonShape ~ .shapePickPanel')).toHaveCount(0);
        await expect(page.locator('#labelShape ~ .shapePickPanel')).toHaveCount(0);
    });
});

test.describe('drawing one', () => {
    ///
    ///**A run ends on its last point, where a polygon closes on its first.**
    ///
    ///Clicking back onto the start of a wire means a wire that goes back where it came from, which is a route
    ///somebody may well want - so the two shapes cannot share the gesture.
    ///
    test('clicking the last point again ends it', async ({ page }) => {
        const before = await shapeCount(page);

        await usePath(page, 0.5);
        await clickARun(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw path/);
    });

    test('Enter ends it too', async ({ page }) => {
        const before = await shapeCount(page);

        await usePath(page, 0.4);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 160, view.y + 160);
        await page.mouse.click(view.x + 340, view.y + 160);

        await page.keyboard.press('Enter');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);
    });

    ///Two points is a wire. It is the polygon that needs three, and only because it needs area.
    test('two points is enough', async ({ page }) => {
        const before = await shapeCount(page);

        await usePath(page, 0.3);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 200, view.y + 200);
        await page.mouse.click(view.x + 400, view.y + 200);
        await page.mouse.click(view.x + 400, view.y + 200);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);
    });

    ///
    ///**The width typed in microns is the width in the file.**
    ///
    ///The number somebody types is a real dimension and the file counts in database units, so this is the one
    ///place the conversion can be seen going the whole way through - toolbar, interop, record, and back out
    ///as an outline on screen.
    ///
    test('the width typed is the width drawn', async ({ page }) => {
        await usePath(page, 0.6);

        const view = await page.locator('#gdsSVG').boundingBox();

        //A flat run, so the drawn outline is exactly the width tall.
        await page.mouse.click(view.x + 150, view.y + 220);
        await page.mouse.click(view.x + 400, view.y + 220);
        await page.mouse.click(view.x + 400, view.y + 220);

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 })
            .toBe(0.6 * UNITS_PER_MICRON);
    });

    test('Escape throws away a half-drawn run', async ({ page }) => {
        const before = await shapeCount(page);

        await usePath(page, 0.5);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 150);
        await page.mouse.click(view.x + 320, view.y + 150);

        await page.keyboard.press('Escape');
        await page.keyboard.press('Enter');

        await page.waitForTimeout(500);

        expect(await shapeCount(page)).toBe(before);
    });
});

test.describe('its corners', () => {
    ///
    ///**A handle per coordinate the file holds, not per corner it draws.**
    ///
    ///A path is stored as a centerline and drawn as the outline built around it, so a three-point wire draws
    ///six corners. Handles on those would be six handles for three coordinates - and dragging one moves the
    ///coordinate at that index, so the back half would be past the end and do nothing while the front half
    ///moved a point that is not where the hand is.
    ///
    test('a three-point path has three handles, not six', async ({ page }) => {
        await usePath(page, 0.5);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 200);
        await page.mouse.click(view.x + 320, view.y + 200);
        await page.mouse.click(view.x + 320, view.y + 320);
        await page.mouse.click(view.x + 320, view.y + 320);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw path/, { timeout: 15000 });

        await page.locator('#selectTool').click();
        await page.mouse.click(view.x + 240, view.y + 200);

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 }).toBe(3);

        //And the outline it is drawn as has more than that, which is the whole difference.
        const points = await shapePoints(page, -1);

        expect(points.trim().split(/\s+/).length).toBeGreaterThan(3);
    });

    ///
    ///And a handle moves the point it sits on. Dragging the last one down lengthens the wire's second leg,
    ///which is only true if the handle and the coordinate are the same corner.
    ///
    test('dragging a handle moves that point of the centerline', async ({ page }) => {
        await usePath(page, 0.5);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 220);
        await page.mouse.click(view.x + 340, view.y + 220);
        await page.mouse.click(view.x + 340, view.y + 220);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw path/, { timeout: 15000 });

        await page.locator('#selectTool').click();
        await page.mouse.click(view.x + 240, view.y + 220);

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 }).toBe(2);

        const before = await heightOfLast(page);

        //The far end, dragged downwards - which turns a flat run into a diagonal and makes it taller.
        const handle = await page.locator('.vertexHandle').last().boundingBox();

        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2));
        await page.mouse.down();
        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2) + 90, { steps: 6 });
        await page.mouse.up();

        //A flat 0.5 µm wire is 500 units tall and the drag adds about six hundred more - well clear of both
        //a handle that did nothing and one that moved some other point by a rounding.
        await expect.poll(async () => heightOfLast(page), { timeout: 15000 }).toBeGreaterThan(before + 400);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move corner/);

    });

    ///A boundary is stored as what it draws, so nothing about it changed.
    test('a boundary still has a handle on every corner', async ({ page }) => {
        await page.locator('#selectTool').click();

        const box = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();

        const points = await page.locator('#gdsSVG .shapeSelected').first().getAttribute('points');

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
            .toBe(points.trim().split(/\s+/).length);
    });
});

test.describe('changing one afterwards', () => {
    ///Draws a path and picks it out, leaving it chosen.
    async function drawAndChoose(page) {
        await usePath(page, 0.4);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 260);
        await page.mouse.click(view.x + 420, view.y + 260);
        await page.mouse.click(view.x + 420, view.y + 260);

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 }).toBe(0.4 * UNITS_PER_MICRON);

        await page.locator('#selectTool').click();
        await page.mouse.click(view.x + 280, view.y + 260);

        await expect(page.locator('#pathRow')).toBeVisible();
    }

    test('the panel shows what the chosen path is drawn with', async ({ page }) => {
        await drawAndChoose(page);

        expect(Number(await page.locator('#chosenPathWidth').inputValue())).toBeCloseTo(0.4, 4);
        await expect(page.locator('#chosenPathEnds')).toHaveValue('Flush');
    });

    ///The one thing a boundary cannot do: change the width of a route without touching a coordinate.
    test('typing a width widens it', async ({ page }) => {
        await drawAndChoose(page);

        await page.locator('#chosenPathWidth').fill('1.2');
        await page.locator('#chosenPathWidth').blur();

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 })
            .toBe(1.2 * UNITS_PER_MICRON);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Path width/);
    });

    test('and it can be taken back', async ({ page }) => {
        await drawAndChoose(page);

        await page.locator('#chosenPathWidth').fill('1.5');
        await page.locator('#chosenPathWidth').blur();

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 })
            .toBe(1.5 * UNITS_PER_MICRON);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 })
            .toBe(0.4 * UNITS_PER_MICRON);
    });

    ///Extended ends reach half a width past each endpoint, which lengthens the outline by a whole width.
    test('changing the ends changes how far it reaches', async ({ page }) => {
        await drawAndChoose(page);

        const widthOf = async () => {
            const points = await shapePoints(page, -1);
            const numbers = points.trim().split(/[\s,]+/).map(Number);
            const xs = numbers.filter((_, at) => at % 2 === 0);

            return Math.max(...xs) - Math.min(...xs);
        };

        const before = await widthOf();

        await page.locator('#chosenPathEnds').selectOption('Extended');

        await expect.poll(async () => widthOf(), { timeout: 15000 }).toBe(before + (0.4 * UNITS_PER_MICRON));

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Path ends/);
    });

    ///
    ///**Two widths, meaning two different things.**
    ///
    ///The toolbar holds the width of the *next* path; the panel holds the width of the chosen one. One
    ///control for both would silently move whichever was not being looked at.
    ///
    test('the panel does not move the width the tool will draw with', async ({ page }) => {
        await drawAndChoose(page);

        await page.locator('#chosenPathWidth').fill('2');
        await page.locator('#chosenPathWidth').blur();

        await expect.poll(async () => heightOfLast(page), { timeout: 15000 }).toBe(2 * UNITS_PER_MICRON);

        await page.locator('#drawTool').click();

        //On the Path row rather than in the bar, so the picker has to be opened onto it to read the box.
        await openShapeSettings(page, '#pathShape');

        expect(Number(await page.locator('#pathWidth').inputValue())).toBeCloseTo(0.4, 4);
    });

    ///A boundary has no width, so nothing is offered for one.
    test('nothing is offered for a shape that is not a path', async ({ page }) => {
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#pathRow')).toHaveCount(0);
    });
});
