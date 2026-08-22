//What the file turned out not to be.
//
//The flattener has always worked out three things nobody was ever told: that nesting was cut short because a
//cell contains itself, that cells a file places are not in it, and now that a layout is larger than this will
//draw. Only the CLI read them. The app opened such a file and drew what it had without a word - and in a tool
//for checking layouts, geometry quietly absent is the worst thing that can happen.
//
//What the flags mean is covered in C#. What is only checkable here is that they reach the screen.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, openedOnItsOwn, uploadFile } = require('./helpers');

///A library whose top cell places a cell that is not in it, which is what a standalone cell file looks like.
const DANGLING = [
    '0', 'SECTION', '2', 'ENTITIES',
    '0', 'INSERT', '8', 'A', '2', 'MISSING', '10', '0', '20', '0',
    '0', 'LWPOLYLINE', '8', 'A', '90', '4', '70', '1',
    '10', '0', '20', '0', '10', '9', '20', '0', '10', '9', '20', '9', '10', '0', '20', '9',
    '0', 'ENDSEC', '0', 'EOF', ''
].join('\n');

///
///**A whole layout says nothing at all.**
///
///A banner that is always there is a banner nobody reads, and this one has to be read - so the ordinary case
///is silence, and it is worth a test of its own that the ordinary case stays silent.
///
test('a layout that is all there says nothing', async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await expect(page.locator('#layoutNotice')).toHaveCount(0);
});

///
///And a file placing cells it does not contain says which. Normal rather than broken - a standalone cell
///references the rest of its library without including it - but the shapes are missing from the picture
///either way, and that is the part somebody has to know.
///
test('a file placing cells it does not contain says so', async ({ page }) => {
    //Opened onto a file first rather than onto the bare app: the upload has to reach a runtime that has
    //finished booting, and waiting for a drawn layout is the only signal that says it has.
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await uploadFile(page, {
        name: 'dangling.dxf',
        mimeType: 'application/dxf',
        buffer: Buffer.from(DANGLING, 'utf8')
    });

    await openedOnItsOwn(page);

    await expect(page.locator('#layoutNotice')).toBeVisible({ timeout: 60000 });

    const said = await page.locator('#layoutNotice').textContent();

    expect(said).toContain('not in it');
    expect(said).toContain('MISSING');
});

///It goes when a file that is all there is opened, rather than staying from the last one.
test('opening a whole layout afterwards clears it', async ({ page }) => {
    //Opened onto a file first rather than onto the bare app: the upload has to reach a runtime that has
    //finished booting, and waiting for a drawn layout is the only signal that says it has.
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await uploadFile(page, {
        name: 'dangling.dxf',
        mimeType: 'application/dxf',
        buffer: Buffer.from(DANGLING, 'utf8')
    });

    await openedOnItsOwn(page);

    await expect(page.locator('#layoutNotice')).toBeVisible({ timeout: 60000 });

    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    //Polled rather than read once: the shapes of the new file are drawn by the view and the banner belongs
    //to the shell, so the two land on different renders and reading straight after the first catches the
    //moment between them.
    await expect.poll(async () => page.locator('#layoutNotice').count(), { timeout: 15000 }).toBe(0);
});
