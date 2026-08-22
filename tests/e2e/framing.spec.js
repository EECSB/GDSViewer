//
//Where the view is looking, carried in the address, and the button that puts it back in the middle.
//
//The session already restored both of these - see session.spec.js, which covers coming back to your own
//framing. What is here is the other half: carrying it to somebody else, which a session cannot do because
//a session is local by design.
//
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, selectView, captureScene, cameraPosition, MOSFET } = require('./helpers');

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

///
///A file nobody has moved carries no framing, which is the state the fit leaves.
///
///Worth pinning rather than assuming: fitToDrawing deliberately reports no settle, because the frame a file
///opens on is one a reopen works out for itself. If that ever changed, every link to the app would start
///carrying a box saying exactly what the app would have done anyway.
///
test('a file nobody has moved carries no framing in the address', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    await page.waitForTimeout(1500);

    expect(boxNamed(page)).toBeNull();
});

test('panning writes the framing into the address', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    const opening = await framing(page);

    await panAcross(page);

    //The report is on settle, a second after the gesture stops.
    await expect.poll(() => boxNamed(page), { timeout: 20000 }).not.toBeNull();

    const written = boxNamed(page).split(',').map(Number);
    const shown = await framing(page);

    expect(written).toHaveLength(4);

    //The address says what is on screen, not something near it.
    for (let i = 0; i < 4; i++)
        expect(written[i]).toBeCloseTo(shown[i], 3);

    //And it moved, rather than the opening frame being written down.
    expect(Math.abs(written[0] - opening[0])).toBeGreaterThan(1);
});

///
///And it names the view alongside, because writing the address at all makes the address the authority.
///
///OnParametersSet re-reads `view` out of the query on every navigation, so a save on an address that does
///not name one recomputes it as the 2D default. On a session-restored 3D view that meant the view somebody
///was looking at vanished a second after they stopped orbiting - which session.spec.js caught, on a slider
///that was no longer in the page, and which this states outright.
///
test('writing the framing names the view too, so the address still says what is on screen', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    await panAcross(page);

    await expect.poll(() => boxNamed(page), { timeout: 20000 }).not.toBeNull();

    expect(new URL(page.url()).searchParams.get('view')).toBe('2d');
});

///Which is the whole point of the parameter: this is the link somebody else would be handed.
test('a framing in the address is where the view opens', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&box=500,600,4000,4000`);

    await expectLoaded(page);

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual([500, 600, 4000, 4000]);
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
///Centering lands in exactly the state a fresh open lands in, so the box has to go with it.
///
///Leaving it would put the next visit somewhere else, which is the opposite of what was just asked for.
///
test('centering frames the drawing again and takes the framing back out of the address', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    const opening = await framing(page);

    await panAcross(page);

    await expect.poll(() => boxNamed(page), { timeout: 20000 }).not.toBeNull();

    await page.locator('#centerView').click();

    await expect.poll(async () => framing(page), { timeout: 20000 }).toEqual(opening);

    await expect.poll(() => boxNamed(page), { timeout: 20000 }).toBeNull();
});

test('the center button is not in the text view, which has no framing', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expectLoaded(page);

    await expect(page.locator('#centerView')).toBeVisible();

    await selectView(page, 'ViewText');

    await expect(page.locator('#centerView')).toHaveCount(0);
});

///
///The 3D half. The opening position is a fixed guess - z at 2000, looking at the origin - so a layout drawn
///anywhere else opens off the side of the window, and this is the button that fixes that.
///
test('centering the 3D view puts the camera on the stack, and says so in the address', async ({ page }) => {
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

    //It moved, and it is standing off the layout rather than sitting in the middle of it.
    expect(Math.hypot(centered.x - opening.x, centered.y - opening.y, centered.z - opening.z)).toBeGreaterThan(1);
    expect(centered.z).toBeGreaterThan(0);

    //And the framing reached the address, so this view is linkable too.
    await expect.poll(() => new URL(page.url()).searchParams.get('camera'), { timeout: 20000 }).not.toBeNull();
});

test('a camera in the address is where the 3D view opens', async ({ page }) => {
    await gotoApp(page, `?file=${MOSFET}&view=3d&camera=0,0,5000,0,0,0`);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(1500);

    await captureScene(page);

    const where = await cameraPosition(page);

    expect(where).not.toBeNull();

    //Through OrbitControls.update, which rebuilds the position from spherical coordinates - so the same
    //place to within a couple of trigonometric functions rather than the same bits.
    expect(Math.hypot(where.x, where.y, where.z - 5000)).toBeLessThan(5);
});
