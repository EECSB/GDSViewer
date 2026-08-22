//The three that finish the editor: retyping a label, typing a size, and copying a whole cell.
//
//What each one does to the file is covered in C# - the retype in LayoutEditTests, the arithmetic in
//ScalingTests, the copy in HierarchyTests. What is only checkable here is that each control reaches the right
//thing: that the label box changes the chosen label rather than the one the tool will place next, that a size
//typed anchors on the corner the position box names, and that copying a cell leaves you looking at the copy.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, chooseShape } = require('./helpers');

const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    //With the cell tree open, since copying a cell is checked by looking for it in the library.
    await gotoExample(page, 'Mosfet', 'View2DSvg', true);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCell(page);
});

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

async function currentCell(page) {
    //The first, because the cell-actions button takes the same class while it is open.
    return (await page.locator('.contextCrumbOn').first().textContent()).trim();
}

///
///The rows in the library that carry exactly this name.
///
///Matched on the name rather than on the row, because the row no longer starts with it: the library is
///drawn as a tree, and every row that has something under it begins with a twisty. Anchoring on the row
///text asked for `^mosfet` against a string starting "&#9662;". The name is also why this returns rows
///plural - a cell placed by two parents is listed under each.
///
function named(page, name) {
    return page.locator('.cellRow').filter({ has: page.locator('.cellRowName', { hasText: new RegExp(`^${name}$`) }) });
}

///Picks one shape the cell holds on its own, and hands back which one it is.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#sizeX').count() === 1
            && await page.locator('#gdsSVG .shapeSelected').count() === 1) {
            return page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');
        }
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

async function edgesOf(page, index) {
    const points = await elementPoints(page, index);

    const numbers = points.trim().split(/[\s,]+/).map(Number);

    const xs = numbers.filter((_, at) => at % 2 === 0);
    const ys = numbers.filter((_, at) => at % 2 === 1);

    return {
        left: Math.min(...xs),
        top: Math.min(...ys),
        width: Math.max(...xs) - Math.min(...xs),
        height: Math.max(...ys) - Math.min(...ys)
    };
}

test.describe('retyping a label', () => {
    ///
    ///Places one, then picks it out - which needs the Select tool back, since Draw places rather than
    ///chooses. The words are typed into the box that opens over the label; see label.spec.
    ///
    async function placeAndChoose(page, says) {
        await page.locator('#drawTool').click();
        await chooseShape(page, '#labelShape');

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 260, view.y + 260);

        await expect(page.locator('#canvasLabelText')).toBeVisible();

        await page.locator('#canvasLabelText').fill(says);
        await page.locator('#canvasLabelText').press('Enter');

        await expect(page.locator('#canvasLabelText')).toHaveCount(0);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Label/, { timeout: 15000 });

        await page.locator('#selectTool').click();

        //
        //Found where it is now, rather than clicked at the point it was placed at.
        //
        //Putting the Draw tool down takes its Shape and Join groups out of the toolbar, which can un-wrap
        //the bar from two rows to one and move the whole view up under the pointer. A screen point
        //remembered from before the tool changed is a point somewhere else afterwards.
        //
        const placed = await page.locator('#gdsSVG text', { hasText: says }).boundingBox();

        await page.mouse.click(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator('#labelRow')).toBeVisible();
    }

    test('the panel shows what it says, in a box', async ({ page }) => {
        await placeAndChoose(page, 'VDD');

        await expect(page.locator('#chosenLabelText')).toHaveValue('VDD');
    });

    test('typing changes what it says', async ({ page }) => {
        await placeAndChoose(page, 'VDD');

        await page.locator('#chosenLabelText').fill('GND');
        await page.locator('#chosenLabelText').blur();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Retype label/, { timeout: 15000 });

        await expect(page.locator('#gdsSVG')).toContainText('GND');
    });

    test('and it can be taken back', async ({ page }) => {
        await placeAndChoose(page, 'VDD');

        await page.locator('#chosenLabelText').fill('GND');
        await page.locator('#chosenLabelText').blur();

        await expect(page.locator('#gdsSVG')).toContainText('GND', { timeout: 15000 });

        await page.locator('#undoEdit').click();

        await expect(page.locator('#gdsSVG')).toContainText('VDD', { timeout: 15000 });
    });

    ///
    ///**Two boxes, meaning two different things.** The toolbar's is what the *next* label will say; the
    ///panel's is what the chosen one says. One control for both would silently retype whichever was not being
    ///looked at.
    ///
    test('the panel does not change what the tool will place next', async ({ page }) => {
        await placeAndChoose(page, 'VDD');

        await page.locator('#chosenLabelText').fill('GND');
        await page.locator('#chosenLabelText').blur();

        await expect(page.locator('#gdsSVG')).toContainText('GND', { timeout: 15000 });

        //The next one placed starts where every one does, rather than carrying the last thing retyped.
        await page.locator('#drawTool').click();
        await chooseShape(page, '#labelShape');

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 360, view.y + 320);

        await expect(page.locator('#canvasLabelText')).toHaveValue('label');
    });

    ///A boundary has nothing to say, so nothing is offered for one.
    test('nothing is offered for a shape that is not a label', async ({ page }) => {
        await chooseAShape(page);

        await expect(page.locator('#labelRow')).toHaveCount(0);
    });
});

test.describe('typing a size', () => {
    test('the boxes say how big the chosen shape is', async ({ page }) => {
        const index = await chooseAShape(page);

        const was = await edgesOf(page, index);

        expect(Number(await page.locator('#sizeX').inputValue())).toBeCloseTo(was.width / UNITS_PER_MICRON, 3);
        expect(Number(await page.locator('#sizeY').inputValue())).toBeCloseTo(was.height / UNITS_PER_MICRON, 3);
    });

    test('typing a width makes it that wide', async ({ page }) => {
        const index = await chooseAShape(page);

        await page.locator('#sizeX').fill('3');
        await page.locator('#sizeX').blur();

        await expect.poll(async () => (await edgesOf(page, index)).width, { timeout: 15000 })
            .toBe(3 * UNITS_PER_MICRON);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Resize/);
    });

    ///
    ///**Anchored on the corner the position box names**, so growing a shape leaves the At number where it
    ///was. Two boxes that moved each other would be two boxes nobody could use together.
    ///
    test('growing it leaves the position alone', async ({ page }) => {
        const index = await chooseAShape(page);

        const wasAt = await page.locator('#atX').inputValue();
        const wasLeft = (await edgesOf(page, index)).left;

        await page.locator('#sizeX').fill('4');
        await page.locator('#sizeX').blur();

        await expect.poll(async () => (await edgesOf(page, index)).width, { timeout: 15000 })
            .toBe(4 * UNITS_PER_MICRON);

        expect((await edgesOf(page, index)).left).toBe(wasLeft);
        expect(await page.locator('#atX').inputValue()).toBe(wasAt);
    });

    ///One box is one axis. Making a width change a height would be a decision nobody asked for.
    test('typing a width leaves the height alone', async ({ page }) => {
        const index = await chooseAShape(page);

        const wasHeight = (await edgesOf(page, index)).height;

        await page.locator('#sizeX').fill('2.5');
        await page.locator('#sizeX').blur();

        await expect.poll(async () => (await edgesOf(page, index)).width, { timeout: 15000 })
            .toBe(2.5 * UNITS_PER_MICRON);

        expect((await edgesOf(page, index)).height).toBe(wasHeight);
    });

    test('and it can be taken back', async ({ page }) => {
        const index = await chooseAShape(page);

        const was = await edgesOf(page, index);

        await page.locator('#sizeY').fill('5');
        await page.locator('#sizeY').blur();

        await expect.poll(async () => (await edgesOf(page, index)).height, { timeout: 15000 })
            .toBe(5 * UNITS_PER_MICRON);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => (await edgesOf(page, index)).height, { timeout: 15000 }).toBe(was.height);
    });

    ///It rounds, unlike everything else here, and the panel says so rather than leaving it to be found out.
    test('the panel says that it rounds', async ({ page }) => {
        await chooseAShape(page);

        await expect(page.locator('#selectionPanel')).toContainText('rounds');
    });
});

test.describe('copying a cell', () => {
    test('the button is beside rename', async ({ page }) => {
        await page.locator('#cellActions').click();

        await expect(page.locator('#copyCell')).toBeVisible();
    });

    ///The same box names both, and which button is pressed says which was meant.
    test('a name already taken is refused', async ({ page }) => {
        await page.locator('#cellActions').click();

        await page.locator('#renameTo').fill(await currentCell(page));

        await expect(page.locator('#copyCell')).toBeDisabled();
    });

    ///
    ///**A copy is a second cell, and you end up looking at it.**
    ///
    ///Nothing places it yet, so this view draws it as a top of its own - which is the honest thing for a cell
    ///nothing references to be, and means the shapes appear twice until an instance is put down.
    ///
    test('copying makes a second cell and opens it', async ({ page }) => {
        const was = await currentCell(page);

        await page.locator('#cellActions').click();
        await page.locator('#renameTo').fill('SPARE');
        await page.locator('#copyCell').click();

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('SPARE');

        //And the original is still there, under its own name.
        await expect(page.locator('.cellRow').filter({ hasText: 'SPARE' }).first()).toBeVisible();
        await expect(named(page, was).first()).toBeVisible();
    });

    test('the copy holds the same number of shapes', async ({ page }) => {
        const before = await shapeCount(page, 'inContext');

        await page.locator('#cellActions').click();
        await page.locator('#renameTo').fill('SPARE');
        await page.locator('#copyCell').click();

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('SPARE');

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(before);
    });

    test('and it is one step that undoes', async ({ page }) => {
        const was = await currentCell(page);
        const before = await shapeCount(page);

        await page.locator('#cellActions').click();
        await page.locator('#renameTo').fill('SPARE');
        await page.locator('#copyCell').click();

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('SPARE');

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Copy cell/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);

        await expect(page.locator('.cellRow').filter({ hasText: 'SPARE' })).toHaveCount(0);
        await expect(named(page, was).first()).toBeVisible();
    });
});
