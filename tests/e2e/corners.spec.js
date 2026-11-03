//Rounding and cutting the corners of what is chosen.
//
//The geometry - an arc's every point on its circle, a radius too big for a wire coming down to fit, an inside
//corner filled - is C#'s and covered in CornersTests. What is here is the part only the editor has: the row
//under Grow, one press of undo, the selection kept, and a shape that comes out with the corners asked for.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, snapToGrid } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
});

///One shape of the cell chosen on its own, by index - see the same helper in turning.spec.js for why.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        if (await page.locator('#roundCorners').count() === 0)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');
        const points = await elementPoints(page, index);

        //A rectangle, so the counts below are a rectangle's.
        if (points !== null && points.trim().split(/\s+/).length <= 5)
            return index;
    }

    throw new Error('no rectangle of this cell could be chosen on its own');
}

async function cornerCount(page, index) {
    const points = await elementPoints(page, index);

    if (points === null)
        return 0;

    const pairs = points.trim().split(/\s+/);

    //A closed outline repeats its first corner, which is not a corner of its own.
    if (pairs[0] === pairs[pairs.length - 1])
        return pairs.length - 1;

    return pairs.length;
}

test('Round turns a rectangle\'s four corners into arcs, as one press of undo', async ({ page }) => {
    //Off, so every corner of the arcs is its own: a 20 nm radius has no room for seventeen points a quarter on
    //the bundled deck's 5 nm grid, where snapping puts them - deck-grid.spec is about that.
    await snapToGrid(page, false);

    const index = await chooseAShape(page);

    expect(await cornerCount(page, index)).toBe(4);

    await page.locator('#cornerBy').fill('0.02');
    await page.locator('#cornerBy').blur();

    await page.locator('#roundCorners').click();

    //Sixty-four sides an ellipse is the starting setting, so a right angle is sixteen steps: seventeen points
    //a corner, four corners.
    await expect.poll(async () => cornerCount(page, index), { timeout: 15000 }).toBe(4 * 17);

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Round corners/);

    //Changed where it stands, so it is still the shape chosen.
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => cornerCount(page, index), { timeout: 15000 }).toBe(4);
});

test('Cut takes each corner off straight, leaving eight', async ({ page }) => {
    const index = await chooseAShape(page);

    await page.locator('#cornerBy').fill('0.02');
    await page.locator('#cornerBy').blur();

    await page.locator('#cutCorners').click();

    await expect.poll(async () => cornerCount(page, index), { timeout: 15000 }).toBe(8);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Cut corners/);
});

///A distance of nothing changes no corner, and the buttons say so rather than doing nothing when pressed.
test('a distance of nothing leaves both buttons off', async ({ page }) => {
    await chooseAShape(page);

    await page.locator('#cornerBy').fill('0');
    await page.locator('#cornerBy').blur();

    await expect(page.locator('#roundCorners')).toBeDisabled();
    await expect(page.locator('#cutCorners')).toBeDisabled();
});
