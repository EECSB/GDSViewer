//What comes back when you return: the file you had open, any edit made to it, the layers you hid, and
//where the controls were left.
//
//Only a browser can show this. The stores are IndexedDB and localStorage, the save runs through .NET
//interop, and the thing that matters most - a tab being closed - is a page lifecycle event. Every check
//here reloads for real rather than calling the storage API, because a session that saves and never
//restores would pass any test that did not.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, layerPairs, layerCheckbox, hideLayer, svgCounts, selectView, selectBackground, selectExample, editorText, saveEditorText, expectEditorLoaded, MOSFET, MOSFET_POLYGONS, SKY130_CELL, fillsDrawn, captureScene, cameraPosition, leaveCell } = require('./helpers');

///
///Waits for the session to name the file given, so a reopen is not racing the save that follows a load.
///
///The save runs after the redraw, where the helpers only wait for the file to be open - so without this a reopen
///can read the session from the file *before* the one just opened.
///
async function expectSessionHolds(page, exampleName) {
    await expect.poll(async () => page.evaluate(async () => {
        const value = await window.gdsStorage.get('gdsviewer.session');

        if (value === null)
            return '';

        //
        //**Decoded the way AppStorage encoded it.** A session under the threshold is stored as plain JSON
        //behind a marker and one over it is deflated and base64'd, and which side of that line a session
        //falls on depends on how many settings the app has - so a helper that read the raw string would
        //stop being able to see anything the day a field was added, having tested nothing in between.
        //
        if (value.startsWith('z')) {
            const bytes = Uint8Array.from(atob(value.slice(1)), letter => letter.charCodeAt(0));

            const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('deflate-raw'));

            return new Response(stream).text();
        }

        return value.slice(1);
    }), { timeout: 60000 }).toContain(exampleName);
}

///
///Reloads onto the bare address, which is where a session is restored rather than a link honored.
///
///Reopening in the same browser context on purpose. Closing a Playwright context throws its storage away
///with it, so a new one is a fresh profile rather than the same browser opened again - which would make
///this test pass only if nothing were ever restored.
///
async function reopen(page) {
    //Nothing said about the sidebars either, since naming anything in the address makes the embedding layer
    //overlay it onto the session and write the result back - which is the session this is trying to read.
    await gotoApp(page, '', null);

    await expectLoaded(page);
}

///
///Waits for the shell to be holding a given file.
///
///Polled rather than read once, because a bare address opens with nothing until the restore replaces it -
///so reading straight after expectLoaded catches the empty state, not the restored file.
///
async function expectOpenFile(page, fileName) {
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(fileName);
}

test('the file you had open comes back', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await reopen(page);

    await expectOpenFile(page, 'Mosfet.gds');
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

///
///The one that would hurt most to lose. An edit lives only in the tab until it is downloaded, so closing
///the browser mid-edit used to throw it away.
///
test('an edit survives the browser being closed', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');

    await expectEditorLoaded(page);

    const text = await editorText(page);
    const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    expect(messages.join(' ')).toContain('Saved');

    //An edited file travels as bytes, so waiting on the name is not enough - wait for the payload.
    await expect.poll(async () => page.evaluate(async () => {
        const value = await window.gdsStorage.get('gdsviewer.session');

        if (value === null)
            return 0;

        return value.length;
    }), { timeout: 60000 }).toBeGreaterThan(1000);

    //A second page in the same context: same origin, same storage, no shared JavaScript - which is what
    //closing a tab and opening a new one actually is.
    const revived = await page.context().newPage();

    await page.close();

    await gotoApp(revived);
    await expectLoaded(revived);
    await selectView(revived, 'ViewText');
    await expectEditorLoaded(revived);

    const restored = await editorText(revived);

    expect(restored).toContain('LAYER: 200');
    expect(restored).not.toContain('LAYER: 65 ');
});

test('a hidden layer stays hidden', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await hideLayer(page);

    const afterHiding = (await svgCounts(page)).polygons;
    expect(afterHiding).toBeLessThan(MOSFET_POLYGONS);

    await reopen(page);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(afterHiding);
    await expect(layerCheckbox(page)).toHaveClass(/layerEyeOff/);
});

///
///And stays hidden the time after that.
///
///Restoring an example refetches it, and the load that does so saves - before the restored state has been
///put back over the defaults that load produced. So the first reopen looked right, because the state was
///applied to the page from the session already in hand, while what was left in the store was the defaults.
///The second reopen is the one that reads it.
///
test('a hidden layer is still hidden the second time the app is reopened', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await hideLayer(page);

    const afterHiding = (await svgCounts(page)).polygons;
    expect(afterHiding).toBeLessThan(MOSFET_POLYGONS);

    await reopen(page);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(afterHiding);

    await reopen(page);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(afterHiding);
    await expect(layerCheckbox(page)).toHaveClass(/layerEyeOff/);
});

///
///The file you chose out of the picker, not the one before it.
///
///Opening an example loads, draws and *saves*, and the picker's own path set the name it saves under
///afterwards - so the session named whichever example had been open before and carried this one's layers.
///Reopening then handed back the wrong file, and every test of this used ?file=, which sets that name up
///front and is the one route that was right.
///
test('the example you chose from the picker is the one that comes back', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await selectExample(page, `${SKY130_CELL}.gds`);

    await expectOpenFile(page, `${SKY130_CELL}.gds`);
    await expectSessionHolds(page, SKY130_CELL);

    await reopen(page);

    await expectOpenFile(page, `${SKY130_CELL}.gds`);
});

test('a layer name loaded from a mapping comes back, with its color', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await page.locator('#layerNamesImport').setInputFiles({
        name: 'sky130.csv',
        mimeType: 'text/csv',
        buffer: Buffer.from('65,20,diff.drawing,#00ff00', 'utf8')
    });

    await expect.poll(async () => {
        return (await fillsDrawn(page)).includes('#00ff00');
    }, { timeout: 60000 }).toBe(true);

    //
    //**The paint is not the save.**
    //
    //A mapping reaches the drawing as soon as the layers are recolored, and the session that would bring it
    //back is written after that - saveSession awaits the write to IndexedDB and only then hands the exit
    //handler a fresh snapshot. Reloading in between finds the session from before the import.
    //
    //This was the suite's one repeatable flake, at two runs in ten. Every other test here already waits on
    //the session itself; this one waited on the picture and reopened into whatever had been written by
    //then.
    //
    await expectSessionHolds(page, 'diff.drawing');

    await reopen(page);

    //Polled, not read once: reopen only waits for a file to be open, and the restore lands after it.
    //
    //Loading a mapping used not to be saved at all, and the color was never stored even when a later
    //rename happened to write the names.
    await expect.poll(async () => {
        const labels = await page.locator('.layerRow .layerName').allTextContents();

        return labels.some(text => text.includes('diff.drawing (65/20)'));
    }, { timeout: 60000 }).toBe(true);

    await expect.poll(async () => {
        return (await fillsDrawn(page)).includes('#00ff00');
    }, { timeout: 60000 }).toBe(true);
});

///
///Restoring straight into the 3D view, which is the one order that used to crash.
///
///The shell's OnAfterRenderAsync runs before the view it just mounted has had its own first render, and
///that first render is what creates the three.js viewer. So the shell applied the session's settings and
///asked for a redraw while there was nothing to draw into: "Cannot read properties of undefined (reading
///'draw')", on startup only, since by the time anything is switched by hand the viewer exists.
///
test('a session restored into the 3D view draws instead of throwing', async ({ page }) => {
    const errors = [];
    page.on('console', message => {
        if (message.type() === 'error')
            errors.push(message.text());
    });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

    //Dress the scene too, since the background is the one setting that lives in the scene rather than in
    //C# - it is applied by an interop call that had nothing to talk to either.
    await selectBackground(page, 'background2.jpg');

    await expectSessionHolds(page, 'background2.jpg');

    //The reopen is the test: no file in the address, so the session is restored, and the 3D view is what
    //it restores into.
    await reopen(page);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

    //It drew, rather than falling over before it could.
    await expect.poll(async () => page.evaluate(() => {
        const canvas = document.querySelector('#container canvas');

        return canvas !== null && canvas.width > 0;
    }), { timeout: 60000 }).toBe(true);

    expect(errors.filter(text => text.includes("reading 'draw'"))).toEqual([]);
    expect(errors.filter(text => text.includes('Unhandled exception'))).toEqual([]);
});

test('the view you were in comes back', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

    await reopen(page);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });
});

test('the opacity slider comes back where it was left', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).opacity).toBe('0.5');

    //Set and dispatched together, because Blazor's @oninput needs a real input event and fill() on a range
    //does not reliably produce one.
    await page.evaluate(() => {
        const slider = document.getElementById('layerOpacity');

        slider.value = '0.2';
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    });

    await expect.poll(async () => (await svgCounts(page)).opacity).toBe('0.2');

    await expectSessionHolds(page, '"o":0.2');

    await reopen(page);

    await expect.poll(async () => (await svgCounts(page)).opacity).toBe('0.2');
});

///
///A link is a deliberate request for something specific. Handing someone their own last file instead would
///make the same link mean different things to different people.
///
test('a link beats the saved session', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await gotoExample(page, 'sky130_fd_sc_hd__nand2_1');

    await expectOpenFile(page, 'sky130_fd_sc_hd__nand2_1.gds');

    await expectSessionHolds(page, 'sky130_fd_sc_hd__nand2_1');

    //And the session followed the link rather than fighting it - including which file the shell says is
    //open, which is read from the session rather than left at whatever was there before.
    await reopen(page);

    await expectOpenFile(page, 'sky130_fd_sc_hd__nand2_1.gds');
});

///
///An unedited example is nine megabytes already sitting on the server, so a session names it instead of
///copying it. Checked through what is stored, since the visible behavior is identical either way.
///
test('an unedited example is stored by name rather than by copying it', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    const stored = await page.evaluate(async () => {
        const value = await window.gdsStorage.get('gdsviewer.session');

        if (value === null)
            return 0;

        return value.length;
    });

    expect(stored).toBeGreaterThan(0);

    //Mosfet.gds is about 2.6 KB, so base64 of it would be well past this even after deflate.
    expect(stored).toBeLessThan(1500);
});

test('storage being unavailable costs the session, not the app', async ({ page }) => {
    //Refuse every write before the app starts, the way a private window or blocked site data would.
    await page.addInitScript(() => {
        window.addEventListener('DOMContentLoaded', () => {
            if (window.gdsStorage)
                window.gdsStorage.set = async () => false;

            if (window.gdsLocalStorage)
                window.gdsLocalStorage.set = () => false;
        });
    });

    await gotoExample(page, MOSFET);

    //The file still opens and draws, which is the whole requirement.
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
    expect(await layerPairs(page)).toContain('65/20');
});

///
///**What survives the tab being closed.**
///
///An async write to IndexedDB cannot be awaited on the way out, so the app keeps a snapshot current and
///writes it synchronously to localStorage when the page goes away. That hung on `pagehide` and
///`beforeunload`; beforeunload was dropped, because pagehide covers every case it did and registering it at
///all put a navigation into the browser's "should I ask about leaving" path - which aborted about one
///reopen in five. This is what says the remaining handler still does the job.
///
///The event is fired rather than the tab closed: Playwright throws a context's storage away with it, so
///there would be nothing left to look at afterwards. What is under test is the handler, not the browser.
///
test('the exit handler writes the session where a closed tab would leave it', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await hideLayer(page, 0);

    //The snapshot is handed over at the end of a save, so there has to have been one.
    await expectSessionHolds(page, MOSFET);

    await page.evaluate(() => window.localStorage.removeItem('gdsviewer.session'));

    await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pagehide')));

    const written = await page.evaluate(() => window.localStorage.getItem('gdsviewer.session'));

    expect(written).not.toBeNull();
    expect(written.length).toBeGreaterThan(0);
});

///
///The page giving its margins to the view is a setting, and comes back with everything else.
///
///It was grouped with the popups as a thing about right now rather than a thing about how you work. The
///popups are panels that happen to be open; this is the page laid out one way rather than another, and
///somebody who wants the whole window for a layout wants it for the next one too.
///
///It is not the browser's full screen. That needs a gesture the browser can refuse and could not be put
///back on a load at all - this is the four rems of padding the page wraps itself in, which nothing stops
///the app from setting for itself.
///
test('a view given the page margins still has them after a reload', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect(page.locator('.viewerPageFull')).toHaveCount(0);

    await page.locator('#fullScreen').click();

    await expect(page.locator('.viewerPageFull')).toHaveCount(1);

    await expectSessionHolds(page, MOSFET);

    await reopen(page);

    await expect(page.locator('.viewerPageFull')).toHaveCount(1);

    //And back again, so it is a setting rather than a door that only opens one way.
    await page.locator('#fullScreen').click();

    await expect(page.locator('.viewerPageFull')).toHaveCount(0);

    await reopen(page);

    await expect(page.locator('.viewerPageFull')).toHaveCount(0);
});

///
///Where you were looking comes back too.
///
///A refresh used to frame the whole layout again, which on a die-sized file means losing a zoom somebody
///spent a while getting to. What is saved is the viewBox itself rather than the box the culling reports -
///that one is grown by half a viewport each way, and restoring it would zoom out by the margin every visit.
///
///Read off the attribute rather than a screenshot, because the claim is about coordinates: the same corner
///of the same layout at the same scale. Polled, since the box lands on the render after the settings do.
///
///**Compared as numbers, not as text.** `svg.viewBox.baseVal` is an SVGRect, whose fields are *single*
///precision - so a box read back out of the browser has about seven significant figures where the double
///that was saved had seventeen. The round trip is exact to a millionth of the view's width, which is a
///thousandth of a pixel, and not exact at all as a string.
///
function boxNumbers(text) {
    return text.trim().split(/\s+/).map(Number);
}

///Each part within a millionth of the view's width, which is far below anything that can be seen.
function expectSameBox(actual, expected) {
    const got = boxNumbers(actual);
    const want = boxNumbers(expected);

    expect(got).toHaveLength(4);

    const slack = Math.abs(want[2]) / 1e6;

    for (let i = 0; i < 4; i++)
        expect(Math.abs(got[i] - want[i])).toBeLessThanOrEqual(slack);
}

test('the pan and zoom you left the view on come back', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    const framed = await page.locator('#gdsSVG').getAttribute('viewBox');

    //Somewhere else, and closer in: a wheel notch anchored under the pointer moves both at once.
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + (view.width * 0.35), view.y + (view.height * 0.4));
    await page.mouse.wheel(0, -400);
    await page.mouse.wheel(0, -400);

    await expect.poll(async () => page.locator('#gdsSVG').getAttribute('viewBox')).not.toBe(framed);

    const moved = await page.locator('#gdsSVG').getAttribute('viewBox');

    //The save is on settle, so the session has to have caught up before the page goes.
    await expectSessionHolds(page, MOSFET);
    await page.waitForTimeout(1800);

    await reopen(page);

    //Polled on the width, which is the part a fit would change most - then compared in full.
    await expect.poll(async () => {
        const now = boxNumbers(await page.locator('#gdsSVG').getAttribute('viewBox'));

        return Math.abs(now[2] - boxNumbers(moved)[2]) < 1;
    }, { timeout: 30000 }).toBe(true);

    expectSameBox(await page.locator('#gdsSVG').getAttribute('viewBox'), moved);

    //And it is not simply the framing coming back under another name.
    expect(moved).not.toBe(framed);
});

///
///And the angle you left the 3D view at.
///
///Six numbers rather than three: where a camera is says nothing about which way it points, and the orbit
///target is what the controls turn around - restoring the position alone leaves you looking at the origin
///from somewhere you never chose to be. Only the position is read back here, because that is what the scene
///exposes and a wrong target moves it: an orbit about the wrong point puts the camera somewhere else.
///
///Compared with slack rather than exactly. Putting the camera back goes through OrbitControls.update, which
///recomputes the position from spherical coordinates - so what comes out is the same place to within the
///error of a couple of trigonometric functions, and not the same bits.
///
test('the angle you left the 3D view at comes back', async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
    await page.waitForTimeout(1500);

    await captureScene(page);

    const opening = await cameraPosition(page);

    expect(opening).not.toBeNull();

    //Orbit: a left drag across the canvas is what turns it.
    const canvas = await page.locator('#container canvas').boundingBox();

    await page.mouse.move(canvas.x + (canvas.width / 2), canvas.y + (canvas.height / 2));
    await page.mouse.down();
    await page.mouse.move(canvas.x + (canvas.width * 0.75), canvas.y + (canvas.height * 0.35), { steps: 12 });
    await page.mouse.up();

    await captureScene(page);

    const turned = await cameraPosition(page);

    expect(distance(turned, opening)).toBeGreaterThan(1);

    //The save is on settle, so the session has to catch up before the page goes.
    await expectSessionHolds(page, MOSFET);
    await page.waitForTimeout(1800);

    await reopen(page);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(1500);

    await captureScene(page);

    const back = await cameraPosition(page);

    //Within a thousandth of how far away it is, which is far below anything that can be seen.
    expect(distance(back, turned)).toBeLessThan(distance(turned, { x: 0, y: 0, z: 0 }) / 1000);

    //And not simply the opening angle under another name.
    expect(distance(back, opening)).toBeGreaterThan(1);
});

///How far apart two points are, for comparing camera positions without asking for the same bits.
function distance(one, other) {
    return Math.hypot(one.x - other.x, one.y - other.y, one.z - other.z);
}

///
///And the cell you were editing.
///
///Saved by name rather than by the path it was reached through. Coming back re-enters it the way opening it
///from the library does - through a shape in it, which rebuilds the whole breadcrumb - so what comes back is
///the crumb and not merely the name at the end of it.
///
///Refused when the file no longer has a cell by that name, which is what a session outliving a rename looks
///like; without that guard the view would open a context for a cell that is not there.
///
test('the cell you were editing is still open when you come back', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => svgCounts(page).then(counts => counts.polygons), { timeout: 60000 })
        .toBeGreaterThan(0);

    //Out of the cell the file opened in, so what comes back is what this test put there and not the
    //default - the two are the same cell on this file, which would make the assertion prove nothing.
    await leaveCell(page);

    //Into a cell the ordinary way, by clicking a shape in it.
    await page.locator('#selectTool').click();

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(view.x + (view.width / 2), view.y + (view.height / 2));

    await expect(page.locator('.contextCrumbOn')).toBeVisible({ timeout: 15000 });

    const editing = (await page.locator('.contextCrumbOn').textContent()).trim();

    expect(editing.length).toBeGreaterThan(0);

    await expectSessionHolds(page, MOSFET);

    await reopen(page);

    //Polled, because the cell is entered on the render after the settings land.
    await expect.poll(async () => {
        const crumb = page.locator('.contextCrumbOn');

        if (await crumb.count() === 0)
            return '';

        return (await crumb.textContent()).trim();
    }, { timeout: 30000 }).toBe(editing);

    //And the breadcrumb is a breadcrumb, not just the name: the way back out is on it.
    await expect(page.locator('.contextCrumb', { hasText: 'All' })).toBeVisible();
});

///
///And the tool that was in hand.
///
///Left out once on the grounds that a tool is what you are doing now rather than how you left the file -
///which is a reasonable line drawn in the wrong place: opening a layout to carry on moving things means
///reaching for Move first, every time.
///
///Move rather than Select, because the two are the same picking with and without corner handles, and only
///one of them can be told from the other by which button is lit.
///
test('the tool you had in hand comes back', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => svgCounts(page).then(counts => counts.polygons), { timeout: 60000 })
        .toBeGreaterThan(0);

    //Pan is where a view opens, and it is the one that is lit by nothing else being.
    await expect(page.locator('#panTool')).toHaveClass(/toolButtonOn/);

    await page.locator('#moveTool').click();

    await expect(page.locator('#moveTool')).toHaveClass(/toolButtonOn/);

    await expectSessionHolds(page, MOSFET);

    await reopen(page);

    await expect(page.locator('#moveTool')).toHaveClass(/toolButtonOn/, { timeout: 30000 });
    await expect(page.locator('#panTool')).not.toHaveClass(/toolButtonOn/);
});

///
///And Draw comes back with the cell it needs, which is the order these two have to land in.
///
///Draw refuses to be picked up outside a cell - it checks for itself and does nothing - so a restore that
///took the tool up before the context was back would leave the view in Pan with a session saying Draw, and
///the next save would agree with the screen and lose it for good.
///
test('Draw comes back, which it can only do inside the cell it was left in', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => svgCounts(page).then(counts => counts.polygons), { timeout: 60000 })
        .toBeGreaterThan(0);

    //Into a cell first, since Draw is disabled outside one.
    await page.locator('#selectTool').click();

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(view.x + (view.width / 2), view.y + (view.height / 2));

    await expect(page.locator('.contextCrumbOn')).toBeVisible({ timeout: 15000 });

    await page.locator('#drawTool').click();

    await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/);

    await expectSessionHolds(page, MOSFET);

    await reopen(page);

    //The cell first, then the tool that needed it.
    await expect(page.locator('.contextCrumbOn')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/, { timeout: 30000 });
});

///
///The cell's actions panel is the one panel worth keeping open.
///
///Every other one in the app goes when the pointer leaves it - Examples, History, the library, the grid,
///the shapes, the backdrops - so a restored one would vanish on the first movement of the mouse. The layer
///settings popup is fixed at the point it was opened from, and putting it back a session later means
///putting it at a coordinate that no longer means anything. This one is a disclosure with no position and
///no timer.
///
///The rename box has to come back filled with it. The toggle is what fills it, and the toggle is not what
///runs on a restore - so without that the panel comes back open and unable to do anything, which is worse
///than coming back shut.
///
test('the cell actions panel is still open, and still usable, after a reload', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => svgCounts(page).then(counts => counts.polygons), { timeout: 60000 })
        .toBeGreaterThan(0);

    //It only exists on the context bar, so a cell has to be open first.
    await page.locator('#selectTool').click();

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(view.x + (view.width / 2), view.y + (view.height / 2));

    await expect(page.locator('#cellActions')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('#renameCell')).toHaveCount(0);

    await page.locator('#cellActions').click();

    await expect(page.locator('#renameCell')).toBeVisible();

    const named = await page.locator('#renameTo').inputValue();

    expect(named.length).toBeGreaterThan(0);

    await expectSessionHolds(page, MOSFET);

    await reopen(page);

    await expect(page.locator('#renameCell')).toBeVisible({ timeout: 30000 });

    //
    //Filled the way the toggle fills it: with what the cell is called now, so renaming is a change to a
    //name rather than typing one out.
    //
    //Rename stays disabled until that name is actually changed, which is true of a panel just opened by
    //hand as well - renaming a cell to what it is already called is not a rename. So the check that this
    //came back *usable* is that typing into it turns the button on.
    //
    await expect(page.locator('#renameTo')).toHaveValue(named);
    await expect(page.locator('#renameCell')).toBeDisabled();

    await page.locator('#renameTo').fill(named + '_2');

    await expect(page.locator('#renameCell')).toBeEnabled();
});
