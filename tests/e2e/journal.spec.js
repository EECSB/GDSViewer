//An edit saves the steps taken since the file was last written whole, rather than the whole file.
//
//That those steps, taken again on the bytes they were saved against, come out as the file byte for byte - for
//every kind of edit, undone, redone and branched - is covered in JournalTests. What is only checkable here is
//the wiring: that an edit writes its steps and leaves the bytes alone, that a reload takes them again, and that
//the moments a file is finished with for now - the page hidden, a download - write it whole.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, shapeBox, allPoints, storedSession, layerPairs, selectView, saveEditorText, editorText, expectEditorLoaded, MOSFET, SKY130_CELL } = require('./helpers');

///
///Counts every write from here on: which key, and how long the value was.
///
///At IndexedDB's own put rather than by wrapping gdsStorage: Blazor's interop keeps the function it found for a
///name the first time it called it, so a wrapper put on afterwards never hears from it. See session.spec.
///
async function countWrites(page) {
    await page.evaluate(() => {
        window.__writes = [];

        if (window.__counting)
            return;

        window.__counting = true;

        const put = IDBObjectStore.prototype.put;

        IDBObjectStore.prototype.put = function (value, key) {
            window.__writes.push([key, String(value).length]);

            return put.call(this, value, key);
        };
    });
}

function writesTo(page, key) {
    return page.evaluate((key) => window.__writes.filter(([written]) => written === key).length, key);
}

///
///One step: a shape of Mosfet nudged a grid step to the right, chosen first if nothing is.
///
///Waited on through the undo button, which names the step, and then through the session being written - the
///save comes after the redraw, and a reload that raced it would read the session before the step.
///
async function nudge(page) {
    if (await page.locator('#gdsSVG .shapeSelected').count() === 0) {
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
    }

    const written = await writesTo(page, 'gdsviewer.session');

    await page.keyboard.press('ArrowRight');

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move/, { timeout: 15000 });
    await expect.poll(async () => writesTo(page, 'gdsviewer.session'), { timeout: 30000 }).toBeGreaterThan(written);
}

///Reopens onto the bare address, which is where the session is put back rather than a link honored.
async function reopen(page) {
    await gotoApp(page, '', null);

    await expectLoaded(page);
}

///The steps the stored session carries, or none.
async function storedSteps(page) {
    const session = JSON.parse(await storedSession(page));

    return (session.j ?? []).length;
}

test.describe('an edit', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => (await allPoints(page)).length, { timeout: 60000 }).toBeGreaterThan(0);

        await countWrites(page);
    });

    ///
    ///**The first edit of an example writes the file**, because until then the session names the example and
    ///there is nothing beside it to take steps on. Every edit after that writes the steps alone.
    ///
    test('saves its steps rather than the file, and a reload takes them again', async ({ page }) => {
        await nudge(page);

        expect(await writesTo(page, 'gdsviewer.session#bytes')).toBe(1);

        await nudge(page);
        await nudge(page);

        //Still the one writing, and the two steps since it beside it.
        expect(await writesTo(page, 'gdsviewer.session#bytes')).toBe(1);
        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(2);

        const edited = await allPoints(page);

        await reopen(page);

        await expect.poll(async () => allPoints(page), { timeout: 60000 }).toEqual(edited);
    });

    ///An undo is a step of its own, and the undo stack comes back as it was left.
    test('an undo is saved as a step too', async ({ page }) => {
        await nudge(page);
        await nudge(page);
        await nudge(page);

        const written = await writesTo(page, 'gdsviewer.session');

        await page.keyboard.press('Control+z');

        await expect.poll(async () => writesTo(page, 'gdsviewer.session'), { timeout: 30000 }).toBeGreaterThan(written);
        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(3);

        expect(await writesTo(page, 'gdsviewer.session#bytes')).toBe(1);

        const undone = await allPoints(page);

        await reopen(page);

        await expect.poll(async () => allPoints(page), { timeout: 60000 }).toEqual(undone);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move/);
        await expect(page.locator('#redoEdit')).toHaveAttribute('title', /Redo Move/);
    });

    ///
    ///**The page hidden writes the file whole**: another tab, the window minimized, a phone's home screen, or the
    ///page on its way out - the last moment anything is sure to run. Fired rather than done, since what is under
    ///test is what the app does about it.
    ///
    test('is written into the file when the page is hidden', async ({ page }) => {
        await nudge(page);
        await nudge(page);

        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(1);

        await page.evaluate(() => {
            Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });

            document.dispatchEvent(new Event('visibilitychange', { bubbles: true }));
        });

        await expect.poll(async () => writesTo(page, 'gdsviewer.session#bytes'), { timeout: 30000 }).toBe(2);
        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(0);

        await page.evaluate(() => {
            delete document.visibilityState;
        });

        const edited = await allPoints(page);

        await reopen(page);

        await expect.poll(async () => allPoints(page), { timeout: 60000 }).toEqual(edited);
    });

    ///
    ///**Steps saved against one writing of the file are never taken again on another.** A session and its bytes
    ///are read one at a time, and a value missing from IndexedDB comes from the exit handler's copy, which can be
    ///older - so the session names the writing its steps belong to, and here it names another one. What opens is
    ///the bytes as they are: no steps, and no undo stack, which describes the file the steps led to.
    ///
    test('steps that do not belong to the bytes beside them are left out, with the undo stack', async ({ page }) => {
        await nudge(page);

        const written = await allPoints(page);

        await nudge(page);

        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(1);

        //The session as stored, naming another writing - raw behind its marker, one of the two ways AppStorage
        //stores a value - and nothing written over it before the page goes. See session.spec, which does the same.
        const session = JSON.parse(await storedSession(page));

        session.bs = 'elsewhere';

        await page.evaluate(async (text) => {
            await window.gdsStorage.set('gdsviewer.session', 'r' + text);

            IDBObjectStore.prototype.put = function () {
                return null;
            };

            window.gdsExitSave.release();
        }, JSON.stringify(session));

        await reopen(page);

        await expect.poll(async () => allPoints(page), { timeout: 60000 }).toEqual(written);
        await expect(page.locator('#undoEdit')).toHaveCount(0);
    });

    ///A file downloaded is a file finished with for now.
    test('is written into the file when the file is downloaded', async ({ page }) => {
        await nudge(page);
        await nudge(page);

        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(1);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        await started;

        await expect.poll(async () => writesTo(page, 'gdsviewer.session#bytes'), { timeout: 30000 }).toBe(2);
        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(0);
    });

    ///
    ///**The text view's save writes the file whole, and lets go of the undo stack.** It reads the whole file back
    ///in from the text, every cell and shape a new object - so an edit on the stack points at a library that is
    ///no longer the file, and saved as a step it would be taken again on the real one at the next reload.
    ///
    test('saved from the text view writes the file whole, and nothing is left to undo', async ({ page }) => {
        await nudge(page);
        await nudge(page);

        await selectView(page, 'ViewText');
        await expectEditorLoaded(page);

        const text = await editorText(page);
        const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

        expect(messages.join(' ')).toContain('Saved');

        await expect.poll(async () => writesTo(page, 'gdsviewer.session#bytes'), { timeout: 30000 }).toBe(2);
        await expect.poll(async () => storedSteps(page), { timeout: 30000 }).toBe(0);

        await selectView(page, 'View2DSvg');

        await expect.poll(async () => (await allPoints(page)).length, { timeout: 60000 }).toBeGreaterThan(0);

        //The pair is drawn only while there is something to undo or redo - and a press of the key does nothing.
        const saved = await allPoints(page);

        await expect(page.locator('#undoEdit')).toHaveCount(0);

        await page.keyboard.press('Control+z');

        await page.waitForTimeout(1000);

        expect(await allPoints(page)).toEqual(saved);
    });
});

///
///**Removing a layer that has shapes on it is an edit, and is kept like one.** It took the shapes out through the
///edit history and went straight to a save, so nothing heard the file had changed: an example's session went on
///naming the example, and a reload brought the shapes back.
///
test('a layer removed with its shapes is still removed after a reload', async ({ page }) => {
    await gotoExample(page, SKY130_CELL, '2d', true);

    await expect.poll(async () => layerPairs(page), { timeout: 60000 }).toContain('66/44');
    await expect(page.locator('#cellTree .cellRowPair[data-kind="layer"]').filter({ hasText: '66/44' })).not.toHaveCount(0);

    page.once('dialog', dialog => dialog.accept());

    await page.locator('#layerSidebar .layerRow').filter({ hasText: '66/44' }).locator('.layerRemove').click();

    await expect.poll(async () => layerPairs(page), { timeout: 30000 }).not.toContain('66/44');

    //The tree heard too, which lists a cell's layers and went on listing this one.
    await expect(page.locator('#cellTree .cellRowPair[data-kind="layer"]').filter({ hasText: '66/44' })).toHaveCount(0);

    await expect.poll(async () => storedSession(page), { timeout: 30000 }).toContain('"ba":true');

    await reopen(page);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${SKY130_CELL}.gds`);
    await expect.poll(async () => (await layerPairs(page)).length, { timeout: 60000 }).toBeGreaterThan(0);

    expect(await layerPairs(page)).not.toContain('66/44');
});
