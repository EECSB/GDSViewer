//Pictures as PNG, and masks as one bitmap a layer.
//
//The rasterizing and the PNG and zip writing for masks are C#'s and checked pixel for pixel in
//MaskBitmapsTests. The PNG of the view and of the layout is the browser's own drawing of the SVG this app
//already writes, and only a browser can say it came out: so these read what was saved - the PNG's signature
//and its size from its header, the zip's file names from its own headers.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, downloadBytes, layerPairs, saveImage } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///A PNG's width and height, out of its IHDR - which is always the first chunk, at a fixed place.
function pngSize(bytes) {
    expect([...bytes.subarray(0, 8)]).toEqual([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    expect(bytes.subarray(12, 16).toString('ascii')).toBe('IHDR');

    return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
}

///The names of the files in a zip, from each local header in turn.
function zipNames(bytes) {
    const names = [];
    let at = 0;

    while (at + 30 <= bytes.length && bytes.readUInt32LE(at) === 0x04034b50) {
        const compressed = bytes.readUInt32LE(at + 18);
        const nameLength = bytes.readUInt16LE(at + 26);
        const extraLength = bytes.readUInt16LE(at + 28);

        names.push(bytes.subarray(at + 30, at + 30 + nameLength).toString('utf8'));

        at += 30 + nameLength + extraLength + compressed;
    }

    return names;
}

test('the view saves as a PNG at twice the size it is on screen', async ({ page }) => {
    const view = await page.locator('#gdsSVG').boundingBox();

    const started = page.waitForEvent('download');

    await saveImage(page, 'png');

    const download = await started;
    const size = pngSize(await downloadBytes(download));

    expect(download.suggestedFilename()).toBe('view.png');
    expect(size.width).toBe(Math.round(view.width * 2));
    expect(size.height).toBe(Math.round(view.height * 2));
});

///The whole layout, from the picker: the SVG choice's picture, four thousand pixels on its longer side.
test('the layout saves as a PNG from the picker', async ({ page }) => {
    await page.locator('#downloadFormat').selectOption('.png');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;
    const size = pngSize(await downloadBytes(download));

    expect(download.suggestedFilename()).toMatch(/\.png$/);
    expect(Math.max(size.width, size.height)).toBe(4096);
});

///
///**Masks ask first.** A pixel size and a polarity, and what the two come to before anything is written -
///then one PNG a layer that has shapes showing, all in one zip, as many as the panel said, and a line saying
///what was made.
///
test('masks are one bitmap a layer, in a zip', async ({ page }) => {
    await page.locator('#downloadFormat').selectOption('.zip');
    await page.locator('#downloadGds').click();

    await expect(page.locator('#masksPanel')).toBeVisible();

    await page.locator('#maskPixel').fill('0.01');
    await page.locator('#maskPixel').blur();

    await expect(page.locator('#maskSize')).toContainText('layer(s)');

    const said = Number(/(\d+) layer/.exec(await page.locator('#maskSize').textContent())[1]);

    expect(said).toBeGreaterThan(0);
    expect(said).toBeLessThanOrEqual((await layerPairs(page)).length);

    const started = page.waitForEvent('download');

    await page.locator('#saveMasks').click();

    const download = await started;
    const names = zipNames(await downloadBytes(download));

    expect(download.suggestedFilename()).toMatch(/masks\.zip$/);
    expect(names.length).toBe(said);
    expect(names.every(name => /^\d+_\d+.*\.png$/.test(name))).toBe(true);

    await expect(page.locator('#downloadNotice')).toContainText(`${said} mask(s)`);
    await expect(page.locator('#masksPanel')).toHaveCount(0);
});

///Every layer switched off is no masks at all, said rather than saved as an empty zip.
test('with every layer hidden there are no masks to write', async ({ page }) => {
    await page.locator('#allLayersShown').click();

    await page.locator('#downloadFormat').selectOption('.zip');
    await page.locator('#downloadGds').click();

    await expect(page.locator('#maskSize')).toContainText('No layer that is showing');

    await page.locator('#saveMasks').click();

    await expect(page.locator('#downloadNotice')).toContainText('no masks to write');
});
