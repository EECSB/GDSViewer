//Moving chosen shapes onto another layer.
//
//That the two numbers are written into the records the element already has - a label by its TEXTTYPE and a
//boundary by its DATATYPE, geometry and place untouched, undo byte for byte - is covered in LayoutEditTests.
//
//What is only checkable here is the picker: that it says which layer they are on, that it says nothing when
//they are not all on one, and that choosing from it moves them and shows it.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, elementFill, shapesDrawn, allFills, layersListed, CLEAR_OF_PANEL, chosenLayer, chooseLayer, layersOffered, dismissSelection, otherShapeClearOfPanel, chooseShape, openedOnItsOwn } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCell(page);
});

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///What every shape in the cell is drawn in, which is the only thing on screen that says which layer it is on.
async function colors(page) {
    return allFills(page, 'inContext').then(fills => fills.join(','));
}

///Picks one shape the cell holds, and says which one it was and what color it is drawn.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        if (await page.locator('#chosenLayer').count() === 0)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        if (await elementPoints(page, index) !== null)
            return index;
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

///
///Two shapes on two known layers, drawn rather than hunted for.
///
///Mosfet.gds has shapes on nine pairs, but finding two of them that are on different layers *and* far
///enough apart that a click lands on the one aimed at is a search whose answer depends on the file. Drawing
///them puts both the layers and the positions beyond doubt.
///
async function twoOnDifferentLayers(page) {
    await page.locator('#drawTool').click();
    await chooseShape(page, '#rectangleShape');

    //Picked from the sidebar, which is where the draw layer is chosen now - the toolbar's dropdown has
    //gone. See draw-layer.spec.
    const layers = await layersListed(page);

    const view = await page.locator('#gdsSVG').boundingBox();

    const drag = async (x1, y1, x2, y2) => {
        const was = await shapeCount(page);

        await page.mouse.move(view.x + x1, view.y + y1);
        await page.mouse.down();
        await page.mouse.move(view.x + x2, view.y + y2, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(was + 1);
    };

    //
    //To the right of the selection panel, which the first of the two clicks below opens over the top-left
    //of the view - and which takes its own clicks, so the second would land on it rather than on the shape.
    //
    await page.locator('.layerRow').nth(0).locator('.layerName').click();
    await drag(CLEAR_OF_PANEL + 20, 120, CLEAR_OF_PANEL + 100, 200);

    await page.locator('.layerRow').nth(1).locator('.layerName').click();
    await drag(CLEAR_OF_PANEL + 160, 120, CLEAR_OF_PANEL + 240, 200);

    await page.locator('#selectTool').click();

    //Well apart, so neither click can land on the other one.
    await page.mouse.click(view.x + CLEAR_OF_PANEL + 60, view.y + 160);
    await page.keyboard.down('Control');
    await page.mouse.click(view.x + CLEAR_OF_PANEL + 200, view.y + 160);
    await page.keyboard.up('Control');

    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(2);

    return layers;
}

test.describe('the picker', () => {
    test('says which layer one chosen shape is on', async ({ page }) => {
        await chooseAShape(page);

        //The panel's heading names the same layer the picker is showing.
        //
        //The picker *is* the heading now. It sat at the bottom of the panel with the layer named again at
        //the top, which was the same fact twice - and the copy you could act on was the one out of sight.
        //
        const showing = await chosenLayer(page);

        expect(showing).toMatch(/^\d+\/\d+$/);

        //Named on the button, with the pair in it whether or not the layer has a name of its own.
        await expect(page.locator('#chosenLayer')).toContainText(showing);

        //And a square of that layer's color beside it, which is why this is not a select.
        await expect(page.locator('#chosenLayer .layerSwatch')).toHaveCount(1);
    });

    ///Rather than naming whichever happened to be first, which would read as a fact and be a guess.
    test('and says nothing when they are not all on one', async ({ page }) => {
        await twoOnDifferentLayers(page);

        expect(await chosenLayer(page)).toBe('');
    });

    test('offers the layers the file has', async ({ page }) => {
        await chooseAShape(page);

        const offered = await layersOffered(page);

        //Mosfet.gds draws nine layer and datatype pairs.
        expect(offered.length).toBe(9);
        expect(offered).toContain('93/44');
    });

    ///Every row carries its layer's color, which is the whole reason this is not a select.
    test('every layer offered is shown with its color', async ({ page }) => {
        await chooseAShape(page);

        await page.locator('#chosenLayer').click();

        await expect(page.locator('#chosenLayerList')).toBeVisible();

        const swatches = await page.locator('.layerPickerOption .layerSwatch').evaluateAll(nodes =>
            nodes.map(node => getComputedStyle(node).backgroundColor));

        expect(swatches.length).toBe(9);

        //Nine layers drawn in nine colors, rather than nine squares of the same one.
        expect(new Set(swatches).size).toBeGreaterThan(1);
        expect(swatches.every(color => color !== 'rgba(0, 0, 0, 0)')).toBe(true);
    });
});

test.describe('moving one', () => {
    test('choosing a layer moves the shape onto it', async ({ page }) => {
        const index = await chooseAShape(page);

        const before = await elementFill(page, index);

        await chooseLayer(page, '93/44');

        //Drawn in the new layer's color, which is the only thing on screen that says which layer it is on.
        await expect.poll(async () =>
            elementFill(page, index), { timeout: 15000 })
            .not.toBe(before);

        expect(await chosenLayer(page)).toBe('93/44');
    });

    ///Nothing was added or removed, so the shape keeps its place and stays chosen.
    test('the shape stays chosen', async ({ page }) => {
        await chooseAShape(page);

        await chooseLayer(page, '93/44');

        await expect.poll(async () => chosenLayer(page), { timeout: 15000 })
            .toBe('93/44');

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });

    test('and its corners do not move', async ({ page }) => {
        const index = await chooseAShape(page);

        const before = await elementPoints(page, index);

        await chooseLayer(page, '93/44');

        await expect.poll(async () => chosenLayer(page), { timeout: 15000 })
            .toBe('93/44');

        expect(await elementPoints(page, index))
            .toBe(before);
    });
});

test.describe('moving several', () => {
    test('two on different layers both end up on the one chosen', async ({ page }) => {
        await twoOnDifferentLayers(page);

        await chooseLayer(page, '93/44');

        //Undecided before, agreed afterwards.
        await expect.poll(async () => chosenLayer(page), { timeout: 15000 })
            .toBe('93/44');

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(2);
    });

    ///
    ///One step for the pair, and undoing it puts each of them back on its own layer rather than both on one.
    ///
    ///Read off every shape in the cell rather than off the chosen ones: undoing lets go of the selection,
    ///because an undo can be of anything and the ones that add or remove a shape move every index after it.
    ///
    test('as one step on the undo stack', async ({ page }) => {
        await twoOnDifferentLayers(page);

        const before = await colors(page);

        await chooseLayer(page, '93/44');

        await expect.poll(async () => chosenLayer(page), { timeout: 15000 })
            .toBe('93/44');

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Change layer 2 shapes/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => colors(page), { timeout: 15000 }).toBe(before);
    });
});

test.describe('afterwards', () => {
    test('the change is in the file that is downloaded', async ({ page }) => {
        const index = await chooseAShape(page);

        const before = await elementPoints(page, index);

        await chooseLayer(page, '93/44');

        await expect.poll(async () => chosenLayer(page), { timeout: 15000 })
            .toBe('93/44');

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        //The same corners, now on 93/44 - found by choosing it again and reading the picker.
        await page.locator('#selectTool').click();

        const moved = (await shapesDrawn(page)).findIndex(shape =>
            shape.points.map(point => point.join(',')).join(' ') === before);

        expect(moved).toBeGreaterThanOrEqual(0);

        const box = await shapeBox(page, moved);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        //Read off the picker, which is where a single shape's layer is named now.
        await expect.poll(async () => chosenLayer(page), { timeout: 15000 }).toBe('93/44');
    });
});

///
///The list closes when the selection moves out from under it.
///
///It names the layer of whatever was chosen when it opened, so left open over a new selection it is a list
///of somewhere else - and the next row clicked would move shapes the person had stopped looking at.
///
///
///Another *shape*, not bare ground.
///
///Clearing the selection takes the whole panel away and the list with it, so a test that did that would
///pass whether or not anything closed the list - it did, until this was written the other way round.
///Choosing a second shape leaves the picker on screen, which is the only way to see the list shut.
///
test('the layer list closes when something else is chosen', async ({ page }) => {
    await chooseAShape(page);

    await page.locator('#chosenLayer').click();

    await expect(page.locator('#chosenLayerList')).toBeVisible();

    const other = await otherShapeClearOfPanel(page, 'inContext');

    expect(other, 'every shape is behind the panel').not.toBeNull();

    await page.mouse.click(other.x, other.y);

    //Still a picker, and no longer a list hanging off it.
    await expect(page.locator('#chosenLayer')).toBeVisible();
    await expect(page.locator('#chosenLayerList')).toHaveCount(0);
});
