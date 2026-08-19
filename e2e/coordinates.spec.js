//Where the pointer is, and putting a shape somewhere exact.
//
//The formatting is pure and covered under Node. What is only checkable here is that the readout follows the
//pointer at all - its text is written straight into the element rather than through the component, so
//nothing in C# knows whether it is working - and that typing a position moves the shape to it.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints } = require('./helpers');

const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Picks one shape the cell holds, and hands back which one it is.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#atX').count() === 1
            && await page.locator('#gdsSVG .shapeSelected').count() === 1) {
            return page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');
        }
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

async function leftOf(page, index) {
    const points = await elementPoints(page, index);

    const numbers = points.trim().split(/[\s,]+/).map(Number);

    return Math.min(...numbers.filter((_, at) => at % 2 === 0));
}

test.describe('the readout', () => {
    test('follows the pointer', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        await expect(page.locator('#cursorAt')).toBeVisible();

        await page.mouse.move(view.x + 200, view.y + 200);

        await expect.poll(async () => page.locator('#cursorAt').textContent(), { timeout: 15000 })
            .toMatch(/µm/);

        const first = await page.locator('#cursorAt').textContent();

        await page.mouse.move(view.x + 400, view.y + 300);

        await expect.poll(async () => page.locator('#cursorAt').textContent(), { timeout: 15000 })
            .not.toBe(first);
    });

    ///It says where the pointer is, not where a snap would take it - the two differ and only one is a fact.
    test('reads in microns, to four places', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 250, view.y + 250);

        await expect.poll(async () => page.locator('#cursorAt').textContent(), { timeout: 15000 })
            .toMatch(/^-?\d+\.\d{4}, -?\d+\.\d{4} µm$/);
    });

    ///Whatever the pointer is being used for, where it is does not change.
    test('keeps up while a tool is in use', async ({ page }) => {
        await page.locator('#measureTool').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 300, view.y + 300);

        const first = await page.locator('#cursorAt').textContent();

        await page.mouse.move(view.x + 500, view.y + 380);

        await expect.poll(async () => page.locator('#cursorAt').textContent(), { timeout: 15000 })
            .not.toBe(first);
    });
});

test.describe('putting a shape somewhere exact', () => {
    test('the boxes say where the chosen shape is', async ({ page }) => {
        await enterCell(page);

        const index = await chooseAShape(page);

        const left = await leftOf(page, index);

        expect(Number(await page.locator('#atX').inputValue())).toBeCloseTo(left / UNITS_PER_MICRON, 4);
    });

    ///
    ///**Typing a position moves it there exactly.**
    ///
    ///The difference between where it is and where it is being asked to be is a distance, and every distance
    ///in this view goes into the cell the way a drag does - so this reaches the same edit the pointer does.
    ///
    test('typing a position moves the shape to it', async ({ page }) => {
        await enterCell(page);

        const index = await chooseAShape(page);

        await page.locator('#atX').fill('4.5');
        await page.locator('#atX').blur();

        await expect.poll(async () => leftOf(page, index), { timeout: 15000 })
            .toBe(4.5 * UNITS_PER_MICRON);
    });

    test('and it goes onto the undo stack as a move', async ({ page }) => {
        await enterCell(page);

        const index = await chooseAShape(page);

        const was = await leftOf(page, index);

        await page.locator('#atX').fill('7.25');
        await page.locator('#atX').blur();

        await expect.poll(async () => leftOf(page, index), { timeout: 15000 })
            .toBe(7.25 * UNITS_PER_MICRON);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => leftOf(page, index), { timeout: 15000 }).toBe(was);
    });

    ///Only along the axis that was typed into: the box is two numbers and each is its own.
    test('moving across leaves it at the height it was', async ({ page }) => {
        await enterCell(page);

        const index = await chooseAShape(page);

        const wasY = await page.locator('#atY').inputValue();

        await page.locator('#atX').fill('3');
        await page.locator('#atX').blur();

        await expect.poll(async () => leftOf(page, index), { timeout: 15000 })
            .toBe(3 * UNITS_PER_MICRON);

        expect(await page.locator('#atY').inputValue()).toBe(wasY);
    });

    ///The size is there to be read. Typing a width would be a scale, which rounds every corner of the shape.
    test('the size is shown but not typed into', async ({ page }) => {
        await enterCell(page);

        await chooseAShape(page);

        await expect(page.locator('#selectionPanel')).toContainText('µm (');
        await expect(page.locator('#selectionPanel')).toContainText('units)');
    });
});
