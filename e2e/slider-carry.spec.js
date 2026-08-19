//
//The two sliders, against a file that arrives after they have been set.
//
//They look like the same control and they are not the same kind of thing. The 2D view's opacity is an
//argument to the picture - passed to SvgWriter.Build on every draw - so a new file is built with whatever
//the slider says and there is nothing to carry. The 3D view's spacing is written *onto* the file, as a
//height on each Layer object, and a new file brings new ones: it has to be re-applied or the scene draws
//at the default while the control still reads where it was left.
//
//Only a browser can answer either. Both are component state behind a slider, and what they produce is a
//scene and a document rather than a value anything in C# can be asked for.
const { test, expect } = require('@playwright/test');
const { gotoExample, expectLoaded, selectExample, selectView, stackHeights, svgCounts,
        MOSFET, SKY130_CELL } = require('./helpers');

//SKY130_CELL is the cell's name; the picker lists files, so the extension goes back on. Same as history.spec.
const SKY130_FILE = `${SKY130_CELL}.gds`;

///Puts a slider where it is wanted and waits for the redraw it asks for to settle.
async function slideTo(page, id, value) {
    await page.locator(`#${id}`).evaluate((slider, wanted) => {
        slider.value = String(wanted);
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    }, value);

    //Both are coalesced behind a Settling, so the draw lands after the drag rather than during it.
    await page.waitForTimeout(1500);
}

test.describe('a slider set before the file that follows it', () => {
    ///
    ///The distance between layers, applied to a file opened after it was chosen.
    ///
    ///Discovery stacks a file's layers at the library's own default and nothing put the slider's value back,
    ///so the second file opened in a session was drawn at 50 however far apart the first one had been
    ///spread. The control still read 700, which is what made it look like it had stopped working.
    ///
    test('the distance is what a newly opened file is stacked at', async ({ page }) => {
        await gotoExample(page, MOSFET, '3d');

        await slideTo(page, 'layerSpacing', 700);

        const first = await stackHeights(page);

        //Spread by the slider: the steps between heights are the number it was set to.
        expect(first.length).toBeGreaterThan(2);
        expect(first[1] - first[0]).toBe(700);

        //A different file, into the same view.
        await selectExample(page, SKY130_FILE);
        await expectLoaded(page);
        await page.waitForTimeout(1500);

        const second = await stackHeights(page);

        expect(second.length).toBeGreaterThan(2);

        //The slider has not moved, and neither has what it means.
        await expect(page.locator('#layerSpacing')).toHaveValue('700');

        //
        //The step, not the heights: two files have different layers and so different stacks, but a step is
        //a step. At the default of 50 this comes back 50, which is the failure this exists for.
        //
        expect(second[1] - second[0]).toBe(700);
    });

    ///
    ///And the slider opens where the file is actually stacked.
    ///
    ///It opened on 10 - below its own minimum of 50, which the browser clamped away, so the number the
    ///control reported was never one the file had been drawn at. Harmless while nothing read it and a bug
    ///the moment the value above is applied to a file.
    ///
    test('the distance the slider opens on is the one the file was stacked at', async ({ page }) => {
        await gotoExample(page, MOSFET, '3d');

        const opened = await page.locator('#layerSpacing').inputValue();
        const heights = await stackHeights(page);

        expect(heights.length).toBeGreaterThan(2);

        //What the control says, and what the scene is - the same number.
        expect(heights[1] - heights[0]).toBe(Number(opened));
    });

    ///
    ///The opacity, which needs no carrying and is checked because it looks like it would.
    ///
    ///It is an argument to the draw rather than state written onto the file, so a new file is built with
    ///whatever the slider says. Worth a test anyway: the reason it works is a structural one, and a change
    ///that moved opacity onto the layers - the way the stack already is - would break this silently.
    ///
    test('the opacity is what a newly opened file is drawn at', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await slideTo(page, 'layerOpacity', 0.2);

        await selectExample(page, SKY130_FILE);
        await expectLoaded(page);
        await page.waitForTimeout(1500);

        await expect(page.locator('#layerOpacity')).toHaveValue('0.2');

        //What the shapes are actually drawn at, computed rather than read off an attribute - see svgCounts.
        //The 0.5 a file opens on is the failure this would show.
        await expect.poll(async () => (await svgCounts(page)).opacity, { timeout: 20000 }).toBe('0.2');
    });

    ///
    ///**And leaving a view and coming back keeps them, which is a different mechanism again.**
    ///
    ///Switching views destroys the component that owns these controls, so a new one starts at the defaults:
    ///measured before the fix, the distance went out at 350 and came back 50, and the opacity 0.2 out and
    ///0.5 back. Both views, so it was not one being worse than the other - and that a session records both
    ///values says they are meant to survive, which made it a hole rather than a decision.
    ///
    ///The shell reads the outgoing view's settings on the way past and hands them to the incoming one once
    ///it exists; see chooseView and applyCarriedSettings. Read *before* the switch, because `viewer` is
    ///still the old component until the render replaces it.
    ///
    test('the distance survives leaving the 3D view and coming back', async ({ page }) => {
        await gotoExample(page, MOSFET, '3d');

        await slideTo(page, 'layerSpacing', 350);

        const spread = await stackHeights(page);

        expect(spread[1] - spread[0]).toBe(350);

        await selectView(page, 'View2DSvg');
        await expectLoaded(page);
        await page.waitForTimeout(800);

        await selectView(page, 'View3D');
        await page.waitForTimeout(2500);

        //The control, and the scene it is meant to describe.
        await expect(page.locator('#layerSpacing')).toHaveValue('350');

        const back = await stackHeights(page);

        expect(back.length).toBeGreaterThan(2);
        expect(back[1] - back[0]).toBe(350);
    });

    ///<summary>And the opacity the same way, since it is the other view's half of the same hole.</summary>
    test('the opacity survives leaving the 2D view and coming back', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await slideTo(page, 'layerOpacity', 0.2);

        await expect.poll(async () => (await svgCounts(page)).opacity, { timeout: 20000 }).toBe('0.2');

        await selectView(page, 'View3D');
        await page.waitForTimeout(1500);

        await selectView(page, 'View2DSvg');
        await expectLoaded(page);

        await expect(page.locator('#layerOpacity')).toHaveValue('0.2');

        await expect.poll(async () => (await svgCounts(page)).opacity, { timeout: 20000 }).toBe('0.2');
    });
});
