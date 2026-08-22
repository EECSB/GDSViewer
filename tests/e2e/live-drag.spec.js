//A drag that follows the pointer.
//
//What a drag does to the file is covered in LayoutEditTests, and that it reaches the file at all by
//editing.spec.js. What is only checkable here is what the eye sees on the way: the shape used to sit still
//until the button came up and then jump to wherever the pointer had got to, which reads as the app having
//missed the gesture and guessed at the end of it.
//
//The picture belongs to Blazor and is rebuilt in C#, so it cannot be redrawn per frame - at twenty thousand
//shapes that is a third of a second each. What moves instead is a copy lifted out of the picture. These
//tests watch that copy, because it is the thing the eye follows.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, chooseShape, MOSFET_POLYGONS } = require('./helpers');

const LIFTED = '#draggingShapes';

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///Where the lifted copy sits on screen, or null when nothing has been lifted.
async function liftedAt(page) {
    return page.evaluate((selector) => {
        const lifted = document.querySelector(selector);

        if (lifted == null)
            return null;

        const box = lifted.getBoundingClientRect();

        return { x: Math.round(box.x), y: Math.round(box.y) };
    }, LIFTED);
}

///Chooses the first shape with the Move tool and hands back where it is and which element it is.
async function takeHold(page) {
    await page.locator('#moveTool').click();

    const box = await shapeBox(page);
    const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

    await page.mouse.click(at.x, at.y);

    await expect(page.locator('#selectionPanel')).toBeVisible();

    const element = Number(await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element'));

    return { at, element, corners: await elementPoints(page, element) };
}

test('the shape follows the pointer rather than waiting for the button', async ({ page }) => {
    const held = await takeHold(page);

    await page.mouse.move(held.at.x, held.at.y);
    await page.mouse.down();

    const seen = [];

    for (let step = 1; step <= 4; step++) {
        await page.mouse.move(held.at.x + (step * 25), held.at.y + (step * 15));

        seen.push(await liftedAt(page));
    }

    await page.mouse.up();

    //Lifted the whole way, and somewhere different every time - which is the difference between following
    //the pointer and arriving at the end of the gesture.
    for (const where of seen)
        expect(where).not.toBeNull();

    const places = new Set(seen.map(where => `${where.x},${where.y}`));

    expect(places.size).toBe(seen.length);

    //And it went the way the pointer went, rather than merely somewhere.
    expect(seen[3].x).toBeGreaterThan(seen[0].x);
    expect(seen[3].y).toBeGreaterThan(seen[0].y);
});

///
///What was lifted is put back, and the real shape is where the preview was.
///
///The preview is a copy taken out of the picture, so a drop that forgot to put it back would leave the
///layout a shape short with a stray one drawn over it - and it would look right, because the two are drawn
///the same.
///
test('the drop puts the picture back and moves the shape', async ({ page }) => {
    const held = await takeHold(page);
    const nodes = await page.locator('#gdsSVG > path[data-elements]').count();

    await page.mouse.move(held.at.x, held.at.y);
    await page.mouse.down();
    await page.mouse.move(held.at.x + 70, held.at.y + 40, { steps: 6 });

    await expect(page.locator(LIFTED)).toHaveCount(1);

    await page.mouse.up();

    await expect(page.locator(LIFTED)).toHaveCount(0);

    await expect.poll(async () => elementPoints(page, held.element), { timeout: 15000 })
        .not.toBe(held.corners);

    //One node a layer again, and every shape still drawn once.
    await expect.poll(async () => page.locator('#gdsSVG > path[data-elements]').count(), { timeout: 15000 })
        .toBe(nodes);

    expect(await shapeCount(page)).toBe(MOSFET_POLYGONS);
});

///Nothing is lifted for a click, or every click would rebuild the picture twice for no movement.
test('a click lifts nothing', async ({ page }) => {
    const held = await takeHold(page);

    await page.mouse.move(held.at.x, held.at.y);
    await page.mouse.down();
    await page.mouse.move(held.at.x, held.at.y);

    await expect(page.locator(LIFTED)).toHaveCount(0);

    await page.mouse.up();

    await expect(page.locator(LIFTED)).toHaveCount(0);
});

///
///A corner drag previews too, and it reshapes rather than translating.
///
///The two gestures do different things to the shape, so a preview that translated for both would tell the
///truth about one of them and a lie about the other.
///
test('dragging a corner previews the new shape', async ({ page }) => {
    await page.locator('#selectTool').click();

    const box = await shapeBox(page);

    await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

    await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
        .toBeGreaterThan(0);

    const handle = await page.locator('.vertexHandle').first().boundingBox();
    const at = { x: handle.x + (handle.width / 2), y: handle.y + (handle.height / 2) };

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x + 60, at.y + 60, { steps: 5 });

    await expect(page.locator(LIFTED)).toHaveCount(1);

    //Reshaped, not translated: the lifted copy is no longer the size it was.
    const stretched = await page.evaluate((selector) => {
        const box = document.querySelector(selector).getBoundingClientRect();

        return { width: Math.round(box.width), height: Math.round(box.height) };
    }, LIFTED);

    await page.mouse.up();

    await expect(page.locator(LIFTED)).toHaveCount(0);

    expect(stretched.width).toBeGreaterThan(0);
    expect(stretched.height).toBeGreaterThan(0);
});

///
///Dragging the shape itself, with the handles up.
///
///The Select tool leaves a handle on every corner and still lets the shape be dragged bodily, so the two
///cases live together: the corners are corners of the thing that is moving, and handles left standing where
///it used to be draw the shape in one place and its corners in another.
///
test('the handles travel with a shape dragged whole', async ({ page }) => {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);
    const at = { x: shape.x + (shape.width / 2), y: shape.y + (shape.height / 2) };

    await page.mouse.click(at.x, at.y);

    await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
        .toBeGreaterThan(1);

    const before = await handleAt(page, 1);

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x + 70, at.y + 45, { steps: 4 });

    const during = await handleAt(page, 1);

    await page.mouse.up();

    expect(during.x).toBeGreaterThan(before.x);
    expect(during.y).toBeGreaterThan(before.y);
});

///Where a handle sits on screen, by which corner it is for.
async function handleAt(page, corner) {
    const box = await page.locator(`.vertexHandle[data-corner="${corner}"]`).boundingBox();

    return { x: Math.round(box.x), y: Math.round(box.y) };
}

///
///**The dot goes with the corner.**
///
///The edge followed the pointer and the handle being dragged by it did not, which reads as the drag not
///having taken at all - the one thing the eye is watching while pulling a corner is the thing that stayed
///still. The preview was right and the only part of it anybody was looking at was wrong.
///
test('the handle being dragged follows the pointer too', async ({ page }) => {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
        .toBeGreaterThan(1);

    const before = await handleAt(page, 0);
    const neighbor = await handleAt(page, 1);

    //The middle of the handle, not a guess at its size. Pressing a few pixels off it lands on the shape
    //instead, which is a whole-shape drag - and that moves every handle, so the test would be watching a
    //different gesture than the one it is named for.
    const box = await page.locator('.vertexHandle[data-corner="0"]').boundingBox();
    const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();

    const seen = [];

    for (let step = 1; step <= 3; step++) {
        await page.mouse.move(at.x + (step * 30), at.y + (step * 20));

        seen.push(await handleAt(page, 0));
    }

    //Read before the release, because after it the redraw puts a handle on the corner's new home and the
    //question stops being about the preview.
    const stayed = await handleAt(page, 1);

    await page.mouse.up();

    //Somewhere different every frame, and the way the pointer went.
    expect(new Set(seen.map(where => `${where.x},${where.y}`)).size).toBe(seen.length);
    expect(seen[2].x).toBeGreaterThan(before.x);
    expect(seen[2].y).toBeGreaterThan(before.y);

    //And only that one. A corner drag reshapes, so a handle on a corner that is not moving must not move.
    expect(stayed).toEqual(neighbor);
});

///
///Puts a label down on empty ground and takes hold of it.
///
///Placed rather than picked out of the file, because a label's box is far wider than the anchor it hangs
///from and the ones the file already has sit over geometry - where a click means the shape, which is what
///Picking.Preferred is for. On bare ground the label is the only thing there.
///
async function takeHoldOfLabel(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await page.locator('#drawTool').click();
    await chooseShape(page, '#labelShape');

    const view = await page.locator('#gdsSVG').boundingBox();
    const at = { x: view.x + 640, y: view.y + 90 };

    //The words go into the box that opens over the label; see label.spec.
    await page.mouse.click(at.x, at.y);

    await expect(page.locator('#canvasLabelText')).toBeVisible();

    await page.locator('#canvasLabelText').fill('VDD');
    await page.locator('#canvasLabelText').press('Enter');

    await expect(page.locator('#canvasLabelText')).toHaveCount(0);
    await expect(page.locator('#gdsSVG text').last()).toHaveText('VDD');

    await page.locator('#moveTool').click();

    //
    //Found where it is now, rather than clicked at the point it was placed at.
    //
    //Putting the Draw tool down takes its Shape and Join groups out of the toolbar, which can un-wrap the
    //bar from two rows to one and move the whole view up under the pointer. A screen point remembered from
    //before the tool changed is a point somewhere else afterwards.
    //
    const placed = await page.locator('#gdsSVG text', { hasText: 'VDD' }).first().boundingBox();
    const on = { x: placed.x + (placed.width / 2), y: placed.y + (placed.height / 2) };

    await page.mouse.click(on.x, on.y);

    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

    return on;
}

///Where the label reading VDD is drawn.
async function labelAt(page) {
    const box = await page.locator('#gdsSVG text', { hasText: 'VDD' }).first().boundingBox();

    return { x: Math.round(box.x), y: Math.round(box.y) };
}

///
///A label is a node of its own rather than a subpath, and was never being lifted - so it alone still sat
///still until the button came up, which is the behavior everything else had already stopped having.
///
test('a label follows the pointer rather than jumping on release', async ({ page }) => {
    const at = await takeHoldOfLabel(page);

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();

    const seen = [];

    for (let step = 1; step <= 3; step++) {
        await page.mouse.move(at.x + (step * 30), at.y + (step * 20));

        seen.push(await labelAt(page));
    }

    await page.mouse.up();

    expect(new Set(seen.map(where => `${where.x},${where.y}`)).size).toBe(seen.length);
    expect(seen[2].x).toBeGreaterThan(seen[0].x);
    expect(seen[2].y).toBeGreaterThan(seen[0].y);
});

///
///What was borrowed is handed back.
///
///The label is the real node rather than a copy of it, so a drop that removed the group without taking it
///out first would take the label out of the picture with it - and on a drag that reported nothing there
///would be no redraw to put it back.
///
test('the label is still there after the drop, where it was dragged to', async ({ page }) => {
    const at = await takeHoldOfLabel(page);

    const labels = await page.locator('#gdsSVG text').count();
    const before = await labelAt(page);

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x + 60, at.y + 40, { steps: 5 });
    await page.mouse.up();

    await expect(page.locator(LIFTED)).toHaveCount(0);

    await expect.poll(async () => page.locator('#gdsSVG text').count(), { timeout: 15000 })
        .toBe(labels);

    //And where the preview left it, rather than back where it started.
    await expect.poll(async () => (await labelAt(page)).x, { timeout: 15000 })
        .toBeGreaterThan(before.x);
});
