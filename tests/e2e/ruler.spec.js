//The 2D view's measuring tool.
//
//Everything about it runs in JS the build cannot check: the screen-to-layout conversion goes through the
//SVG's own screen matrix, the overlay is drawn by appending to the element Blazor owns, and the pointer
//has to stop panning while it is measuring. None of that is visible to a unit test - the arithmetic in
//viewGeometry is, and is covered under jstests/.
const { test, expect } = require('@playwright/test');
const { gotoExample, MOSFET, snapToGrid } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect(page.locator('#gdsSVG')).toBeVisible();

    //
    //Snapping off, which is how every test in this file was written and what they still mean.
    //
    //It is on out of the box now. At the default pitch of a micron, and this view fitted at roughly seven
    //database units to the pixel, a gesture of a few dozen pixels is a fraction of one grid step - so two
    //clicks meant to be apart land on the same crossing and the shape, path or reading collapses. These
    //are about the tools rather than about the grid, so the grid is taken out of them.
    //
    await snapToGrid(page, false);
});

///The middle of the SVG on screen, and a point offset from it by that many pixels.
async function pointIn(page, dx = 0, dy = 0) {
    const box = await page.locator('#gdsSVG').boundingBox();

    return { x: box.x + (box.width / 2) + dx, y: box.y + (box.height / 2) + dy };
}

test('the view starts in pan mode with no ruler drawn', async ({ page }) => {
    await expect(page.locator('#measureTool')).toBeVisible();

    //The tool group says which one is on.
    await expect(page.getByRole('button', { name: 'Pan' })).toHaveClass(/toolButtonOn/);
    await expect(page.locator('#measureTool')).not.toHaveClass(/toolButtonOn/);

    await expect(page.locator('#rulerOverlay')).toHaveCount(0);
});

test('two clicks measure a distance and leave it on screen', async ({ page }) => {
    await page.locator('#measureTool').click();

    await expect(page.locator('#measureTool')).toHaveClass(/toolButtonOn/);

    const from = await pointIn(page, -100, -50);
    const to = await pointIn(page, 100, 50);

    await page.mouse.click(from.x, from.y);
    await page.mouse.move(to.x, to.y);
    await page.mouse.click(to.x, to.y);

    const overlay = page.locator('#rulerOverlay');

    await expect(overlay).toHaveCount(1);

    //A line, its two end ticks, and the two lines of reading.
    await expect(overlay.locator('line')).toHaveCount(2);
    await expect(overlay.locator('circle')).toHaveCount(2);

    const reading = await overlay.locator('text').first().textContent();

    //Database units and microns, since Mosfet.gds has a nanometer grid.
    expect(reading).toMatch(/[\d.]+ units/);
    expect(reading).toMatch(/µm/);

    //And the deltas, which are what say where the second point is relative to the first.
    const deltas = await overlay.locator('text').nth(1).textContent();

    expect(deltas).toMatch(/dx -?\d+\s+dy -?\d+/);
});

///Measures a horizontal drag of that many pixels, and gives back the reading in layout units.
async function measurePixels(page, pixels) {
    const from = await pointIn(page, -pixels / 2, 0);
    const to = await pointIn(page, pixels / 2, 0);

    await page.mouse.click(from.x, from.y);
    await page.mouse.click(to.x, to.y);

    const reading = await page.locator('#rulerOverlay text').first().textContent();

    return parseFloat(reading.match(/([\d.]+) units/)[1]);
}

///
///The reading is in the layout's coordinates rather than in pixels.
///
///Asserted as a proportion rather than against a computed scale, because working the scale out here would
///mean repeating what the SVG does with preserveAspectRatio - and getting it wrong, which is how the first
///version of this test failed: it divided the viewBox width by the element's width, where a viewBox is
///actually fitted by whichever dimension is tighter. The code goes through the SVG's own screen matrix
///precisely so it never has to know that, and this checks the property that follows whatever the scale is.
///
test('the reading is in layout units rather than in pixels', async ({ page }) => {
    await page.locator('#measureTool').click();

    const short = await measurePixels(page, 100);
    const long = await measurePixels(page, 200);

    //Twice the drag is twice the distance, whatever a pixel happens to be worth.
    expect(long / short).toBeGreaterThan(1.9);
    expect(long / short).toBeLessThan(2.1);

    //And it is emphatically not the pixel count, or this would pass against a ruler that converted
    //nothing at all.
    expect(Math.abs(long - 200)).toBeGreaterThan(1);
});

///
///Measuring takes the pointer over completely. A drag that both panned and measured would move the thing
///being measured while it was being measured, which is the one interaction that cannot be allowed.
///
test('measuring does not pan the view', async ({ page }) => {
    const before = await page.locator('#gdsSVG').getAttribute('viewBox');

    await page.locator('#measureTool').click();

    const from = await pointIn(page, -80, -80);
    const to = await pointIn(page, 80, 80);

    //A full drag, not just clicks - which is what would pan if the guard were not there.
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();

    expect(await page.locator('#gdsSVG').getAttribute('viewBox')).toBe(before);
});

test('panning still works once the tool is switched back', async ({ page }) => {
    await page.locator('#measureTool').click();
    await page.getByRole('button', { name: 'Pan' }).click();

    await expect(page.getByRole('button', { name: 'Pan' })).toHaveClass(/toolButtonOn/);

    const before = await page.locator('#gdsSVG').getAttribute('viewBox');

    const from = await pointIn(page, -80, 0);
    const to = await pointIn(page, 80, 0);

    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(to.x, to.y, { steps: 8 });
    await page.mouse.up();

    expect(await page.locator('#gdsSVG').getAttribute('viewBox')).not.toBe(before);
});

test('leaving the tool clears what was measured', async ({ page }) => {
    await page.locator('#measureTool').click();

    const from = await pointIn(page, -60, 0);
    const to = await pointIn(page, 60, 0);

    await page.mouse.click(from.x, from.y);
    await page.mouse.click(to.x, to.y);

    await expect(page.locator('#rulerOverlay')).toHaveCount(1);

    await page.getByRole('button', { name: 'Pan' }).click();

    await expect(page.locator('#rulerOverlay')).toHaveCount(0);
});

///
///A measurement is something somebody is doing to the layout rather than something the layout contains,
///so it must not end up in the file the download button writes.
///
test('the measurement is left out of the downloaded SVG', async ({ page }) => {
    await page.locator('#measureTool').click();

    const from = await pointIn(page, -60, 0);
    const to = await pointIn(page, 60, 0);

    await page.mouse.click(from.x, from.y);
    await page.mouse.click(to.x, to.y);

    await expect(page.locator('#rulerOverlay')).toHaveCount(1);

    const started = page.waitForEvent('download');

    await page.locator('#downloadImage').click();

    const stream = await (await started).createReadStream();
    const chunks = [];

    for await (const chunk of stream)
        chunks.push(chunk);

    const svg = Buffer.concat(chunks).toString('utf8');

    //The layout is there and the measurement is not.
    expect(svg).toContain('<path');
    expect(svg).not.toContain('rulerOverlay');
});
