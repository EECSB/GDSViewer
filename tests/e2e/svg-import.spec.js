//Opening an SVG drawing.
//
//What a drawing means on a mask - paint as area, a hole by the fill rule, a pen's width, a layer per Inkscape
//layer or per color - is C#'s and measured in SvgReaderTests. What is only checkable here is the way in: the
//app decides the format from the front of the file, a drawing draws, its layers arrive named and colored the
//way the drawing had them, and the download is GDSII.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, openedOnItsOwn, uploadFile, layerPairs, layerLabel, downloadBytes } = require('./helpers');

///
///A drawing ten millimeters square: a red square, a blue ring cut by even-odd, and a black line drawn with a
///pen - three colors, so three layers.
///
const DRAWING = `<?xml version="1.0" encoding="UTF-8"?>
<!-- Created with a test -->
<svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="10mm" viewBox="0 0 10 10">
  <rect x="1" y="1" width="2" height="2" fill="#ff0000"/>
  <path fill-rule="evenodd" fill="#0000ff" d="M5 1 H9 V5 H5 Z M6 2 H8 V4 H6 Z"/>
  <line x1="1" y1="8" x2="9" y2="8" stroke="black" stroke-width="0.5"/>
</svg>`;

async function openDrawing(page, name = 'drawing.svg', contents = DRAWING) {
    await uploadFile(page, {
        name,
        mimeType: 'image/svg+xml',
        buffer: Buffer.from(contents, 'utf8')
    });

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
}

test.beforeEach(async ({ page }) => {
    await gotoApp(page);
});

test('a drawing draws, a layer to each color', async ({ page }) => {
    await openDrawing(page);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(3);

    const pairs = await layerPairs(page);

    expect(pairs.length).toBe(3);

    //Named after the paint that made them, which is the one thing a number cannot say.
    const names = await Promise.all(pairs.map(pair => layerLabel(page, pair)));

    expect(names.join(' ')).toContain('#ff0000');
    expect(names.join(' ')).toContain('#0000ff');
});

///**The format is read off the front of the file, not off its name** - the same as every other format here.
test('a drawing named .gds still opens as one', async ({ page }) => {
    await openDrawing(page, 'renamed.gds');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(3);
});

///
///**An SVG dropped on the drawing opens as a drawing**, the way Open reads it, rather than being put down on the open
///layout as a picture made of squares - which it was, until the 2026-10-21 audit.
///
test('a drawing dropped on the drawing opens as a drawing', async ({ page }) => {
    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    const carrying = await page.evaluateHandle(contents => {
        const transfer = new DataTransfer();

        transfer.items.add(new File([contents], 'dropped.svg', { type: 'image/svg+xml' }));

        return transfer;
    }, DRAWING);

    await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying });

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(3);
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///What leaves is GDSII: a drawing opened here is opened to become a layout.
test('the download is GDSII', async ({ page }) => {
    await openDrawing(page);

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;
    const bytes = await downloadBytes(download);

    expect(download.suggestedFilename()).toMatch(/\.gds$/);

    //A GDSII stream opens with its HEADER record: six bytes long, type 0x0002.
    expect([...bytes.subarray(0, 4)]).toEqual([0x00, 0x06, 0x00, 0x02]);
});
