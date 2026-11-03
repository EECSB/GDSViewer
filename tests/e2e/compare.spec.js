//Comparing the open file with another: the Compare panel, its markers, and stepping through them.
//
//What a difference is - the area one file draws and the other does not, after both are flattened and merged
//- is C#'s and measured in CompareTests. What is here is the part only the page has: choosing the other file
//in the panel, the sentence that says what the comparison came to, the markers over the drawing, the arrows
//that step from one to the next, and a comparison coming off when the file it describes changes.
//
//The panel hangs off a button in the bar, beside Download. It was a third list in the layer sidebar.
const { test, expect } = require('@playwright/test');
const path = require('path');
const { gotoExample, shapeCount, shapeBox, selectView } = require('./helpers');

const SAMPLES = path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await page.locator('#compareButton').click();

    await expect(page.locator('#comparePanel')).toBeVisible();
});

///
///**In the bar beside the file buttons, not in the side panel.** Layers and Rules describe the file on screen and
///Compare brings a second one in, which is what Open and Download are about.
///
test('Compare is a button beside Download, and the side panel has two lists', async ({ page }) => {
    const button = await page.locator('#compareButton').boundingBox();
    const download = await page.locator('#downloadGds').boundingBox();

    expect(Math.abs(button.y - download.y)).toBeLessThan(4);
    expect(button.x).toBeGreaterThan(download.x);

    await expect(page.locator('[aria-label="Which list this panel shows"] .sidebarPanelChoice')).toHaveText(['Layers', 'Rules']);

    //Hanging under its button.
    const panel = await page.locator('#comparePanel').boundingBox();

    expect(panel.y).toBeGreaterThan(button.y + button.height - 1);
});

///
///**It stays up while the drawing is worked over**, unlike the bar's menus: comparing is stepping through differences
///while looking at them. The button or its cross put it away, and its marks stay on the drawing until Clear.
///
test('a press on the drawing leaves the panel up, and the cross puts it away with the marks left on', async ({ page }) => {
    await compareWith(page, 'sky130_fd_sc_hd__a2111o_1.gds');

    await expect(page.locator('#gdsSVG #compareMarkers')).toHaveCount(1);

    const view = await page.locator('#svgWrapper').boundingBox();

    await page.mouse.click(view.x + view.width - 60, view.y + view.height - 60);

    await expect(page.locator('#comparePanel')).toBeVisible();

    await page.locator('#compareClose').click();

    await expect(page.locator('#comparePanel')).toHaveCount(0);
    await expect(page.locator('#gdsSVG #compareMarkers')).toHaveCount(1);

    //And the button brings it back as it was, the other file still chosen.
    await page.locator('#compareButton').click();

    await expect(page.locator('#compareSummary')).toContainText('difference(s)');
});

///The marks are drawn in the 2D view, so the button is off in the others and the panel goes with the view.
test('Compare is off outside the 2D view, and its panel goes with the view', async ({ page }) => {
    await selectView(page, 'View3D');

    await expect(page.locator('#compareButton')).toBeDisabled();
    await expect(page.locator('#comparePanel')).toHaveCount(0);

    await selectView(page, 'View2DSvg');

    await expect(page.locator('#compareButton')).toBeEnabled();
    await expect(page.locator('#comparePanel')).toBeVisible();
});

async function compareWith(page, file) {
    await page.locator('#compareImport').setInputFiles(path.join(SAMPLES, file));
}

test('a file compared with itself is the same', async ({ page }) => {
    await compareWith(page, 'Mosfet.gds');

    await expect(page.locator('#compareSummary')).toContainText('The same as Mosfet.gds');
    await expect(page.locator('#gdsSVG #compareMarkers')).toHaveCount(0);
});

///A different cell differs nearly everywhere: markers over the drawing, the first one picked out, and Next walking on.
test('a different file is marked, and the arrows step through it', async ({ page }) => {
    await compareWith(page, 'sky130_fd_sc_hd__a2111o_1.gds');

    await expect(page.locator('#compareSummary')).toContainText('difference(s)');

    const markers = page.locator('#gdsSVG #compareMarkers polygon');

    await expect.poll(async () => markers.count(), { timeout: 15000 }).toBeGreaterThan(1);

    const total = await markers.count();

    await expect(page.locator('#compareAt')).toHaveText(`1 of ${total}`);
    await expect(page.locator('#gdsSVG .differenceCurrent')).toHaveAttribute('data-difference', '0');

    await page.locator('#compareNext').click();

    await expect(page.locator('#compareAt')).toHaveText(`2 of ${total}`);
    await expect(page.locator('#gdsSVG .differenceCurrent')).toHaveAttribute('data-difference', '1');

    //And round from the first to the last.
    await page.locator('#comparePrevious').click();
    await page.locator('#comparePrevious').click();

    await expect(page.locator('#compareAt')).toHaveText(`${total} of ${total}`);
});

///**A comparison is a claim about the file as it was.** An edit takes the markers off and says so, and
///Compare puts them back for the file as it now is - against the same other file, without choosing it again.
test('an edit takes the comparison off until it is run again', async ({ page }) => {
    await compareWith(page, 'Mosfet.gds');

    await expect(page.locator('#compareSummary')).toContainText('The same');

    //Put away first: it hangs over the top of the drawing, where the shape may be.
    await page.locator('#compareClose').click();

    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
    await page.keyboard.press('Delete');

    await page.locator('#compareButton').click();

    await expect(page.locator('#compareStale')).toContainText('changed since it was compared');

    await page.locator('#compareRun').click();

    await expect(page.locator('#compareSummary')).toContainText('only in Mosfet.gds');
});

test('Clear puts the other file away and takes the marks off', async ({ page }) => {
    await compareWith(page, 'sky130_fd_sc_hd__a2111o_1.gds');

    await expect(page.locator('#gdsSVG #compareMarkers')).toHaveCount(1);

    await page.locator('#compareClear').click();

    await expect(page.locator('#gdsSVG #compareMarkers')).toHaveCount(0);
    await expect(page.locator('#compareRun')).toHaveCount(0);
});
