//
//A pattern over a layer's color, so two layers of a similar shade are still two layers.
//
//The tile geometry is covered in C# - see SvgWriterTests - and what is left is what only a browser can
//answer: whether the definition a layer references actually reaches its shapes, whether the choice
//survives a reload, and whether a thumbnail of one cell leaves the drawing behind it alone. That last one
//is not hypothetical: pointing at a cell in the tree used to repaint the whole layout, and patterns are
//what would have made it obvious.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, openLayerSettings, MOSFET } = require('./helpers');

///Chooses a pattern for one layer, through the popup the way a person would.
async function useFill(page, index, fill) {
    await openLayerSettings(page, index);

    await page.locator(`.layerFillChoice[data-fill="${fill}"]`).click();

    //The choice redraws and saves; both are coalesced, so this waits for the render rather than the click.
    await expect(page.locator(`.layerFillChoice[data-fill="${fill}"]`)).toHaveAttribute('aria-pressed', 'true');

    await page.locator('.layerSettingsPopup .closeButton').click();

    await expect(page.locator('.layerSettingsPopup')).toHaveCount(0);
}

///
///What each drawn layer is actually filled with, computed rather than read off an attribute.
///
///The layers only. `#gdsSVG path` catches the grid's lines too, and the grid draws a different number of
///them depending on how fine it has become - so counting every path in the drawing made a change of one
///layer look like a change of one layer plus or minus a grid line. Same test as helpers.js uses, since
///the per-layer class is what tells the picture from what is drawn over it.
///
async function fills(page) {
    return page.evaluate(() => {
        return [...document.querySelectorAll('#gdsSVG path')]
            .filter(one => /(^|\s)l-?\d+_\d+(\s|$)/.test(one.getAttribute('class') || ''))
            .map(one => getComputedStyle(one).fill);
    });
}

test.describe('a pattern over a layer', () => {
    ///
    ///Nothing is patterned until somebody says so.
    ///
    ///Which is most of the point: every file, every download and every thumbnail has to be exactly what it
    ///was, so the whole feature is invisible until it is asked for.
    ///
    test('a file opens with every layer solid', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const drawn = await fills(page);

        expect(drawn.length).toBeGreaterThan(0);

        for (const fill of drawn)
            expect(fill).not.toContain('url(');

        await expect(page.locator('#gdsSVG pattern')).toHaveCount(0);
    });

    ///
    ///And a layer that was given one is filled from a definition rather than with a color.
    ///
    ///Computed, so this proves the rule reached the shape. Writing the markup is what the C# tests check;
    ///whether a browser resolves `url(#fill_l65_20)` to something that exists is a different question, and
    ///a broken reference paints nothing at all rather than failing loudly.
    ///
    test('the chosen pattern reaches the shapes on that layer', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const before = await fills(page);

        await useFill(page, 0, 'Diagonal');

        const after = await fills(page);

        //Exactly one layer changed, and it changed to a pattern.
        const patterned = after.filter(fill => fill.includes('url('));

        expect(patterned).toHaveLength(1);

        //The rest are the colors they were.
        expect(after.filter(fill => !fill.includes('url(')).length).toBe(before.length - 1);

        //And the definition it names is in the page, with something drawn inside it.
        await expect(page.locator('#gdsSVG pattern')).toHaveCount(1);

        const inside = await page.locator('#gdsSVG pattern').evaluate(node => node.children.length);

        expect(inside).toBeGreaterThan(1);
    });

    ///<summary>Each of the eight is a choice that lands, rather than the first one standing in for the rest.</summary>
    test('every pattern offered can be chosen', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const offered = await page.evaluate(async () => {
            document.querySelector('.layerRow .layerSettingsButton').click();

            await new Promise(resolve => setTimeout(resolve, 600));

            const found = [...document.querySelectorAll('.layerFillChoice')].map(one => one.dataset.fill);

            document.querySelector('.layerSettingsPopup .closeButton').click();

            return found;
        });

        //Solid, and the seven patterns.
        expect(offered).toEqual([
            'None', 'Dots', 'Squares', 'Grid', 'Dashes', 'Diagonal', 'BackDiagonal', 'CrossHatch']);

        for (const fill of offered.slice(1)) {
            await useFill(page, 0, fill);

            const patterned = (await fills(page)).filter(one => one.includes('url('));

            expect(patterned, `${fill} did not reach the layer`).toHaveLength(1);
        }

        //And back to solid, which has to be reachable or a pattern is a one-way door.
        await useFill(page, 0, 'None');

        expect((await fills(page)).filter(one => one.includes('url('))).toHaveLength(0);
    });

    ///<summary>The swatch beside the layer says what the drawing does, or the list stops describing it.</summary>
    test('the layer list shows the pattern it gave the layer', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        //A plain colored box until there is a pattern to show.
        await expect(page.locator('.layerRow').first().locator('span.layerSwatch')).toHaveCount(1);

        await useFill(page, 0, 'Grid');

        const row = page.locator('.layerRow').first();

        await expect(row.locator('svg.layerSwatch')).toHaveCount(1);
        await expect(row.locator('svg.layerSwatch pattern')).toHaveCount(1);

        //And the rows below it are untouched.
        await expect(page.locator('.layerRow').nth(1).locator('span.layerSwatch')).toHaveCount(1);
    });

    ///
    ///A pattern comes back with the session, the way a color does.
    ///
    ///It is somebody's decision about their own screen rather than anything in the file, which is exactly
    ///the kind of thing that is infuriating to have to make twice.
    ///
    ///**Onto the bare address**, which is where a session is read rather than a link honored. Reloading the
    ///`?file=` address instead does not test this at all: measured, the chosen *color* did not come back
    ///either, because naming the file in the address opens the example fresh. Same reason session.spec has
    ///a reopen of its own.
    ///
    test('a pattern survives a reload', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await useFill(page, 0, 'CrossHatch');

        //The session is written after the render; give it the round trip rather than racing it.
        await page.waitForTimeout(1500);

        //Nothing named in the address, so nothing overlays what is being read back.
        await gotoApp(page, '', null);

        await expectLoaded(page);

        //A bare address opens with nothing until the restore replaces it, so this waits for the file.
        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');

        await expect.poll(async () => (await fills(page)).filter(one => one.includes('url(')).length,
            { timeout: 30000 }).toBe(1);

        await openLayerSettings(page, 0);

        await expect(page.locator('.layerFillChoice[data-fill="CrossHatch"]')).toHaveAttribute('aria-pressed', 'true');
    });

    ///
    ///**A stipple is the same size on screen however far you zoom in.**
    ///
    ///Which is what KLayout does, and it is not what SVG does for free: a pattern is written in the
    ///layout's own units, so left alone it would be a wall of solid tone at the fit and four enormous
    ///stripes across a single via. The interop rescales it against the viewBox - see scalePatterns - and
    ///what this measures is the result of that arithmetic rather than that the function was called.
    ///
    test('a pattern stays one size on screen as the view is zoomed', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await useFill(page, 0, 'Grid');

        ///How many screen pixels one repeat covers: the tile in layout units, through the transform, into
        ///the viewBox's own scale.
        const onScreen = async () => {
            return page.evaluate(() => {
                const svg = document.getElementById('gdsSVG');
                const pattern = svg.querySelector('pattern.layerFill');
                const box = svg.viewBox.baseVal;
                const across = svg.getBoundingClientRect().width;

                const transform = pattern.getAttribute('patternTransform') || 'scale(1)';
                const scale = Number(/scale\(([-\d.eE+]+)\)/.exec(transform)[1]);

                return Number(pattern.getAttribute('width')) * scale * across / box.width;
            });
        };

        const spanned = () => page.evaluate(() => document.getElementById('gdsSVG').viewBox.baseVal.width);

        const before = await onScreen(page);
        const wide = await spanned();

        //Well clear of a hairline and well short of a stripe - the range a texture is legible in at all.
        expect(before).toBeGreaterThan(4);
        expect(before).toBeLessThan(20);

        const middle = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(middle.x + (middle.width / 2), middle.y + (middle.height / 2));

        for (let turn = 0; turn < 6; turn++)
            await page.mouse.wheel(0, -400);

        await page.waitForTimeout(900);

        //Genuinely closer in, or what follows passes because nothing happened.
        expect(await spanned()).toBeLessThan(wide / 2);

        const after = await onScreen(page);

        //Within a pixel of where it was, against a viewBox that is now a fraction of its old width.
        expect(Math.abs(after - before)).toBeLessThan(1);
    });

    ///
    ///The two controls that shape a pattern are only there for a layer that has one.
    ///
    ///A size box on a solid layer is a number that changes nothing, and a Background/Pattern switch is a
    ///pair of buttons where one of them has nothing on the other end. Both read as the control being
    ///broken rather than as not applying.
    ///
    test('the pattern color and size appear only once there is a pattern', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await openLayerSettings(page, 0);

        await expect(page.locator('#patternSize')).toHaveCount(0);
        await expect(page.locator('#colorPattern')).toHaveCount(0);
        await expect(page.locator('#resetColor')).toHaveText('Reset to palette');

        await page.locator('.layerFillChoice[data-fill="Dots"]').click();

        await expect(page.locator('#patternSize')).toHaveValue(String(9));
        await expect(page.locator('#colorBackground')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('#colorPattern')).toHaveAttribute('aria-pressed', 'false');

        //And they go again when the layer goes back to solid, taking the picker with them.
        await page.locator('#colorPattern').click();
        await expect(page.locator('#resetColor')).toHaveText('Match the layer');

        await page.locator('.layerFillChoice[data-fill="None"]').click();

        await expect(page.locator('#patternSize')).toHaveCount(0);
        await expect(page.locator('#resetColor')).toHaveText('Reset to palette');
    });

    ///
    ///A pattern color changes the marks and leaves the ground alone.
    ///
    ///Measured in the drawing rather than in the popup: the tile's ground is the layer's own color washed
    ///out, and the motif over it is what the second color is for. Coloring both would be recoloring the
    ///layer by another route, which is the failure this is here to catch.
    ///
    test('a pattern can be colored apart from the layer', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await openLayerSettings(page, 0);
        await page.locator('.layerFillChoice[data-fill="Grid"]').click();

        ///The tile's ground, and everything drawn over it.
        const tile = () => page.evaluate(() => {
            const pattern = document.querySelector('#gdsSVG pattern.layerFill');
            const parts = [...pattern.children];

            return {
                ground: parts[0].getAttribute('fill'),
                marks: parts.slice(1)
                    .map(one => one.getAttribute('stroke') || one.getAttribute('fill'))
                    .filter(one => one !== null && one !== 'none')
            };
        });

        const following = await tile();

        expect(following.marks.length).toBeGreaterThan(0);

        //Until something says otherwise the marks are the layer's own color, which is the old behavior.
        for (const mark of following.marks)
            expect(mark).toBe(following.ground);

        await page.locator('#colorPattern').click();

        for (const [nth, level] of [[0, '0'], [1, '255'], [2, '0']]) {
            await page.locator('.layerSettingsChannel').nth(nth).fill(level);
            await page.locator('.layerSettingsChannel').nth(nth).press('Enter');
        }

        await expect.poll(async () => (await tile()).marks[0], { timeout: 15000 }).toBe('#00ff00');

        //The ground is untouched, which is the half that says this is not just a recolor.
        expect((await tile()).ground).toBe(following.ground);

        //And back again, which is the only way to undo it.
        await page.locator('#resetColor').click();

        await expect.poll(async () => (await tile()).marks[0], { timeout: 15000 }).toBe(following.ground);
    });

    ///
    ///The size box says how many pixels one repeat covers, and it does.
    ///
    ///Measured on screen rather than in the markup: the tile is written in layout units and the interop
    ///rescales it, so a box wired only to the attribute would show a number that had nothing to do with
    ///what anybody is looking at.
    ///
    test('the pattern size is the number of pixels a repeat covers', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await openLayerSettings(page, 0);
        await page.locator('.layerFillChoice[data-fill="Grid"]').click();

        const onScreen = () => page.evaluate(() => {
            const svg = document.getElementById('gdsSVG');
            const pattern = svg.querySelector('pattern.layerFill');
            const transform = pattern.getAttribute('patternTransform') || 'scale(1)';
            const scale = Number(/scale\(([-\d.eE+]+)\)/.exec(transform)[1]);

            return Number(pattern.getAttribute('width')) * scale * svg.getBoundingClientRect().width / svg.viewBox.baseVal.width;
        });

        await expect.poll(onScreen, { timeout: 15000 }).toBeCloseTo(9, 0);

        await page.locator('#patternSize').fill('24');
        await page.locator('#patternSize').press('Enter');

        await expect.poll(onScreen, { timeout: 15000 }).toBeCloseTo(24, 0);

        //Out of range is clamped rather than refused: this is a spinner being nudged.
        await page.locator('#patternSize').fill('900');
        await page.locator('#patternSize').press('Enter');

        await expect.poll(onScreen, { timeout: 15000 }).toBeCloseTo(64, 0);
    });

    ///<summary>And both come back with the file, the way the pattern itself does.</summary>
    test('the pattern color and size survive a reload', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await openLayerSettings(page, 0);
        await page.locator('.layerFillChoice[data-fill="Diagonal"]').click();

        await page.locator('#patternSize').fill('18');
        await page.locator('#patternSize').press('Enter');

        await page.locator('#colorPattern').click();

        for (const [nth, level] of [[0, '0'], [1, '255'], [2, '0']]) {
            await page.locator('.layerSettingsChannel').nth(nth).fill(level);
            await page.locator('.layerSettingsChannel').nth(nth).press('Enter');
        }

        await page.locator('.layerSettingsPopup .closeButton').click();

        //The session is written after the render; give it the round trip rather than racing it.
        await page.waitForTimeout(1500);

        await gotoApp(page, '', null);

        await expectLoaded(page);
        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');

        await expect.poll(async () => page.evaluate(() => {
            const pattern = document.querySelector('#gdsSVG pattern.layerFill');

            if (pattern == null)
                return null;

            return {
                pixels: pattern.getAttribute('data-pixels'),
                marks: pattern.children[1]?.getAttribute('stroke')
            };
        }), { timeout: 30000 }).toEqual({ pixels: '18', marks: '#00ff00' });
    });

    ///
    ///**A thumbnail leaves the drawing behind it alone.**
    ///
    ///A `<style>` inside an inline SVG is hoisted to the document, and an id is resolved document-wide - so
    ///two pictures of one file wrote the same per-layer rules and the later won for both. Measured before
    ///the fix: opening the cell tree and pointing at a row took the *layout* from the slider's 0.5 to the
    ///thumbnail's 0.85, and moving the pointer away put it back. Patterns would have handed it the
    ///thumbnail's tile as well, which is a different pitch because a cell is smaller than the file.
    ///
    test('a cell preview does not repaint the layout behind it', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d', true);

        await useFill(page, 0, 'Dots');

        const before = await page.evaluate(() => {
            const one = document.querySelector('#gdsSVG path');

            return { fill: getComputedStyle(one).fill, opacity: getComputedStyle(one).opacity };
        });

        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellTreePreview svg.cellPreview')).toBeVisible({ timeout: 15000 });

        const after = await page.evaluate(() => {
            const one = document.querySelector('#gdsSVG path');

            return { fill: getComputedStyle(one).fill, opacity: getComputedStyle(one).opacity };
        });

        //The drawing is what it was, both ways it could have been taken over.
        expect(after.opacity).toBe(before.opacity);
        expect(after.fill).toBe(before.fill);

        //And the preview drew its own pattern rather than borrowing the layout's.
        const scopes = await page.evaluate(() => {
            return [...document.querySelectorAll('pattern')].map(one => one.id);
        });

        expect(new Set(scopes).size).toBe(scopes.length);
    });
});
