//Changing a layout in the 2D view: dragging a shape, deleting one, and taking it back.
//
//The edits themselves are covered in LayoutEditTests, including that undo restores the file byte for
//byte. What is only checkable here is the wiring: that a drag in pixels becomes a move in the cell's own
//coordinates, that every instance of a cell moves with it, and that the change reaches the download -
//which is the whole point, and the one thing a unit test on the model cannot see.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, allPoints, chooseShape, openedOnItsOwn, uploadFile } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);

    await uploadFile(page, 'e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    await page.locator('#selectTool').click();
});

///Clicks shapes until one from the placed cell is picked out, then enters that cell.
async function enterLeaf(page) {
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        if ((await page.locator('#selectionPanel').textContent()).includes('TOP > LEAF')) {
            //Again, on the same shape: the first click took hold of the placement, the second goes inside
            //it. See descendsOnClick in Viewer2DSvg.
            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            await expect(page.locator('#contextBar')).toContainText('LEAF');

            return;
        }
    }

    throw new Error('no shape from the placed cell was found');
}

///Picks out the shape currently being looked through, and gives back its bounding box on screen.
async function chooseInContext(page) {
    const box = await shapeBox(page, 0, 'inContext');

    await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

    await expect(page.locator('#selectionPanel')).toBeVisible();

    return box;
}

///Where every drawn shape is, as a stable string, for comparing before and after.
async function corners(page) {
    return allPoints(page).then(points => points.sort().join(' | '));
}

test('there is nothing to undo before anything is changed', async ({ page }) => {
    await expect(page.locator('#undoEdit')).toHaveCount(0);
});

///
///The one that matters: a drag on screen becomes a move in the cell, and all three instances follow.
///
test('dragging a shape moves every instance of its cell', async ({ page }) => {
    await enterLeaf(page);

    const before = await corners(page);

    const box = await chooseInContext(page);

    await page.mouse.move(box.x + (box.width / 2), box.y + (box.height / 2));
    await page.mouse.down();
    await page.mouse.move(box.x + (box.width / 2) + 60, box.y + (box.height / 2) + 30, { steps: 6 });
    await page.mouse.up();

    await expect.poll(async () => corners(page), { timeout: 15000 }).not.toBe(before);

    //Three squares of the cell moved, and the top's own did not - so exactly three of the four changed.
    const after = (await corners(page)).split(' | ');
    const was = before.split(' | ');

    const changed = after.filter(shape => !was.includes(shape));

    expect(changed).toHaveLength(3);
});

test('a drag can be undone and redone', async ({ page }) => {
    await enterLeaf(page);

    const before = await corners(page);

    const box = await chooseInContext(page);

    await page.mouse.move(box.x + (box.width / 2), box.y + (box.height / 2));
    await page.mouse.down();
    await page.mouse.move(box.x + (box.width / 2) + 70, box.y + (box.height / 2), { steps: 6 });
    await page.mouse.up();

    await expect.poll(async () => corners(page), { timeout: 15000 }).not.toBe(before);

    const moved = await corners(page);

    await expect(page.locator('#undoEdit')).toBeEnabled();

    await page.locator('#undoEdit').click();

    await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);

    await page.locator('#redoEdit').click();

    await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(moved);
});

test('a click without a drag changes nothing', async ({ page }) => {
    await enterLeaf(page);

    const before = await corners(page);

    await chooseInContext(page);

    expect(await corners(page)).toBe(before);
    await expect(page.locator('#undoEdit')).toHaveCount(0);
});

test('deleting a shape removes every instance of it', async ({ page }) => {
    await enterLeaf(page);
    await chooseInContext(page);

    await chooseShape(page, '#deleteShape');

    //The three squares of the cell go; the top's own stays.
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(1);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
});

///
///The edit has to reach the file, not only the picture. Downloaded and re-uploaded, because that is the
///round trip somebody actually does - and an edit the download did not carry would be silent.
///
test('an edit is in the file that is downloaded', async ({ page }) => {
    await enterLeaf(page);
    await chooseInContext(page);

    await chooseShape(page, '#deleteShape');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(1);

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;
    const path = await download.path();

    //Straight back in, which is the only way to ask what the bytes actually say.
    await uploadFile(page, path);

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(1);
});

///
///Clicking a shape outside the cell being edited moves the work to *its* cell.
///
///**This is the trade the Edit button paid for.** With the button, a click on a faded shape chose it and
///stopped there, and the cell you were working in only changed when you said so. Now a click means the
///cell of the shape under it, whichever that is - which is what makes one click enough to start editing,
///and which also means a stray click on the surroundings takes the work with it. The context bar is what
///says where you ended up, and it is one click back.
///
test('clicking a shape outside the cell being edited moves to its cell', async ({ page }) => {
    await enterLeaf(page);

    await expect(page.locator('#contextBar')).toContainText('LEAF');

    //The top's own square, which was faded and out of context a moment ago.
    const box = await shapeBox(page, 0, 'outOfContext');

    await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

    await expect(page.locator('#selectionPanel')).toBeVisible();
    await expect(page.locator('#contextBar')).toContainText('TOP');

    //And it can be changed now, which is the point: what was chosen is in the cell being edited.
    await expect(page.locator('#deleteShape')).toBeVisible();
});
