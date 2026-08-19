//What can be done to a cell rather than to what is in it: renaming it, deleting it, and repeating a
//placement of it as one array record.
//
//The bytes are covered in HierarchyTests - every placement renamed with the cell, a removed cell going back
//where it was, an array spaced by its step rather than by its whole width.
//
//What is only checkable here is the wiring: that the buttons refuse what they should, that renaming does not
//throw you out of the cell you are in, and that arraying a placement writes one element rather than many.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, shapeClearOfThePanel, openedOnItsOwn } = require('./helpers');

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

async function openCellActions(page) {
    await page.locator('#cellActions').click();

    await expect(page.locator('#renameTo')).toBeVisible();
}

///The cell being edited, as the breadcrumb has it.
async function currentCell(page) {
    return (await page.locator('.contextCrumbOn').textContent()).trim();
}

///Catches everything, so there is something to make a cell out of.
async function chooseEverything(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 5, view.y + 5);
    await page.mouse.down();
    await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
    await page.mouse.up();

    await expect(page.locator('#selectionPanel')).toContainText('shapes');
}

///Groups everything into a cell, leaving one instance of it in the cell being edited.
async function withAnInstance(page) {
    await chooseEverything(page);

    const drawn = await shapeCount(page);

    await page.locator('#makeCell').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

    return drawn;
}

test.describe('renaming', () => {
    test('the box starts at what the cell is called', async ({ page }) => {
        const name = await currentCell(page);

        await openCellActions(page);

        await expect(page.locator('#renameTo')).toHaveValue(name);

        //And renaming it to what it is called already is not a change.
        await expect(page.locator('#renameCell')).toBeDisabled();
    });

    test('a name already taken is refused, and says so', async ({ page }) => {
        await withAnInstance(page);

        await openCellActions(page);

        await page.locator('#renameTo').fill('CELL');

        await expect(page.locator('#renameCell')).toBeDisabled();
        await expect(page.locator('#renameCell')).toHaveAttribute('title', /already has a cell called CELL/);
    });

    test('an empty name is refused', async ({ page }) => {
        await openCellActions(page);

        await page.locator('#renameTo').fill('');

        await expect(page.locator('#renameCell')).toBeDisabled();
    });

    ///
    ///**Renaming does not throw you out of the cell.**
    ///
    ///Everything about where you are is held by name - the breadcrumb, what counts as editable, which shapes
    ///are drawn faded - so the context has to be rebuilt on the new one. Left alone, the next redraw would be
    ///looking for a cell that no longer answers to that.
    ///
    test('the breadcrumb follows the new name', async ({ page }) => {
        await openCellActions(page);

        await page.locator('#renameTo').fill('RENAMED');
        await page.locator('#renameCell').click();

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('RENAMED');

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Rename cell/);
    });

    test('and undoing puts the old name back', async ({ page }) => {
        const was = await currentCell(page);

        await openCellActions(page);

        await page.locator('#renameTo').fill('RENAMED');
        await page.locator('#renameCell').click();

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('RENAMED');

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBeGreaterThan(0);

        //Out of the cell, since undo lets go of where it was - but the name is back in the file.
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#selectionPanel')).toContainText(was);
    });
});

test.describe('deleting', () => {
    ///
    ///**Refused while anything still places it.**
    ///
    ///Deleting a referenced cell leaves every instance naming a cell that is not there: a file that parses,
    ///opens, and draws nothing where they were. The count is on the button so the answer to "why not" is
    ///there before it is pressed.
    ///
    test('a cell something places cannot be deleted, and says how many', async ({ page }) => {
        await withAnInstance(page);

        //Into the new cell, which the one above it now places. Two clicks: the first takes hold of that
        //placement, the second goes inside it. See descendsOnClick in Viewer2DSvg.
        await page.locator('#selectTool').click();

        //Clear of the selection panel, which the first of the two clicks opens over the canvas.
        const shape = await shapeClearOfThePanel(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect.poll(async () => currentCell(page), { timeout: 15000 }).toBe('CELL');

        await openCellActions(page);

        await expect(page.locator('#deleteCell')).toBeDisabled();
        await expect(page.locator('#deleteCell')).toHaveAttribute('title', /1 placement names this cell/);
    });

    test('one nothing places can be, and the shapes go with it', async ({ page }) => {
        const drawn = await shapeCount(page);

        expect(drawn).toBeGreaterThan(0);

        await openCellActions(page);

        await expect(page.locator('#deleteCell')).toBeEnabled();

        await page.locator('#deleteCell').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(0);

        //And there is nowhere to be any more.
        await expect(page.locator('#contextBar')).toHaveCount(0);
    });

    test('and undoing brings the cell back', async ({ page }) => {
        const drawn = await shapeCount(page);

        await openCellActions(page);
        await page.locator('#deleteCell').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(0);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(drawn);
    });
});

test.describe('arraying a placement', () => {
    ///Choosing a shape reached through the instance, which is what an array repeats.
    async function chooseThroughTheInstance(page) {
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#arrayOpen')).toBeVisible();

        await page.locator('#arrayOpen').click();

        await expect(page.locator('#arrayColumns')).toBeVisible();
    }

    ///
    ///**One record where copying would be one element per place.**
    ///
    ///The whole reason an array reference exists, and what the copying path cannot do. It became possible
    ///the moment cells could be made: an AREF places a cell, and until then there was never one to point at.
    ///
    test('the button offers an array rather than a count of copies', async ({ page }) => {
        await withAnInstance(page);
        await chooseThroughTheInstance(page);

        await page.locator('#arrayColumns').fill('3');
        await page.locator('#arrayColumns').blur();

        await page.locator('#arrayRows').fill('2');
        await page.locator('#arrayRows').blur();

        //"Add 3 × 2 array" - the verb first, like the other two things this button can say.
        await expect(page.locator('#arrayMake')).toContainText('Add 3');
        await expect(page.locator('#arrayMake')).toContainText('array');
        await expect(page.locator('#arrayMake')).toHaveAttribute('title', /one array record/);
    });

    test('arraying draws the cell once per place', async ({ page }) => {
        const drawn = await withAnInstance(page);

        await chooseThroughTheInstance(page);

        await page.locator('#arrayColumns').fill('3');
        await page.locator('#arrayColumns').blur();

        await page.locator('#arrayRows').fill('2');
        await page.locator('#arrayRows').blur();

        await page.locator('#arrayMake').click();

        //Six places, each drawing everything the cell holds.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(drawn * 6);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Array/);
    });

    test('and undoing leaves the one instance it was', async ({ page }) => {
        const drawn = await withAnInstance(page);

        await chooseThroughTheInstance(page);

        await page.locator('#arrayColumns').fill('4');
        await page.locator('#arrayColumns').blur();

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(drawn * 4);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(drawn);
    });

    test('an array is in the file that is downloaded', async ({ page }) => {
        const drawn = await withAnInstance(page);

        await chooseThroughTheInstance(page);

        await page.locator('#arrayColumns').fill('3');
        await page.locator('#arrayColumns').blur();

        await page.locator('#arrayMake').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(drawn * 3);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(drawn * 3);
    });
});
