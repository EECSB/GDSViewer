//The address describes what is on screen: ?view= and ?file= open the app where a link was taken from,
//and choosing either through the UI writes it back. This is the layer that makes a link shareable, so
//it is worth asserting both directions rather than only that the parameters are read.
const { test, expect } = require('@playwright/test');
const {
    gotoApp,
    gotoExample,
    openExamples,
    openFile,
    selectView,
    selectExample,
    svgCounts,
    MOSFET,
    SKY130_CELL
} = require('./helpers');

///
///A bare address opens the bundled example, and then says so.
///
///?view= is still not written in - a bare link already lands in the 2D view, so there is nothing to
///record. ?file= is, because something was chosen: the address describes what is on screen, and arriving
///bare now ends in the same state as arriving on ?file=Mosfet.
///
test('a bare address opens the bundled example and names it', async ({ page }) => {
    await gotoApp(page);

    await expect(page.locator('#gdsSVG')).toBeVisible();
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    const query = new URL(page.url()).searchParams;

    expect(query.get('file')).toBe(MOSFET);
    expect(query.get('view')).toBeNull();
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

test('a ?file= that names nothing is reported and leaves the app usable', async ({ page }) => {
    const messages = [];
    page.on('dialog', dialog => { messages.push(dialog.message()); dialog.dismiss(); });

    await gotoApp(page, '?file=nand2_1');

    //Names the shape of a real one, because these names are long and a link is usually wrong by a
    //character or two.
    await expect.poll(() => messages.join(' ')).toContain('no bundled example');
    await expect.poll(() => messages.join(' ')).toContain('sky130_');

    //Still usable afterwards: choosing a real file works. Polled, because the draw follows the load rather
    //than coming with it - reading once races whatever the open path happens to do first.
    await selectExample(page, `${MOSFET}.gds`);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBeGreaterThan(0);
});

test('choosing a view writes it into the address', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await selectView(page, 'View3D');

    await expect.poll(() => new URL(page.url()).searchParams.get('view')).toBe('3d');
    //And the file it was already showing is still named there.
    expect(new URL(page.url()).searchParams.get('file')).toBe(MOSFET);
});

test('choosing an example writes it into the address without disturbing the view', async ({ page }) => {
    await gotoApp(page, '?view=3d');

    await selectExample(page, `${MOSFET}.gds`);

    await expect.poll(() => new URL(page.url()).searchParams.get('file')).toBe(MOSFET);
    expect(new URL(page.url()).searchParams.get('view')).toBe('3d');
});
