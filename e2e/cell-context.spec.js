//Descending into a cell to edit it, and climbing back out.
//
//The model is covered in CellContextTests - what is only checkable here is that descending marks the
//right shapes, the breadcrumb goes where it says, and the fade actually happens. Against the fixture,
//because no bundled file has a placement that resolves: see selection.spec.js.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, shapesMarked, snapToGrid, openedOnItsOwn, leaveCell, uploadFile } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);

    await uploadFile(page, 'e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    //One shape of the top's own, and one from each of the three placements of LEAF.
    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    await page.locator('#selectTool').click();
});

///Clicks shapes until one belonging to a placed cell is picked out, and returns its index.
async function clickIntoLeaf(page) {
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const text = await page.locator('#selectionPanel').textContent();

        if (text.includes('TOP > LEAF')) {
            //Again, on the same shape. The first click took hold of the placement - which is what a move or
            //a turn of the instance acts on - and the second goes inside it. See descendsOnClick.
            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            await expect(page.locator('#contextBar')).toContainText('LEAF');

            return i;
        }
    }

    throw new Error('no shape from a placed cell was found');
}

test('leaving the cell a file opens in puts everything back out of context', async ({ page }) => {
    //A file opens inside its own top cell now, so being outside every cell is somewhere to go rather than
    //where you start - and this is what it looks like when you get there.
    await leaveCell(page);

    //And nothing is marked one way or the other.
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(0);
    await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(0);
});

test('entering a cell fades what is outside it and marks what is in', async ({ page }) => {
    await clickIntoLeaf(page);


    await expect(page.locator('#contextBar')).toBeVisible();
    await expect(page.locator('#contextBar')).toContainText('LEAF');

    //One instance is the one being looked through; the other two move with it; the top's own square is
    //outside the cell entirely.
    await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(1);
    await expect.poll(async () => shapeCount(page, 'alsoAffected'), { timeout: 15000 }).toBe(2);
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(1);
});

///
///The count is the honest part: an edit in this cell moves three copies, not the one that was clicked.
///
///**Placements, not shapes.** The first version counted shapes held by the context, which on a flat file
///reads "21 instances" for twenty-one shapes in one structure - true of nothing. The number worth showing
///is how many times the cell is placed, because that is the multiplier on the edit.
///
test('the bar says how many times the cell is placed', async ({ page }) => {
    await clickIntoLeaf(page);

    await expect(page.locator('#contextBar')).toContainText('placed 3 times');
});

///A cell placed once is not worth a count, and a flat file is that case for its whole layout.
test('a cell placed once says nothing about how many times', async ({ page }) => {
    //The top structure, which holds one square directly and is placed nowhere.
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        if (!(await page.locator('#selectionPanel').textContent()).includes(' > '))
            break;
    }


    await expect(page.locator('#contextBar')).toBeVisible();
    await expect(page.locator('#contextBar')).not.toContainText('placed');
});

test('the fade is real rather than only a class', async ({ page }) => {
    await clickIntoLeaf(page);

    const faded = (await shapesMarked(page, 'outOfContext'))[0].opacity;
    const inside = (await shapesMarked(page, 'inContext'))[0].opacity;

    expect(faded).toBeLessThan(0.2);
    expect(inside).toBeGreaterThan(faded * 2);
});

test('entering the top level holds its own shape and not the placed ones', async ({ page }) => {
    //The top's own square is the one whose chain has no arrow in it.
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const text = await page.locator('#selectionPanel').textContent();

        if (!text.includes(' > '))
            break;
    }


    await expect(page.locator('#contextBar')).toContainText('TOP');

    await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(1);
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(3);

    //One instance, so no count is shown.
    await expect(page.locator('#contextBar')).not.toContainText('instances');
});

test('the breadcrumb climbs back out one level at a time', async ({ page }) => {
    await clickIntoLeaf(page);

    //TOP > LEAF, so pressing TOP goes up a level.
    await page.locator('#contextBar').getByRole('button', { name: 'TOP', exact: true }).click();

    await expect(page.locator('#contextBar')).toBeVisible();
    await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(1);
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(3);
});

test('All leaves the context and puts the fade back', async ({ page }) => {
    await clickIntoLeaf(page);

    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(1);

    await page.locator('#contextBar').getByRole('button', { name: 'All' }).click();

    await expect(page.locator('#contextBar')).toHaveCount(0);
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(0);
    await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(0);
});

///
///Clicking again inside the cell already being edited changes nothing about where you are.
///
///Entering re-marks every shape and rebuilds the markup, so doing it on every click within a cell would
///flicker the whole layout each time something was picked out - and the shape being chosen is the thing
///that has to survive it.
///
test('clicking within the cell being edited keeps you there', async ({ page }) => {
    const first = await clickIntoLeaf(page);

    await expect(page.locator('#contextBar')).toContainText('LEAF');

    await clickIntoLeaf(page);

    await expect(page.locator('#contextBar')).toContainText('LEAF');
    await expect(page.locator('#selectionPanel')).toBeVisible();
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

    expect(first).toBeGreaterThanOrEqual(0);
});

///Changing the opacity rebuilds every shape, and the context has to survive that - it is about the file.
test('a redraw keeps the context', async ({ page }) => {
    await clickIntoLeaf(page);

    await page.locator("#layerOpacity").fill('0.9');

    await expect(page.locator('#contextBar')).toBeVisible();
    await expect.poll(async () => shapeCount(page, 'outOfContext'), { timeout: 15000 }).toBe(1);
});

///
///**And it still works with snapping switched on**, which it did not.
///
///The hit test was asked at the *snapped* point. Snapping decides where a point goes; it has no business
///deciding what was clicked - and the crossing nearest a shape in a placed cell is outside it, so with
///snapping on a click reached nothing and there was no way to descend at all.
///
///Here rather than beside the other snapping tests because this is where it showed. A plain click on a
///top-level shape survives it: those are larger, and the crossing often still lands inside one - which is
///exactly why a test written there passed with the bug put back.
///
test('a cell can be entered while snapping is on', async ({ page }) => {
    await snapToGrid(page);

    await clickIntoLeaf(page);

    await expect(page.locator('#contextBar')).toBeVisible();
    await expect(page.locator('#contextBar')).toContainText('LEAF');
});
