//Drawing a shape that is not a rectangle.
//
//A rectangle is one drag and needs no state. A polygon is a gesture that lasts - corners accumulate, the
//outline follows the pointer, and there are four different ways to end it. None of that is checkable
//anywhere but in a browser, because all of it is what the pointer and the keyboard are doing over time.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapePoints, shapeBox, chooseShape, openedOnItsOwn } = require('./helpers');

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
}

///Clicks each corner in turn, as offsets from the view's top left.
async function placeCorners(page, corners) {
    await chooseShape(page, '#polygonShape');

    const view = await page.locator('#gdsSVG').boundingBox();

    for (const [x, y] of corners)
        await page.mouse.click(view.x + x, view.y + y);

    return view;
}

const TRIANGLE = [[120, 120], [280, 150], [200, 280]];

test.describe('choosing the shape', () => {
    ///The one that needs no explaining, and the one somebody reaching for Draw most often wants.
    test('rectangle is what the tool starts on', async ({ page }) => {
        await expect(page.locator('#rectangleShape')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#polygonShape')).not.toHaveClass(/shapePickOn/);
    });

    ///Only while a polygon is being drawn: a rectangle needs no telling, and a line of instructions that is
    ///always on screen is a line nobody reads by the third time.
    test('the hint is there for polygons and not for rectangles', async ({ page }) => {
        await expect(page.locator('#drawHint')).toHaveCount(0);

        await chooseShape(page, '#polygonShape');

        await expect(page.locator('#drawHint')).toBeVisible();

        await chooseShape(page, '#rectangleShape');

        await expect(page.locator('#drawHint')).toHaveCount(0);
    });

    ///
    ///Centered on the canvas and clear of the bar above it.
    ///
    ///It used to be pinned to the left edge, which put it over the cell tree's own column and read as part of
    ///the sidebar rather than as the view telling you something. Both halves are asked, because the fix has
    ///two ways to go wrong that look nothing alike: the centering is a transform, and the vertical clearance
    ///was an `em` resolving against this box's *own* 0.78em font-size - so it sat on the bar it was meant to
    ///sit under, and shrinking the type had silently moved it up.
    ///
    test('the hint is centered under the bar rather than pinned left', async ({ page }) => {
        await chooseShape(page, '#polygonShape');

        await expect(page.locator('#drawHint')).toBeVisible();

        const laid = await page.evaluate(() => {
            const hint = document.getElementById('drawHint').getBoundingClientRect();
            const canvas = document.querySelector('.viewCanvas').getBoundingClientRect();
            const bar = document.getElementById('contextBar');

            let clearOfBar = null;

            if (bar !== null)
                clearOfBar = hint.y - bar.getBoundingClientRect().bottom;

            return {
                offCenter: Math.abs((hint.x + hint.width / 2) - (canvas.x + canvas.width / 2)),
                clearOfBar: clearOfBar,
                insideCanvas: hint.x >= canvas.x && hint.right <= canvas.right
            };
        });

        expect(laid.offCenter).toBeLessThan(2);
        expect(laid.insideCanvas).toBe(true);

        //Below the bar, not on it. Null when there is no bar, which is not this test's business.
        if (laid.clearOfBar !== null)
            expect(laid.clearOfBar).toBeGreaterThan(0);
    });
});

test.describe('placing corners', () => {
    test('each click puts a corner down', async ({ page }) => {
        await placeCorners(page, TRIANGLE);

        //Three handles, and the outline they make.
        await expect(page.locator('#drawPreview circle')).toHaveCount(3);
        await expect(page.locator('#drawPreview polygon')).toHaveCount(1);
    });

    ///The first is what closes the ring, so it has to be findable.
    test('the first corner is drawn larger than the rest', async ({ page }) => {
        await placeCorners(page, TRIANGLE);

        const radii = await page.locator('#drawPreview circle').evaluateAll(nodes =>
            nodes.map(node => Number(node.getAttribute('r'))));

        expect(radii[0]).toBeGreaterThan(radii[1]);
    });

    test('backspace takes the last one back', async ({ page }) => {
        await placeCorners(page, TRIANGLE);

        await expect(page.locator('#drawPreview circle')).toHaveCount(3);

        await page.keyboard.press('Backspace');

        await expect(page.locator('#drawPreview circle')).toHaveCount(2);
    });

    test('escape drops the whole outline', async ({ page }) => {
        await placeCorners(page, TRIANGLE);

        await page.keyboard.press('Escape');

        await expect(page.locator('#drawPreview')).toHaveCount(0);
    });
});

test.describe('closing it', () => {
    ///
    ///**Three ways, because which one somebody reaches for is a habit rather than a rule.**
    ///
    ///Clicking the first corner is what every layout editor does and needs no instructions; Enter and a
    ///double-click are what people try when they have not found that.
    ///
    for (const how of ['first corner', 'Enter', 'double-click']) {
        test(`${how} finishes the shape`, async ({ page }) => {
            const before = await shapeCount(page);

            const view = await placeCorners(page, TRIANGLE);

            if (how === 'first corner')
                await page.mouse.click(view.x + TRIANGLE[0][0], view.y + TRIANGLE[0][1]);
            else if (how === 'Enter')
                await page.keyboard.press('Enter');
            else
                await page.mouse.dblclick(view.x + 200, view.y + 200);

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before + 1);

            //And the outline being drawn is gone, rather than left on screen over the shape it became.
            await expect(page.locator('#drawPreview')).toHaveCount(0);
        });
    }

    ///
    ///A double-click is two pointerdowns, so the second would otherwise leave a corner on top of the one
    ///before it - a zero-length edge in the file, which is a shape most readers complain about.
    ///
    test('a double-click does not leave a corner on top of another', async ({ page }) => {
        const view = await placeCorners(page, TRIANGLE);

        await page.mouse.dblclick(view.x + 200, view.y + 320);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBeGreaterThan(3);

        const points = await shapePoints(page, -1);
        const corners = points.trim().split(/\s+/);

        //A boundary repeats its first corner to close the ring; nothing else may repeat.
        expect(corners[0]).toBe(corners[corners.length - 1]);
        expect(new Set(corners).size).toBe(corners.length - 1);
    });

    test('two corners are not a shape', async ({ page }) => {
        const before = await shapeCount(page);

        await placeCorners(page, [[120, 120], [260, 160]]);

        await page.keyboard.press('Enter');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });
});

test.describe('what comes out', () => {
    test('the shape has the corners it was given', async ({ page }) => {
        const before = await shapeCount(page);

        const view = await placeCorners(page, [[120, 120], [280, 130], [300, 250], [180, 290], [110, 210]]);

        await page.keyboard.press('Enter');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const points = await shapePoints(page, -1);

        //Five, plus the repeat that closes the ring.
        expect(points.trim().split(/\s+/)).toHaveLength(6);
    });

    test('it goes onto the undo stack as one step', async ({ page }) => {
        const before = await shapeCount(page);

        await placeCorners(page, TRIANGLE);

        await page.keyboard.press('Enter');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    test('and into the file that is downloaded', async ({ page }) => {
        const before = await shapeCount(page);

        await placeCorners(page, TRIANGLE);

        await page.keyboard.press('Enter');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(before + 1);
    });

    ///
    ///Switching away mid-outline drops it rather than leaving corners to reappear on the next polygon.
    ///
    ///The pencil is pointed at first, because the shape menu is dismissed by a press outside it and placing
    ///corners is a press on the layout. That is the menu working: it goes away while a shape is being drawn
    ///and comes back on the pencil, which is how it is reached at all once drawing has started.
    ///
    test('changing shape drops a half-drawn outline', async ({ page }) => {
        await placeCorners(page, TRIANGLE);

        await expect(page.locator('#drawPreview circle')).toHaveCount(3);

        await page.locator('#drawTool').hover();

        await chooseShape(page, '#rectangleShape');
        await chooseShape(page, '#polygonShape');

        await expect(page.locator('#drawPreview')).toHaveCount(0);
    });
});
