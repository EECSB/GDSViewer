//An undo stack that outlives the page.
//
//The library tests already prove the hard part - that a stack written down and rebuilt against a reopened
//file undoes onto the right shapes, byte for byte. See EditPersistenceTests.
//
//What is only checkable here is the wiring: that the stack reaches the session at all, that it comes back
//with the file rather than after it, and that leaving the 2D view for another one and coming back does not
//quietly throw it away - which it did, because the view is destroyed and rebuilt each time.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, chooseShape, selectView } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///Enters a cell and deletes one shape, which is one step on the stack and one shape fewer on screen.
async function deleteAShape(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    const before = await shapeCount(page);

    const inside = await shapeBox(page, 0, 'inContext');

    await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));
    await chooseShape(page, '#deleteShape');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 })
        .toBe(before - 1);

    return before;
}

///Reopens the app the way a refresh does - no query string, so the session is what opens the file.
async function refresh(page) {
    await page.goto('/');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
}

test.describe('a refresh', () => {
    ///The change itself already survived, because the session carries the file's bytes. The stack did not.
    test('keeps the change and the ability to take it back', async ({ page }) => {
        const before = await deleteAShape(page);

        await refresh(page);

        //Still deleted, which was already true before any of this.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 1);

        //And still undoable, which was not.
        await expect(page.locator('#undoEdit')).toBeEnabled();
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Delete/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    test('and the ability to put it back again', async ({ page }) => {
        const before = await deleteAShape(page);

        await refresh(page);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);

        await page.locator('#redoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);
    });

    ///What had been undone before the page went away is still waiting to be redone after it.
    test('keeps a redo that was already waiting', async ({ page }) => {
        const before = await deleteAShape(page);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);

        await refresh(page);

        await expect(page.locator('#undoEdit')).toBeDisabled();
        await expect(page.locator('#redoEdit')).toBeEnabled();

        await page.locator('#redoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);
    });

    ///Several steps, taken back one at a time - so the stack came back as a stack and not as one lump.
    test('keeps a stack of them, in order', async ({ page }) => {
        await page.locator('#selectTool').click();

        const first = await shapeBox(page);

        await page.mouse.click(first.x + (first.width / 2), first.y + (first.height / 2));

        const before = await shapeCount(page);

        for (let i = 0; i < 3; i++) {
            const inside = await shapeBox(page, 0, 'inContext');

            await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));
            await chooseShape(page, '#deleteShape');

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before - (i + 1));
        }

        await refresh(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 3);

        for (let i = 2; i >= 0; i--) {
            await page.locator('#undoEdit').click();

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before - i);
        }

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });
});

///
///**The stack belongs to the file, not to the view.**
///
///The 2D view is destroyed and rebuilt every time somebody looks at another one and comes back, so a stack
///that lived in it went with it - and the session written while the other view was on screen had no stack
///in it either, because each view writes its own settings and the 3D one has never heard of this.
///
test.describe('leaving the view', () => {
    test('and coming back keeps what can be undone', async ({ page }) => {
        const before = await deleteAShape(page);

        await selectView(page, 'ViewText');

        await expect(page.locator('#gdsSVG')).toHaveCount(0);

        await selectView(page, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(before - 1);

        await expect(page.locator('#undoEdit')).toBeEnabled();

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });
});

///
///Opening something else is not a refresh. The edits were made against a library that is no longer open, and
///a stack of those would apply itself to nothing - or worse, to something.
///
test.describe('a different file', () => {
    test('starts with nothing to undo', async ({ page }) => {
        await deleteAShape(page);

        await expect(page.locator('#undoEdit')).toBeEnabled();

        await page.goto('/?file=sky130_fd_sc_hd__nand2_1&view=View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        await expect(page.locator('#undoEdit')).toHaveCount(0);
    });
});
