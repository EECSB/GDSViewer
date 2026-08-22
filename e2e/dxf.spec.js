//Opening a DXF.
//
//The conversion is covered in DxfTests, entity by entity, against the library it produces. What is only
//checkable here is the way in: that the app decides the format from the front of the file rather than from
//its name, that a drawing draws, and that what comes back out of the download button is GDSII under a name
//somebody's tools will take - which is the one thing about reading a format nothing writes.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, selectView, openedOnItsOwn, uploadFile } = require('./helpers');

///
///A drawing with one square, one wire and a block placed twice.
///
///Written out in full rather than built up, because a DXF is only readable as a whole: the pairs mean nothing
///apart from the sections they sit in, and a helper that assembled them would hide the very structure this is
///checking gets walked.
///
const DRAWING = [
    '999', 'a test drawing',
    '0', 'SECTION', '2', 'HEADER',
    '9', '$INSUNITS', '70', '13',
    '0', 'ENDSEC',
    '0', 'SECTION', '2', 'TABLES',
    '0', 'TABLE', '2', 'LAYER',
    '0', 'LAYER', '2', 'OUTLINE',
    '0', 'LAYER', '2', 'WIRING',
    '0', 'ENDTAB',
    '0', 'ENDSEC',
    '0', 'SECTION', '2', 'BLOCKS',
    '0', 'BLOCK', '2', 'PAD', '10', '0', '20', '0',
    '0', 'LWPOLYLINE', '8', 'OUTLINE', '90', '4', '70', '1',
    '10', '0', '20', '0', '10', '2', '20', '0', '10', '2', '20', '2', '10', '0', '20', '2',
    '0', 'ENDBLK',
    '0', 'ENDSEC',
    '0', 'SECTION', '2', 'ENTITIES',
    '0', 'LWPOLYLINE', '8', 'OUTLINE', '90', '4', '70', '1',
    '10', '0', '20', '0', '10', '20', '20', '0', '10', '20', '20', '20', '10', '0', '20', '20',
    '0', 'LWPOLYLINE', '8', 'WIRING', '90', '2', '70', '0', '43', '1',
    '10', '2', '20', '10', '10', '18', '20', '10',
    '0', 'INSERT', '8', 'OUTLINE', '2', 'PAD', '10', '30', '20', '0',
    '0', 'INSERT', '8', 'OUTLINE', '2', 'PAD', '10', '30', '20', '10',
    '0', 'ENDSEC',
    '0', 'EOF', ''
].join('\n');

///Uploads a drawing under whatever name is given, and waits for something to be drawn.
async function openDrawing(page, name = 'parts.dxf', contents = DRAWING) {
    await uploadFile(page, {
        name,
        mimeType: 'application/dxf',
        buffer: Buffer.from(contents, 'utf8')
    });

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
}

test.beforeEach(async ({ page }) => {
    //With the cell tree open, since these read the library - see gotoApp.
    await gotoApp(page, '', true);
});

test.describe('opening one', () => {
    ///Two squares, a wire, and the block placed twice - four shapes drawn from a drawing with three entities.
    test('a drawing draws', async ({ page }) => {
        await openDrawing(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///
    ///**The format is read off the front of the file, not off its name.**
    ///
    ///A drawing renamed .gds is what a round trip through somebody's email produces, and it opens correctly
    ///for the same reason an OASIS called .gds does.
    ///
    test('a drawing named .gds still opens as one', async ({ page }) => {
        await openDrawing(page, 'renamed.gds');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///And the layer names come across, since GDSII has only numbers to remember them by.
    test('the drawing\'s layer names are shown', async ({ page }) => {
        await openDrawing(page);

        await expect(page.locator('#layerSidebar')).toContainText('OUTLINE');
        await expect(page.locator('#layerSidebar')).toContainText('WIRING');
    });

    ///The block became a cell, and the two inserts placements of it.
    test('a block becomes a cell that is placed', async ({ page }) => {
        await openDrawing(page);

        await selectView(page, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 30000 }).toBe(4);

        await expect(page.locator('.cellRow').filter({ hasText: 'PAD' })).toContainText('placed 2');
    });

    ///Something that is not a drawing is still refused, rather than half-read.
    test('a file that is neither is reported', async ({ page }) => {
        const said = [];

        await page.exposeFunction('reportAlert', message => said.push(String(message)));
        await page.evaluate(() => { window.alert = message => window.reportAlert(message); });

        await uploadFile(page, {
            name: 'notes.txt',
            mimeType: 'text/plain',
            buffer: Buffer.from('this is not a layout of any kind at all', 'utf8')
        });

        await openedOnItsOwn(page);

        await expect.poll(() => said.length, { timeout: 60000 }).toBeGreaterThan(0);
        expect(said.join(' ')).toMatch(/Could not read this file/);
    });
});

test.describe('what comes back out', () => {
    ///
    ///**A drawing downloads as GDSII, under a name that says so.**
    ///
    ///This reads DXF and nothing writes it, so the library in memory is GDSII whichever way it arrived -
    ///handing it back as parts.dxf would be a file every tool refuses on sight.
    ///
    test('it downloads as a .gds', async ({ page }) => {
        await openDrawing(page);

        const [download] = await Promise.all([
            page.waitForEvent('download', { timeout: 60000 }),
            page.locator('#downloadGds').click()
        ]);

        expect(download.suggestedFilename()).toMatch(/\.gds$/);
    });

    ///
    ///And the picker starts on GDSII rather than offering to keep a format nothing can write.
    ///
    test('the download format starts on GDSII', async ({ page }) => {
        await openDrawing(page);

        await expect(page.locator('#downloadFormat')).toHaveValue('.gds');
    });
});
