//The Move tool: the same picking as Select, with nothing that can catch a drag.
//
//What moving a shape does to the file is covered in LayoutEditTests, and that a drag reaches it at all is
//covered by editing.spec.js. What is only checkable here is the difference between the two tools, and it is
//a difference in what the pointer lands on: a chosen shape wears a handle on every corner, those handles
//are tested before the shape they belong to, and on a small shape there is barely anywhere left to take
//hold of it. Aiming to move a shape and pulling a corner out of it instead is a slip that looks like the
//tool working, which is why it is worth a spec of its own.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, shapePoints, elementPoints, shapesMarked, allPoints, chosenShapeClearOfPanel } = require('./helpers');

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

///
///Picks a shape of the cell and hands back its number and its corners.
///
///Chosen with whichever tool is in hand, since both choose the same way - that they do is one of the things
///worth asserting.
///
async function chooseAShape(page) {
    const held = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < held; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        const index = Number(await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element'));

        if (await elementPoints(page, index) !== null)
            return { index, corners: await elementPoints(page, index), box: await shapeBox(page, nth, 'inContext') };
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

///How many distinct corners a shape has, which is what says whether it was moved or reshaped.
function cornersOf(points) {
    return new Set(points.trim().split(/\s+/)).size;
}

///The width and height of what those corners cover.
function sizeOf(points) {
    const numbers = points.trim().split(/[\s,]+/).map(Number);
    const xs = numbers.filter((_, at) => at % 2 === 0);
    const ys = numbers.filter((_, at) => at % 2 === 1);

    return { wide: Math.max(...xs) - Math.min(...xs), tall: Math.max(...ys) - Math.min(...ys) };
}

test.describe('the tool', () => {
    test('is offered beside Select', async ({ page }) => {
        await expect(page.locator('#moveTool')).toBeVisible();
    });

    test('only one of the two is on at a time', async ({ page }) => {
        await page.locator('#moveTool').click();

        await expect(page.locator('#moveTool')).toHaveClass(/toolButtonOn/);
        await expect(page.locator('#selectTool')).not.toHaveClass(/toolButtonOn/);

        await page.locator('#selectTool').click();

        await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);
        await expect(page.locator('#moveTool')).not.toHaveClass(/toolButtonOn/);
    });

    test('v reaches it from the keyboard', async ({ page }) => {
        await page.keyboard.press('v');

        await expect(page.locator('#moveTool')).toHaveClass(/toolButtonOn/);
    });

    ///Choosing is unchanged - the panel and everything on it are the same tool's worth of work.
    test('a shape can still be chosen with it', async ({ page }) => {
        await page.locator('#moveTool').click();

        await chooseAShape(page);

        await expect(page.locator('#selectionPanel')).toBeVisible();
    });
});

test.describe('the handles', () => {
    test('are up under Select, so a corner can be pulled', async ({ page }) => {
        await page.locator('#selectTool').click();
        await chooseAShape(page);

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
            .toBeGreaterThan(0);
    });

    test('and are not, under Move', async ({ page }) => {
        await page.locator('#moveTool').click();
        await chooseAShape(page);

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('.vertexHandle')).toHaveCount(0);
    });

    ///Switching between the two puts them up and takes them away, rather than leaving the last tool's.
    test('go away when Move is chosen with a shape already held', async ({ page }) => {
        await page.locator('#selectTool').click();
        await chooseAShape(page);

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
            .toBeGreaterThan(0);

        await page.locator('#moveTool').click();

        await expect(page.locator('.vertexHandle')).toHaveCount(0);
    });
});

///
///Chooses a shape under Move, and says where to take hold of it near one of its corners.
///
///**Just inside the corner rather than exactly on it.** A handle is a disc a few pixels across, so the
///corner region is what this is about - but the corner *point* is shared ground: it is on the boundary of
///whatever else meets there, and a click on it can land on a neighbor. That would be testing the aim
///rather than the tools. A few pixels in is unambiguously this shape, and still well inside where the
///handle sat.
///
async function grabNearACorner(page) {
    await page.locator('#moveTool').click();

    const chosen = await chooseAShape(page);

    await expect(page.locator('.vertexHandle')).toHaveCount(0);

    const nth = (await shapesMarked(page, 'inContext')).findIndex(shape => shape.element === chosen.index);
    const box = await shapeBox(page, nth, 'inContext');

    //Capped against the shape's own size, since a thin one has no room for a fixed inset.
    const inset = {
        x: Math.min(8, box.width / 4),
        y: Math.min(8, box.height / 4)
    };

    return {
        index: chosen.index,
        corners: chosen.corners,
        at: { x: box.x + inset.x, y: box.y + inset.y }
    };
}

async function dragFrom(page, at) {
    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x + 70, at.y + 70, { steps: 6 });
    await page.mouse.up();
}

test.describe('dragging a corner', () => {
    ///
    ///**The whole point.** The same gesture, in the same place, on the same shape: under Select it takes the
    ///corner and the shape changes shape; under Move it takes the shape and the shape keeps its own.
    ///
    ///Measured as the corner count and the size rather than as the coordinates, because a move changes every
    ///coordinate too - what tells the two apart is that a move keeps the shape congruent and a corner drag
    ///does not.
    ///
    test('reshapes it under Select', async ({ page }) => {
        await page.locator('#selectTool').click();

        const chosen = await chooseAShape(page);

        await expect.poll(async () => page.locator('.vertexHandle').count(), { timeout: 15000 })
            .toBeGreaterThan(0);

        const before = sizeOf(chosen.corners);

        const handle = await page.locator('.vertexHandle').first().boundingBox();

        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2));
        await page.mouse.down();
        await page.mouse.move(handle.x + (handle.width / 2) + 70, handle.y + (handle.height / 2) + 70, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => elementPoints(page, chosen.index), { timeout: 15000 })
            .not.toBe(chosen.corners);

        //One corner went somewhere the others did not, so the shape is a different shape.
        const after = sizeOf(await elementPoints(page, chosen.index));

        expect(after.wide === before.wide && after.tall === before.tall).toBe(false);
    });

    test('moves the whole shape under Move', async ({ page }) => {
        const chosen = await grabNearACorner(page);

        const before = sizeOf(chosen.corners);
        const corners = cornersOf(chosen.corners);

        await dragFrom(page, chosen.at);

        await expect.poll(async () => elementPoints(page, chosen.index), { timeout: 15000 })
            .not.toBe(chosen.corners);

        const moved = await elementPoints(page, chosen.index);

        //Same shape, somewhere else: as many corners as it had, covering the same ground.
        expect(cornersOf(moved)).toBe(corners);
        expect(sizeOf(moved)).toEqual(before);
    });

    ///And it is one step on the undo stack, the same as a drag under Select.
    test('and it can be taken back', async ({ page }) => {
        const chosen = await grabNearACorner(page);

        await dragFrom(page, chosen.at);

        await expect.poll(async () => elementPoints(page, chosen.index), { timeout: 15000 })
            .not.toBe(chosen.corners);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => elementPoints(page, chosen.index), { timeout: 15000 })
            .toBe(chosen.corners);
    });
});

///The rubber band belongs to the picking rather than to the handles, so it works under either.
test('a band still catches several shapes', async ({ page }) => {
    await page.locator('#moveTool').click();

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 5, view.y + 5);
    await page.mouse.down();
    await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
    await page.mouse.up();

    await expect(page.locator('#selectionPanel')).toContainText('shapes');
    expect((await shapesMarked(page, 'inContext')).length).toBeGreaterThan(1);
});

///
///**And a drag takes every one of them.** Choosing several says the next gesture is about all of them, and
///a move is the gesture that says so most plainly: take hold of one of the chosen and the rest come.
///
///Nothing checked this, and it did not work. The press that begins a drag waits for the release before it
///reports itself - a second click on a chosen shape descends into its cell, and reporting the press at once
///would descend halfway through taking hold of a placement. That wait was written for a selection of one
///and asked `chosen.length === 1`, so pressing on one of several was reported immediately and *replaced*
///the selection with the shape under the pointer before the drag had begun. Everything downstream was
///already right - the preview lifts the whole set, and the edit writes one move per distinct model - which
///is what made it invisible: they were being handed a set of one. See onSelectClick in JavaScriptInterOp.
///
///Both tools, because the wait belongs to the picking and neither tool has its own copy of it.
///
for (const tool of ['#selectTool', '#moveTool']) {
    test(`dragging one of several moves them all, under ${tool.slice(1)}`, async ({ page }) => {
        await page.locator(tool).click();

        //
        //**Caught with a band rather than clicked one at a time.** A shape's middle is not always inside it,
        //and this cell's are stacked - so a modifier-click aimed at a second shape can land on bare canvas,
        //which clears the selection rather than growing it. The band takes whatever it crosses and says
        //nothing about aim, which is also how somebody choosing several actually does it.
        //
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();

        const chosen = page.locator('#gdsSVG .shapeSelected');

        await expect.poll(async () => chosen.count(), { timeout: 15000 }).toBeGreaterThan(1);

        const held = await chosen.count();
        const before = await chosen.evaluateAll(nodes => nodes.map(node => node.getAttribute('points')));

        //
        //A point with a chosen shape actually under it, asked after the whole selection is made: the panel
        //grows with what is in it, so somewhere clear of it with one shape chosen can be behind it with
        //several.
        //
        const grab = await chosenShapeClearOfPanel(page, 'inContext');

        expect(grab, 'every chosen shape is behind the panel').not.toBeNull();

        //Sideways only, so anything that failed to come along still has a coordinate in common with where
        //it was and shows up plainly as left behind.
        await page.mouse.move(grab.x, grab.y);
        await page.mouse.down();
        await page.mouse.move(grab.x + 70, grab.y, { steps: 6 });
        await page.mouse.up();

        //
        //Not one of them still where it was, asked of the whole picture and as a set: an edit is free to
        //hand the shapes back in another order, and the question is not which went where but whether any of
        //them stayed behind. One left standing is the whole bug.
        //
        await expect.poll(async () => {
            const now = new Set(await allPoints(page));

            return before.filter(shape => now.has(shape)).length;
        }, { timeout: 30000 }).toBe(0);

        //And all of them still chosen, so the group outlives the move that was made with it.
        await expect(chosen).toHaveCount(held);
    });
}
