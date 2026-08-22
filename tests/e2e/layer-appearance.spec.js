//Turning a layer's labels off, and giving it a color of your own.
//
//Both live in one layer's settings and change what is drawn, so what needs a browser is the wiring
//between the two - the parser side is covered by SvgWriterTests and the storage side by StorageTests.
const { test, expect } = require('@playwright/test');
const {
    gotoApp,
    gotoExample,
    expectLoaded,
    openLayerSettings,
    labelsToggle,
    pickColor,
    layerNameBox,
    layerCheckbox,
    hideLayer,
    showLayer,
    layerPairs,
    svgCounts,
    fillsDrawn,
    MOSFET,
    MOSFET_POLYGONS,
    MOSFET_LABELS
} = require('./helpers');

//Mosfet.gds carries all three of its labels on 68/5, which is the sixth of its nine pairs. That is what
//makes it worth using here: turning one layer's labels off should take all three, and turning any other
//layer's off should take none - which one switch for the whole file could not tell apart.
const LABELED_ROW = 5;
const UNLABELED_ROW = 0;

///Every fill color currently drawn, as hex. See fillsDrawn in helpers for why it is computed and converted.
async function fills(page) {
    return fillsDrawn(page);
}

///
///Back to bare numbers, for the tests that are about an *unnamed* layer.
///
///A bundled example arrives with the shipped sky130 mapping over it, so 65/20 is called diff before anything
///is touched. Clear is the same button somebody wanting numbers would press, and it is durable - see
///SavedSession.NoBundledLayerNames.
///
async function clearNames(page) {
    await page.locator('.layerSidebarClear').click();

    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
        { timeout: 20000 }).not.toContain('met1');
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

///
///The point of the switch: labels usually share a layer with the shapes they name, so hiding the pair to
///be rid of the writing would take that geometry too.
///
test('labels can be turned off without taking their geometry with them', async ({ page }) => {
    expect((await svgCounts(page)).labels).toBe(MOSFET_LABELS);

    await openLayerSettings(page, LABELED_ROW);
    await labelsToggle(page).uncheck();

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(0);
    expect((await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await labelsToggle(page).check();

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(MOSFET_LABELS);
});

///
///The reason the switch moved out of the sidebar: it answers for one layer, not for the file.
///
test('turning off one layer\'s labels leaves another layer\'s alone', async ({ page }) => {
    expect((await svgCounts(page)).labels).toBe(MOSFET_LABELS);

    //A layer that carries no labels at all - switching it off can have nothing to take.
    await openLayerSettings(page, UNLABELED_ROW);
    await labelsToggle(page).uncheck();

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(MOSFET_LABELS);

    //And the one that does carry them.
    await openLayerSettings(page, LABELED_ROW);
    await labelsToggle(page).uncheck();

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(0);
});

test('a layer\'s labels switch survives a reload', async ({ page }) => {
    await openLayerSettings(page, LABELED_ROW);
    await labelsToggle(page).uncheck();

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(0);

    await gotoApp(page);
    await expectLoaded(page);

    await expect.poll(async () => (await svgCounts(page)).labels).toBe(0);

    //And the switch itself comes back off, not just the drawing.
    await openLayerSettings(page, LABELED_ROW);
    await expect(labelsToggle(page)).not.toBeChecked();
});

///
///The swatch reports the color; the gear beside it is what opens anything. Two things to press in one
///narrow row, with the one that looks most like a control being the readout, is what this replaced.
///
test('the color swatch is a readout rather than a button', async ({ page }) => {
    const swatch = page.locator('#layerSidebar .layerRow').first().locator('.layerSwatch');

    await expect(swatch).toHaveCount(1);

    //A span, not a button - so there is nothing for a click to do.
    expect(await swatch.evaluate(node => node.tagName)).toBe('SPAN');

    await swatch.click();

    await expect(page.locator('.layerSettingsField')).toHaveCount(0);
});

test('every layer shows a swatch of its own color', async ({ page }) => {
    const swatches = page.locator('#layerSidebar .layerSwatch');

    //One per row, which for this file is nine layer/datatype pairs.
    await expect(swatches).toHaveCount(9);

    const colors = await page.evaluate(() =>
        [...document.querySelectorAll('#layerSidebar .layerSwatch')].map(b => getComputedStyle(b).backgroundColor));

    //Distinct, because the palette gives each pair its own.
    expect(new Set(colors).size).toBe(colors.length);
});

///
///The picker is open in the popup rather than being a swatch that opens the operating system's one.
///
///That is the point of building it: a native color dialog covers the layout, which is the thing being
///recolored - and covering it is what the popup was moved out of the sidebar to avoid.
///
test('the picker is open in the popup, not behind a swatch', async ({ page }) => {
    await openLayerSettings(page, 0);

    await expect(page.locator('.layerSettingsField')).toBeVisible();
    await expect(page.locator('.layerSettingsHue')).toBeVisible();

    //Nothing that would hand the job to the browser.
    await expect(page.locator('.layerSettingsPopup input[type=color]')).toHaveCount(0);
});

test('a color chosen for a layer is drawn, and remembered', async ({ page }) => {
    const before = (await fills(page))[0];

    await openLayerSettings(page, 0);
    await pickColor(page, { hue: 120 });

    await expect.poll(async () => (await fills(page))[0]).not.toBe(before);

    const chosen = (await fills(page))[0];

    await gotoApp(page);
    await expectLoaded(page);

    await expect.poll(async () => (await fills(page))[0]).toBe(chosen);
});

///
///Dragging across the field changes the color without touching the hue, and the slider changes the hue
///without losing where the field was. Those are separate axes, and a picker that muddles them cannot be
///used to arrive anywhere on purpose.
///
test('the field and the slider each change the color', async ({ page }) => {
    await openLayerSettings(page, 0);

    await pickColor(page, { hue: 200, saturation: 0.9, value: 0.9 });
    await expect.poll(async () => (await fills(page))[0]).toMatch(/^#[0-9a-f]{6}$/);

    const first = (await fills(page))[0];

    //The field alone: same hue, a corner of the square that is nearly white.
    await page.locator('.layerSettingsField').click({ position: { x: 2, y: 2 } });
    await expect.poll(async () => (await fills(page))[0]).not.toBe(first);

    const washedOut = (await fills(page))[0];

    //The slider alone, from there.
    await page.locator('.layerSettingsHue').evaluate(slider => {
        slider.value = '20';
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    });

    await expect.poll(async () => (await fills(page))[0]).not.toBe(washedOut);
});

///
///Dragging adds one color to the history, not one per pixel crossed.
///
///Applying and remembering used to be the same call, and applying happens on every mousemove so the
///layout recolors as you move - so a single drag filled the strip with every shade the pointer passed
///over, and the colors actually chosen were pushed off the end of it.
///
test('the history takes a color when the drag ends, not while it runs', async ({ page }) => {
    await openLayerSettings(page, 0);

    const swatches = page.locator('.layerSettingsHistory .layerSwatch');
    const before = await swatches.count();

    const field = page.locator('.layerSettingsField');
    const box = await field.boundingBox();
    const original = (await fills(page))[0];

    //Pressing is the start of a drag, and it applies a color - the layout recolors from here on.
    await page.mouse.move(box.x + 30, box.y + 30);
    await page.mouse.down();

    //Waited on, and this is the part that has to be got right: the assertion below is that a count is
    //*still* what it was, which any "not yet" satisfies. Without holding here until the press has
    //visibly done something, it passed before the press had been handled at all - so it passed against
    //the very behavior it was written to catch.
    await expect.poll(async () => (await fills(page))[0]).not.toBe(original);

    //Asserted mid-drag, which is the whole test: nothing is remembered until the pointer comes up. This
    //is checked here rather than by dragging and counting, because a dragged path is exactly what would
    //not be reproduced - Playwright's mouse.move does not drive @onmousemove the way a hand does, so a
    //test that counted the path's colors passed against the flooding it was written to catch.
    await expect(swatches).toHaveCount(before);

    await page.mouse.up();

    await expect.poll(async () => swatches.count()).toBe(before + 1);
});

///
///The reason the picker carries a history: a layout is colored to a scheme, and picking the same green
///out of a gradient four times is nobody's idea of a good time.
///
test('colors used recently are offered again', async ({ page }) => {
    await openLayerSettings(page, 0);
    await pickColor(page, { hue: 120 });

    await expect.poll(async () => (await fills(page))[0]).toMatch(/^#[0-9a-f]{6}$/);

    const chosen = (await fills(page))[0];

    //A second layer, which should now be offered the first one's color.
    await openLayerSettings(page, 1);

    const history = page.locator('.layerSettingsHistory .layerSwatch');
    await expect(history.first()).toBeVisible();

    //Newest first, so the color just used is the one at the front.
    await history.first().click();

    //Two layers share it now.
    await expect.poll(async () => (await fills(page)).filter(fill => fill === chosen).length).toBeGreaterThan(1);
});

test('a layer can be put back on the palette', async ({ page }) => {
    const before = (await fills(page))[0];

    await openLayerSettings(page, 0);
    await pickColor(page, { hue: 120 });

    await expect.poll(async () => (await fills(page))[0]).not.toBe(before);

    await page.getByTitle('Give this layer back the color it was assigned from the gradient').click();

    await expect.poll(async () => (await fills(page))[0]).toBe(before);
});

///
///The popup's title is the name box. Renaming was only reachable by clicking the row's own text, in a
///sidebar narrow enough that the box had no room to show what was being typed.
///
test('a layer can be named from its settings', async ({ page }) => {
    //Cleared first, because a bundled example arrives with the shipped sky130 mapping over it and 65/20 is
    //called diff. This test is about the *unnamed* case, which now has to be got back to.
    await clearNames(page);

    await openLayerSettings(page, 0);

    //Holding what the layer is actually called, which for an unnamed one is its pair - not an empty box
    //with the pair hovering behind it as a placeholder.
    await expect(layerNameBox(page)).toHaveValue('65/20');

    await layerNameBox(page).fill('diff.drawing');
    await layerNameBox(page).press('Enter');

    //The row shows the name and keeps the numbers, the way a named layer reads everywhere else.
    await expect.poll(async () => layerPairs(page)).toContain('65/20');
    await expect.poll(async () =>
        (await page.locator('.layerRow .layerName').allTextContents()).some(text => text.includes('diff.drawing (65/20)')))
        .toBe(true);
});

///
///The box starts out holding the pair, so leaving it alone must not name the layer after itself.
///
///Without this, opening the settings and closing them would set the name to "65/20": the row would then
///read "65/20 (65/20)", and the file would count as having a layermap loaded.
///
test('leaving the name box alone does not name a layer after itself', async ({ page }) => {
    await clearNames(page);

    await openLayerSettings(page, 0);

    await expect(layerNameBox(page)).toHaveValue('65/20');

    //Committed without being changed, which is what closing or tabbing away does.
    await layerNameBox(page).press('Enter');

    await expect.poll(async () =>
        (await page.locator('.layerRow .layerName').allTextContents())[0].trim()).toBe('65/20');

    //And typing the pair back over a real name clears it again.
    await layerNameBox(page).fill('diff.drawing');
    await layerNameBox(page).press('Enter');

    await expect.poll(async () =>
        (await page.locator('.layerRow .layerName').allTextContents())[0]).toContain('diff.drawing');

    await layerNameBox(page).fill('65/20');
    await layerNameBox(page).press('Enter');

    await expect.poll(async () =>
        (await page.locator('.layerRow .layerName').allTextContents())[0].trim()).toBe('65/20');
});

///
///The color as three numbers, for when it is a number you have rather than a shade you are looking for.
///
test('the channels show the color and can be typed into', async ({ page }) => {
    await openLayerSettings(page, 0);

    const channels = page.locator('.layerSettingsChannel');
    await expect(channels).toHaveCount(3);

    //65/20 starts on the first color of the palette, #b30000.
    await expect(channels.nth(0)).toHaveValue('179');
    await expect(channels.nth(1)).toHaveValue('0');
    await expect(channels.nth(2)).toHaveValue('0');

    await channels.nth(1).fill('255');
    await channels.nth(1).blur();

    await expect.poll(async () => (await fills(page))[0]).toBe('#b3ff00');

    //The others follow the color rather than being kept beside it, so they still agree.
    await expect(channels.nth(0)).toHaveValue('179');
    await expect(channels.nth(2)).toHaveValue('0');
});

///
///Renaming rebuilds the row list, which used to hand every row back switched on - so naming one layer
///un-hid every layer that had been hidden, and would now turn every layer's labels back on with it.
///
test('renaming a layer leaves the other layers as they were', async ({ page }) => {
    //Hide the second layer.
    await hideLayer(page, 1);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBeLessThan(MOSFET_POLYGONS);

    const hiddenCount = (await svgCounts(page)).polygons;

    //Then name the first one.
    await openLayerSettings(page, 0);
    await layerNameBox(page).fill('diff.drawing');
    await layerNameBox(page).press('Enter');

    await expect.poll(async () =>
        (await page.locator('.layerRow .layerName').allTextContents()).some(text => text.includes('diff.drawing')))
        .toBe(true);

    //Still hidden.
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(hiddenCount);
    await expect(layerCheckbox(page, 1)).toHaveClass(/layerEyeOff/);
});


///
///The settings popup sits against the right-hand edge of the canvas, beside the list it belongs to.
///
///It used to be placed from the click and only pulled back when that put it over the sidebar. The gear is
///at the end of a layer row and the popup is wider than the row, so subtracting its width from the pointer
///landed it a quarter of the view away, with nothing in between. It is pinned to the edge now.
///
///**A seam, not a gap**, which is a tighter claim than "not overlapping" and is the one that failed for a
///long time without showing it: .popupDiv centers itself with translateX(-50%), and this popup is given an
///exact left instead - so for as long as it kept that transform it landed half its own width, 130px, to the
///left of wherever it was told. Asking only that it stay off the sidebar passed throughout.
///
test('the layer settings popup sits against the edge of the canvas', async ({ page }) => {
    await openLayerSettings(page, 0);

    const placed = await page.evaluate(() => {
        const box = (selector) => document.querySelector(selector).getBoundingClientRect();

        return {
            fromTheEdge: Math.round(box('.viewWrapper').right - box('.layerSettingsPopup').right),
            onScreen: Math.round(box('.layerSettingsPopup').left)
        };
    });

    //Off the canvas is wrong, and so is a hand's width in from it.
    expect(placed.fromTheEdge).toBeGreaterThanOrEqual(0);
    expect(placed.fromTheEdge).toBeLessThan(20);

    //And still on the page, rather than pushed off the left in the course of getting off the right.
    expect(placed.onScreen).toBeGreaterThan(0);
});

///
///And it says what it is.
///
///The panel opens over the layout with a name box, two numbers, a role and a color field in it, and until
///now nothing saying what any of that belonged to - the row it was opened from is behind it once it is up.
///
test('the layer settings popup is titled, with the way out at the other end', async ({ page }) => {
    await openLayerSettings(page, 0);

    const title = page.locator('.layerSettingsTitle');

    await expect(title).toHaveText('Layer settings');

    //Left, with the close button at the far end of the same row.
    const laidOut = await page.evaluate(() => {
        const box = (selector) => document.querySelector(selector).getBoundingClientRect();

        return {
            titleEndsBeforeClose: box('.layerSettingsTitle').right < box('.layerSettingsHeader .closeButton').left,
            titleAtTheLeft: Math.round(box('.layerSettingsTitle').left - box('.layerSettingsHeader').left)
        };
    });

    expect(laidOut.titleEndsBeforeClose).toBe(true);
    expect(laidOut.titleAtTheLeft).toBeLessThan(4);
});
