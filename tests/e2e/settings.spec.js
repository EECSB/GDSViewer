//The settings under the gear at the far end of the view's bar: light or dark, and the frame rate.
//
//**Light unless dark was chosen**, and dark from the first paint of the next visit - js/theme.js sets the page
//in the head, before Blazor has started, so a dark visitor never sees the app open white and flick over.
//What this checks is the page, the panels and the drawing ground going dark together and coming back; the
//rest of the dark look is a stylesheet keyed on one attribute, see the dark mode section of app.css.
const { test, expect } = require('@playwright/test');
const { gotoExample, expectLoaded, selectView, shapeBox, MOSFET } = require('./helpers');

///The drop-down under the gear, opened by its press and left alone if it already is.
async function openSettings(page) {
    if (await page.locator('#settingsPicker').count() === 0)
        await page.locator('#settingsMenu').click();

    await expect(page.locator('#settingsPicker')).toBeVisible();
}

///The page's own ground and the view's, which is the drawing's.
const LIGHT_GROUND = 'rgb(255, 255, 255)';
const DARK_GROUND = 'rgb(23, 24, 27)';

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
});

test('the app opens light, with the switch off', async ({ page }) => {
    await expect(page.locator('html')).not.toHaveAttribute('data-theme', 'dark');
    await expect(page.locator('body')).toHaveCSS('background-color', LIGHT_GROUND);

    await openSettings(page);

    await expect(page.locator('#darkMode')).not.toBeChecked();
});

///
///The page, the panels and the drawing's ground, together - and all of them back again.
///
///The view pane sets its white inline, which is the one place the dark look needs !important, so it is the
///part most worth reading back. The drop-down stays open: it is a switch, not an answer to a question.
///
test('dark mode darkens the page, the panels and the drawing, and light puts them back', async ({ page }) => {
    await openSettings(page);
    await page.locator('#darkMode').check();

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(page.locator('body')).toHaveCSS('background-color', DARK_GROUND);
    await expect(page.locator('#viewPane')).toHaveCSS('background-color', DARK_GROUND);
    await expect(page.locator('.layerList')).toHaveCSS('background-color', 'rgb(33, 35, 39)');

    await expect(page.locator('#settingsPicker')).toBeVisible();

    await page.locator('#darkMode').uncheck();

    await expect(page.locator('html')).not.toHaveAttribute('data-theme', 'dark');
    await expect(page.locator('#viewPane')).toHaveCSS('background-color', LIGHT_GROUND);
    await expect(page.locator('.layerList')).toHaveCSS('background-color', LIGHT_GROUND);
});

///
///Remembered, and there before the app is.
///
///Read at the moment the document is ready rather than after the app has loaded, because "before Blazor" is
///the whole point of theme.js: an attribute set by the app would pass a test that waited for it, and still
///open the page white.
///
test('dark mode is there from the start of the next visit', async ({ page }) => {
    await openSettings(page);
    await page.locator('#darkMode').check();

    await page.reload({ waitUntil: 'domcontentloaded' });

    expect(await page.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');

    await expectLoaded(page);
    await openSettings(page);

    await expect(page.locator('#darkMode')).toBeChecked();
});

///The text editor draws in a canvas of its own, so it is told separately - and follows straight away.
test('the text editor follows the page into the dark and out again', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');

    const ground = page.locator('.monaco-editor-background').first();

    await expect(ground).toBeVisible({ timeout: 60000 });
    await expect(ground).toHaveCSS('background-color', 'rgb(255, 255, 254)');

    await openSettings(page);
    await page.locator('#darkMode').check();

    await expect(ground).toHaveCSS('background-color', 'rgb(30, 30, 30)');

    await page.locator('#darkMode').uncheck();

    await expect(ground).toHaveCSS('background-color', 'rgb(255, 255, 254)');
});

///Like the bar's other menus, a press anywhere else puts it away.
test('a press elsewhere puts the settings away', async ({ page }) => {
    await openSettings(page);

    await page.locator('#gdsSVG').click({ position: { x: 20, y: 20 } });

    await expect(page.locator('#settingsPicker')).toHaveCount(0);
});

///
///About is the last thing under the gear, and opens the popup the title bar's link does - the same one, not a copy -
///putting the menu away as it does. Its × puts it away again.
///
test('About, last in the settings, opens the title bar\'s About', async ({ page }) => {
    await openSettings(page);

    const items = page.locator('#settingsPicker > *');
    const last = items.last();

    await expect(last).toHaveAttribute('id', 'aboutItem');
    await expect(last).toHaveText('About');

    await last.click();

    await expect(page.locator('#settingsPicker')).toHaveCount(0);
    await expect(page.locator('#aboutPopup')).toBeVisible();
    await expect(page.locator('#aboutPopup')).toContainText('free and open source');

    await page.locator('#closeAbout').click();

    await expect(page.locator('#aboutPopup')).toHaveCount(0);

    //The title bar's link opens the very same popup.
    await page.locator('#aboutLink').click();

    await expect(page.locator('#aboutPopup')).toBeVisible();
});

///
///And with no title bar - an embed that asked for none - About in the settings is the way to it, and it still opens.
///The popup was written inside the bar, and went with it.
///
test('About opens from the settings with the title bar left out', async ({ page }) => {
    await page.goto('/?file=Mosfet&view=2d&banner=false', { waitUntil: 'domcontentloaded' });

    await expectLoaded(page);

    await expect(page.locator('.top-row')).toHaveCount(0);

    await openSettings(page);
    await page.locator('#aboutItem').click();

    await expect(page.locator('#aboutPopup')).toBeVisible();
    await expect(page.locator('#aboutPopup')).toContainText('free and open source');
});

///
///Debug info: frames a second, a graph of the last ten seconds and the longest frame, in the drawing's corner.
///
///Read off the panel rather than off the counter behind it, since the panel is the thing somebody looks at - a
///rate, a line on the graph once there are two samples to join, and a longest frame. A page that is drawing
///shows more than nothing a second, whatever machine runs this.
///
async function expectMeterRunning(page) {
    const meter = page.locator('#frameMeter');

    await expect(meter).toBeVisible();
    await expect(meter.locator('.frameMeterRate')).toHaveText(/^[1-9]\d* fps$/, { timeout: 10000 });
    await expect(meter.locator('.frameMeterLongest')).toHaveText(/^\d+ ms$/);

    await expect.poll(async () => (await meter.locator('.frameMeterLine').getAttribute('points')) || '', { timeout: 10000 })
        .toMatch(/^[\d.]+,[\d.]+( [\d.]+,[\d.]+)+$/);
}

///Within a few pixels of a box's top left corner.
async function expectInCornerOf(page, selector) {
    const box = await page.locator(selector).boundingBox();
    const meter = await page.locator('#frameMeter').boundingBox();

    expect(meter.x - box.x).toBeGreaterThanOrEqual(0);
    expect(meter.x - box.x).toBeLessThan(16);
    expect(meter.y - box.y).toBeGreaterThanOrEqual(0);
    expect(meter.y - box.y).toBeLessThan(16);
}

///
///Off to begin with, on at the switch, and gone again with nothing left counting.
///
///**In the drawing's corner, not the view's.** The 2D view is the cell list and the drawing side by side, and a
///panel placed against the pair would sit over the list's heading.
///
test('debug info shows the frame rate in the corner of the drawing, and off takes it away', async ({ page }) => {
    await openSettings(page);

    await expect(page.locator('#debugInfo')).not.toBeChecked();
    await expect(page.locator('#frameMeter')).toHaveCount(0);
    expect(await page.evaluate(() => frameMeter.running)).toBe(false);

    await page.locator('#debugInfo').check();

    await expectMeterRunning(page);
    await expectInCornerOf(page, '.viewCanvas');

    await page.locator('#debugInfo').uncheck();

    await expect(page.locator('#frameMeter')).toHaveCount(0);
    expect(await page.evaluate(() => frameMeter.running)).toBe(false);
});

///The same panel in 3D, carried across the switch of view; the text view has no drawing to put it over.
test('the frame rate follows into the 3D view and stays out of the text view', async ({ page }) => {
    await openSettings(page);
    await page.locator('#debugInfo').check();
    await expectMeterRunning(page);

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();
    await expectMeterRunning(page);
    await expectInCornerOf(page, '#container');

    await selectView(page, 'ViewText');

    await expect(page.locator('#frameMeter')).toHaveCount(0);
});

///
///**In 3D, the scene's own count as well.** three keeps no frame rate - its renderer counts frames, draw calls and
///triangles - so the panel measures the scene's frames over the same second as the page's, which says whether the
///scene drew every frame the browser showed. The 2D view has no scene, and none of these rows.
///
///**Nothing while the scene stands still**, since it is drawn only when something changes - see animate in
///ThreeInterop.js - and more than nothing while the zoom buttons move the camera.
///
test('in 3D the panel adds the frames the scene drew, its draw calls and its triangles', async ({ page }) => {
    await openSettings(page);
    await page.locator('#debugInfo').check();
    await expectMeterRunning(page);

    const meter = page.locator('#frameMeter');

    await expect(meter.locator('.frameMeterScene')).toBeHidden();

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();

    await expect(meter.locator('.frameMeterScene')).toHaveText('0 fps', { timeout: 10000 });

    //A press of each per reading, so the camera is still moving in the second the panel reads.
    await expect.poll(async () => {
        await page.locator('#zoomIn').click();
        await page.locator('#zoomOut').click();

        return meter.locator('.frameMeterScene').textContent();
    }, { timeout: 10000 }).toMatch(/^[1-9]\d* fps$/);

    await expect(meter.locator('.frameMeterCalls')).toHaveText(/^[1-9]\d*$/);
    await expect(meter.locator('.frameMeterTriangles')).toHaveText(/^[1-9][\d.]*( k| M)?$/);

    await selectView(page, 'View2DSvg');

    await expectMeterRunning(page);
    await expect(meter.locator('.frameMeterScene')).toBeHidden();
});

///Remembered on this computer, the way the theme is.
test('debug info is still on after a reload', async ({ page }) => {
    await openSettings(page);
    await page.locator('#debugInfo').check();
    await expectMeterRunning(page);

    await page.reload();
    await expectLoaded(page);

    await expectMeterRunning(page);

    await openSettings(page);

    await expect(page.locator('#debugInfo')).toBeChecked();
});

///
///It is something to look at, not something in the way.
///
///A press on it lands on the drawing under it, and the selection panel - which wants the same corner and is
///the one with buttons in it - steps down below it rather than sliding underneath.
///
test('the frame rate takes no presses, and the selection panel sits below it', async ({ page }) => {
    await openSettings(page);
    await page.locator('#debugInfo').check();
    await expectMeterRunning(page);

    const meter = await page.locator('#frameMeter').boundingBox();

    const under = await page.evaluate(
        ({ x, y }) => document.elementFromPoint(x, y).closest('#gdsSVG') != null,
        { x: meter.x + (meter.width / 2), y: meter.y + (meter.height / 2) });

    expect(under).toBe(true);

    await page.locator('#selectTool').click();

    const shape = await shapeBox(page, 0);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    const panel = page.locator('#selectionPanel');

    await expect(panel).toBeVisible();

    expect((await panel.boundingBox()).y).toBeGreaterThan(meter.y + meter.height);
});
