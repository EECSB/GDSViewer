//Renaming, copying and deleting from the row itself, on the right button.
//
//The three words are the same at every level and mean something different at each: a cell is renamed in the
//library, a layer in the table the whole app shares, a label in its own record. What is only checkable in a
//browser is that the right menu comes up on the right row and that its lines reach the things they name -
//the edits themselves are covered in LayoutEditTests, and the menu's own placement in shape-menu.spec.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, layerLabel } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg', true);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await expect(page.locator('#cellTree')).toBeVisible();
});

///The rows of the docked tree, by which of the three levels they are.
function rows(page, kind) {
    return page.locator(`#cellTree .cellRowPair[data-kind="${kind}"]`);
}

///Raises the menu on a row and waits for it to be there.
async function menuOn(locator, page) {
    await locator.click({ button: 'right' });

    await expect(page.locator('#shapeMenu')).toBeVisible();
}

///What the menu is offering, and whether each line can be pressed.
async function lines(page) {
    return page.locator('#shapeMenu .shapeMenuItem').evaluateAll(found =>
        found.map(one => ({
            says: one.querySelector('.shapeMenuSays').textContent.trim(),
            offered: !one.disabled
        })));
}

///Enters the cell the click lands in, so edits are allowed.
async function enterTheCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#contextBar')).toBeVisible();
}

test.describe('a cell row', () => {
    ///
    ///The three the bar above the view offers, aimed at a row instead.
    ///
    ///Which is the point of having them here: the bar acts on the cell being edited, so renaming any other
    ///cell meant entering it first, and a cell nothing places cannot be entered by clicking a shape at all.
    ///
    test('offers rename, copy and delete', async ({ page }) => {
        await menuOn(rows(page, 'cell').first(), page);

        expect((await lines(page)).map(one => one.says)).toEqual(['Rename', 'Copy', 'Delete']);
    });

    ///
    ///Delete refuses while something places the cell, and says how many.
    ///
    ///A cell taken out from under its placements leaves every one of them naming a cell that is not there -
    ///a file that parses, opens, and draws nothing where they were. The count is the answer to "why not",
    ///which is why the line is disabled rather than missing.
    ///
    test('will not delete a cell something places', async ({ page }) => {
        await enterTheCell(page);

        //Group a shape into a cell of its own, which gives the file a placed cell to try this on.
        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        await expect.poll(async () => rows(page, 'cell').count(), { timeout: 15000 }).toBeGreaterThan(1);

        await menuOn(rows(page, 'cell').nth(1), page);

        const found = await lines(page);
        const remove = found.find(one => one.says === 'Delete');

        expect(remove.offered).toBe(false);
        await expect(page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Delete' }))
            .toHaveAttribute('title', /placement/);
    });

    ///<summary>Rename turns the row into a box, and Enter puts the new name on the cell.</summary>
    test('renames in the row itself', async ({ page }) => {
        await menuOn(rows(page, 'cell').first(), page);

        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Rename' }).click();

        const box = page.locator('#cellTree .cellRowNameBox');

        await expect(box).toBeVisible();

        await box.fill('RENAMED');
        await box.press('Enter');

        await expect(box).toHaveCount(0);
        await expect(rows(page, 'cell').first()).toContainText('RENAMED');

        //One step on the stack, named for what it was.
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Rename/, { timeout: 15000 });
    });

    ///<summary>And Escape leaves the name alone, which is the other half of a box.</summary>
    test('Escape abandons the rename', async ({ page }) => {
        const was = (await rows(page, 'cell').first().textContent()).trim();

        await menuOn(rows(page, 'cell').first(), page);
        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Rename' }).click();

        const box = page.locator('#cellTree .cellRowNameBox');

        await box.fill('NOTTHIS');
        await box.press('Escape');

        await expect(box).toHaveCount(0);
        await expect(rows(page, 'cell').first()).not.toContainText('NOTTHIS');
        expect((await rows(page, 'cell').first().textContent()).trim()).toBe(was);
    });

    ///
    ///Copy makes a second cell under a name nothing is using.
    ///
    ///Named rather than asked for: there is no box on a menu line, and stopping to invent a name is not what
    ///somebody pressing Copy on a row is doing. The copy can be renamed from the same menu afterwards.
    ///
    test('copies a cell under a free name', async ({ page }) => {
        const before = await rows(page, 'cell').count();
        const name = (await rows(page, 'cell').first().locator('.cellRowName').textContent()).trim();

        await menuOn(rows(page, 'cell').first(), page);
        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Copy' }).click();

        await expect.poll(async () => rows(page, 'cell').count(), { timeout: 15000 }).toBe(before + 1);

        await expect(page.locator('#cellTree')).toContainText(`${name}_1`);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Copy cell/, { timeout: 15000 });
    });
});

test.describe('a layer row', () => {
    ///Opens the first layer row of the tree, which needs its cell open first.
    async function layerRow(page) {
        await expect.poll(async () => rows(page, 'layer').count(), { timeout: 15000 }).toBeGreaterThan(0);

        return rows(page, 'layer').first();
    }

    ///
    ///Rename, and copy or delete the shapes the layer holds in that cell.
    ///
    ///A layer runs through the whole library, so "delete this layer" has to mean something with an extent
    ///somebody can see. The cell the row sits under is the scope the row is already showing a count for.
    ///
    test('offers rename, and the shapes it holds', async ({ page }) => {
        await menuOn(await layerRow(page), page);

        const found = await lines(page);

        expect(found[0].says).toBe('Rename');
        expect(found[1].says).toMatch(/^Copy/);
        expect(found[2].says).toMatch(/^Delete/);

        //The count is on the line, so it says how much it is about to act on.
        expect(found[2].says).toMatch(/Delete (the shape|\d+ shapes)/);
    });

    ///
    ///Copy and delete are refused outside the cell the row belongs to.
    ///
    ///Editing is only allowed in the cell being edited - that is what the whole context machinery is for -
    ///and a line that quietly did nothing would look exactly like the menu being broken.
    ///
    test('will not edit a layer of a cell that is not open', async ({ page }) => {
        await menuOn(await layerRow(page), page);

        const found = await lines(page);

        expect(found.find(one => one.says.startsWith('Copy')).offered).toBe(false);
        expect(found.find(one => one.says.startsWith('Delete')).offered).toBe(false);

        //Rename is not an edit to the file at all, so it is offered wherever you are.
        expect(found[0].offered).toBe(true);
    });

    ///<summary>Inside the cell, deleting a layer's shapes takes exactly those away, as one step.</summary>
    test('deletes the shapes it holds, as one step', async ({ page }) => {
        await enterTheCell(page);

        const before = await shapeCount(page, 'inContext');

        expect(before).toBeGreaterThan(1);

        await menuOn(await layerRow(page), page);

        const remove = page.locator('#shapeMenu .shapeMenuItem', { hasText: /^Delete/ });

        //How many the line said it would take.
        const said = Number(/(\d+)/.exec(await remove.textContent())?.[1] ?? 1);

        await remove.click();

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(before - said);

        //One press of undo, not one per shape.
        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(before);
    });

    ///<summary>Renaming from the tree names the layer everywhere, since a layer's name is the app's own.</summary>
    test('renaming a layer reaches the sidebar too', async ({ page }) => {
        await menuOn(await layerRow(page), page);
        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Rename' }).click();

        const box = page.locator('#cellTree .cellRowNameBox');

        await expect(box).toBeVisible();

        await box.fill('poly');
        await box.press('Enter');

        await expect(box).toHaveCount(0);

        //
        //The sidebar's list is the other place that says what a layer is called.
        //
        //layerLabel rather than layerPairs: the pairs helper strips a row down to its numbers on purpose,
        //so a name would not show up in it however well the rename worked.
        //
        const named = (await rows(page, 'layer').first().locator('.cellRowName').textContent()).trim();
        const pair = /(\d+\/\d+)/.exec(named)?.[1] ?? named;

        await expect.poll(async () => layerLabel(page, pair), { timeout: 15000 }).toContain('poly');
    });
});

test.describe('a layer row of the sidebar', () => {
    ///
    ///The same menu, raised from the other list that shows layers.
    ///
    ///Drawn by the 2D view rather than by the shell: two of the three lines are edits to the layout, and a
    ///second menu in the sidebar would be a second set of the same answers to keep in step.
    ///
    test('raises the same three', async ({ page }) => {
        await page.locator('.layerRow').first().click({ button: 'right' });

        await expect(page.locator('#shapeMenu')).toBeVisible();

        const found = await lines(page);

        expect(found[0].says).toBe('Rename');
        expect(found[1].says).toMatch(/^Copy/);
        expect(found[2].says).toMatch(/^Delete/);
    });

    ///<summary>And it acts on the cell being edited, which is where an edit can land.</summary>
    test('copies the shapes of that layer in the cell being edited', async ({ page }) => {
        await enterTheCell(page);

        await page.locator('.layerRow').first().click({ button: 'right' });

        await expect(page.locator('#shapeMenu')).toBeVisible();

        const copy = page.locator('#shapeMenu .shapeMenuItem', { hasText: /^Copy/ });

        await expect(copy).toBeEnabled();

        await copy.click();

        //
        //Something is on the clipboard now, which is the one visible result of a copy.
        //
        //Off the right of the view: the selection panel sits over the left of it and takes the press
        //before the canvas can, so a menu raised near the corner never comes up at all.
        //
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + (view.width * 0.88), view.y + (view.height * 0.12), { button: 'right' });

        await expect(page.locator('#shapeMenu .shapeMenuItem', { hasText: /^Paste/ })).toHaveCount(1);
    });
});

test.describe('a shape row', () => {
    ///Opens a layer of the tree so its shapes are listed, and gives back the first shape row.
    async function shapeRow(page) {
        await expect.poll(async () => rows(page, 'layer').count(), { timeout: 15000 }).toBeGreaterThan(0);

        await rows(page, 'layer').first().locator('.cellRowFold').click();

        await expect.poll(async () => rows(page, 'shape').count(), { timeout: 15000 }).toBeGreaterThan(0);

        return rows(page, 'shape').first();
    }

    ///
    ///Rename is a label's line and nothing else's, and says so rather than going missing.
    ///
    ///A boundary has no name to change - it has a layer and a set of corners. The line is there and refuses,
    ///so the menu is the same length for two rows that look the same.
    ///
    test('offers rename only for a label', async ({ page }) => {
        await menuOn(await shapeRow(page), page);

        const found = await lines(page);

        expect(found.map(one => one.says)).toEqual(['Rename', 'Copy', 'Delete']);

        const named = (await rows(page, 'shape').first().textContent()).includes('label');

        expect(found[0].offered).toBe(named);
    });

    ///<summary>Delete takes that one shape away, having entered its cell to do it.</summary>
    test('deletes the one shape', async ({ page }) => {
        const before = await shapeCount(page);

        await menuOn(await shapeRow(page), page);

        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Delete' }).click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBeLessThan(before);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Delete/, { timeout: 15000 });
    });
});
