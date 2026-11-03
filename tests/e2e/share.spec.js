//The share popup: a link to send, or the code that embeds the viewer in a page.
//
//Both are an address underneath, built by ShareLink from the popup's choices - which parameters each choice
//writes is pinned by ShareLinkTests in C#. What needs a browser is the rest: that the popup starts on what is
//open, that its switches change what comes out, that a link it makes opens what it says, and that Copy puts
//the text where a paste will find it.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, uploadFile, openedOnItsOwn, MOSFET } = require('./helpers');

///The popup, opened from its button in the bar.
async function openShare(page) {
    await page.locator('#shareButton').click();

    await expect(page.locator('#sharePopup')).toBeVisible();
}

///What the popup is offering to copy.
async function shared(page) {
    return page.locator('#shareCode').inputValue();
}

test('the link starts on the file and the view that are open', async ({ page, baseURL }) => {
    await gotoExample(page, MOSFET, '2d');
    await openShare(page);

    await expect(page.locator('#shareFile')).toBeChecked();
    expect(await shared(page)).toBe(`${baseURL}/?file=Mosfet&view=2d`);

    await page.locator('#shareView3D').click();

    expect(await shared(page)).toBe(`${baseURL}/?file=Mosfet&view=3d`);
});

///And a link it makes opens what it says, which is the whole point of having one.
test('a link it makes opens that file in that view, with that much of the app', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openShare(page);

    await page.locator('#shareView3D').click();
    await page.locator('#shareMode').selectOption('NoEdit');

    const link = await shared(page);

    expect(link).toContain('mode=noedit');

    await page.goto(link);
    await expectLoaded(page);

    expect(await openFile(page)).toBe(`${MOSFET}.gds`);
    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });
    await expect(page.locator('#newLayout')).toBeDisabled();
});

///Where the view is looking goes in the link while it is ticked, shortened to two places, and not once it is not.
test('the framing on screen goes in the link, or is left out', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&view=2d&box=-123.456789,-200,2400.5,1800.25`);
    await expectLoaded(page);

    //On the view first: Share asks the view where it is looking, and the address lets go of the box once it lands.
    await expect.poll(async () => page.locator('#gdsSVG').getAttribute('viewBox'), { timeout: 20000 })
        .toBe('-123.456789 -200 2400.5 1800.25');

    await openShare(page);

    expect(await shared(page)).toContain('box=-123.46,-200,2400.5,1800.25');

    await page.locator('#shareFraming').uncheck();

    expect(await shared(page)).not.toContain('box=');
});

///
///An embed is an iframe around a link, and starts as the drawing on its own - its switches add the rest.
///
///Only the switches that mean something in the view chosen are offered: the cell tree and the grid in 2D,
///the backdrop and VR in 3D.
///
test('an embed is an iframe set up by its switches', async ({ page, baseURL }) => {
    await gotoExample(page, MOSFET, '2d');
    await openShare(page);

    await page.locator('#shareEmbedTab').click();

    expect(await shared(page)).toBe(`<iframe src="${baseURL}/?file=Mosfet&amp;view=2d&amp;mode=viewer&amp;tree=false" width="100%" height="600" style="border: 0;" title="GDS Viewer"></iframe>`);

    await page.locator('#shareBanner').check();
    await page.locator('#shareTree').check();
    await page.locator('#shareWidth').fill('800');

    const frame = await shared(page);

    expect(frame).toContain('banner=true');
    expect(frame).not.toContain('tree=false');
    expect(frame).toContain('width="800"');

    //The 3D view's own switches, and none of the 2D view's - but the cell tree, which it has too (the 2026-10-21 audit).
    await page.locator('#shareView3D').click();

    await expect(page.locator('#shareGrid')).toHaveCount(0);
    await expect(page.locator('#shareBackground')).toBeVisible();

    await page.locator('#shareTree').uncheck();

    expect(await shared(page)).toContain('tree=false');

    await page.locator('#shareBackground').selectOption('background2.jpg');
    await page.locator('#shareXr').check();

    expect(await shared(page)).toContain('background=background2.jpg');
    expect(await shared(page)).toContain('allow="xr-spatial-tracking"');
});

test('Copy puts it on the clipboard, and says so', async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);

    await gotoExample(page, MOSFET, '2d');
    await openShare(page);

    await page.locator('#shareCopy').click();

    await expect(page.locator('#shareCopy')).toHaveText('Copied');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await shared(page));
});

///A file from this computer is not in the link - a link carries settings, never a file - and the popup says why.
test('a file opened from this computer is left out of the link, and the popup says why', async ({ page }) => {
    await gotoApp(page);
    await uploadFile(page, 'e2e/fixtures/placed.gds');
    await openedOnItsOwn(page);
    await expectLoaded(page);

    await openShare(page);

    await expect(page.locator('#shareFile')).toHaveCount(0);
    await expect(page.locator('#shareNoFile')).toBeVisible();
    expect(await shared(page)).not.toContain('file=');
});

test('Escape, the cross and a press beside it each put it away', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    await openShare(page);
    await page.keyboard.press('Escape');
    await expect(page.locator('#sharePopup')).toHaveCount(0);

    await openShare(page);
    await page.locator('#closeShare').click();
    await expect(page.locator('#sharePopup')).toHaveCount(0);

    await openShare(page);
    await page.locator('#shareBackdrop').click({ position: { x: 5, y: 5 } });
    await expect(page.locator('#sharePopup')).toHaveCount(0);
});
