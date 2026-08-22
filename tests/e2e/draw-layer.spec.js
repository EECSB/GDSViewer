//Choosing which layer a shape is drawn on, from the sidebar.
//
//The toolbar had a dropdown for this and no longer does: the layers are already listed down the side of the
//screen, and going up to a dropdown to name the one you are looking at was a detour. So while the Draw tool
//is out the rows are a control, and the rest of the time they are the readout they always were - which is
//the part worth a test, because the row already had a click on it for renaming and the two must not both
//fire.
//
//The marked row is now the only place the answer is shown, so these read it with `drawingLayer`.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, drawingLayer, layersListed } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///Into a cell, since there is no Draw tool until something has said which cell a shape would go in.
async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#drawTool')).toBeVisible();
}

///The Pan button, which is the one tool in the group with no id of its own.
function panTool(page) {
    return page.locator('#toolGroup').getByRole('button', { name: 'Pan', exact: true });
}

///
///**The row itself is a control only while the Draw tool is out.**
///
///What a press on the row body can answer is "put the next shape here", and that is not a question with
///the pointer in hand - so the rest of the time the row is the readout it always was. The eye, the lock and
///the gear on it are their own buttons and are pressable throughout; this is about the row.
///
test('the rows are a readout until the Draw tool is out', async ({ page }) => {
    await expect(page.locator('.layerRow')).not.toHaveCount(0);
    await expect(page.locator('.layerRowPickable')).toHaveCount(0);
    await expect(page.locator('.layerRowDrawing')).toHaveCount(0);

    await enterCell(page);
    await page.locator('#drawTool').click();

    await expect(page.locator('.layerRowPickable')).not.toHaveCount(0);
    await expect(page.locator('.layerRowDrawing')).not.toHaveCount(0);

    //And back to a readout when the tool is put down.
    await panTool(page).click();

    await expect(page.locator('.layerRowPickable')).toHaveCount(0);
    await expect(page.locator('.layerRowDrawing')).toHaveCount(0);
});

///
///One row, and the first the file has - which is where startDrawingShapes puts it.
///
///Worth pinning now the dropdown has gone, because the mark is the only thing that says where a shape will
///land. Two rows marked, or none, would leave that unanswerable from the screen.
///
test('exactly one row is marked, and it is the first layer the file has', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    await expect(page.locator('.layerRowDrawing')).toHaveCount(1);

    const listed = await layersListed(page);

    expect(await drawingLayer(page)).toBe(listed[0]);
});

///The dropdown it replaced is gone, rather than sitting beside it saying the same thing twice.
test('there is no layer dropdown in the toolbar', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    await expect(page.locator('#drawLayer')).toHaveCount(0);
});

test('clicking a row takes that layer, and the mark moves with it', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    const before = await drawingLayer(page);

    //A row that is not the one already chosen.
    const rows = page.locator('.layerRow');
    const count = await rows.count();

    let clicked = -1;

    for (let i = 0; i < count; i++) {
        if (await rows.nth(i).evaluate(node => node.classList.contains('layerRowDrawing')))
            continue;

        await rows.nth(i).locator('.layerName').click();

        clicked = i;

        break;
    }

    expect(clicked).toBeGreaterThan(-1);

    expect(await drawingLayer(page)).not.toBe(before);
    await expect(page.locator('.layerRowDrawing')).toHaveCount(1);
    await expect(rows.nth(clicked)).toHaveClass(/layerRowDrawing/);
});

///`65/20` becomes the class `l65_20`; see classesFor in SvgWriter.
function pathOfLayer(layer) {
    return 'path.l' + layer.replace('/', '_');
}

///How many shapes a layer's merged path is drawing, by counting its subpaths.
async function subpaths(page, layer) {
    return page.locator(pathOfLayer(layer))
        .evaluate(node => (node.getAttribute('d').match(/M/g) || []).length);
}

///
///The point of the whole thing: the shape lands where the sidebar says it will.
///
///Both halves are checked, because only the pair says the click did anything - a shape landing on the layer
///the toolbar names is true whether or not the row was ever listened to. The layer that was chosen *before*
///the click has to be the one that did not grow.
///
test('a shape drawn after the click goes on that layer and not the last one', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    const was = await drawingLayer(page);

    const rows = page.locator('.layerRow');
    const count = await rows.count();

    for (let i = 0; i < count; i++) {
        if (await rows.nth(i).evaluate(node => node.classList.contains('layerRowDrawing')))
            continue;

        await rows.nth(i).locator('.layerName').click();

        break;
    }

    const now = await drawingLayer(page);

    expect(now).not.toBe(was);

    const beforeOnNew = await subpaths(page, now);
    const beforeOnOld = await subpaths(page, was);

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 200, view.y + 200);
    await page.mouse.down();
    await page.mouse.move(view.x + 300, view.y + 280, { steps: 6 });
    await page.mouse.up();

    await expect.poll(async () => subpaths(page, now), { timeout: 15000 }).toBe(beforeOnNew + 1);

    expect(await subpaths(page, was)).toBe(beforeOnOld);
});

///
///The row had a click on it already, for renaming. Both firing would open a text box over the name of the
///layer that had just been chosen, which is two answers to a question that was asked once - so which of
///them won had to be pinned here.
///
///**There is only one answer now.** Renaming moved to the settings behind the gear, where the box has the
///width of a popup rather than of a sidebar column, so the row's press is left meaning the one thing it
///could ever answer: this is the layer the next shape goes on.
///
test('the click chooses the layer and opens no name box', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    await page.locator('.layerRow').nth(2).locator('.layerName').click();

    await expect(page.locator('.layerNameBox')).toHaveCount(0);
    await expect(page.locator('.layerRow').nth(2)).toHaveClass(/layerRowDrawing/);

    //And with the tool put down it opens no box either - the press that used to.
    await panTool(page).click();
    await page.locator('.layerRow').nth(3).locator('.layerName').click();

    await expect(page.locator('.layerNameBox')).toHaveCount(0);
});

///
///Hiding a layer and taking it as the one to draw on are opposite answers, so the eye must not do both.
///The gear is the same case: it opens that layer's settings, which is not a statement about where a shape
///goes.
///
test('the eye and the settings button leave the draw layer alone', async ({ page }) => {
    await enterCell(page);
    await page.locator('#drawTool').click();

    const before = await drawingLayer(page);

    await page.locator('.layerRow').nth(3).locator('.layerEyeButton').click();

    expect(await drawingLayer(page)).toBe(before);

    await page.locator('.layerRow').nth(4).locator('.layerSettingsButton').click();

    expect(await drawingLayer(page)).toBe(before);
});
