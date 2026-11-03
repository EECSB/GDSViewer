//
//The two switches on a layer row: the eye that draws it, and the lock that lets it be worked on.
//
//**Three states, not two.** Hiding takes a layer off the screen, which is the wrong answer when it is the
//thing being worked *against* - a via has to line up with the metal over it, and nothing lines up with what
//it cannot see. Locking leaves it drawn, fades it, and takes it out of everything that picks. What is left
//unlocked is the working set, and it can be as many layers as it needs to be.
//
//**Only a browser can answer this.** Whether a faded shape can still be picked is settled by HitTest in C#
//and reported back through the browser, and what a drag then does to it is pointer handling on top of that -
//so the question needs a real pointer against a real document. The first attempt at this feature faded the
//shapes in CSS and left them every bit as draggable, which looked exactly like it worked.
//
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, shapeBox, allPoints, snapToGrid, svgCounts, uploadFile, allFills, MOSFET, MOSFET_POLYGONS } = require('./helpers');

///One GDSII record: its length and type word, then the payload.
function record(type, dataType, payload) {
    const header = Buffer.alloc(4);

    header.writeUInt16BE(4 + payload.length, 0);
    header.writeUInt8(type, 2);
    header.writeUInt8(dataType, 3);

    return Buffer.concat([header, payload]);
}

function int2(...values) {
    const bytes = Buffer.alloc(values.length * 2);

    values.forEach((value, i) => bytes.writeInt16BE(value, i * 2));

    return bytes;
}

function int4(...values) {
    const bytes = Buffer.alloc(values.length * 4);

    values.forEach((value, i) => bytes.writeInt32BE(value, i * 4));

    return bytes;
}

function ascii(text) {
    let bytes = Buffer.from(text, 'ascii');

    if (bytes.length % 2 === 1)
        bytes = Buffer.concat([bytes, Buffer.alloc(1)]);

    return bytes;
}

///
///A library of its own: two squares on 200/0 and 201/0, layers no PDK here has, so the app lays none over it and
///its rows are the file's alone - colored from the palette, and bare when an undo re-registers them.
///
function ownLibrary() {
    const stamp = int2(126, 1, 1, 0, 0, 0, 126, 1, 1, 0, 0, 0);
    const units = Buffer.from('3e4189374bc6a7ef3944b82fa09b5a53', 'hex');
    const none = Buffer.alloc(0);

    const square = (layer, x) => Buffer.concat([
        record(0x08, 0x00, none),
        record(0x0D, 0x02, int2(layer)),
        record(0x0E, 0x02, int2(0)),
        record(0x10, 0x03, int4(x, 0, x + 1000, 0, x + 1000, 1000, x, 1000, x, 0)),
        record(0x11, 0x00, none)
    ]);

    return Buffer.concat([
        record(0x00, 0x02, int2(600)),
        record(0x01, 0x02, stamp),
        record(0x02, 0x06, ascii('OWN')),
        record(0x03, 0x05, units),
        record(0x05, 0x02, stamp),
        record(0x06, 0x06, ascii('TOP')),
        square(200, 0),
        square(201, 2000),
        record(0x07, 0x00, none),
        record(0x04, 0x00, none)
    ]);
}

///The row whose name contains what is asked for.
function rowNamed(page, name) {
    return page.locator('.layerRow').filter({ has: page.locator('.layerName', { hasText: name }) }).first();
}

///
///Every layer with something of its own under a point on the screen, as pairs.
///
///Asked of the drawing itself through isPointInFill rather than by naming pairs, so it does not pin the
///fixture's geometry.
///
async function coveringAt(page, at) {
    return page.evaluate(([x, y]) => {
        const svg = document.querySelector('#gdsSVG');
        const where = new DOMPoint(x, y).matrixTransform(svg.getScreenCTM().inverse());
        const found = [];

        for (const path of svg.querySelectorAll('path')) {
            const layer = [...path.classList].find(name => /^l-?\d+_\d+$/.test(name));

            if (layer !== undefined && path.isPointInFill(where))
                found.push(layer.slice(1).replace('_', '/'));
        }

        return found;
    }, [at.x, at.y]);
}

///
///How every path in the drawing is currently painted and whether it can be pressed, in buckets.
///
///Bucketed rather than listed: what matters is how many are held back and how many are live, and a list of
///nineteen paths would pin which shapes the fixture happens to have.
///
async function paintBuckets(page) {
    return page.evaluate(() => {
        const buckets = {};

        for (const path of document.querySelectorAll('#gdsSVG path')) {
            const style = getComputedStyle(path);
            const key = style.opacity + '/' + style.pointerEvents;

            buckets[key] = (buckets[key] || 0) + 1;
        }

        return buckets;
    });
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    await expect(page.locator('#gdsSVG')).toBeVisible({ timeout: 60000 });
});

test('every layer row carries an eye and a lock', async ({ page }) => {
    const rows = await page.locator('.layerRow').count();

    expect(rows).toBeGreaterThan(0);

    await expect(page.locator('.layerEyeButton')).toHaveCount(rows);
    await expect(page.locator('.layerLockButton')).toHaveCount(rows);

    //The checkbox they replaced is gone rather than sitting beside them.
    await expect(page.locator('.layerVisible')).toHaveCount(0);
});

///
///The eye takes the layer off the screen entirely, which is what tells it from the lock.
///
test('the eye stops the layer being drawn', async ({ page }) => {
    const before = (await page.locator('#gdsSVG path').count());

    await rowNamed(page, 'poly (66/20)').locator('.layerEyeButton').click();

    await expect.poll(async () => page.locator('#gdsSVG path').count()).toBe(before - 1);

    await expect(rowNamed(page, 'poly (66/20)').locator('.layerEyeButton')).toHaveClass(/layerEyeOff/);

    //And back again, to exactly what was there.
    await rowNamed(page, 'poly (66/20)').locator('.layerEyeButton').click();

    await expect.poll(async () => page.locator('#gdsSVG path').count()).toBe(before);
});

///
///The lock leaves it drawn and fades it - the difference from the eye, stated.
///
test('the lock leaves the layer drawn and fades it', async ({ page }) => {
    const drawn = await page.locator('#gdsSVG path').count();

    await rowNamed(page, 'diff (65/20)').locator('.layerLockButton').click();

    await expect(rowNamed(page, 'diff (65/20)').locator('.layerLockButton')).toHaveClass(/layerLockOn/);

    //Still every path it had: locking is not hiding.
    await expect(page.locator('#gdsSVG path')).toHaveCount(drawn);

    const buckets = await paintBuckets(page);
    const faded = Object.entries(buckets).filter(([key]) => key.startsWith('0.12/'));

    //Exactly one layer held back, and the rest of the drawing untouched.
    expect(faded.length).toBe(1);
    expect(faded[0][1]).toBe(1);

    for (const [key] of faded)
        expect(key.endsWith('/none')).toBe(true);
});

///
///**Any number of them at once**, which is the whole reason this is a lock per row and not one isolated
///layer. A via and the two metals it joins are three layers in hand together; everything else can go.
///
test('several layers can be locked at once', async ({ page }) => {
    for (const name of ['diff (65/20)', 'poly (66/20)', 'li1 (67/20)'])
        await rowNamed(page, name).locator('.layerLockButton').click();

    await expect(page.locator('.layerLockOn')).toHaveCount(3);

    const buckets = await paintBuckets(page);
    const faded = Object.entries(buckets).filter(([key]) => key.startsWith('0.12/'));

    expect(faded.length).toBe(1);
    expect(faded[0][1]).toBe(3);

    //And the rest are still live.
    expect(Object.keys(buckets).some(key => key.endsWith('/auto'))).toBe(true);
});

///
///What the fade only looks like: a locked shape cannot be chosen.
///
///**Asserted on which layer answered, not that none did.** These layers overlap, so locking the one that
///answered and pressing the same place may legitimately find whatever is beneath it. What must never happen
///is the locked layer answering.
///
test.describe('what a click can land on', () => {
    ///The point of the first drawn shape, and the layer that answers a press there.
    async function pointAndLayer(page) {
        const box = await shapeBox(page, 0);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        await page.locator('#selectTool').click();
        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#selectionPanel')).toBeVisible();

        return { at, pair: (await page.locator('#chosenLayer').textContent()).match(/\d+\/\d+/)[0] };
    }

    ///Clears whatever is chosen by pressing where nothing is.
    async function letGo(page) {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 4, view.y + 4);

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    }

    test('a locked layer cannot be chosen', async ({ page }) => {
        const { at, pair } = await pointAndLayer(page);

        await letGo(page);

        await rowNamed(page, pair).locator('.layerLockButton').click();

        await page.mouse.click(at.x, at.y);

        //Either nothing answered, or something under it did - never the locked layer.
        if (await page.locator('#selectionPanel').count() > 0)
            await expect(page.locator('#chosenLayer')).not.toContainText(pair);
    });

    test('unlocking puts it back within reach', async ({ page }) => {
        const { at, pair } = await pointAndLayer(page);

        await letGo(page);

        const lock = rowNamed(page, pair).locator('.layerLockButton');

        await lock.click();
        await expect(lock).toHaveClass(/layerLockOn/);

        await lock.click();
        await expect(lock).not.toHaveClass(/layerLockOn/);

        await letGo(page);
        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#chosenLayer')).toContainText(pair);
    });

    ///
    ///A selection made before locking lets go of what has just gone out of reach.
    ///
    ///Otherwise the shapes stay chosen on a locked layer and the move tool goes on dragging them: they were
    ///picked while they were still pickable, and nothing asks again on the way into a drag.
    ///
    test('locking lets go of anything chosen on that layer', async ({ page }) => {
        const { pair } = await pointAndLayer(page);

        await expect(page.locator('#selectionPanel')).toBeVisible();

        await rowNamed(page, pair).locator('.layerLockButton').click();

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });

    ///
    ///And it cannot be dragged, which is the half that changes the file.
    ///
    ///Locked from a point the *other* layers do not cover, so the drag has nothing legitimate to take hold
    ///of and the drawing must stand still.
    ///
    test('a locked layer cannot be dragged', async ({ page }) => {
        const box = await shapeBox(page, 0);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        //Every layer with something of its own under that point - all of which have to be locked for the
        //press to find nothing at all.
        const covering = await coveringAt(page, at);

        expect(covering.length).toBeGreaterThan(0);

        for (const pair of covering)
            await rowNamed(page, pair).locator('.layerLockButton').click();

        await expect(page.locator('.layerLockOn')).toHaveCount(covering.length);

        const before = await allPoints(page);

        await page.locator('#moveTool').click();

        await page.mouse.move(at.x, at.y);
        await page.mouse.down();
        await page.mouse.move(at.x + 70, at.y + 60, { steps: 8 });
        await page.mouse.up();

        await page.waitForTimeout(800);

        //Every outline exactly where it was, which is the claim: a locked shape does not move.
        expect(await allPoints(page)).toEqual(before);

        //
        //**Something may well be chosen, and that is correct.**
        //
        //With nothing under the pointer to take hold of, the drag is a rubber band rather than a move - and
        //a band catches whatever unlocked shapes it crosses on the way. What it must not have caught is any
        //of the layers that were locked.
        //
        if (await page.locator('#selectionPanel').count() > 0) {
            const chosen = await page.locator('#chosenLayer').textContent();

            for (const pair of covering)
                expect(chosen).not.toContain(pair);
        }
    });
});

///
///**A hidden layer is out of reach of everything that picks, not only of a click.**
///
///A click has always passed through a hidden layer. A rubber band and Select All asked only about locks, so
///they took a hidden layer's shapes by their boxes, and the chosen shapes came back as dashed outlines over
///canvas with nothing on it. A press that slips a few pixels before it lets go is a band - which is how it was
///found: by clicking where a hidden layer was.
///
test.describe('a hidden layer is out of reach', () => {
    ///Every pair the chosen shapes are on: the panel's "on 65/20, 67/20" line for several, the picker for one.
    async function chosenPairs(page) {
        if (await page.locator('#selectionPanel').count() === 0)
            return [];

        const several = page.locator('#selectionPanel .selectionRow', { hasText: /^on \d/ });

        if (await several.count() > 0)
            return (await several.first().textContent()).match(/\d+\/\d+/g);

        return [await page.locator('#chosenLayer').getAttribute('data-layer')];
    }

    test('a press that slips does not choose it', async ({ page }) => {
        const box = await shapeBox(page, 0);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };
        const covering = await coveringAt(page, at);

        expect(covering.length).toBeGreaterThan(0);

        for (const pair of covering)
            await rowNamed(page, pair).locator('.layerEyeButton').click();

        await expect(page.locator('.layerEyeOff')).toHaveCount(covering.length);

        //Snapping off, so the slip is a band its own few pixels across rather than a whole grid step.
        await snapToGrid(page, false);
        await page.locator('#selectTool').click();

        await page.mouse.move(at.x, at.y);
        await page.mouse.down();
        await page.mouse.move(at.x + 4, at.y + 3, { steps: 2 });
        await page.mouse.up();

        await page.waitForTimeout(500);

        const chosen = await chosenPairs(page);

        for (const pair of covering)
            expect(chosen).not.toContain(pair);
    });

    test('Select All leaves it alone', async ({ page }) => {
        const box = await shapeBox(page, 0);

        //Into the cell first: Ctrl+A takes what the cell being edited holds, and a click is what enters it.
        await page.locator('#selectTool').click();
        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();

        const pair = await page.locator('#chosenLayer').getAttribute('data-layer');

        await rowNamed(page, pair).locator('.layerEyeButton').click();
        await expect(rowNamed(page, pair).locator('.layerEyeButton')).toHaveClass(/layerEyeOff/);

        await page.keyboard.press('Control+a');

        await expect(page.locator('#selectionPanel')).toBeVisible();
        expect(await chosenPairs(page)).not.toContain(pair);
    });
});

///
///**The header over the list: every row's eye, padlock and × at once.**
///
///Each switch goes back to normal first - any layer hidden and the eye draws them all, any locked and the
///padlock unlocks them all - and only with the whole column at normal does it put everything away. Its
///controls carry classes of their own, because the rows' are counted: a header answering to .layerEyeButton
///would be one more eye than there are layers.
///
test.describe('the header over the list', () => {
    test('its eye hides every layer, and draws them all again', async ({ page }) => {
        const rows = await page.locator('.layerRow').count();
        const all = page.locator('#allLayersShown');

        await expect(all).toHaveAttribute('aria-pressed', 'true');

        await all.click();

        await expect(page.locator('.layerRow .layerEyeOff')).toHaveCount(rows);
        await expect(all).toHaveAttribute('aria-pressed', 'false');
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(0);

        await all.click();

        await expect(page.locator('.layerRow .layerEyeOff')).toHaveCount(0);
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

        //Still one eye per row: the header's is not one of them.
        await expect(page.locator('.layerEyeButton')).toHaveCount(rows);
    });

    test('with a column of both, each switch puts everything back to normal', async ({ page }) => {
        await page.locator('.layerRow').nth(1).locator('.layerEyeButton').click();
        await page.locator('.layerRow').nth(2).locator('.layerLockButton').click();

        await expect(page.locator('#allLayersShown')).toHaveAttribute('aria-pressed', 'mixed');
        await expect(page.locator('#allLayersLocked')).toHaveAttribute('aria-pressed', 'mixed');

        await page.locator('#allLayersShown').click();
        await page.locator('#allLayersLocked').click();

        await expect(page.locator('.layerRow .layerEyeOff')).toHaveCount(0);
        await expect(page.locator('.layerRow .layerLockOn')).toHaveCount(0);

        //And from all unlocked, the padlock locks the lot.
        await page.locator('#allLayersLocked').click();

        await expect(page.locator('.layerRow .layerLockOn')).toHaveCount(await page.locator('.layerRow').count());
    });

    ///Asked first, with what goes; and the whole of it comes back on one undo, every row saying what it said.
    test('its × removes every layer after asking, and one undo puts them back', async ({ page }) => {
        const names = await page.locator('.layerRow .layerName').allTextContents();

        let asked = '';

        page.once('dialog', dialog => {
            asked = dialog.message();
            dialog.dismiss();
        });

        await page.locator('#removeAllLayers').click();

        await expect.poll(() => asked).toContain('remove all');
        await expect(page.locator('.layerRow')).toHaveCount(names.length);

        page.once('dialog', dialog => dialog.accept());
        await page.locator('#removeAllLayers').click();

        await expect(page.locator('.layerRow')).toHaveCount(0);
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(0);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => page.locator('.layerRow .layerName').allTextContents()).toEqual(names);
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
    });

    ///
    ///And in their colors, on a file no PDK is laid over - whose rows come back bare from the file and get their
    ///settings put back by the shell. The view flattens before the shell runs that, holding the bare rows, so the
    ///settings go into those rows rather than over them; until 2026-11-03 the shapes came back in a new layer's gray
    ///under swatches showing their colors.
    ///
    test('undone on a file of its own, the shapes come back in their colors', async ({ page }) => {
        //Opened as a file, not added to the example: the question the app asks of an upload while one is open.
        await uploadFile(page, { name: 'own.gds', mimeType: 'application/octet-stream', buffer: ownLibrary() });
        await page.locator('#importAsFile').click();

        await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBe(2);
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(2);

        const rows = await page.locator('.layerRow').count();
        const before = [...await allFills(page)].sort();

        expect(before.length).toBe(2);

        page.once('dialog', dialog => dialog.accept());
        await page.locator('#removeAllLayers').click();

        await expect(page.locator('.layerRow')).toHaveCount(0);
        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(0);

        await page.locator('#undoEdit').click();

        await expect(page.locator('.layerRow')).toHaveCount(rows);
        await expect.poll(async () => [...await allFills(page)].sort()).toEqual(before);
        expect(before).not.toContain('rgb(128, 128, 128)');
    });

    test('its switches stand over the columns they act on', async ({ page }) => {
        const lefts = await page.evaluate(() => {
            const x = element => Math.round(element.getBoundingClientRect().left);
            const row = document.querySelector('.layerRow');

            return {
                eye: [x(document.querySelector('#allLayersShown')), x(row.querySelector('.layerEyeButton'))],
                lock: [x(document.querySelector('#allLayersLocked')), x(row.querySelector('.layerLockButton'))],
                remove: [x(document.querySelector('#removeAllLayers')), x(row.querySelector('.layerRemove'))]
            };
        });

        expect(lefts.eye[0]).toBe(lefts.eye[1]);
        expect(lefts.lock[0]).toBe(lefts.lock[1]);
        expect(lefts.remove[0]).toBe(lefts.remove[1]);
    });

    ///
    ///The padlock's handler always said it saved, and there was nowhere for a lock to go.
    ///
    ///Back through a bare address rather than a reload of this one: a session is put back only where the
    ///address names no file, since a link is a request for that file as it is - the same way the labels
    ///switch's own test comes back.
    ///
    test('a lock outlives a reload', async ({ page }) => {
        const lock = rowNamed(page, 'poly (66/20)').locator('.layerLockButton');

        await lock.click();
        await expect(lock).toHaveClass(/layerLockOn/);

        await gotoApp(page);
        await expectLoaded(page);

        await expect(rowNamed(page, 'poly (66/20)').locator('.layerLockButton')).toHaveClass(/layerLockOn/, { timeout: 60000 });
        await expect(page.locator('.layerRow .layerLockOn')).toHaveCount(1);
    });
});
