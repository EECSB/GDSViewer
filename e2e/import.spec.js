const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const { gotoApp, gotoExample, MOSFET, shapeCount, shapeBox, openFile, selectView } = require('./helpers');

///
///Opening a file while another one is already open.
///
///The app used to replace what was on screen without asking, which is right when somebody has finished with
///a layout and wrong when they meant to put one file inside another. Both answers are ordinary, so the
///upload asks - and the answer that was not reachable before is the interesting one: the incoming file's
///cells join the open library and its top cell is carried on the pointer, the way a cell picked out of the
///tree already was.
///
///Uploads go through gotoApp first, always. A file set on #fileUpload after a bare goto arrives before
///Blazor's InputFile has attached its listener, and is silently dropped - see helpers.js.
///
const NAND = path.join(__dirname, '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', 'sky130_fd_sc_hd__nand2_1.gds');

///Uploads a real sky130 cell, which is a two-cell library and so has something to import.
async function upload(page, name = 'nand2.gds') {
    await page.locator('#fileUpload').setInputFiles({
        name,
        mimeType: 'application/octet-stream',
        buffer: fs.readFileSync(NAND)
    });
}

///
///The cells the library holds, off the docked tree the specs open with tree=true.
///
///The tree lists a cell nothing places at its own root, so an imported cell shows up there before it has
///been put down anywhere - which is what makes this readable at all between the import and the placement.
///
async function cellNames(page) {
    if (await page.locator('#cellTree').count() === 0)
        await page.locator('#cellTreeButton').click();

    await expect(page.locator('#cellTree')).toBeVisible({ timeout: 30000 });

    const names = await page.locator('#cellTree .cellRowPair[data-kind="cell"]').allTextContents();

    return names.map(name => name.trim());
}

///How many cells the tree is showing, polled - the tree redraws after the edit, not with it.
function cellRowCount(page) {
    return page.locator('#cellTree .cellRowPair[data-kind="cell"]').count();
}

test.describe('a file opened while one is already open', () => {
    test('asks rather than replacing what is on screen', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const before = await shapeCount(page);

        await upload(page);

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });

        //Nothing has happened yet: the layout on screen is the one that was there.
        expect(await shapeCount(page)).toBe(before);

        await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
    });

    ///Cancelling is the third answer, and the one that has to leave no trace at all.
    test('cancelling keeps the open file and forgets the new one', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const before = await shapeCount(page);

        await upload(page);

        await page.locator('#importCancel').click();

        await expect(page.locator('#importDialog')).toHaveCount(0);

        expect(await shapeCount(page)).toBe(before);

        await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
    });

    ///The answer the app already had, still reachable - this is what an upload used to do with no question.
    test('opening it on its own replaces the layout, as it always did', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        await upload(page);

        await page.locator('#importAsFile').click();

        await expect(page.locator('#importDialog')).toHaveCount(0);

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');
    });

    ///
    ///**The cells arrive and the top one is on the pointer**, which is the whole feature.
    ///
    ///Both halves are asserted because either can happen without the other: the import can land with nothing
    ///picked up, and something can be picked up that was never imported.
    ///
    test('adding it brings the cells in and puts the top one on the pointer', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const before = await cellNames(page);

        expect(before.some(name => name.includes('nand2'))).toBe(false);

        await upload(page);

        await page.locator('#importAsCell').click();

        await expect(page.locator('#importDialog')).toHaveCount(0);

        //Carried: the ghost exists and is drawn, before anything has been put down.
        await expect(page.locator('#carriedCell')).toHaveCount(1, { timeout: 60000 });

        await expect.poll(async () => page.locator('#carriedCell path, #carriedCell polygon').count(),
            { timeout: 30000 }).toBeGreaterThan(0);

        //And the library holds what came in, under the open file's own name still.
        await expect.poll(async () => cellRowCount(page), { timeout: 30000 }).toBeGreaterThan(before.length);

        const after = await cellNames(page);

        expect(after.some(name => name.includes('nand2'))).toBe(true);

        await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
    });

    ///
    ///The ghost follows the pointer, which is the part a static check cannot see.
    ///
    ///Read as the transform the interop moves rather than as a screenshot: startCarrying moves one transform
    ///per pointer move precisely so that carrying a cell of hundreds of shapes does not re-render anything,
    ///and the transform is what that machinery actually writes.
    ///
    test('what is carried follows the pointer', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        await upload(page);

        await page.locator('#importAsCell').click();

        await expect(page.locator('#carriedCell')).toHaveCount(1, { timeout: 60000 });

        const canvas = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(canvas.x + canvas.width * 0.3, canvas.y + canvas.height * 0.3);

        const first = await page.locator('#carriedCell').getAttribute('transform');

        await page.mouse.move(canvas.x + canvas.width * 0.7, canvas.y + canvas.height * 0.6);

        await expect.poll(async () => page.locator('#carriedCell').getAttribute('transform'),
            { timeout: 15000 }).not.toBe(first);
    });

    ///Putting it down is a placement in the library, which is what makes this an import and not a paste.
    test('a click puts it down as a placement', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const before = await shapeCount(page);

        await upload(page);

        await page.locator('#importAsCell').click();

        await expect(page.locator('#carriedCell')).toHaveCount(1, { timeout: 60000 });

        const canvas = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
        await page.mouse.down();
        await page.mouse.up();

        //More on screen than there was, because the placed cell draws its own shapes into the layout.
        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(before);
    });

    ///
    ///
    ///The question belongs to the 2D view, since the other two have no pointer to place anything with.
    ///
    ///Opened by a link so the file is a deliberate one - otherwise this would pass for the wrong reason,
    ///the app skipping the question over its own untouched example rather than over the view.
    ///
    test('the question is not asked in a view that cannot place anything', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        await selectView(page, 'View3D');

        await upload(page);

        await expect(page.locator('#importDialog')).toHaveCount(0);

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');
    });
});

///
///The first upload of a visit is not a question.
///
///The app opens an example of its own when no link names a file and there is no session to restore, so
///there is always something on screen - and without this the very first upload would be asked whether to
///add to a layout nobody chose and nobody has touched. The answer there is always "open on its own", which
///makes it a step rather than a choice.
///
test.describe('the example the app opened for itself', () => {
    test('is replaced by an upload without asking', async ({ page }) => {
        await gotoApp(page);

        //What the app chose for itself, which is the file this is about.
        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('Mosfet');

        await upload(page);

        //Straight through: no dialog at any point, and the uploaded file is what is open.
        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');

        await expect(page.locator('#importDialog')).toHaveCount(0);
    });

    ///
    ///**One edit and the question comes back**, which is the line the whole exception turns on.
    ///
    ///An untouched suggestion costs nothing to replace. A layout somebody has drawn in is theirs, whatever
    ///it started as, and replacing that silently would throw the work away - so the same file, edited, is
    ///asked about.
    ///
    test('is asked about once something has been drawn into it', async ({ page }) => {
        await gotoApp(page);

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('Mosfet');

        //
        //An edit by the ordinary route: the first click enters the cell, the second chooses a shape in it,
        //and Delete takes it out. Which shape does not matter - that the file has been changed does.
        //
        const before = await shapeCount(page);

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await page.keyboard.press('Delete');

        await expect.poll(async () => shapeCount(page), { timeout: 30000 }).toBeLessThan(before);

        await upload(page);

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });
    });
});
