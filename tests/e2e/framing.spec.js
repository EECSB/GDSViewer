//
//Where the view is looking, read out of a link, and the button that puts it back in the middle.
//
//The session already restored both of these - see session.spec.js, which covers coming back to your own
//framing. What is here is the other half: carrying it to somebody else, which a session cannot do because
//a session is local by design.
//
//**A link's framing is read on the way in and never written back.** The app used to write box= and camera=
//into the address a second after every pan or orbit, so the address bar was always a link to where you were
//looking. Share makes that link now, from the view, and the address is left as it was opened.
//
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, selectView, captureScene, cameraPosition, viewSettled, MOSFET } = require('./helpers');

///The viewBox as the page actually has it, four numbers.
async function framing(page) {
    return page.evaluate(() => {
        const svg = document.getElementById('gdsSVG');

        if (svg == null)
            return null;

        return svg.getAttribute('viewBox').trim().split(/\s+/).map(Number);
    });
}

///A drag across the drawing, which is a pan with the Pan tool in hand - and that is what the view opens in.
async function panAcross(page) {
    const box = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(box.x + (box.width * 0.6), box.y + (box.height * 0.6));
    await page.mouse.down();
    await page.mouse.move(box.x + (box.width * 0.3), box.y + (box.height * 0.35), { steps: 12 });
    await page.mouse.up();
}

///What ?box= currently says, or null when the address does not say.
function boxNamed(page) {
    return new URL(page.url()).searchParams.get('box');
}

///What ?camera= currently says, or null when the address does not say.
function cameraNamed(page) {
    return new URL(page.url()).searchParams.get('camera');
}

///The link the share popup offers for what is on screen, with the popup put away again afterwards.
async function sharedLink(page) {
    await page.locator('#shareButton').click();

    await expect(page.locator('#sharePopup')).toBeVisible();

    const link = await page.locator('#shareCode').inputValue();

    await page.locator('#closeShare').click();

    await expect(page.locator('#sharePopup')).toHaveCount(0);

    return link;
}

///What a link's box= says, as four numbers, or null when it says nothing.
function boxIn(link) {
    const box = new URL(link).searchParams.get('box');

    if (box === null)
        return null;

    return box.split(',').map(Number);
}

///
///A file nobody has moved carries no framing in the address, which is the state the fit leaves.
///
///Worth pinning rather than assuming: fitToDrawing deliberately reports no settle, because the frame a file
///opens on is one a reopen works out for itself.
///
test('a file nobody has moved carries no framing in the address', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    await page.waitForTimeout(1500);

    expect(boxNamed(page)).toBeNull();
});

///
///Moving the view is no reason to touch the address: it says what was opened, and Share is what makes a link
///to where you are. Waited past the settle, a second after the gesture stops, which is when it used to be written.
///
test('panning leaves the address alone', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    //The open's own change first: the file leaves the address once the open has saved, which can be a moment
    //after it is on screen, and that is not the change this is about.
    await expect.poll(() => new URL(page.url()).searchParams.get('file'), { timeout: 20000 }).toBeNull();

    const before = page.url();

    await panAcross(page);

    await page.waitForTimeout(2500);

    expect(page.url()).toBe(before);
    expect(boxNamed(page)).toBeNull();
});

///Where the framing goes instead: into the link Share makes, as what is on screen and not something near it.
test('the link Share makes carries the framing on screen', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    const opening = await framing(page);

    await panAcross(page);

    //The view says where it came to rest a second after the gesture stops, and Share asks it for that.
    await expect.poll(async () => boxIn(await sharedLink(page)), { timeout: 20000 }).not.toBeNull();

    const written = boxIn(await sharedLink(page));
    const shown = await framing(page);

    expect(written).toHaveLength(4);

    //To the two places Share writes.
    for (let i = 0; i < 4; i++)
        expect(written[i]).toBeCloseTo(shown[i], 1);

    //And it moved, rather than the opening frame being written down.
    expect(Math.abs(written[0] - opening[0])).toBeGreaterThan(1);
});

///Which is the whole point of the parameter: this is the link somebody else would be handed.
test('a framing in the address is where the view opens', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&box=500,600,4000,4000`);

    await expectLoaded(page);

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);
});

///
///**And then it leaves the address**, because it was a way in rather than a setting. Left there, a reload would
///put the view back on the link's framing however far it had moved since.
///
test('a framing from a link leaves the address once it has landed', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&view=2d&box=500,600,4000,4000`);

    await expectLoaded(page);

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);
    await expect.poll(() => boxNamed(page), { timeout: 20000 }).toBeNull();

    //
    //The file it named has gone the same way, and the view stays - named, and still on the framing it brought.
    //
    //Waited for rather than read: the two go a moment apart. The framing is applied once the drawing is on the
    //page, and the file leaves when its open has finished saving - which can be the later of the two.
    //
    await expect.poll(() => new URL(page.url()).searchParams.get('file'), { timeout: 20000 }).toBeNull();

    const left = new URL(page.url()).searchParams;

    expect(left.get('view')).toBe('2d');
    expect(await framing(page)).toEqual([500, 600, 4000, 4000]);
});

///
///The session keeps it from there, like a framing of your own - and keeps it however often the page is reopened.
///
///Twice on purpose. Putting a box on the view raised no settle, so the save that follows every restore wrote an
///empty box over the one just put back: the first reopen came back to it and the second to the fit. The address
///hid that while it held on to box= for a reload to read again.
///
test('a framing from a link is kept by the session, however often the page is reopened', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&view=2d&box=500,600,4000,4000`);

    await expectLoaded(page);

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);

    for (let visit = 0; visit < 2; visit++) {
        //Past the save that follows a box landing.
        await page.waitForTimeout(2000);

        //The bare address, which is where a session is restored rather than a link honored.
        await gotoApp(page, '', null);
        await expectLoaded(page);

        await expect.poll(async () => framing(page), { timeout: 30000 }).toEqual([500, 600, 4000, 4000]);
    }
});

///Spaces are what the attribute and the session use, so an address written that way has to read too.
test('a framing written with spaces reads the same as one written with commas', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&box=500%20600%204000%204000`);

    await expectLoaded(page);

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);
});

///
///A typo costs that one setting, the way a misspelled tool does - not the page.
///
///Both halves matter, and for different reasons. A box of three numbers is nonsense; a box of no width is
///four perfectly good numbers that a browser handed to it stops drawing on rather than draws small.
///
for (const [what, box] of [['three numbers', '1,2,3'], ['no width', '0,0,0,500'], ['words', 'a,b,c,d']]) {
    test(`a framing of ${what} is ignored, and the drawing is framed`, async ({ page }) => {
        await gotoApp(page, `?file=${MOSFET}&box=${box}`);

        await expectLoaded(page);

        await page.waitForTimeout(1500);

        const shown = await framing(page);

        //The fit, which is what the view does when nothing has been said.
        expect(shown[2]).toBeGreaterThan(1);
        expect(shown[3]).toBeGreaterThan(1);

        //And the layout is in it, rather than the view sitting on a corner of nothing.
        const drawn = await page.locator('#gdsSVG path[data-elements], #gdsSVG polygon[data-element]').count();

        expect(drawn).toBeGreaterThan(0);
    });
}

///
///Centering lands in exactly the state a fresh open lands in, so a link made afterwards carries no framing.
///
///Carrying one would send somebody to a box that says what the app would have done anyway.
///
test('centering frames the drawing again, and a link made after it carries no framing', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    const opening = await framing(page);

    await panAcross(page);

    await expect.poll(async () => boxIn(await sharedLink(page)), { timeout: 20000 }).not.toBeNull();

    await page.locator('#centerView').click();

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual(opening);

    expect(boxIn(await sharedLink(page))).toBeNull();
});

test('the center button is not in the text view, which has no framing', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    await expect(page.locator('#centerView')).toBeVisible();

    await selectView(page, 'ViewText');

    await expect(page.locator('#centerView')).toHaveCount(0);
});

///
///The 3D half. A file opens framed, from above at forty-five degrees, and an orbit or a zoom can lose it again;
///this is the button that brings it back to that view.
///
test('centering the 3D view puts the camera on the stack, and a link made from there carries it', async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
    await page.waitForTimeout(1500);

    await captureScene(page);

    const opening = await cameraPosition(page);

    expect(opening).not.toBeNull();

    await page.locator('#centerView').click();

    await page.waitForTimeout(500);

    await captureScene(page);

    const centered = await cameraPosition(page);

    //It moved, and it is standing off the layout rather than sitting in the middle of it - in front of it and
    //above it, looking down.
    expect(Math.hypot(centered.x - opening.x, centered.y - opening.y, centered.z - opening.z)).toBeGreaterThan(1);
    expect(centered.z).toBeGreaterThan(0);
    expect(centered.y).toBeGreaterThan(0);

    //Linkable through Share, the way a 2D framing is - and the address stays as it was opened.
    await expect.poll(async () => new URL(await sharedLink(page)).searchParams.get('camera'), { timeout: 20000 }).not.toBeNull();

    expect(cameraNamed(page)).toBeNull();
});

test('a camera in the address is where the 3D view opens, and then leaves the address', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&view=3d&camera=0,0,5000,0,0,0`);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(1500);

    await captureScene(page);

    const where = await cameraPosition(page);

    expect(where).not.toBeNull();

    //Through OrbitControls.update, which rebuilds the position from spherical coordinates - so the same
    //place to within a couple of trigonometric functions rather than the same bits.
    expect(Math.hypot(where.x, where.y, where.z - 5000)).toBeLessThan(5);

    //Gone once it has landed, with the 3D view still named - an address without one would open in 2D.
    await expect.poll(() => cameraNamed(page), { timeout: 20000 }).toBeNull();

    expect(new URL(page.url()).searchParams.get('view')).toBe('3d');
});

///
///**Zooming by press, beside Center**, for whoever has no wheel or trackpad to hand. A press is four of the wheel's
///notches, about the middle of the view rather than under a pointer - a button has none over the drawing - and a
///press out takes a press in back exactly.
///
test('the zoom buttons zoom the 2D view about its middle, and back again', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    const box = async () => (await page.locator('#gdsSVG').getAttribute('viewBox')).trim().split(/\s+/).map(Number);

    await viewSettled(page);

    const opening = await box();
    const press = Math.pow(1.125, 4);

    await page.locator('#zoomIn').click();
    await viewSettled(page);

    const zoomed = await box();

    expect(zoomed[2]).toBeCloseTo(opening[2] / press, 2);
    expect(zoomed[3]).toBeCloseTo(opening[3] / press, 2);

    //The middle stays where it was.
    expect(zoomed[0] + (zoomed[2] / 2)).toBeCloseTo(opening[0] + (opening[2] / 2), 2);
    expect(zoomed[1] + (zoomed[3] / 2)).toBeCloseTo(opening[1] + (opening[3] / 2), 2);

    await page.locator('#zoomOut').click();
    await viewSettled(page);

    const back = await box();

    for (let i = 0; i < 4; i++)
        expect(back[i]).toBeCloseTo(opening[i], 2);
});

///The 3D half: the camera moved along its line to what it turns around, nearer for in and back for out.
test('the zoom buttons move the 3D camera toward the stack, and back again', async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
    await page.waitForTimeout(1500);

    await captureScene(page);

    //The middle of the slabs, which is where the opening view is aimed.
    const middle = await page.evaluate(async () => {
        const THREE = await import('three');
        const bounds = new THREE.Box3();

        window.__gdsScene.traverse(object => {
            if (object.isMesh && object.userData.outlines != null)
                bounds.expandByObject(object);
        });

        const center = bounds.getCenter(new THREE.Vector3());

        return { x: center.x, y: center.y, z: center.z };
    });

    const away = (camera) => Math.hypot(camera.x - middle.x, camera.y - middle.y, camera.z - middle.z);
    const opening = await cameraPosition(page);

    await page.locator('#zoomIn').click();
    await expect.poll(async () => away(await cameraPosition(page))).toBeLessThan(away(opening) * 0.8);

    await page.locator('#zoomOut').click();
    await expect.poll(async () => {
        const back = await cameraPosition(page);

        return Math.hypot(back.x - opening.x, back.y - opening.y, back.z - opening.z);
    }).toBeLessThan(1);
});
