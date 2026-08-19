//Opening an OASIS file, the format meant to replace GDSII.
//
//The reader itself is covered by OasisTests, which checks it against all 897 samples converted by
//KLayout. What needs a browser is the rest of the path: that the format is recognized from the file
//rather than its name, that what comes out draws, and that it reaches everything a .gds does.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    openFile,
    layerPairs,
    svgCounts,
    selectView,
    MOSFET,
    MOSFET_POLYGONS,
    MOSFET_LABELS,
    MOSFET_LAYER_PAIRS, openedOnItsOwn } = require('./helpers');

///
///Mosfet.gds as OASIS, converted by KLayout and committed.
///
///Committed rather than converted here: the reader's own tests make these with KLayout, and making the
///browser suite depend on a tool being installed would be a poor trade for one 583-byte file.
///
function oasisBytes() {
    return require('fs').readFileSync(require('path').join(__dirname, 'fixtures', 'Mosfet.oas'));
}

///
///Uploads, and fails outright if the app complained about it.
///
///The shell names a file *before* it parses it, so waiting on the name proves nothing: an upload that is
///refused leaves the previous layout on screen under the new file's name. Every test in this file uploads
///the same layout the app already has open, so a refused upload draws exactly the right number of
///polygons on exactly the right layers - which is how the first version of this passed while OASIS uploads
///were failing on a synchronous read the browser does not allow.
///
async function upload(page, name, bytes) {
    const complaints = [];
    const onDialog = (dialog) => {
        complaints.push(dialog.message());

        dialog.dismiss();
    };

    page.on('dialog', onDialog);

    await page.locator('#fileUpload').setInputFiles({ name, mimeType: 'application/octet-stream', buffer: bytes });

    await openedOnItsOwn(page);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(name);

    //Given a moment to arrive: the parse runs after the name is set, so a complaint about it comes later.
    await page.waitForTimeout(500);

    page.off('dialog', onDialog);

    if (complaints.length > 0)
        throw new Error(`The app refused ${name}: ${complaints.join(' | ')}`);
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

///
///The whole point: an OASIS file opens and draws the same layout as the GDSII it came from.
///
test('an OASIS file opens and draws', async ({ page }) => {
    await upload(page, 'converted.oas', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);
    await expect.poll(async () => (await svgCounts(page)).labels).toBe(MOSFET_LABELS);
});

///And onto the same layers, which is what the sidebar lists.
test('its layers come out as the same pairs', async ({ page }) => {
    await upload(page, 'converted.oas', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);

    expect(await layerPairs(page)).toEqual(MOSFET_LAYER_PAIRS);
});

///
///The format is read off the file, not off its name.
///
///An extension is a guess about a file that the file itself has already answered, and a renamed one is
///common - a .oas mailed as .gds, or the other way round. Both of these are the same bytes.
///
test('the format is decided by the file, not by what it is called', async ({ page }) => {
    await upload(page, 'misnamed.gds', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);
});

///It reaches the 3D view too, which builds from the same flattened layout.
test('an OASIS file draws in 3D as well', async ({ page }) => {
    await upload(page, 'converted.oas', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();
});

///Reads a download, however many chunks it arrives in.
async function bytesOf(download) {
    const stream = await download.createReadStream();
    const chunks = [];

    for await (const chunk of stream)
        chunks.push(chunk);

    return Buffer.concat(chunks);
}

///
///A file that arrived as OASIS goes back out as OASIS without anyone choosing.
///
///It is a GDSII library in memory either way - the reader converts on the way in - so this is the picker
///beside the download button defaulting to the format the file came in, which is what makes opening one
///and saving it a no-op rather than a conversion nobody asked for.
///
test('a file that arrived as OASIS is saved as OASIS by default', async ({ page }) => {
    await upload(page, 'converted.oas', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);

    await expect(page.locator('#downloadFormat')).toHaveValue('.oas');

    const download = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const file = await download;

    expect(file.suggestedFilename()).toBe('converted.oas');
    expect((await bytesOf(file)).subarray(0, 13).toString('latin1')).toBe('%SEMI-OASIS\r\n');
});

///
///And switched to GDS it comes out as GDSII, named as GDSII.
///
///Never a .oas holding GDSII: the name has to follow the bytes, or what is handed over is a file every
///tool refuses on sight.
///
test('the same file switched to GDS comes out as GDSII, named .gds', async ({ page }) => {
    await upload(page, 'converted.oas', oasisBytes());

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);

    await page.locator('#downloadFormat').selectOption('.gds');

    const download = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const file = await download;

    expect(file.suggestedFilename()).toBe('converted.gds');

    //A GDSII HEADER: length 6, record type 0x0002.
    expect([...(await bytesOf(file)).subarray(0, 4)]).toEqual([0x00, 0x06, 0x00, 0x02]);
});
