const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const { gotoApp, gotoExample, MOSFET, MOSFET_POLYGONS, shapeCount, openFile, acceptsClosingWhatIsOpen } = require('./helpers');

///
///Dropping a layout file onto the view.
///
///**The point of the feature is that it is not a second way in.** A drop sets the file on the same hidden
///input the Open dialog uses and dispatches that input's own change event, so everything after the drop is
///the upload path already there - which is why the assertions below are mostly about what the *upload* does:
///the offer to bring the file in as a cell, the cells landing in the library, the file opening on its own.
///If a drop ever stops reaching those, it has grown a path of its own and that is the bug.
///
///What is genuinely new here, and so tested directly, is everything around the drop: which drags are taken,
///where they may be dropped, what is drawn while one is held over the view, and the fact that a file dropped
///anywhere in the window never navigates the browser away from the app.
///
///The drags are synthetic. A real one starts outside the browser and no automation can raise it, so a spec
///builds the DataTransfer itself and dispatches the same events the browser would - which is what the code
///under test reads, and all of it.
///
const NAND = path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', 'sky130_fd_sc_hd__nand2_1.gds');

///
///A drag carrying one real layout file, as a handle the dispatches below can share.
///
///Shared on purpose: a drag is one gesture, and dragover and the drop that ends it carry the same
///DataTransfer. Building a fresh one per event would be a different drag each time and would not exercise
///the thing being tested.
///
async function fileBeingDragged(page, name = 'nand2.gds') {
    const bytes = Array.from(fs.readFileSync(NAND));

    return page.evaluateHandle(([data, called]) => {
        const carrying = new DataTransfer();

        carrying.items.add(new File([new Uint8Array(data)], called, { type: 'application/octet-stream' }));

        return carrying;
    }, [bytes, name]);
}

///A drag of something that is not a file at all - a selection, a link, anything from inside the page.
async function textBeingDragged(page) {
    return page.evaluateHandle(() => {
        const carrying = new DataTransfer();

        carrying.setData('text/plain', 'not a layout');

        return carrying;
    });
}

///
///Whether anything is drawing the "drop to open" overlay.
///
///Not pinned to a selector, because which box wears it is the thing the tests below are about: the 2D view
///lights `.viewCanvas` and the other two light the pane, since only the 2D view has a sidebar inside the
///pane to keep out of.
///
function viewIsLit(page) {
    return page.locator('.fileDropOver').count();
}

///
///Dispatches a drag event and answers whether the app canceled it.
///
///**This exists because `page.url()` cannot tell.** Navigating to a dropped file is the default action of a
///real drop, and a synthetic event has no default action to take - so a spec that drops a file on the
///toolbar and then asserts the address is unchanged passes whether the app prevents that default or not.
///Checked by deleting both preventDefault calls: all nine tests here went on passing.
///
///defaultPrevented is the thing itself and it is observable, so it is what is asserted. It has to be read in
///the page, since Playwright's own dispatchEvent hands back nothing.
///
function dispatchDrag(page, selector, type, carrying) {
    return page.evaluate(([selector, type, carrying]) => {
        const raised = new DragEvent(type, { dataTransfer: carrying, bubbles: true, cancelable: true });

        document.querySelector(selector).dispatchEvent(raised);

        return raised.defaultPrevented;
    }, [selector, type, carrying]);
}

test.describe('a layout file dropped on the view', () => {
    ///
    ///The whole feature in one assertion: a drop reaches the offer an upload reaches.
    ///
    ///`#importDialog` is put up by mayOfferImport, which is four screens down the upload path from the input
    ///the drop touches. Nothing else in the app can raise it.
    ///
    test('asks the same question the Open dialog asks', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });
        await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying });

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });

        //Both answers offered, which is what was asked for: replace what is open, or bring the file into it.
        await expect(page.locator('#importAsCell')).toHaveCount(1);
        await expect(page.locator('#importAsFile')).toHaveCount(1);
    });

    ///The answer that puts the dropped file inside the open one.
    test('can be added to the layout already open', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });
        await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying });

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });

        await page.locator('#importAsCell').click();

        await expect(page.locator('#importDialog')).toHaveCount(0);

        //The cell came in and is on the pointer, and the open file is still the open file.
        await expect(page.locator('#carriedCell')).toHaveCount(1, { timeout: 60000 });

        await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
    });

    ///And the answer that replaces it.
    test('can be opened on its own instead', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });
        await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying });

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });

        await page.locator('#importAsFile').click();

        await expect(page.locator('#importDialog')).toHaveCount(0);

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');
    });

    ///
    ///A file dropped on the app's own example, which mayOfferImport stands down for.
    ///
    ///There is nothing to ask about - nothing on screen is the visitor's yet - so the drop falls through to
    ///the plain confirm, exactly as the Open button does from the same state. Which is the point: the two
    ///routes fall through together.
    ///
    test('replaces the app\'s own example after the plainer question', async ({ page }) => {
        await gotoApp(page);

        acceptsClosingWhatIsOpen(page);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });
        await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying });

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toContain('nand2');
    });
});

test.describe('what the view says while a file is held over it', () => {
    test('lights up for a file, and goes dark when the drag leaves the window', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        expect(await viewIsLit(page)).toBe(0);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });

        await expect.poll(async () => viewIsLit(page), { timeout: 30000 }).toBe(1);

        //A dragleave with nothing on the other side of it is the drag going out of the document.
        await page.locator('#gdsSVG').dispatchEvent('dragleave', { relatedTarget: null });

        await expect.poll(async () => viewIsLit(page), { timeout: 30000 }).toBe(0);
    });

    ///
    ///The reason onDragLeave reads relatedTarget rather than simply clearing.
    ///
    ///The 2D view is one element per shape, so a pointer crossing the view fires dragleave continuously. A
    ///handler that took every one of them would put the overlay out while the file was still over the view.
    ///
    test('stays lit while the pointer crosses shapes inside the view', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await fileBeingDragged(page);

        await page.locator('#gdsSVG').dispatchEvent('dragover', { dataTransfer: carrying });

        await expect.poll(async () => viewIsLit(page), { timeout: 30000 }).toBe(1);

        //A handle rather than a locator: relatedTarget is serialized into the page, and a locator is not a
        //thing that can be.
        const stillInside = await page.locator('#viewPane').elementHandle();

        await page.locator('#gdsSVG').dispatchEvent('dragleave', { relatedTarget: stillInside });

        expect(await viewIsLit(page)).toBe(1);
    });

    ///
    ///**The drawing lights up, and the cell tree beside it does not.**
    ///
    ///`#viewPane` is the pane *and* the tree: the 2D view puts the tree in a column inside it, unlike the
    ///layer sidebar on the right which is a column beside it. Lighting the pane therefore outlined a box
    ///that was mostly a list of cell names, and offered a drop over ground the drawing does not own.
    ///
    ///Both halves are asserted. That something is lit does not say it is the right something, and that the
    ///tree is dark does not say the drawing is lit.
    ///
    test('lights the drawing, not the cell tree beside it', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        await expect(page.locator('#cellTree')).toBeVisible({ timeout: 60000 });

        const carrying = await fileBeingDragged(page);

        await dispatchDrag(page, '#gdsSVG', 'dragover', carrying);

        await expect(page.locator('.viewCanvas.fileDropOver')).toHaveCount(1, { timeout: 30000 });
        await expect(page.locator('#viewPane.fileDropOver')).toHaveCount(0);

        //The tree is not inside the box that is lit, which is the whole of the complaint.
        expect(await page.locator('.viewCanvas.fileDropOver #cellTree').count()).toBe(0);
    });

    ///And a file held over the tree is not offered a drop at all.
    test('does not offer a drop over the cell tree', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        await expect(page.locator('#cellTree')).toBeVisible({ timeout: 60000 });

        const carrying = await fileBeingDragged(page);

        //Canceled, so the browser still cannot navigate to it - but nothing is lit and nothing opens.
        expect(await dispatchDrag(page, '#cellTree', 'dragover', carrying)).toBe(true);

        expect(await viewIsLit(page)).toBe(0);

        expect(await dispatchDrag(page, '#cellTree', 'drop', carrying)).toBe(true);

        await expect(page.locator('#importDialog')).toHaveCount(0);
    });

    ///
    ///A drag from inside the page has to go on behaving as it did.
    ///
    ///**Left alone, not merely refused.** Canceling a text drag would be quietly taking over every drag in the
    ///app, so what is asserted is that the events come back uncanceled - the opposite of what the file drags
    ///above assert, and the reason carriesFiles is the first thing both handlers ask.
    ///
    test('ignores a drag that is not carrying files', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await textBeingDragged(page);

        expect(await dispatchDrag(page, '#gdsSVG', 'dragover', carrying)).toBe(false);

        expect(await viewIsLit(page)).toBe(0);

        expect(await dispatchDrag(page, '#gdsSVG', 'drop', carrying)).toBe(false);

        await expect(page.locator('#importDialog')).toHaveCount(0);
    });

    ///
    ///**The near miss, which is the one that would cost the visitor their layout.**
    ///
    ///A file dropped on a page that is not handling it is a navigation: the app closes and whatever was drawn
    ///in it goes with it. So the drop is canceled everywhere rather than only where it is taken - both events,
    ///since it is the dragover that decides whether a drop happens at all and the drop that decides what the
    ///browser does with the file.
    ///
    ///Nothing opens either, which is the other half: canceled is not the same as taken.
    ///
    test('a file dropped off the view is canceled rather than opened', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const before = await shapeCount(page);

        const carrying = await fileBeingDragged(page);

        expect(await dispatchDrag(page, '#examplesButton', 'dragover', carrying)).toBe(true);

        expect(await viewIsLit(page)).toBe(0);

        expect(await dispatchDrag(page, '#examplesButton', 'drop', carrying)).toBe(true);

        await expect(page.locator('#importDialog')).toHaveCount(0);

        expect(await shapeCount(page)).toBe(before);
        await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
    });

    ///And on the view, where it is both canceled and acted on.
    test('a file dropped on the view is canceled too', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg', true);

        const carrying = await fileBeingDragged(page);

        expect(await dispatchDrag(page, '#gdsSVG', 'dragover', carrying)).toBe(true);
        expect(await dispatchDrag(page, '#gdsSVG', 'drop', carrying)).toBe(true);

        await expect(page.locator('#importDialog')).toBeVisible({ timeout: 60000 });
    });
});

///
///A page that has turned Open off has turned this off with it.
///
///A drop is Open by another route, so a read-only embed that took one would be handing back the thing it
///says it does not do. It still swallows the drop rather than letting the browser navigate.
///
test('a read-only page takes no dropped file', async ({ page }) => {
    await gotoApp(page, '?file=Mosfet&view=View2DSvg&mode=readonly');

    await expect(page.locator('#viewPane')).toHaveAttribute('data-file-drop', 'off');

    //
    //**Drawn before anything is counted.**
    //
    //gotoApp waits for the app, not for the layout - gotoExample is the one that waits for both, and it
    //cannot be used here because it has no way to say `mode=`. Reading the count straight after the goto
    //caught it at nought on a loaded machine, and the assertion at the end then read the twenty that had
    //arrived in the meantime and called it a file the drop had opened. Which is the wrong way round: the
    //count was the one that moved, not the drop.
    //
    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(MOSFET_POLYGONS);

    const before = await shapeCount(page);

    const carrying = await fileBeingDragged(page);

    //Canceled all the same. Refusing to open the file is not a reason to let the browser navigate to it.
    expect(await dispatchDrag(page, '#gdsSVG', 'dragover', carrying)).toBe(true);

    expect(await viewIsLit(page)).toBe(0);

    expect(await dispatchDrag(page, '#gdsSVG', 'drop', carrying)).toBe(true);

    await expect(page.locator('#importDialog')).toHaveCount(0);

    expect(await shapeCount(page)).toBe(before);
    await expect.poll(async () => openFile(page), { timeout: 30000 }).toContain('Mosfet');
});
