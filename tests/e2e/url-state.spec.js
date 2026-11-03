//The address is a way in: ?view= and ?file= open the app where a link was taken from. Only ?view= is
//written back, when a view is chosen. ?file= leaves the address once its file is open and nothing writes it
//in again, so a reload brings back the session - the file as it was left - rather than a fresh copy of it.
//Share is what makes a link to the file now, and the 3D view's QR code. Both directions are asserted, since
//a parameter that is read correctly can still be written back where it should not be.
const { test, expect } = require('@playwright/test');
const {
    gotoApp,
    gotoExample,
    openExamples,
    openFile,
    selectView,
    selectExample,
    svgCounts,
    hideLayer,
    layerCheckbox,
    MOSFET,
    MOSFET_POLYGONS,
    SKY130_CELL
} = require('./helpers');

///What the address says about a parameter, or null when it says nothing.
function named(page, parameter) {
    return new URL(page.url()).searchParams.get(parameter);
}

///
///A bare address opens the bundled example, and stays bare.
///
///Neither parameter is written in. A bare link already lands in the 2D view, so there is no view to record,
///and the address does not name the open file at all - a reload goes to the session, which knows it.
///
test('a bare address opens the bundled example and does not name it', async ({ page }) => {
    await gotoApp(page);

    await expect(page.locator('#gdsSVG')).toBeVisible();
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    expect(named(page, 'file')).toBeNull();
    expect(named(page, 'view')).toBeNull();
});

test('?view= opens that view', async ({ page }) => {
    await gotoApp(page, '?view=3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    await gotoApp(page, '?view=text');
    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 30000 });
});

///
///?view= beats the view a session remembers, the way ?file= beats the file it remembers.
///
///This could not be seen until a bare start opened a file: with nothing loaded there was no session to
///save, so a second visit carrying ?view= had nothing to lose to. Once the first visit left a session
///behind, the remembered view won and the link was ignored - which would make a shared ?view= link show
///different people different things.
///
test('?view= beats the view the session remembers', async ({ page }) => {
    //Leave a session behind that remembers the 3D view.
    await gotoApp(page, '?view=3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    //Then ask for a different one, with no ?file= - which is the path that consults the session.
    await gotoApp(page, '?view=2d');

    //Waited out before the view is judged, and this is the whole difficulty of the test: the address is
    //applied on the first render and the session lands several renders later, so reading straight after
    //the page settles sees 2D either way and passes whether the bug is there or not. The file arriving is
    //what says the restore has run - only then is the view it left behind the view on screen.
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    await expect(page.locator('#gdsSVG')).toBeVisible();
    await expect(page.locator('#container canvas')).toHaveCount(0);
});

test('an unrecognized view falls back to 2D rather than failing', async ({ page }) => {
    await gotoApp(page, '?view=wibble');

    await expect(page.locator('#gdsSVG')).toBeVisible();
});

test('?file= opens that example, and ?view= alongside it is honored too', async ({ page }) => {
    await gotoExample(page, SKY130_CELL, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    //The shell names what it opened, and the PDK links - which only appear for a sky130 cell - agree.
    //They live in the Examples popup now rather than across the top, so it has to be opened to see them.
    expect(await openFile(page)).toBe(`${SKY130_CELL}.gds`);

    await openExamples(page);

    await expect(page.getByRole('link', { name: /PDK Docs/ })).toBeVisible();
});

///
///**And then ?file= leaves the address**, once its file is open. The view stays: an address without one
///would put the 3D view back to 2D on the next navigation.
///
test('?file= leaves the address once its file is open, and ?view= stays', async ({ page }) => {
    await gotoExample(page, SKY130_CELL, '3d');

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${SKY130_CELL}.gds`);
    await expect.poll(() => named(page, 'file'), { timeout: 20000 }).toBeNull();

    expect(named(page, 'view')).toBe('3d');
    await expect(page.locator('#container canvas')).toBeVisible();
});

///
///**Which is what makes a reload come back to the file as it was left.** With ?file= still there, a reload
///was a link followed again: the file opened fresh and the session was not asked, so a layer hidden since
///was showing again.
///
test('a reload after following a link brings back the session, not a fresh copy of the file', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
    await expect.poll(() => named(page, 'file'), { timeout: 20000 }).toBeNull();

    await hideLayer(page);

    const afterHiding = (await svgCounts(page)).polygons;

    expect(afterHiding).toBeLessThan(MOSFET_POLYGONS);

    await page.reload();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);
    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 30000 }).toBe(afterHiding);
    await expect(layerCheckbox(page)).toHaveClass(/layerEyeOff/);
});

test('a ?file= that names nothing is reported and leaves the app usable', async ({ page }) => {
    const messages = [];
    page.on('dialog', dialog => { messages.push(dialog.message()); dialog.dismiss(); });

    await gotoApp(page, '?file=nand2_1');

    //Names the shape of a real one, because these names are long and a link is usually wrong by a
    //character or two.
    await expect.poll(() => messages.join(' ')).toContain('no bundled example');
    await expect.poll(() => messages.join(' ')).toContain('sky130_');

    //And it stays in the address, so it can be put right where it was typed.
    expect(named(page, 'file')).toBe('nand2_1');

    //Still usable afterwards: choosing a real file works. Polled, because the draw follows the load rather
    //than coming with it - reading once races whatever the open path happens to do first.
    await selectExample(page, `${MOSFET}.gds`);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBeGreaterThan(0);

    //Until something else is open, when a reload has to come back to that rather than to the broken link.
    await expect.poll(() => named(page, 'file'), { timeout: 20000 }).toBeNull();
});

test('choosing a view writes it into the address, and nothing about the file', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await selectView(page, 'View3D');

    await expect.poll(() => named(page, 'view')).toBe('3d');

    //A link opened it, and that link's file= left once it was open.
    expect(named(page, 'file')).toBeNull();
});

test('choosing an example leaves the address alone, the view included', async ({ page }) => {
    await gotoApp(page, '?view=3d');

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    await selectExample(page, `${SKY130_CELL}.gds`);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${SKY130_CELL}.gds`);

    expect(named(page, 'file')).toBeNull();
    expect(named(page, 'view')).toBe('3d');
});

///
///**Inside somebody else's page the address is theirs**, and is left exactly as they wrote it.
///
///The browser can bring a frame back at the address it has now rather than the one the page wrote - going
///Back to the page does - and every viewer on a page shares one session. A frame that had lost its file=
///would open whichever file was saved last, so two embeds of two files would come back as the same one.
///
test('inside an iframe, the address is left as the page around it wrote it', async ({ page, baseURL }) => {
    const embedded = `${baseURL}/?file=${MOSFET}&view=2d&tree=false&box=500,600,4000,4000`;

    //A page on the app's own origin, the way an embedder's page is on theirs: written here rather than served.
    await page.route(`${baseURL}/embedding-page.html`, route => route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html><iframe src="${embedded.replaceAll('&', '&amp;')}" width="1200" height="800"></iframe>`
    }));

    await page.goto('/embedding-page.html');

    const frame = page.frameLocator('iframe');

    await expect.poll(async () => frame.locator('#mainAppContainer').getAttribute('data-file'), { timeout: 60000 })
        .toBe(`${MOSFET}.gds`);

    //The point at which the address would have been tidied, were it the app's: the link's framing has landed.
    await expect.poll(async () => {
        const box = await frame.locator('#gdsSVG').getAttribute('viewBox');

        return (box ?? '').trim().split(/\s+/).map(Number);
    }, { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);

    await page.waitForTimeout(1000);

    expect(page.mainFrame().childFrames()[0].url()).toBe(embedded);
});
