//Choosing more than one shape, and copying them into a cell.
//
//The group edit is covered in LayoutEditTests, including that several deletions undo backwards so the
//file comes back byte for byte. What is only checkable here is the picking: that a modifier adds and
//takes away, that a rubber band catches what it is dragged over, and that a group moves and deletes as
//one step rather than as several.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, allPoints, shapePoints, otherShapeClearOfPanel, snapToGrid, chooseShape, openedOnItsOwn, uploadFile } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);

    await uploadFile(page, 'e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    //Snapping is on out of the box; these gestures are a few dozen pixels, which is a fraction of a grid
    //step at this zoom, and they collapse. This file is about picking several shapes, not about the grid.
    await snapToGrid(page, false);

    await page.locator('#selectTool').click();
});

///The middle of the nth drawn polygon, on screen.
async function middleOf(page, nth) {
    const box = await shapeBox(page, nth);

    return { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };
}

async function clickShape(page, nth, modifier) {
    const at = await middleOf(page, nth);

    if (modifier)
        await page.keyboard.down(modifier);

    await page.mouse.click(at.x, at.y);

    if (modifier)
        await page.keyboard.up(modifier);
}

///Enters the placed cell, so that edits are allowed.
async function enterLeaf(page) {
    for (let i = 0; i < 4; i++) {
        await clickShape(page, i);

        if ((await page.locator('#selectionPanel').textContent()).includes('TOP > LEAF')) {
            //Again, on the same shape: the first click took hold of the placement, the second goes inside
            //it. See descendsOnClick in Viewer2DSvg.
            await clickShape(page, i);

            await expect(page.locator('#contextBar')).toContainText('LEAF');

            return;
        }
    }

    throw new Error('no shape from the placed cell was found');
}

///
///The Paste line of the shape menu, which is where Paste lives.
///
///It was a labeled button in the toolbar that appeared as soon as anything was copied - and pushed the
///undo pair along as it did, so a copy rearranged the bar. It is beside Copy and Cut now, in the menu over
///the shapes, and on Ctrl+V.
///
function pasteLine(page) {
    return page.locator('.shapeMenuItem', { hasText: /^Paste/ });
}

///
///Opens the menu somewhere with nothing on it, which is where a paste is usually aimed.
///
///Off the right of the view rather than at a written-down point: the selection panel sits over the left of
///it, and a right-click that lands on the panel never reaches the view at all.
///
async function openMenuOnEmptyCanvas(page) {
    const box = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(box.x + (box.width * 0.88), box.y + (box.height * 0.12), { button: 'right' });

    await expect(page.locator('#shapeMenu')).toBeVisible();
}

///Copies whatever is chosen, then pastes it through the menu.
async function pasteThroughTheMenu(page) {
    await openMenuOnEmptyCanvas(page);

    await pasteLine(page).click();
}

test.describe('picking several', () => {
    test('a plain click replaces what was chosen', async ({ page }) => {
        await clickShape(page, 0);
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

        await clickShape(page, 1);
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });

    ///Either modifier, because which one adds is a habit rather than a rule.
    for (const modifier of ['Control', 'Shift']) {
        test(`${modifier} adds to the selection`, async ({ page }) => {
            await clickShape(page, 0);
            await clickShape(page, 1, modifier);
            await clickShape(page, 2, modifier);

            await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(3);
            await expect(page.locator('#selectionPanel')).toContainText('3 shapes');
        });
    }

    ///The half of "toggle" a selection is unusable without.
    test('the modifier takes one back out again', async ({ page }) => {
        await clickShape(page, 0);
        await clickShape(page, 1, 'Control');

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(2);

        await clickShape(page, 1, 'Control');

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });

    test('the panel says what several shapes have in common', async ({ page }) => {
        await clickShape(page, 0);
        await clickShape(page, 1, 'Control');
        await clickShape(page, 2, 'Control');

        const panel = page.locator('#selectionPanel');

        await expect(panel).toContainText('3 shapes');

        //The layers they are on, and the box around the lot.
        await expect(panel).toContainText(/\d+\/\d+/);
        await expect(panel).toContainText(/\(-?\d+, -?\d+\) to \(-?\d+, -?\d+\)/);
    });

    ///
    ///A drag that starts on the background is a box rather than a move, and catches what it crosses.
    ///
    test('a rubber band catches everything it is dragged over', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        //From one corner of the view to the other, which is everything.
        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(4);
        await expect(page.locator('#selectionPanel')).toContainText('4 shapes');
    });

    test('a band with a modifier adds to what was already chosen', async ({ page }) => {
        await clickShape(page, 0);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.keyboard.down('Control');
        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();
        await page.keyboard.up('Control');

        //All four, and the one already chosen is not counted twice.
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(4);
    });

    test('a click on the background still clears', async ({ page }) => {
        await clickShape(page, 0);
        await expect(page.locator('#selectionPanel')).toBeVisible();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 4, view.y + 4);

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });

    ///Handles are for one shape. On a dozen they would be a hundred circles over the geometry.
    test('handles are shown for one shape and not for several', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await expect(page.locator('#vertexHandles circle')).toHaveCount(5);

        //Add another, and they go. Clear of the selection panel, which the first click just opened over
        //the top-left of the view and which takes its own clicks.
        const other = await otherShapeClearOfPanel(page, 'inContext');

        expect(other, 'every shape is behind the panel').not.toBeNull();

        await page.keyboard.down('Control');
        await page.mouse.click(other.x, other.y);
        await page.keyboard.up('Control');

        await expect(page.locator('#vertexHandles')).toHaveCount(0);
    });
});

test.describe('editing several', () => {
    ///
    ///**One undo step, not three.** A group edit that went on the stack as several would need three
    ///presses to take back one gesture.
    ///
    test('deleting several is one step', async ({ page }) => {
        await enterLeaf(page);

        //Everything, then delete whatever the cell holds.
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();

        await expect(page.locator('#selectionPanel')).toContainText('4 shapes');

        await chooseShape(page, '#deleteShape');

        //The three instances of the cell go together.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(1);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);

        //And there is nothing left to undo, which is what says it was one step.
        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    test('the undo button names how many it would take back', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await chooseShape(page, '#deleteShape');

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Delete/);
    });
});

test.describe('copy and paste', () => {
    ///
    ///Nothing to paste is no Paste line - and, with nothing chosen either, no menu at all.
    ///
    ///The menu opens for a selection or for a clipboard, and this state is neither. A press that opens an
    ///empty panel over the layout is worse than one that does nothing.
    ///
    test('there is nothing to paste until something is copied', async ({ page }) => {
        await enterLeaf(page);

        const box = await page.locator('#gdsSVG').boundingBox();
        const empty = { x: box.x + (box.width * 0.88), y: box.y + (box.height * 0.12) };

        //Entering a cell leaves the shape that was clicked chosen, and a selection is enough to open the
        //menu on its own - so it has to go before this can ask about the clipboard.
        await page.mouse.click(empty.x, empty.y);

        await expect(page.locator('#selectionPanel')).toHaveCount(0);

        await page.mouse.click(empty.x, empty.y, { button: 'right' });

        await expect(page.locator('#shapeMenu')).toHaveCount(0);

        //And with a shape chosen, the menu opens and simply has no Paste on it.
        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();

        await page.mouse.click(empty.x, empty.y, { button: 'right' });

        await expect(page.locator('#shapeMenu')).toBeVisible();
        await expect(pasteLine(page)).toHaveCount(0);
    });

    test('copying then pasting adds the shapes to the cell', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await page.locator('#copyShapes').click();

        await openMenuOnEmptyCanvas(page);

        //One shape on the clipboard reads "Paste" rather than "Paste 1": a count of one is a count
        //nobody needs, and the line is shorter without it.
        await expect(pasteLine(page)).toHaveText('Paste');

        await page.keyboard.press('Escape');

        await pasteThroughTheMenu(page);

        //One shape into a cell placed three times, so three more are drawn.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///
    ///The copy lands under the pointer, not a fixed step from where it was copied.
    ///
    ///Measured against the *right-click point* rather than against the original: an offset paste would also
    ///put the shape "somewhere else", which is why the older test below cannot tell the two apart. Half a
    ///shape's width of slack, because it is the middle of the copy that goes to the pointer and the shape
    ///has to be rounded onto the file's integer grid to get there.
    ///
    test('a paste lands where the pointer is', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await page.locator('#copyShapes').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        const aimedAt = {
            x: view.x + (view.width * 0.82),
            y: view.y + (view.height * 0.7)
        };

        await page.mouse.click(aimedAt.x, aimedAt.y, { button: 'right' });
        await expect(page.locator('#shapeMenu')).toBeVisible();
        await pasteLine(page).click();

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(2);

        //The one that was not there before, wherever it went.
        const landed = await shapeBox(page, 1, 'inContext');

        expect(Math.abs((landed.x + (landed.width / 2)) - aimedAt.x)).toBeLessThan(landed.width);
        expect(Math.abs((landed.y + (landed.height / 2)) - aimedAt.y)).toBeLessThan(landed.height);
    });

    ///
    ///And it comes back chosen, with the tool that can move it.
    ///
    ///A paste is the middle of a gesture - what follows it is dragging the copies into place - so landing
    ///them unselected under whatever tool happened to be in hand meant finding them again first. Pan is the
    ///tool here on purpose: it is the one where the copies could not be picked up at all.
    ///
    test('a paste leaves the copies chosen, with Select in hand', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await page.locator('#copyShapes').click();
        await page.locator('#panTool').click();

        await pasteThroughTheMenu(page);

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(2);

        //One shape was copied, so exactly one comes back marked - the copy, not the original as well.
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

        await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);
        await expect(page.locator('#panTool')).not.toHaveClass(/toolButtonOn/);
    });

    ///
    ///Pasted offset rather than exactly on top, so a copy is visibly a second shape rather than one that
    ///appears to have done nothing at all.
    ///
    test('a pasted shape does not land exactly on the one it came from', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        const original = await shapePoints(page, 0, 'inContext');

        await page.locator('#copyShapes').click();
        await pasteThroughTheMenu(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        const all = await allPoints(page);

        //The original is still there, and the copy is somewhere else.
        expect(all).toContain(original);
        expect(all.filter(points => points === original)).toHaveLength(1);
    });

    test('several copied shapes paste as one step', async ({ page }) => {
        await enterLeaf(page);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();

        await page.locator('#copyShapes').click();

        await openMenuOnEmptyCanvas(page);
        await expect(pasteLine(page)).toHaveText('Paste 4');
        await page.keyboard.press('Escape');

        await pasteThroughTheMenu(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBeGreaterThan(4);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    test('a paste is in the file that is downloaded', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await page.locator('#copyShapes').click();
        await pasteThroughTheMenu(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await uploadFile(page, path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(7);
    });
});

///
///Cut: onto the clipboard and out of the cell, in one gesture.
///
///Copy and then delete, which is what a cut is - and one entry on the undo stack rather than two, because
///the delete is already a single CompoundEdit and copying is not an edit at all.
///
test.describe('cutting', () => {
    test('takes the chosen shapes out and puts them on the clipboard', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        const before = await shapeCount(page);

        await page.locator('#cutShapes').click();

        //One shape out of a cell placed three times, so three fewer are drawn.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 3);

        //And on the clipboard, ready to go back.
        await openMenuOnEmptyCanvas(page);

        //One shape on the clipboard reads "Paste" rather than "Paste 1": a count of one is a count
        //nobody needs, and the line is shorter without it.
        await expect(pasteLine(page)).toHaveText('Paste');

        await page.keyboard.press('Escape');

        await pasteThroughTheMenu(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    ///One press, not two: the clipboard is filled without an edit, so only the removal is on the stack.
    test('is one step on the undo stack, and says it was a cut', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        const before = await shapeCount(page);

        await page.locator('#cutShapes').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 3);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Cut/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    test('Ctrl+X does the same as the button', async ({ page }) => {
        await enterLeaf(page);

        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        const before = await shapeCount(page);

        await page.keyboard.press('Control+x');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 3);

        //And onto the clipboard, which is what makes it a cut rather than a delete.
        await openMenuOnEmptyCanvas(page);

        await expect(pasteLine(page)).toHaveCount(1);
    });

    ///
    ///**Cut gives back everything it takes.**
    ///
    ///Copy used to skip labels, which was survivable while the original stayed put and stops being so the
    ///moment there is a cut: a shape and the label naming it would have come back from a paste as the shape
    ///alone. So the clipboard carries labels, and this is what says it still does.
    ///
    test('a label goes with the shapes and comes back with them', async ({ page }) => {
        await enterLeaf(page);

        //Everything the cell holds, which for this fixture is one square - so a label is placed to have one.
        await page.locator('#drawTool').click();
        await chooseShape(page, '#labelShape');

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 150);

        await expect(page.locator('#canvasLabelText')).toBeVisible();
        await page.locator('#canvasLabelText').fill('PIN');
        await page.locator('#canvasLabelText').press('Enter');
        await expect(page.locator('#canvasLabelText')).toHaveCount(0);

        await expect(page.locator('#gdsSVG text', { hasText: 'PIN' })).not.toHaveCount(0);

        //Choose it, cut it, and it is gone from the drawing.
        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'PIN' }).first().boundingBox();

        await page.mouse.click(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await page.locator('#cutShapes').click();

        await expect.poll(async () => page.locator('#gdsSVG text', { hasText: 'PIN' }).count(), { timeout: 15000 })
            .toBe(0);

        //And the paste brings the words back, not an empty shape.
        await pasteThroughTheMenu(page);

        await expect.poll(async () => page.locator('#gdsSVG text', { hasText: 'PIN' }).count(), { timeout: 15000 })
            .toBeGreaterThan(0);
    });
});
