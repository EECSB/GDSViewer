const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const { gotoApp, gotoExample, MOSFET, shapeCount, shapeBox, selectView, openFile, SKY130_CELL, uploadFile } = require('./helpers');

const NAND = path.join(__dirname, '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', SKY130_CELL + '.gds');

///
///How often the hierarchy is resolved.
///
///**The one fault in this app that draws the right picture.** Flattening is the expensive part of opening a
///file, and everything is arranged so it happens once per open and once per edit: the shell keeps the result
///beside the library it came from and hands it to whichever view is mounted, which is what makes a 2D to 3D
///switch cost nothing. A view that quietly went back to flattening for itself would draw exactly the same
///layout and be slower, and every other spec here would stay green through it — they all ask what is on
///screen, and what is on screen would be right.
///
///So this one asks the app instead. `GdsFlattener.Flattens` counts whole-library flattens wherever they are
///called from, `Counters.FlattenCount` hands it to JavaScript, and the assertions below are differences
///rather than totals — the app has already opened a file of its own by the time a spec arrives, and how many
///times it did so on the way is not this spec's business.
///
async function flattens(page) {
    return page.evaluate(() => window.DotNet.invokeMethod('GDSViewer', 'FlattenCount'));
}

test.describe('resolving the hierarchy', () => {
    ///
    ///Opening a file flattens it once, and the drawing comes from that one pass rather than a second.
    ///
    ///**In the instance that is already running**, which rules out both of the ways this is easy to measure
    ///wrongly. An address is a fresh page load: the runtime starts again and the counter with it, so
    ///subtracting a count read before from one read after compares two different runs of the app - the first
    ///version of this test did exactly that, and asked for one against a fresh instance's total. And the
    ///Examples picker draws a preview of whatever it is pointing at, which is a whole-library flatten of its
    ///own - opening a file through it measures three, all of them honest work and only one of them this
    ///question.
    ///
    ///An upload is the route with neither problem: one instance throughout, and no popup to draw a preview
    ///into. It lands without a question because what is on screen is the app's own untouched example - see
    ///[the import dialog](import.spec.js).
    ///
    test('happens once when a file is opened', async ({ page }) => {
        await gotoApp(page);

        //Drawn, so the app's own open has finished rather than being caught halfway.
        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        const before = await flattens(page);

        await uploadFile(page, {
            name: `${SKY130_CELL}.gds`,
            mimeType: 'application/octet-stream',
            buffer: fs.readFileSync(NAND)
        });

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        expect(await flattens(page) - before).toBe(1);
    });

    ///<summary>Leaving 2D for 3D costs nothing: the shell hands over what it already has.</summary>
    test('does not happen when a view is entered from the shell', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        const before = await flattens(page);

        await selectView(page, 'View3D');

        //Mounted and drawing, so the switch is complete before the count is read.
        await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

        expect(await flattens(page) - before).toBe(0);
    });

    ///
    ///**And coming back costs nothing either, which took a parameter to arrange.**
    ///
    ///A view is destroyed when it is switched away from and built again on the way back, and a view built
    ///for the second time has flattened nothing yet - so its own guard, which asks whether the library it
    ///holds is the one it last flattened, is true. Its `OnInitializedAsync` renders, and rendering with no
    ///prepared layout resolved the whole hierarchy again; the shell's own call arrived afterwards carrying
    ///the layout it had kept all along, too late to save the work.
    ///
    ///The shell hands it over as a parameter now, which Blazor sets before a component initializes - so the
    ///view has it in time to render with. `Render` still takes one as well, for every call the shell makes
    ///afterwards; the parameter is only about the render a view does for itself on the way in.
    ///
    ///This test asked for one flatten until the day it was fixed, which is how the fix announced itself.
    ///
    test('does not happen when a view is entered again', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        await selectView(page, 'View3D');

        await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

        const before = await flattens(page);

        await selectView(page, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        expect(await flattens(page) - before).toBe(0);
    });

    ///
    ///An edit flattens once, because the library changed in place and what the shell held is of the file as
    ///it was. Once, though - not once for the view's own redraw and again for the shell's.
    ///
    test('happens once for an edit, and only once', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        //The first click enters the cell, the second chooses a shape in it.
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        const was = await shapeCount(page);
        const before = await flattens(page);

        await page.keyboard.press('Delete');

        await expect.poll(async () => shapeCount(page), { timeout: 30000 }).toBeLessThan(was);

        expect(await flattens(page) - before).toBe(1);
    });
});
