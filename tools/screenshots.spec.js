//
//Takes the pictures FEATURES-DEMO.md and the readme are built from.
//
//**Regenerated rather than collected.** Screenshots are the part of documentation that rots without anything
//failing: the app moves, the pictures do not, and nobody notices until a reader is looking at a toolbar that
//has not existed for six months. This runs the app and takes them again, so bringing them up to date is a
//command rather than an afternoon.
//
//It is not part of the test suite - see playwright.screenshots.config.js for why - and it asserts only enough
//to know the app was ready when the shutter opened. A picture taken mid-draw is the one failure a screenshot
//cannot report on its own, so every shot waits for something specific to be on screen first.
//
//    npm run screenshots
//
const { test, expect } = require('@playwright/test');
const path = require('path');

const IMAGES = path.join(__dirname, '..', 'docs', 'images');

//Mosfet.gds is the hand-made one: nine layers, a few dozen shapes, and small enough that the whole thing is
//legible at 1400 pixels. A sky130 standard cell is the real thing and is used where density is the point.
const MOSFET = 'Mosfet';
const CELL = 'sky130_fd_sc_hd__nand2_1';

//The bundled mapping, served from the app's own origin - so the layers arrive named, colored and roled
//without a second host, which is what makes Trace net possible in a picture.
const LAYERMAP = 'resources/GDS%20Files/sky130-roles.csv';

///
///Opens the app, waits for the file to be in the shell, and puts the editor full screen.
///
///data-file rather than the layer sidebar, because the text view does not draw one - the same reason the
///suite's own helper reads it there.
///
///**Full screen for every picture.** The page's own margins and the header take about a third of the frame
///otherwise, so a screenshot of the editor is mostly a screenshot of the page around it. The toolbar button
///is a CSS class rather than the browser's Fullscreen API - see Viewer.razor's viewerPageFull - which is why
///it works here at all.
///
async function open(page, query) {
    await page.goto(`/${query}`);

    await expect
        .poll(async () => page.locator('#mainAppContainer').getAttribute('data-file'), { timeout: 120000 })
        .not.toBe('');

    await page.locator('#fullScreen').click();

    await expect(page.locator('.viewerPageFull')).toBeVisible();

    //The layout is refitted to the larger box, which is a render away.
    await page.waitForTimeout(500);
}

///Waits until the 2D view has actually drawn something, rather than until it exists.
async function drawn(page) {
    await expect
        .poll(async () => page.locator('#gdsSVG path[class^="l"]').count(), { timeout: 60000 })
        .toBeGreaterThan(0);

    //One more frame, so a picture is never taken between the markup landing and the browser painting it.
    await page.waitForTimeout(600);
}

async function shoot(page, name, options = {}) {
    await page.screenshot({ path: path.join(IMAGES, `${name}.png`), ...options });
}

test('the 2D editor', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await shoot(page, '2d-view');
});

test('a real standard cell, dense', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await shoot(page, '2d-standard-cell');
});

test('the 3D view', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=3d&layermap=${LAYERMAP}`);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

    //WebGL needs longer than the SVG does: the scene is built, extruded and then orbited into place.
    await page.waitForTimeout(4000);

    //
    //**Pulled open, because the collapsed stack is the one thing this picture must not show.**
    //
    //The slider starts near its low end, where the layers sit almost on top of each other and a screenshot
    //of "layers extruded and stacked in space" shows a flat lump. The restack is live, so this settles in a
    //frame rather than needing a redraw.
    //
    //**110 because the camera does not refit.** Only a headset gets one - see ThreeInterop's restack, which
    //refits when xr.isPresenting so the layout does not walk off into the room. On a desktop the camera stays
    //where it is, so a stack opened wider than what was already in frame runs off the top: at 520 the upper
    //layers were gone entirely and at 200 the top one was still clipped. A wheel over the canvas does not fix
    //it either - Playwright's wheel does not reach OrbitControls here, which was tried and did nothing. This
    //is the number that fits, and it is worth knowing it is about the frame rather than about the feature.
    //
    await page.locator('#layerSpacing').evaluate(slider => {
        slider.value = '110';
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    });

    await page.waitForTimeout(2000);

    await shoot(page, '3d-view');
});

test('the text editor', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=text`);

    //Monaco arrives through its own AMD loader, so the element exists before the records are in it.
    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 60000 });
    await expect(page.locator('.view-lines')).toContainText('HEADER', { timeout: 60000 });

    await page.waitForTimeout(800);

    await shoot(page, 'text-view');
});

test('the layer sidebar, named from a layermap', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await expect(page.locator('#layerSidebar')).toBeVisible();

    await shoot(page, 'layer-sidebar', { clip: await box(page, '#layerSidebar') });
});

///
///The tree is open in every 2D picture here, because it opens by default - so this one has to show what the
///others do not, or it is the same screenshot under a second name. The first version of it was exactly that,
///byte for byte identical to the standard cell's.
///
///What it has that they do not is depth: a cell holds layers, and a layer holds shapes. Two rows expanded is
///what says so.
///
test('the cell tree, opened down to the shapes', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&tree=true&layermap=${LAYERMAP}`);
    await drawn(page);

    await expect(page.locator('#cellTree')).toBeVisible();

    //licon1 has fifteen, which is enough to read as a list rather than as one more row.
    const folds = page.locator('#cellTree .cellRowFold');

    await folds.nth(6).click();
    await page.waitForTimeout(300);

    await folds.nth(5).click();
    await page.waitForTimeout(600);

    await shoot(page, 'cell-tree');
});

test('the examples picker', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#examplesButton').click();

    await expect(page.locator('#examplePicker')).toBeVisible();
    await page.waitForTimeout(600);

    await shoot(page, 'examples');
});

test('the shape picker and a shape\'s own settings', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await enterCell(page);

    await page.locator('#drawTool').click();

    await expect(page.locator('#shapePicker')).toBeVisible();

    //Hovering the row opens its panel, which is the thing worth a picture.
    await page.locator('#pathShape').hover();

    await expect(page.locator('#pathShape ~ .shapePickPanel')).toBeVisible();

    await shoot(page, 'shape-picker');
});

test('a shape chosen, and what can be done to it', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    //The Select tool first: the panel is what a *selection* offers, and a click with Pan in hand pans.
    await page.locator('#selectTool').click();

    await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);

    await clickAShape(page);

    await expect(page.locator('#selectionPanel')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(400);

    await shoot(page, 'selection');
});

test('the layer settings popup', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('.layerRow .layerSettingsButton').first().click();

    await expect(page.locator('.layerSettingsField').first()).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(400);

    await shoot(page, 'layer-settings');
});

test('the grid, in real units', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&grid=true&snap=true&pitch=0.5&unit=um&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.waitForTimeout(600);

    await shoot(page, 'grid');
});

///
///A net traced across the layout, which is the feature the layermap exists for.
///
///Needs the role column: nothing in a GDSII file says which of its numbers carry a net, so without the
///mapping the button is disabled and there is no picture to take.
///
test('a net traced through its vias', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#selectTool').click();

    //A li1 wire, which climbs through mcon into met1 - three layers, so the highlight crosses the stack
    //rather than lighting one shape.
    await clickShapeOnLayer(page, 'l67_20');

    await expect(page.locator('#traceNet')).toBeEnabled({ timeout: 30000 });

    await page.locator('#traceNet').click();

    //**The net comes back as a selection**, drawn by the same highlight everything else uses - so more than
    //one shape marked is how this knows the trace reached past where it started.
    await expect
        .poll(async () => page.locator('#gdsSVG .shapeSelected').count(), { timeout: 30000 })
        .toBeGreaterThan(1);

    await page.waitForTimeout(600);

    await shoot(page, 'trace-net');
});

///The ruler, dragged across the layout.
test('measuring across the layout', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#measureTool').click();

    const view = await page.locator('#gdsSVG').boundingBox();

    //Across the transistor, corner to corner, so the reading is a diagonal rather than a width.
    await page.mouse.move(view.x + 420, view.y + 300);
    await page.mouse.down();
    await page.mouse.move(view.x + 800, view.y + 480, { steps: 12 });

    await page.waitForTimeout(500);

    await shoot(page, 'measure');

    await page.mouse.up();
});

///A layer given a hatch, which is what tells two similar shades apart.
test('fill patterns over the colors', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    //diff, then a diagonal hatch from the pattern row of its settings.
    await page.locator('.layerRow .layerSettingsButton').first().click();

    await expect(page.locator('.layerSettingsFills').first()).toBeVisible({ timeout: 30000 });

    await page.locator('.layerSettingsFills button').nth(5).click();

    await page.waitForTimeout(800);

    await shoot(page, 'patterns');
});

///
///The rules, in the panel the layers usually have.
///
///Loaded through the bundled deck rather than a fixture, because that is the button somebody actually
///presses and a picture of a hand-fed deck would be a picture of something nobody does.
///
test('the rules panel, with the bundled deck loaded', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#sidebarPanelRules').click();
    await page.locator('#drcBundled').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

    await shoot(page, 'rules-sidebar', { clip: await box(page, '#layerSidebar') });
});

///
///A run that finds something, which is the picture the feature is for.
///
///The bundled cells are signed-off layout and the real deck finds nothing in them - the right answer and a
///useless picture. The demonstration deck is the one whose limits the layout cannot meet, so the markers,
///the counts and the message all have something to show.
///
test('violations marked on the drawing', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#sidebarPanelRules').click();
    await page.locator('#sidebarInfo').click();
    await page.locator('#loadStrictDeck').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

    await page.locator('#drcRun').click();

    await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });

    //The marks are drawn into the SVG, so wait for them rather than for the message that counts them.
    await expect
        .poll(async () => page.locator('#gdsSVG .drcMarker, #gdsSVG .drcMarkerPoint').count(), { timeout: 60000 })
        .toBeGreaterThan(0);

    await page.waitForTimeout(600);

    await shoot(page, 'rules-violations');
});

///Clicks the middle of the first shape drawn on a given layer's path, so a test can choose *which* shape
///rather than whichever happens to be first.
async function clickShapeOnLayer(page, className) {
    const shape = await page.locator(`#gdsSVG path.${className}`).first().boundingBox();

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Into a cell, so the drawing tools are live - they are disabled until something says which cell a new
///shape would go in.
async function enterCell(page) {
    await page.locator('#selectTool').click();

    await clickAShape(page);

    await expect
        .poll(async () => page.locator('#drawTool').isDisabled(), { timeout: 30000 })
        .toBe(false);
}

///Clicks the middle of the first drawn shape, which is how everything in this view is chosen.
async function clickAShape(page) {
    const shape = await page.locator('#gdsSVG path[class^="l"]').first().boundingBox();

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///The rectangle an element occupies, for a screenshot of one panel rather than the page.
async function box(page, selector) {
    const found = await page.locator(selector).boundingBox();

    return {
        x: Math.max(0, Math.floor(found.x)),
        y: Math.max(0, Math.floor(found.y)),
        width: Math.ceil(found.width),
        height: Math.ceil(found.height)
    };
}
