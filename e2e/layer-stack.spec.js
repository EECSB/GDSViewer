//A layer's place in the wafer: how far up it sits, and how thick it is.
//
//The 3D view spaces layers evenly until it is told otherwise, which says only what order they are in. A
//process gives each layer a real height and thickness. The arithmetic is covered by LayerStackTests; what
//needs a browser is that a number typed into the settings popup reaches the geometry, is not undone by the
//spacing slider, and comes back after a reload.
const { test, expect } = require('@playwright/test');
const {
    gotoApp,
    gotoExample,
    expectLoaded,
    openLayerSettings,
    selectView,
    svgCounts,
    MOSFET
} = require('./helpers');

const HEIGHT = '.layerSettingsHeight';
const THICKNESS = '.layerSettingsThickness';

///Types a value into one of the stack boxes and lets it commit, the way leaving the field would.
async function setStack(page, selector, value) {
    await page.locator(selector).fill(String(value));
    await page.locator(selector).blur();
}

///
///Dismisses the layer settings.
///
///By its own header's close button rather than by Escape, which nothing here listens for - and the popup
///sits over the layout, so anything clicking the toolbar afterwards would click through it.
///
async function closeSettings(page) {
    await page.locator('.layerSettingsHeader .closeButton').click();

    await expect(page.locator('.layerSettingsField')).toHaveCount(0);
}

///
///Records what the 3D view is handed on every redraw.
///
///Installed before the app loads, through a property whose getter returns a fresh wrapper. Assigning
///window.drawInterOp after the fact does nothing at all: .NET resolves a JS function by name once and
///holds the reference, so a later assignment is never seen. The obvious version of this recorded an empty
///list while the scene was visibly being built - the same trap render-3d.spec.js carries a note about.
///
async function recordDraws(page) {
    await page.addInitScript(() => {
        let real = null;
        window.__draws = [];

        Object.defineProperty(window, 'drawInterOp', {
            configurable: true,
            get() {
                return function (data) {
                    window.__draws.push(data);

                    return real.apply(this, arguments);
                };
            },
            set(fn) { real = fn; }
        });
    });
}

///
///Where each extruded slab starts and how deep it goes.
///
///Read off the interop payload rather than measured off the geometry: what is being tested is that the
///number typed in is the number handed over, and three's own bevel makes a measured slab a hair off.
///
async function extrusions(page) {
    return page.evaluate(async () => {
        const slider = document.getElementById('layerSpacing');

        window.__draws = [];

        //The cheapest way to ask for a redraw from outside - and it moves the spacing while it is at it,
        //which is exactly the thing a placed layer has to survive.
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));

        const last = window.__draws[window.__draws.length - 1];

        return ((last && last.elements) || []).map(element => ({
            offset: element.layer.offset,
            depth: element.layer.depth
        }));
    });
}

///
///Records every restack the slider asks for, and every full redraw.
///
///Installed the same way and for the same reason as recordDraws above - a property whose getter returns a
///fresh wrapper, because .NET resolves a JS function by name once and holds it, so assigning after the fact
///is never seen.
///
///The two together are what separate the slider's cheap half from its expensive one: a restack is a Y write
///per object and happens on every step, a redraw is the whole scene and happens once the drag stops.
///
async function recordRestacks(page) {
    await page.addInitScript(() => {
        let real = null;
        window.__restacks = [];

        Object.defineProperty(window, 'restackLayers', {
            configurable: true,
            get() {
                return function (offsets) {
                    window.__restacks.push([...offsets]);

                    return real.apply(this, arguments);
                };
            },
            set(fn) { real = fn; }
        });
    });
}

test.beforeEach(async ({ page }) => {
    //Before the app loads, or the hook is never seen. Harmless for the tests that do not read it.
    await recordDraws(page);
    await recordRestacks(page);

    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
});

test('a height and a thickness can be typed in, and stay typed in', async ({ page }) => {
    await openLayerSettings(page, 0);

    await setStack(page, HEIGHT, 4321);
    await setStack(page, THICKNESS, 250);

    await expect(page.locator(HEIGHT)).toHaveValue('4321');
    await expect(page.locator(THICKNESS)).toHaveValue('250');
});

///
///The box starts on the value the layer is actually at, not blank.
///
///A blank box would say the layer had no height, when the view has already given it one - and there would
///be nothing to nudge.
///
test('the boxes open on the height the view already gave the layer', async ({ page }) => {
    await openLayerSettings(page, 0);

    await expect(page.locator(HEIGHT)).not.toHaveValue('');
    await expect(page.locator(THICKNESS)).not.toHaveValue('');
});

///
///What the whole feature is for: the number typed in is the number the 3D view extrudes at.
///
test('a height typed in is the height the 3D view draws at', async ({ page }) => {
    await openLayerSettings(page, 0);

    await setStack(page, HEIGHT, 4321);
    await setStack(page, THICKNESS, 250);

    await closeSettings(page);

    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    const slabs = await extrusions(page);

    expect(slabs.length).toBeGreaterThan(0);
    expect(slabs).toContainEqual({ offset: 4321, depth: 250 });
});

///
///And the spacing slider does not undo it.
///
///The stack is recomputed on every move of that slider, so without the custom flag a height typed in
///would last exactly until the next nudge - which is the failure this feature would most likely have.
///
test('the spacing slider leaves a layer that was placed by hand', async ({ page }) => {
    await openLayerSettings(page, 0);

    await setStack(page, HEIGHT, 4321);
    await setStack(page, THICKNESS, 250);

    await closeSettings(page);

    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    //extrusions moves the slider itself, so this is already a redraw at a different spacing.
    const slabs = await extrusions(page);

    expect(slabs).toContainEqual({ offset: 4321, depth: 250 });

    //And the layers that were left alone did move with it, so the slider is not simply doing nothing.
    expect(slabs.some(slab => slab.offset !== 4321)).toBe(true);
});

test('Reset stack puts the layer back on the even spacing', async ({ page }) => {
    await openLayerSettings(page, 0);

    const automatic = await page.locator(HEIGHT).inputValue();

    await setStack(page, HEIGHT, 4321);
    await expect(page.locator(HEIGHT)).toHaveValue('4321');

    await page.locator('.layerSettingsFooter').getByText('Reset stack').click();

    await expect(page.locator(HEIGHT)).toHaveValue(automatic);
});

///Only offered when there is something to put back.
test('Reset stack is not offered for a layer that was never placed', async ({ page }) => {
    await openLayerSettings(page, 0);

    await expect(page.locator('.layerSettingsFooter').getByText('Reset stack')).toHaveCount(0);

    await setStack(page, HEIGHT, 4321);

    await expect(page.locator('.layerSettingsFooter').getByText('Reset stack')).toHaveCount(1);
});

///
///It comes back, which is a round trip through storage rather than through the page's own memory.
///
test('a height survives the browser being reopened', async ({ page }) => {
    await openLayerSettings(page, 0);

    await setStack(page, HEIGHT, 4321);
    await setStack(page, THICKNESS, 250);

    //Wait for the save to have carried it, rather than racing the reload.
    await expect.poll(async () => page.evaluate(async () => {
        const value = await window.gdsStorage.get('gdsviewer.session');

        if (value === null)
            return '';

        return value;
    }), { timeout: 60000 }).not.toBe('');

    await gotoApp(page);
    await expectLoaded(page);

    await openLayerSettings(page, 0);

    await expect(page.locator(HEIGHT)).toHaveValue('4321');
    await expect(page.locator(THICKNESS)).toHaveValue('250');
});

///
///The other way in: a layermap carrying the stack.
///
///The point of the two extra columns. A process table converts to this mechanically, so a whole wafer
///arrives in one file rather than being typed layer by layer - which is what GDS3D's process definition
///file does for it, and the reason this was worth having at all.
///
test('a mapping can carry the whole stack', async ({ page }) => {
    const said = [];

    await page.exposeFunction('reportAlert', message => said.push(String(message)));
    await page.evaluate(() => { window.alert = message => window.reportAlert(message); });

    await page.locator('#layerNamesImport').setInputFiles({
        name: 'stack.csv',
        mimeType: 'text/csv',
        buffer: Buffer.from([
            '#layer,datatype,name,color,height,thickness',
            '65,20,diff.drawing,#ff0000,1000,120',
            '66,20,poly.drawing,#00ff00,2000,180'
        ].join('\n'), 'utf8')
    });

    await expect.poll(() => said.length, { timeout: 60000 }).toBeGreaterThan(0);
    expect(said.join(' ')).toContain('Updated 2');

    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    const slabs = await extrusions(page);

    //
    //**The heights the mapping asked for, plus the ten the measurement itself moved the slider by.**
    //
    //extrusions() nudges the spacing by ten to force a redraw, and a layer given a height now answers the
    //spacing slider like every other one - which is the whole of what was fixed here. It used to be skipped
    //instead, so the nudge was invisible and this asserted the raw heights; the layers that had heights
    //stayed put while the rest spread away from them, and on a real file that is a stack coming apart around
    //a clump that never budged.
    //
    //So the spread lands on each layer in proportion to its place in the order: 65/20 is the first layer in
    //this file and the floor the stack is measured from, which gains nothing, and 66/20 is the second and
    //gains the one step.
    //
    expect(slabs).toContainEqual({ offset: 1000, depth: 120 });
    expect(slabs).toContainEqual({ offset: 2010, depth: 180 });
});

///
///And back out again, so a stack built in the popup can be saved and handed to somebody else.
///
test('the exported mapping carries a height that was typed in', async ({ page }) => {
    await openLayerSettings(page, 0);

    await setStack(page, HEIGHT, 4321);
    await setStack(page, THICKNESS, 250);

    await closeSettings(page);

    const download = await Promise.all([
        page.waitForEvent('download'),
        page.locator('#layerNamesExport').click()
    ]);

    const stream = await download[0].createReadStream();
    const chunks = [];

    for await (const chunk of stream)
        chunks.push(chunk);

    const csv = Buffer.concat(chunks).toString('utf8');

    expect(csv.split('\n')[0]).toContain('height,thickness');
    //Not anchored at the end: the columns are positional and a layer carrying a role writes that one too, so
    //the row goes on past the thickness. A bundled example arrives with the shipped sky130 mapping over it,
    //which gives 65/20 a role. What is being asked is that the height and thickness are in *their* columns.
    expect(csv).toMatch(/^\d+,\d+,[^,]*,[^,]*,4321,250(,|$)/m);
});

///Emptying a box is the way back to the even spacing without hunting for the number it had.
test('clearing a box puts the layer back on the even spacing', async ({ page }) => {
    await openLayerSettings(page, 0);

    const automatic = await page.locator(HEIGHT).inputValue();

    await setStack(page, HEIGHT, 4321);
    await expect(page.locator(HEIGHT)).toHaveValue('4321');

    await setStack(page, HEIGHT, '');

    await expect(page.locator(HEIGHT)).toHaveValue(automatic);
});

///
///The slider moves the layers on every step of the drag, not once it is let go.
///
///It used to be settled - one redraw after the drag stopped - because a redraw hands three.js the whole
///scene, measured at 7.6 seconds a step on a large layout. That is the right answer to the cost and the wrong
///answer to the question: moving a layer up the stack is a Y translation on geometry that is already built.
///So the heights go over on their own, per step, and the redraw stays behind the debounce.
///
test('the spacing slider restacks on every step, not on release', async ({ page }) => {
    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    await page.waitForTimeout(1200);

    //Both counters from here, so the file opening does not count as a step.
    await page.evaluate(() => { window.__restacks = []; window.__draws = []; });

    //Six steps of a drag, without releasing between them - which is what @oninput reports.
    await page.locator('#layerSpacing').evaluate(slider => {
        for (const value of [120, 200, 280, 360, 440, 520]) {
            slider.value = String(value);
            slider.dispatchEvent(new Event('input', { bubbles: true }));
        }
    });

    //One restack per step. Polled rather than read once: each one is an interop hop.
    await expect.poll(async () => page.evaluate(() => window.__restacks.length), { timeout: 15000 })
        .toBeGreaterThanOrEqual(6);

    //And the scene was not rebuilt six times, which is the cost the debounce is still there for.
    const rebuilt = await page.evaluate(() => window.__draws.length);

    expect(rebuilt).toBeLessThan(6);

    //The last restack is the height the slider ended on.
    const last = await page.evaluate(() => window.__restacks[window.__restacks.length - 1]);

    expect(last.length).toBeGreaterThan(1);
    expect(last[1] - last[0]).toBe(520);
});

///
///And every layer separates by the same amount.
///
///Height used to be per layer *number*, so every purpose of one layer shared a step - which put licon1 at
///poly's height and mcon at li1's, and left half the rows of a sky130 cell not moving apart when the slider
///was pulled. See SetStackingOffsets for the reversal and what it costs.
///
test('every layer separates by the same step', async ({ page }) => {
    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    await page.waitForTimeout(1200);

    await page.evaluate(() => { window.__restacks = []; });

    await page.locator('#layerSpacing').evaluate(slider => {
        slider.value = '300';
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    });

    const offsets = await expect.poll(async () =>
        page.evaluate(() => window.__restacks[window.__restacks.length - 1] ?? null),
        { timeout: 15000 }).not.toBeNull()
        .then(async () => page.evaluate(() => window.__restacks[window.__restacks.length - 1]));

    //Mosfet uses nine pairs, so nine heights - not the five its five layer numbers would have given.
    expect(offsets.length).toBe(9);

    for (let at = 1; at < offsets.length; at++)
        expect(offsets[at] - offsets[at - 1]).toBe(300);
});
