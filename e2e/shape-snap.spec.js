//Snapping to what is already drawn.
//
//The rule - a corner beating an edge it lies on, a point past the end of a segment taking that end - is pure
//and covered under Node in jstests/viewGeometry.test.js.
//
//What is only checkable here is that it reaches the pointer: that a corner dragged near an existing one
//lands on it exactly, that the grid does not get the last word, and that something on screen says it
//happened. Silent snapping is what makes snapping feel broken.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapePoints, shapeBox, showGrid, snapToGrid , usePitch, chooseShape, openGridMenu } = require('./helpers');

const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    //
    //A micron, pinned rather than taken from the file.
    //
    //This file is about a corner beating the grid, and it can only show that where the two disagree -
    //the fixture's corners are not on a micron. They are all multiples of the pitch the file now opens
    //on, which is its own grid raised a decade, so left to itself the two answers would coincide and
    //every test here would pass without proving anything.
    //
    await usePitch(page, 1);

    await enterCellAndDraw(page);
});

async function enterCellAndDraw(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await page.locator('#drawTool').click();
    await chooseShape(page, '#rectangleShape');
}

///Where a point in the layout's coordinates sits on screen, through the SVG's own matrix.
async function onScreen(page, at) {
    return page.evaluate(([x, y]) => {
        const svg = document.getElementById('gdsSVG');
        const point = svg.createSVGPoint();

        point.x = x;
        point.y = y;

        const screen = point.matrixTransform(svg.getScreenCTM());

        return { x: screen.x, y: screen.y };
    }, [at.x, at.y]);
}

///A corner of a shape the cell already holds, well away from the others.
async function aCorner(page) {
    const points = await shapePoints(page, 0, 'inContext');

    const numbers = points.trim().split(/[\s,]+/).map(Number);

    return { x: numbers[0], y: numbers[1] };
}

async function cornersOfLast(page) {
    const points = await shapePoints(page, -1);

    return points.trim().split(/\s+/).map(pair => {
        const [x, y] = pair.split(',').map(Number);

        return { x: x, y: y };
    });
}

test.describe('the switch', () => {
    test('is off to begin with, and beside the grid', async ({ page }) => {
        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).toBeVisible();
        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).not.toHaveClass(/shapePickOn/);
    });

    test('turns on and off', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();
        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).toHaveClass(/shapePickOn/);

        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();
        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).not.toHaveClass(/shapePickOn/);
    });

    ///How you work on a file rather than what your hand is doing, so it comes back like the grid does.
    test('survives a reload', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();

        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).toHaveClass(/shapePickOn/);

        await page.goto('/');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        await openGridMenu(page);
        await expect(page.locator('#shapeSnapToggle')).toHaveClass(/shapePickOn/);
    });
});

test.describe('snapping', () => {
    ///
    ///**A corner dragged near an existing one lands on it exactly.**
    ///
    ///The whole point: butting one shape against another is what a grid cannot do, because the thing being
    ///butted against is wherever it happens to be rather than on any pitch.
    ///
    test('a drawn corner lands on the corner it was dragged near', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();

        const corner = await aCorner(page);
        const at = await onScreen(page, corner);

        const before = await shapeCount(page);

        //Started a few pixels off it, which is inside the reach and nowhere near it in layout units.
        await page.mouse.move(at.x + 4, at.y + 3);
        await page.mouse.down();
        await page.mouse.move(at.x + 160, at.y + 120, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const drawn = await cornersOfLast(page);

        expect(drawn).toContainEqual(corner);
    });

    ///
    ///**And the grid does not get the last word.**
    ///
    ///With both switched on, rounding to the nearest crossing after finding the corner would put the new
    ///shape a fraction off it, with nothing on screen to say why. An existing corner is almost never on the
    ///pitch, which is what makes this checkable at all.
    ///
    test('a corner beats the grid when both are on', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();
        await snapToGrid(page);

        const corner = await aCorner(page);

        //The fixture's corners are not on the micron pitch, so the two answers differ.
        expect(corner.x % UNITS_PER_MICRON !== 0 || corner.y % UNITS_PER_MICRON !== 0).toBe(true);

        const at = await onScreen(page, corner);

        const before = await shapeCount(page);

        await page.mouse.move(at.x + 4, at.y + 3);
        await page.mouse.down();
        await page.mouse.move(at.x + 160, at.y + 120, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        expect(await cornersOfLast(page)).toContainEqual(corner);
    });

    test('with it off, the pointer is left where it is', async ({ page }) => {
        const corner = await aCorner(page);
        const at = await onScreen(page, corner);

        const before = await shapeCount(page);

        await page.mouse.move(at.x + 4, at.y + 3);
        await page.mouse.down();
        await page.mouse.move(at.x + 160, at.y + 120, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        expect(await cornersOfLast(page)).not.toContainEqual(corner);
    });
});

test.describe('showing it', () => {
    ///A corner that lands somewhere it was not put, with nothing to say why, is what makes snapping feel
    ///broken rather than helpful.
    test('a mark appears where the pointer has been taken', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();

        const at = await onScreen(page, await aCorner(page));

        await page.mouse.move(at.x + 4, at.y + 3);
        await page.mouse.down();
        await page.mouse.move(at.x + 5, at.y + 4);

        await expect(page.locator('#snapMark')).toHaveCount(1);

        await page.mouse.up();
    });

    test('and goes when the pointer is nowhere near anything', async ({ page }) => {
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();

        const at = await onScreen(page, await aCorner(page));

        await page.mouse.move(at.x + 4, at.y + 3);
        await page.mouse.down();

        await expect(page.locator('#snapMark')).toHaveCount(1);

        //Well away from anything the cell holds.
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + view.width - 8, view.y + view.height - 8, { steps: 6 });

        await expect(page.locator('#snapMark')).toHaveCount(0);

        await page.mouse.up();
    });
});

///
///With both switches on, whichever is nearer wins.
///
///**Shapes used to win whatever the grid said.** Anything within ten pixels of the pointer took the point,
///so a grid crossing directly under it lost to an edge nine pixels away - and measured on the bundled cell
///that put a corner 225 to 400 units off a pitch of 1000, which reads as the grid being broken rather than
///as the other switch working.
///
test.describe('with the grid on as well', () => {
    test('a crossing under the pointer beats an edge further away', async ({ page }) => {
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await showGrid(page);
        await snapToGrid(page);
        await openGridMenu(page);
        await page.locator('#shapeSnapToggle').click();

        await page.locator('#drawTool').click();
        await chooseShape(page, '#rectangleShape');

        const view = await page.locator('#gdsSVG').boundingBox();

        //In the empty corner of the view, well clear of the cell - so the grid is the only thing near and
        //the shape switch being on must make no difference at all.
        await page.mouse.move(view.x + view.width - 140, view.y + 40);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 40, view.y + 130, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBeGreaterThan(18);

        const drawn = await shapePoints(page, -1);
        const numbers = drawn.trim().split(/[\s,]+/).map(Number);

        //One micron is a thousand units in this file, and every corner is on it.
        for (const number of numbers)
            expect(Math.abs(number % 1000)).toBe(0);
    });
});
