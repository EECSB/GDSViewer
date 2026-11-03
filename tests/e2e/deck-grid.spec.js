//The rule deck's grid under the app's own drawing: the corners a tool works out - an ellipse's, a rounded
//corner's, the squares of writing, a mark's curves, a path's outline - go on the grid the deck's off-grid rules
//ask for, while snapping is on.
//
//A bundled example arrives with the sky130 deck, whose grid.1 asks for every corner on 5 nm: five of Mosfet's
//units. The rounding is C#'s and covered in GridTests. What is here is that each tool reaches it, that snapping
//is the switch, and that the grid is the deck's rather than the view's - each shown against the same gesture
//without it, so a pass cannot be corners that happened to land on the grid.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, allPoints, snapToGrid, chooseShape, openShapeSettings, setShapeSetting, closeShapeSettings, useDrawingLayer, bareNumbers, MOSFET } = require('./helpers');

///The deck's grid on Mosfet: sky130's grid.1 is 5 nm, and Mosfet's unit is a nanometer.
const STEP = 5;

///How many corners of an outline written "x,y x,y ..." are off the deck's grid.
function offGrid(points) {
    return points.trim().split(/\s+/)
        .map(pair => pair.split(',').map(Number))
        .filter(([x, y]) => x % STEP !== 0 || y % STEP !== 0)
        .length;
}

///Every element the drawing shows, by index - one node each, or a layer's path listing its elements.
async function elementIndices(page) {
    return page.evaluate(() => {
        const svg = document.getElementById('gdsSVG');
        const indices = [];

        for (const node of svg.querySelectorAll(':scope > [data-element]'))
            indices.push(node.getAttribute('data-element'));

        for (const path of svg.querySelectorAll(':scope > path[data-elements]'))
            indices.push(...path.getAttribute('data-elements').trim().split(/\s+/));

        return indices;
    });
}

///The one element that was not there before, by index.
async function newIndex(page, was) {
    await expect.poll(async () => (await elementIndices(page)).length, { timeout: 15000 }).toBe(was.length + 1);

    return (await elementIndices(page)).find(index => !was.includes(index));
}

///The corners of the one element that was not there before.
async function addedSince(page, was) {
    return elementPoints(page, await newIndex(page, was));
}

///How many corners of what is in hand are off the grid. Its own drawing is in database units, its middle at the origin.
async function carriedOffGrid(page) {
    return page.evaluate((step) => {
        const path = document.querySelector('#carriedCell path');

        if (path == null)
            return null;

        //Its corners, read back from the steps the drawing is written in.
        const numbers = window.viewGeometry.subpaths(path.getAttribute('d')).flatMap(run => run.corners);
        let off = 0;

        for (let i = 0; i + 1 < numbers.length; i += 2) {
            if (numbers[i] % step !== 0 || numbers[i + 1] % step !== 0)
                off++;
        }

        return off;
    }, STEP);
}


async function dragBox(page, from, to) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + from[0], view.y + from[1]);
    await page.mouse.down();
    await page.mouse.move(view.x + to[0], view.y + to[1], { steps: 8 });
    await page.mouse.up();
}

///One rectangle of the cell chosen on its own, as corners.spec chooses one. Returns its index.
async function chooseARectangle(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        if (await page.locator('#roundCorners').count() === 0)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');
        const points = await elementPoints(page, index);

        if (points !== null && points.trim().split(/\s+/).length <= 5)
            return index;
    }

    throw new Error('no rectangle of this cell could be chosen on its own');
}

///How many corners of everything drawn are off the deck's grid. Mosfet is drawn on it, so any is something made here.
async function offGridAnywhere(page) {
    return (await allPoints(page)).reduce((off, points) => off + offGrid(points), 0);
}

///Everything drawn, as one string - to wait on, so what is read after an edit is the edit's and not the picture before it.
async function drawing(page) {
    return JSON.stringify(await allPoints(page));
}

async function changedFrom(page, was) {
    await expect.poll(async () => drawing(page), { timeout: 15000 }).not.toBe(was);
}

///
///An ellipse, and a rectangle over its right side, drawn on met1 to the right of the selection panel - where a
///rectangle's edge crosses the ellipse's, a union works a corner out.
///
async function ellipseAndRectangle(page) {
    await chooseShape(page, '#ellipseShape');

    let was = await shapeCount(page);

    await dragBox(page, [400, 120], [600, 300]);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 1);

    await chooseShape(page, '#rectangleShape');

    was = await shapeCount(page);

    await dragBox(page, [540, 160], [700, 330]);

    return was;
}

///Both chosen: the ellipse left of the rectangle, and the rectangle below the ellipse.
async function chooseBoth(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.locator('#selectTool').click();
    await page.mouse.click(view.x + 440, view.y + 210);
    await page.keyboard.down('Control');
    await page.mouse.click(view.x + 660, view.y + 315);
    await page.keyboard.up('Control');

    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(2);
}

///Drawing on met1 from here, which the deck declares - grid.1 is over every layer it declares.
async function drawOnMet1(page) {
    const names = await page.locator('.layerRow .layerName').allTextContents();
    const at = names.findIndex(name => name.includes('(68/20)'));

    expect(at).toBeGreaterThanOrEqual(0);
    expect(await useDrawingLayer(page, at)).toBe('68/20');
}

///A run on a slant through points of the view, and a point on it to choose it by.
const SLANTED = [[150, 150], [330, 290]];
const ON_SLANTED = [240, 220];

///A run square to the axes, turning once, and a point on it.
const SQUARE = [[150, 200], [420, 200], [420, 330]];
const ON_SQUARE = [285, 200];

///
///A run out and nearly straight back, which at a width of 0.3 µm comes back over itself near the turn - and a point
///on it near the start, where only the first leg covers it. Where the two legs lie over each other the outline goes
///round twice, which is a hole to a click as it is to an even-odd fill.
///
const BACK_OVER = [[420, 150], [760, 200], [430, 250]];
const ON_BACK_OVER = [440, 153];

///The path in hand, at a width in microns - with none it would be a hairline, which has no outline to put anywhere.
async function usePath(page, microns) {
    await chooseShape(page, '#pathShape');
    await setShapeSetting(page, '#pathShape', '#pathWidth', microns);
}

///Clicks a run through points of the view, ending it by clicking the last one again.
async function clickARun(page, points) {
    const view = await page.locator('#gdsSVG').boundingBox();

    for (const [x, y] of points)
        await page.mouse.click(view.x + x, view.y + y);

    const [x, y] = points[points.length - 1];

    await page.mouse.click(view.x + x, view.y + y);
}

///
///Whether the element at that index is a path, chosen with Select at a point of the view on it: the panel offers a
///width for a path and for nothing else. It leaves Select in hand and the element chosen.
///
async function isAPath(page, at, index) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.locator('#selectTool').click();
    await page.mouse.click(view.x + at[0], view.y + at[1]);

    await expect(page.locator('#gdsSVG .shapeSelected').first()).toHaveAttribute('data-element', index);
    await expect(page.locator('#selectionPanel')).toBeVisible();

    return await page.locator('#pathRow').count() > 0;
}

///Takes back the last drawing and puts the pencil back in hand, Path still the shape and the width as it was.
async function undoAndDrawAgain(page, was) {
    await page.locator('#undoEdit').click();

    await expect.poll(async () => (await elementIndices(page)).length, { timeout: 15000 }).toBe(was.length);

    await page.locator('#drawTool').click();
    await chooseShape(page, '#pathShape');
}

//With the page's margins, which the shapes drawn here were placed in by pixel - see gotoExample.
test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg', false, false);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    //The deck a bundled example arrives with, which comes after the file and so is waited for.
    await page.locator('#sidebarPanelRules').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

    await page.locator('#sidebarPanelLayers').click();

    await snapToGrid(page, true);

    //Into the cell and out with the pencil - a row is marked as the one drawn on only while it is out.
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#drawTool')).toBeEnabled({ timeout: 30000 });

    await page.locator('#drawTool').click();

    await drawOnMet1(page);
});

test('an ellipse\'s corners go on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    await chooseShape(page, '#ellipseShape');

    let was = await elementIndices(page);

    await dragBox(page, [140, 140], [360, 280]);

    expect(offGrid(await addedSince(page, was))).toBe(0);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => (await elementIndices(page)).length, { timeout: 15000 }).toBe(was.length);

    await snapToGrid(page, false);

    was = await elementIndices(page);

    await dragBox(page, [140, 140], [360, 280]);

    expect(offGrid(await addedSince(page, was))).toBeGreaterThan(0);
});

///
///The grid is the deck's: with the deck gone the ellipse is on the database unit again, snapping or not. Gone with a
///PDK of no rules put in use - see bareNumbers - since Clear went from the rules panel (2026-10-19).
///
test('with no deck there is no grid to go on', async ({ page }) => {
    await bareNumbers(page);

    await page.locator('#sidebarPanelRules').click();

    await expect(page.locator('#drcRun')).toHaveCount(0);

    await page.locator('#sidebarPanelLayers').click();

    await chooseShape(page, '#ellipseShape');

    const was = await elementIndices(page);

    await dragBox(page, [140, 140], [360, 280]);

    expect(offGrid(await addedSince(page, was))).toBeGreaterThan(0);
});

test('rounded corners go on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    await page.locator('#selectTool').click();

    const index = await chooseARectangle(page);

    await page.locator('#cornerBy').fill('0.1');
    await page.locator('#cornerBy').blur();

    await page.locator('#roundCorners').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Round corners/);

    //Rounded, and every corner of the arcs on the grid.
    await expect.poll(async () => (await elementPoints(page, index)).trim().split(/\s+/).length, { timeout: 15000 }).toBeGreaterThan(5);

    expect(offGrid(await elementPoints(page, index))).toBe(0);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => (await elementPoints(page, index)).trim().split(/\s+/).length, { timeout: 15000 }).toBeLessThanOrEqual(5);

    await snapToGrid(page, false);

    //Chosen again, since the undo let go of it - and the same rectangle, as nothing else in the cell moved.
    expect(await chooseARectangle(page)).toBe(index);

    await page.locator('#roundCorners').click();

    await expect.poll(async () => offGrid(await elementPoints(page, index)), { timeout: 15000 }).toBeGreaterThan(0);
});

///
///A point of the drawing, on screen, whose place in the layout is off the deck's grid across - for writing put down
///without snapping to start somewhere the grid is not. Looked for along a line low in the view.
///
async function pointOffTheGrid(page) {
    return page.evaluate((step) => {
        const svg = document.getElementById('gdsSVG');
        const box = svg.getBoundingClientRect();
        const toLayout = svg.getScreenCTM().inverse();
        const point = svg.createSVGPoint();

        point.y = box.top + (box.height * 0.75);

        for (let x = box.left + (box.width * 0.25); x < box.left + (box.width * 0.5); x++) {
            point.x = x;

            if (Math.round(point.matrixTransform(toLayout).x) % step !== 0)
                return { x: x, y: point.y };
        }

        return null;
    }, STEP);
}

///
///**Writing is a whole number of the deck's grid steps a stroke, from a corner on the grid**, so every corner of it
///lands there - put down with the Text row, and sized again by its frame's corner (2026-10-19, when the height box
///went). Without snapping it starts where it is clicked, and clicked off the grid it is off it.
///
test('writing put down and sized by its frame is on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    await chooseShape(page, '#textShape');

    const view = await page.locator('#gdsSVG').boundingBox();
    let was = await drawing(page);

    await page.mouse.click(view.x + (view.width * 0.3), view.y + (view.height * 0.75));

    await expect(page.locator('#canvasWritingText')).toBeFocused();

    await page.keyboard.press('Enter');

    await changedFrom(page, was);

    expect(await offGridAnywhere(page)).toBe(0);

    //Sized by its corner, to a width nothing chose.
    const corner = await page.locator('#pieceFrame .pieceHandle').boundingBox();
    const width = Number(await page.locator('#pieceFrame .pieceOutline').getAttribute('width'));

    was = await drawing(page);

    await page.mouse.move(corner.x + (corner.width / 2), corner.y + (corner.height / 2));
    await page.mouse.down();
    await page.mouse.move(corner.x + 70, corner.y + 23, { steps: 6 });
    await page.mouse.up();

    await changedFrom(page, was);

    expect(Number(await page.locator('#pieceFrame .pieceOutline').getAttribute('width'))).not.toBe(width);
    expect(await offGridAnywhere(page)).toBe(0);

    //Without snapping, clicked where the layout is off the grid.
    await page.keyboard.press('Escape');

    await snapToGrid(page, false);

    const off = await pointOffTheGrid(page);

    expect(off).not.toBeNull();

    was = await drawing(page);

    await page.mouse.click(off.x, off.y);

    await expect(page.locator('#canvasWritingText')).toBeFocused();

    await page.keyboard.press('Enter');

    await changedFrom(page, was);

    expect(await offGridAnywhere(page)).toBeGreaterThan(0);
});

///
///Where the rectangle's edges cross the ellipse's, the union puts a corner, and at the nearest database unit it is
///off the grid both of them are on. Without snapping, the same union leaves it there.
///
test('a union\'s corners go on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    expect(await offGridAnywhere(page)).toBe(0);

    const before = await ellipseAndRectangle(page);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);

    await chooseBoth(page);
    await page.locator('#combineUnion').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Union/);
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);

    expect(await offGridAnywhere(page)).toBe(0);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);

    await snapToGrid(page, false);

    await chooseBoth(page);
    await page.locator('#combineUnion').click();

    await expect.poll(async () => offGridAnywhere(page), { timeout: 15000 }).toBeGreaterThan(0);
});

///A shape drawn with Merge on is joined by a union too, so its corners go on the grid the same way.
test('a shape drawn joined goes on the deck\'s grid with what it joins', async ({ page }) => {
    await chooseShape(page, '#ellipseShape');

    const was = await shapeCount(page);

    await dragBox(page, [400, 120], [600, 300]);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 1);

    await page.locator('#joinToggle').click();
    await chooseShape(page, '#rectangleShape');

    const ellipse = await drawing(page);

    await dragBox(page, [540, 160], [700, 330]);

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw joined/);
    await changedFrom(page, ellipse);

    //Joined rather than added beside: no more shapes than the ellipse made, and fewer where the join reaches one
    //of the cell's own met1 shapes as well.
    expect(await shapeCount(page)).toBeLessThanOrEqual(was + 1);
    expect(await offGridAnywhere(page)).toBe(0);
});

///And one dragged onto another with Merge on, which the move joins by a union the same way.
test('a shape dragged onto another with Merge on goes on the deck\'s grid with it', async ({ page }) => {
    await chooseShape(page, '#ellipseShape');

    const was = await shapeCount(page);

    await dragBox(page, [400, 120], [600, 300]);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 1);

    //Clear of the ellipse, to be dragged onto it.
    await chooseShape(page, '#rectangleShape');
    await dragBox(page, [640, 160], [720, 330]);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 2);

    await page.locator('#moveTool').click();
    await page.locator('#joinToggle').click();

    const view = await page.locator('#gdsSVG').boundingBox();
    const at = { x: view.x + 690, y: view.y + 315 };

    await page.mouse.click(at.x, at.y);

    await expect(page.locator('#gdsSVG .shapeSelected')).not.toHaveCount(0);

    const apart = await drawing(page);

    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x - 100, at.y, { steps: 6 });
    await page.mouse.up();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move joined/);
    await changedFrom(page, apart);

    expect(await offGridAnywhere(page)).toBe(0);
});

///
///A grow moves every edge, so every corner it makes is worked out - here by a distance off the grid, 102 nm, which
///the deck's grid takes to the nearest step and the database unit leaves where it is.
///
test('growing puts every corner on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    await page.locator('#selectTool').click();

    await chooseARectangle(page);

    await page.locator('#growBy').fill('0.102');
    await page.locator('#growBy').blur();

    const was = await drawing(page);

    await page.locator('#growApply').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Grow/);
    await changedFrom(page, was);

    expect(await offGridAnywhere(page)).toBe(0);

    await page.locator('#undoEdit').click();

    await expect(page.locator('#undoEdit')).not.toHaveAttribute('title', /Undo Grow/);

    await snapToGrid(page, false);

    await chooseARectangle(page);
    await page.locator('#growApply').click();

    await expect.poll(async () => offGridAnywhere(page), { timeout: 15000 }).toBeGreaterThan(0);
});

test('a ring resonator\'s curves go on the deck\'s grid while snapping, and not without it', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    await page.locator('#markKind').selectOption('ring');
    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toBeVisible();

    expect(await carriedOffGrid(page)).toBe(0);

    await page.keyboard.press('Escape');

    await expect(page.locator('#carryingStamp')).toHaveCount(0);

    await snapToGrid(page, false);

    await openShapeSettings(page, '#markShape');
    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toBeVisible();

    expect(await carriedOffGrid(page)).toBeGreaterThan(0);
});

///
///A path is checked by its outline, half the width either side of every corner - and along a slanted run that is
///offset on a normal that is not square to the grid, so it lands between grid points wherever the clicks did. While
///snapping it is drawn as that outline on the grid, a polygon, and the line over the drawing says so; without
///snapping the same clicks are a path, and its outline is off the grid.
///
test('a slanted path is drawn as its outline on the deck\'s grid while snapping, and as a path without it', async ({ page }) => {
    await usePath(page, 0.5);

    let was = await elementIndices(page);

    await clickARun(page, SLANTED);

    let index = await newIndex(page, was);

    expect(offGrid(await elementPoints(page, index))).toBe(0);

    await expect(page.locator('#drawRefusal')).toContainText('Drawn as a polygon on the deck\'s 0.005 µm grid (grid.1): along its slanted runs');
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Draw path/);

    expect(await isAPath(page, ON_SLANTED, index)).toBe(false);

    await snapToGrid(page, false);
    await undoAndDrawAgain(page, was);

    await clickARun(page, SLANTED);

    index = await newIndex(page, was);

    expect(offGrid(await elementPoints(page, index))).toBeGreaterThan(0);

    //A new gesture, so the line about the last one is gone.
    await expect(page.locator('#drawRefusal')).toHaveCount(0);

    expect(await isAPath(page, ON_SLANTED, index)).toBe(true);
});

///
///Square to the axes, on clicks on the grid and a width of whole steps, a path's outline is on the grid too and it
///stays a path. Round ends are half circles, worked out, so the same run with them is drawn as its outline.
///
test('a square path stays a path, and with round ends it is drawn as its outline', async ({ page }) => {
    await usePath(page, 0.5);

    const was = await elementIndices(page);

    await clickARun(page, SQUARE);

    let index = await newIndex(page, was);

    expect(offGrid(await elementPoints(page, index))).toBe(0);
    await expect(page.locator('#drawRefusal')).toHaveCount(0);

    expect(await isAPath(page, ON_SQUARE, index)).toBe(true);

    await undoAndDrawAgain(page, was);

    await openShapeSettings(page, '#pathShape');
    await page.locator('#pathEnds').selectOption('Round');
    await page.locator('#pathEnds').blur();
    await closeShapeSettings(page);

    await clickARun(page, SQUARE);

    index = await newIndex(page, was);

    expect(offGrid(await elementPoints(page, index))).toBe(0);
    await expect(page.locator('#drawRefusal')).toContainText('along its round ends');

    expect(await isAPath(page, ON_SQUARE, index)).toBe(false);
});

///
///A step wide, a slanted run's outline is within half a step of its centerline all along, and on the grid it is a
///line. So it is refused, with why and what to do - and turning snapping off, one of the two, draws it.
///
test('a path too narrow for the deck\'s grid is refused, and is drawn with snapping off', async ({ page }) => {
    await usePath(page, 0.005);

    const was = await elementIndices(page);

    await clickARun(page, SLANTED);

    await expect(page.locator('#drawRefusal')).toContainText('Too narrow for the deck\'s 0.005 µm grid (grid.1)');
    await expect(page.locator('#drawRefusal')).toContainText('turn snapping off');

    expect(await elementIndices(page)).toEqual(was);

    await snapToGrid(page, false);
    await chooseShape(page, '#pathShape');

    await clickARun(page, SLANTED);

    await newIndex(page, was);

    await expect(page.locator('#drawRefusal')).toHaveCount(0);
});

///
///A run that comes back within its own width of itself has an outline that crosses itself, and no one polygon is
///that - a path is the only thing that draws it. So it stays a path, and the line says why its outline is off the grid.
///
test('a path that comes back over itself stays a path, and the line says why', async ({ page }) => {
    await usePath(page, 0.3);

    const was = await elementIndices(page);

    await clickARun(page, BACK_OVER);

    const index = await newIndex(page, was);

    await expect(page.locator('#drawRefusal')).toContainText('Kept a path: it comes back over itself');
    await expect(page.locator('#drawRefusal')).toContainText('along its slanted runs');

    expect(await isAPath(page, ON_BACK_OVER, index)).toBe(true);
});
