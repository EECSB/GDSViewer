//The 3D view's own settings, in the menu that hangs from the cube of the view switch while that view is on: how far it
//draws, and whether the layers show their textures. Kept in this browser by the interop - see VIEW_OPTIONS in
//ThreeInterop.js - and shared with the chip game's panel, whose side of that is in chip-game.spec.js. The planes and the
//haze are only a browser's to say. And Admire and the backdrop, which are set here and nowhere else.
const { test, expect } = require('@playwright/test');
const { gotoExample, captureScene, cameraPosition, openViewSettings, MOSFET } = require('./helpers');

///Opens the scene's settings menu, and finds the scene the way the specs find it, until it has.
async function openView(page) {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => {
        await captureScene(page);

        return page.evaluate(() => window.__gdsScene != null);
    }).toBe(true);
}

///How far the camera draws, and the haze the far side fades into - or null where there is none.
async function reach(page) {
    return page.evaluate(() => {
        const scene = window.__gdsScene;
        const camera = scene.children.find(child => child.isCamera);

        let haze = null;

        if (scene.fog != null)
            haze = { from: scene.fog.near, to: scene.fog.far };

        return { far: camera.far, haze: haze };
    });
}

///How many of the layers' slabs there are, and how many are drawn with their surfaces on - see surfaceOf.
async function surfaced(page) {
    return page.evaluate(() => {
        const slabs = [];

        window.__gdsScene.traverse(object => {
            if (object.isMesh && object.userData.words != null)
                slabs.push(object);
        });

        return { slabs: slabs.length, on: slabs.filter(slab => slab.material.userData.surfaced === true).length };
    });
}

///
///The draw distance: All to begin with, which draws everything with no haze; less brings the far plane in, a share of
///the chip's width past its near side, with the far side faded - and this browser keeps it for the next visit.
///
test('the draw distance takes the far plane in with the far side faded, All draws everything, and it is kept', async ({ page }) => {
    await openView(page);
    await openViewSettings(page);

    await expect(page.locator('#viewDrawDistance')).toHaveValue('1');
    await expect(page.locator('#viewDrawDistanceSaid')).toHaveText('All');

    const all = await reach(page);

    expect(all.haze).toBeNull();

    await page.locator('#viewDrawDistance').fill('0.25');

    await expect(page.locator('#viewDrawDistanceSaid')).toHaveText('25%');
    await expect.poll(async () => (await reach(page)).haze).not.toBeNull();

    const short = await reach(page);

    expect(short.far).toBeLessThan(all.far);
    expect(short.haze.to).toBeCloseTo(short.far, 3);
    expect(short.haze.from).toBeLessThan(short.haze.to);

    //The next visit.
    await openView(page);
    await openViewSettings(page);

    await expect(page.locator('#viewDrawDistance')).toHaveValue('0.25');
    await expect.poll(async () => (await reach(page)).haze).not.toBeNull();

    await page.locator('#viewDrawDistance').fill('1');

    await expect(page.locator('#viewDrawDistanceSaid')).toHaveText('All');
    await expect.poll(async () => (await reach(page)).haze).toBeNull();
});

///
///Show textures: off to begin with, the flat colors the view always drew; ticked, every layer is drawn with its surface,
///and this browser keeps that for the next visit.
///
test('Show textures puts the layers\' surfaces on in the 3D view, and it is kept', async ({ page }) => {
    const problems = [];

    page.on('console', message => {
        if (message.type() === 'error' && /shader|WebGLProgram/i.test(message.text()))
            problems.push(message.text());
    });

    await openView(page);

    const plain = await surfaced(page);

    expect(plain.slabs).toBeGreaterThan(0);
    expect(plain.on).toBe(0);

    await openViewSettings(page);

    const textures = page.locator('#viewTextures');

    await expect(textures).not.toBeChecked();

    await textures.check();

    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);

    //The next visit.
    await openView(page);

    await expect.poll(async () => (await surfaced(page)).on).toBe(plain.slabs);

    await openViewSettings(page);

    await expect(textures).toBeChecked();

    await textures.uncheck();

    await expect.poll(async () => (await surfaced(page)).on).toBe(0);

    expect(problems).toEqual([]);
});

///
///**The backdrop is in the menu too**, and only there: a row each - a picture, the names kept, the one up ticked. And the
///menu stays open as one is chosen, where a menu that asks one question goes: the backdrop is one of several things set
///here, and the next to try is a press away.
///
test('the menu chooses the backdrop too, a picture each, and stays open', async ({ page }) => {
    await openView(page);
    await openViewSettings(page);

    const backdrops = page.locator('#viewBackground');

    await expect(backdrops.locator('.backgroundPick')).toHaveCount(5);
    await expect(backdrops.locator('img.backgroundSwatch')).toHaveCount(4);

    const names = await backdrops.locator('.backgroundName').allTextContents();

    expect(names.map(one => one.trim())).toEqual(['No Background', 'White', 'White Room', 'Future Room', 'Neon']);

    await expect(backdrops.locator('.shapePickOn')).toHaveAttribute('data-background', 'none');

    expect(await page.evaluate(() => window.__gdsScene.background)).toBeNull();

    await backdrops.locator('[data-background="background1.jpg"]').click();

    await expect.poll(async () => page.evaluate(() => window.__gdsScene.background != null), { timeout: 30000 })
        .toBe(true);
    await expect(page.locator('#viewSettingsMenu')).toBeVisible();
    await expect(backdrops.locator('.shapePickOn')).toHaveAttribute('data-background', 'background1.jpg');

    //The next a press away, with the menu still up.
    await backdrops.locator('[data-background="none"]').click();

    await expect.poll(async () => page.evaluate(() => window.__gdsScene.background == null)).toBe(true);
    await expect(backdrops.locator('.shapePickOn')).toHaveAttribute('data-background', 'none');
});

///
///**Admire is a switch in the menu**, and nowhere else since 2026-10-17, when its button left the view's corner: the
///switch sets the camera going round, and what it was left at is what it says when the menu next opens.
///
test('Admire is a switch in the menu, and nowhere else', async ({ page }) => {
    await openView(page);

    await expect(page.locator('#admire')).toHaveCount(0);

    await openViewSettings(page);

    const admire = page.locator('#viewAdmire');

    await expect(admire).not.toBeChecked();

    const before = await cameraPosition(page);

    await admire.check();

    //Driven from the render loop, so it takes frames rather than a call to move.
    await expect.poll(async () => {
        const now = await cameraPosition(page);

        return now.x !== before.x || now.y !== before.y || now.z !== before.z;
    }, { timeout: 15000 }).toBe(true);

    //Put away and opened again, it says the camera is still going round.
    await page.mouse.move(4, 4);

    await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);

    await openViewSettings(page);

    await expect(admire).toBeChecked();

    await admire.uncheck();

    await expect(admire).not.toBeChecked();
});

///
///**The menu hangs from the cube of the view switch**, the one control in the bar that already means the 3D view: once
///that view is on, pointed at or pressed, it opens the settings under it, from its left edge, and the pointer leaving
///puts them away. Its two buttons along the bar - one for this menu, one for the backdrop - are gone.
///
///Elsewhere the cube is the way to the 3D view and nothing else: pointed at it opens nothing, the press that switches
///opens nothing, and the next press, the view being on, opens them.
///
test('the settings hang from the cube while the 3D view is on, and the cube only switches elsewhere', async ({ page }) => {
    await openView(page);

    const cube = page.locator('#viewPick3D');
    const menu = page.locator('#viewSettingsMenu');

    await expect(cube).toHaveAttribute('data-view', 'View3D');
    await expect(cube).toHaveAttribute('aria-expanded', 'false');
    await expect(cube).toHaveClass(/viewPickOpens/);

    await expect(page.locator('#viewSettingsButton')).toHaveCount(0);
    await expect(page.locator('#backgroundPicker')).toHaveCount(0);

    await cube.hover();

    await expect(menu).toBeVisible();
    await expect(cube).toHaveAttribute('aria-expanded', 'true');

    const at = await cube.boundingBox();
    const under = await menu.boundingBox();

    expect(Math.round(under.x)).toBe(Math.round(at.x));
    expect(under.y).toBeGreaterThanOrEqual(at.y + at.height - 1);

    //
    //The cube stays the round end of the switch with the menu open, though the menu makes it a first child. And the
    //corner says it opens something.
    //
    const looks = await cube.evaluate(box => ({
        corners: getComputedStyle(box).borderRadius,
        mark: getComputedStyle(box, '::after').content
    }));

    expect(looks.corners).toMatch(/^0px 3px 3px 0px/);
    expect(looks.mark).not.toBe('none');

    //The walk down into it keeps it, and the pointer leaving puts it away. Stepped, or the move is one event at the end.
    await page.mouse.move(at.x + (at.width / 2), under.y + 30, { steps: 20 });

    await expect(menu).toBeVisible();

    await page.mouse.move(4, 4);

    await expect(menu).toHaveCount(0);
    await expect(cube).toHaveAttribute('aria-expanded', 'false');

    //In the 2D view the cube is plain, and pointed at, then pressed, it switches to 3D and opens nothing.
    await gotoExample(page, MOSFET, '2d');

    await expect(cube).not.toHaveAttribute('aria-expanded', /.*/);
    await expect(cube).not.toHaveClass(/viewPickOpens/);

    await cube.hover();
    await cube.click();

    await expect(cube).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#container canvas')).toBeVisible();
    await expect(cube).toHaveAttribute('aria-expanded', 'false');
    await expect(menu).toHaveCount(0);

    //The next press, the 3D view being on, opens them.
    await cube.click();

    await expect(menu).toBeVisible();
});
