//The sidebar marking which layers the chosen shapes are on.
//
//The row being drawn on has carried a mark for a while - see draw-layer.spec. The same mark goes on every layer
//a chosen shape is on, so picking something out in the view says in the list which layer it is. The two look
//alike on screen and have a class each, so these read the selection's with `chosenLayers` and leave the draw
//mark to its own spec.
//
//The panel's picker is the yardstick where there is one: it names the layer of what is chosen when that is a
//single layer, and the sidebar has to agree with it.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, chosenLayer, chooseLayer, chosenLayers, dismissSelection, drawingLayer, hideLayer, layersListed, selectView } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await page.locator('#selectTool').click();
});

///The first shape, by its middle - which on this file lands on it, and enters the cell it is in.
async function chooseFirstShape(page) {
    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#chosenLayer')).toBeVisible();
}

///Everything the cell holds, with a band from one corner of the view to the other - see aligning.spec.
async function chooseEverything(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 5, view.y + 5);
    await page.mouse.down();
    await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
    await page.mouse.up();

    await expect(page.locator('#selectionPanel')).toContainText('shapes');
}

///What a row looks like marked: its tint and the bar down its side.
function look(row) {
    const style = getComputedStyle(row);

    return `${style.backgroundColor} ${style.boxShadow}`;
}

test('one chosen shape marks the row of its layer, and no other', async ({ page }) => {
    await expect(page.locator('.layerRowChosen')).toHaveCount(0);

    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    //The layer the panel's picker names, which is the other place on screen that says it.
    expect(await chosenLayers(page)).toEqual([await chosenLayer(page)]);

    await expect(page.locator('.layerRowChosen')).toHaveAttribute('title', 'The chosen shape is on this layer');
});

///
///The mark is the draw mark, which is what was asked for: one look for "this layer", whichever question put it
///there. Compared against the row being drawn on rather than against numbers, so the two cannot drift apart.
///
test('the mark looks the same as the row being drawn on', async ({ page }) => {
    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    const chosen = await page.locator('.layerRowChosen').evaluate(look);
    const plain = await page.locator('.layerRow:not(.layerRowChosen)').first().evaluate(look);

    await page.locator('#drawTool').click();

    const drawing = await page.locator('.layerRowDrawing').evaluate(look);

    expect(chosen).toBe(drawing);
    expect(chosen).not.toBe(plain);
});

///
///Several layers, all marked - the case the panel's picker cannot answer, and says so by naming none.
///
///Each row also says how much of the selection it holds. The counts have to add up to one total, and every row
///has to agree on what that total is, or the tooltips are counting different selections.
///
test('a band across the cell marks every layer it caught a shape on', async ({ page }) => {
    await chooseFirstShape(page);
    await chooseEverything(page);

    //Every layer Mosfet draws has a shape in the cell, so the band catches some of each.
    await expect.poll(async () => chosenLayers(page)).toEqual(await layersListed(page));

    expect(await chosenLayer(page)).toBe('');

    const titles = await page.locator('.layerRowChosen').evaluateAll(rows => rows.map(row => row.getAttribute('title')));
    const read = titles.map(title => title.match(/^(One|\d+) of the (\d+) chosen shapes (is|are) on this layer$/));

    expect(read.every(Boolean)).toBe(true);

    const total = read[0][2];
    let counted = 0;

    for (const found of read) {
        expect(found[2]).toBe(total);

        if (found[1] === 'One')
            counted += 1;
        else
            counted += Number(found[1]);
    }

    expect(counted).toBe(Number(total));
});

test('choosing nothing puts the mark out', async ({ page }) => {
    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    expect(await dismissSelection(page)).toBe(true);

    await expect(page.locator('.layerRowChosen')).toHaveCount(0);
});

///The shape stays chosen when it changes layer, so the mark has to go where the shape went.
test('moving the chosen shape onto another layer moves the mark with it', async ({ page }) => {
    await chooseFirstShape(page);

    let to = '93/44';

    if (await chosenLayer(page) === to)
        to = '66/20';

    await chooseLayer(page, to);

    await expect.poll(async () => chosenLayers(page), { timeout: 15000 }).toEqual([to]);
});

///Hiding a layer lets go of what was chosen on it, and the row stops saying it holds anything.
test('hiding the layer drops the mark with the shape', async ({ page }) => {
    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    const marked = await chosenLayers(page);
    const listed = await layersListed(page);

    await hideLayer(page, listed.indexOf(marked[0]));

    await expect(page.locator('.layerRowChosen')).toHaveCount(0);
});

///
///Taking up the pen puts down what was chosen, and its mark with it - so the one row lit is the one a shape
///would go on, rather than that and the selection's layer looking like two places to draw.
///
test('taking up the Draw tool leaves only the draw mark', async ({ page }) => {
    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    await page.locator('#drawTool').click();

    await expect(page.locator('.layerRowChosen')).toHaveCount(0);
    await expect(page.locator('.layerRowDrawing')).toHaveCount(1);
});

///
///**And the draw mark is the chosen shape's layer** - asked for 2026-10-19: a shape picked out with the select tool
///says which layer is being worked on, and the pen opened on the first layer the file has instead. Not when that is
///the first layer anyway, which would pass either way: a shape on a later layer is chosen for it.
///
test('the Draw tool opens on the layer of the shape chosen', async ({ page }) => {
    await chooseFirstShape(page);

    const listed = await layersListed(page);
    let chosen = (await chosenLayers(page))[0];

    //Another shape, on a layer that is not the first, if the first one chosen was on it.
    if (chosen === listed[0]) {
        const other = page.locator(`#gdsSVG path:not(.l${listed[0].replace('/', '_')})`).first();
        const box = await other.boundingBox();

        await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
        await expect.poll(async () => (await chosenLayers(page))[0]).not.toBe(listed[0]);

        chosen = (await chosenLayers(page))[0];
    }

    expect(await chosenLayers(page)).toHaveLength(1);

    await page.locator('#drawTool').click();

    await expect(page.locator('.layerRowDrawing')).toHaveCount(1);
    expect(await drawingLayer(page)).toBe(chosen);
});

///The 3D view chooses nothing, so a selection made in 2D must not go on marking rows beside it.
test('the 3D view marks no rows', async ({ page }) => {
    await chooseFirstShape(page);

    await expect(page.locator('.layerRowChosen')).toHaveCount(1);

    await selectView(page, 'View3D');

    await expect(page.locator('.layerRow')).not.toHaveCount(0);
    await expect(page.locator('.layerRowChosen')).toHaveCount(0);
});
