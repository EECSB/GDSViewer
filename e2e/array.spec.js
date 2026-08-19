//Repeating what is chosen into a grid of copies.
//
//That a copy carries its element's records - a label copying as a label rather than as the polygon it never
//was, an original left where it stood, an undo that is byte for byte - is covered in LayoutEditTests.
//
//What is only checkable here is the gesture: the counts and the pitch coming off the panel, the copies
//landing where the pitch says on screen, and one press being one step.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, allPoints, openedOnItsOwn } = require('./helpers');

//Mosfet.gds says a database unit is a nanometer, so a micron is a thousand of them.
const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCell(page);
});

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Picks one shape the cell holds, and opens the array boxes on it.
async function chooseOneAndOpen(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#arrayOpen').count() === 1) {
            await page.locator('#arrayOpen').click();

            await expect(page.locator('#arrayColumns')).toBeVisible();

            return;
        }
    }

    throw new Error('no shape of this cell could be chosen');
}

async function setArray(page, columns, rows, pitchX, pitchY) {
    await page.locator('#arrayColumns').fill(String(columns));
    await page.locator('#arrayColumns').blur();

    await page.locator('#arrayRows').fill(String(rows));
    await page.locator('#arrayRows').blur();

    await page.locator('#arrayPitchX').fill(String(pitchX));
    await page.locator('#arrayPitchX').blur();

    await page.locator('#arrayPitchY').fill(String(pitchY));
    await page.locator('#arrayPitchY').blur();
}

///Every polygon drawn, as its points string - which is how a copy is told apart from what it came from.
async function outlines(page) {
    return allPoints(page);
}

test.describe('the panel', () => {
    ///Four boxes on every selection would make everybody pay for something most selections do not want.
    test('the boxes are behind a disclosure', async ({ page }) => {
        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await expect(page.locator('#arrayOpen')).toBeVisible();
        await expect(page.locator('#arrayColumns')).toHaveCount(0);

        await page.locator('#arrayOpen').click();

        await expect(page.locator('#arrayColumns')).toBeVisible();
    });

    ///Edge to edge with the original, which is what abutting anything wants and needs no thought.
    test('the pitch starts at the size of what was chosen', async ({ page }) => {
        await chooseOneAndOpen(page);

        const drawn = await page.locator('#gdsSVG .shapeSelected').getAttribute('points');

        const numbers = drawn.trim().split(/[\s,]+/).map(Number);
        const xs = numbers.filter((_, at) => at % 2 === 0);

        const width = (Math.max(...xs) - Math.min(...xs)) / UNITS_PER_MICRON;

        expect(Number(await page.locator('#arrayPitchX').inputValue())).toBeCloseTo(width, 3);
    });

    ///The count is on the button, so nobody has to press it to find out how much it was going to add.
    test('the button says how many it will add', async ({ page }) => {
        await chooseOneAndOpen(page);

        await setArray(page, 3, 2, 2, 2);

        //Six places in the grid, less the one already there.
        await expect(page.locator('#arrayMake')).toContainText('Add 5 more');
    });

    test('one by one has nothing to add', async ({ page }) => {
        await chooseOneAndOpen(page);

        await setArray(page, 1, 1, 2, 2);

        await expect(page.locator('#arrayMake')).toBeDisabled();
        await expect(page.locator('#arrayMake')).toContainText('Nothing to add');
    });

    ///A number typed by accident should not be a file with a hundred thousand shapes in it.
    test('too many at once is refused rather than attempted', async ({ page }) => {
        await chooseOneAndOpen(page);

        await setArray(page, 200, 200, 1, 1);

        await expect(page.locator('#arrayMake')).toBeDisabled();
        await expect(page.locator('#arrayMake')).toHaveAttribute('title', /more than [\d,]+ shapes/);
    });
});

test.describe('making one', () => {
    test('a row of three adds two more', async ({ page }) => {
        await chooseOneAndOpen(page);

        const before = await shapeCount(page);

        await setArray(page, 3, 1, 2, 0);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 2);
    });

    test('and a grid adds one for every place but the first', async ({ page }) => {
        await chooseOneAndOpen(page);

        const before = await shapeCount(page);

        await setArray(page, 3, 2, 2, 2);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 5);
    });

    ///
    ///**The copies land a pitch apart, across the screen.**
    ///
    ///The pitch is a distance on screen, brought into the cell the same way a drag is - so a row marches
    ///across the view rather than along the cell's own axes.
    ///
    test('the copies are a pitch apart', async ({ page }) => {
        await chooseOneAndOpen(page);

        const from = await page.locator('#gdsSVG .shapeSelected').getAttribute('points');
        const before = await outlines(page);

        await setArray(page, 3, 1, 5, 0);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before.length + 2);

        const leftOf = points => Math.min(...points.trim().split(/[\s,]+/)
            .map(Number)
            .filter((_, at) => at % 2 === 0));

        const added = (await outlines(page)).filter(points => !before.includes(points));

        expect(added).toHaveLength(2);

        const wanted = [leftOf(from) + (5 * UNITS_PER_MICRON), leftOf(from) + (10 * UNITS_PER_MICRON)];

        expect(added.map(leftOf).sort((one, other) => one - other)).toEqual(wanted);
    });

    ///Rows go down the screen for a positive pitch, which is what "two rows" means to whoever asked.
    test('a positive row pitch goes down the screen', async ({ page }) => {
        await chooseOneAndOpen(page);

        const from = await page.locator('#gdsSVG .shapeSelected').getAttribute('points');
        const before = await outlines(page);

        await setArray(page, 1, 2, 0, 4);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before.length + 1);

        const topOf = points => Math.min(...points.trim().split(/[\s,]+/)
            .map(Number)
            .filter((_, at) => at % 2 === 1));

        const added = (await outlines(page)).filter(points => !before.includes(points));

        //Down the screen is a larger Y here, because this view draws the layout's Y downwards.
        expect(topOf(added[0])).toBe(topOf(from) + (4 * UNITS_PER_MICRON));
    });

    test('a negative pitch goes the other way', async ({ page }) => {
        await chooseOneAndOpen(page);

        const from = await page.locator('#gdsSVG .shapeSelected').getAttribute('points');
        const before = await outlines(page);

        await setArray(page, 2, 1, -6, 0);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before.length + 1);

        const leftOf = points => Math.min(...points.trim().split(/[\s,]+/)
            .map(Number)
            .filter((_, at) => at % 2 === 0));

        const added = (await outlines(page)).filter(points => !before.includes(points));

        expect(leftOf(added[0])).toBe(leftOf(from) - (6 * UNITS_PER_MICRON));
    });
});

test.describe('afterwards', () => {
    test('the whole array is one step on the undo stack', async ({ page }) => {
        await chooseOneAndOpen(page);

        const before = await shapeCount(page);

        await setArray(page, 3, 2, 2, 2);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 5);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Array 5 shapes/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    test('an array is in the file that is downloaded', async ({ page }) => {
        await chooseOneAndOpen(page);

        const before = await shapeCount(page);

        await setArray(page, 4, 1, 3, 0);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 3);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(before + 3);
    });

    ///
    ///Every copy is a new element, so the numbering after the first has shifted - the selection cannot be
    ///kept the way a move or a turn keeps it, and is let go rather than left naming shapes nobody chose.
    ///
    test('the selection is let go, because the numbering moved', async ({ page }) => {
        await chooseOneAndOpen(page);

        const before = await shapeCount(page);

        await setArray(page, 2, 1, 3, 0);

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(0);
        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });
});
