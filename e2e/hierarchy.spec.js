//Editing the hierarchy: making a cell out of a selection, and flattening one back.
//
//That a cell is well formed, that a placement draws its cell turned, that undo is byte for byte and that a
//cycle is refused are covered in HierarchyTests, where the library can be read directly.
//
//What is only checkable here is the loop: group, flatten, and back to where it started - which is the claim
//that hierarchy is editable rather than only readable. Placing by hand is checked below and again in
//instance.spec: this file has the cell whose origin is nowhere near its shapes, which is the case that can
//tell being carried by the middle from being carried by the origin.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, allPoints, openedOnItsOwn } = require('./helpers');

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

///Everything the cell holds, as outlines - which is what has to come back unchanged after a round trip.
async function outlines(page) {
    return allPoints(page).then(points => points.sort().join('|'));
}

///Catches everything with a band from one corner of the view to the other.
async function chooseEverything(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 5, view.y + 5);
    await page.mouse.down();
    await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
    await page.mouse.up();

    await expect(page.locator('#selectionPanel')).toContainText('shapes');
}

///Picks out one shape the cell holds.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#makeCell').count() === 1)
            return;
    }

    throw new Error('no shape of this cell could be chosen');
}

test.describe('making a cell', () => {
    test('the name starts at one nothing is using', async ({ page }) => {
        await chooseAShape(page);

        await expect(page.locator('#cellName')).toHaveValue('CELL');
        await expect(page.locator('#makeCell')).toBeEnabled();
    });

    test('a name already taken is refused, and says so', async ({ page }) => {
        await chooseAShape(page);

        //The cell being edited, whatever this file calls it - which is not the file's own name.
        const taken = (await page.locator('.contextCrumbOn').textContent()).trim();

        await page.locator('#cellName').fill(taken);

        await expect(page.locator('#makeCell')).toBeDisabled();
        await expect(page.locator('#makeCell')).toHaveAttribute('title', new RegExp(`already has a cell called ${taken}`));
    });

    test('an empty name is refused', async ({ page }) => {
        await chooseAShape(page);

        await page.locator('#cellName').fill('');

        await expect(page.locator('#makeCell')).toBeDisabled();
    });

    ///
    ///**The picture does not move.**
    ///
    ///The shapes go into a cell keeping the coordinates they had, and the instance goes at the origin - so
    ///what is drawn is exactly what was drawn. Re-basing the contents would read better inside the new cell
    ///and would move every shape on screen by a rounding.
    ///
    test('grouping leaves the layout looking the same', async ({ page }) => {
        await chooseEverything(page);

        const before = await outlines(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        expect(await outlines(page)).toBe(before);
    });

    ///The shapes are now reached through an instance rather than sitting in the cell directly.
    test('and the shapes are reached through the new cell', async ({ page }) => {
        await chooseEverything(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        //The chain now runs through CELL.
        await expect(page.locator('#selectionPanel')).toContainText('CELL');
    });

    test('it is one step, and undoing it puts everything back', async ({ page }) => {
        await chooseEverything(page);

        const before = await outlines(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        await page.locator('#undoEdit').click();

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });
});

test.describe('flattening one', () => {
    ///
    ///**Group, then flatten, and the file draws what it drew.**
    ///
    ///The loop that says hierarchy is editable rather than only readable: down into a cell and back out
    ///again, with the picture unchanged at both ends.
    ///
    test('flattening an instance puts its shapes back where they were drawn', async ({ page }) => {
        await chooseEverything(page);

        const before = await outlines(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        //Choose a shape reached through the new instance; the button appears where the chain is shown.
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#flattenInstance')).toBeVisible();

        await page.locator('#flattenInstance').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Flatten/, { timeout: 15000 });

        //Same picture, and the instance is gone.
        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);
    });

    test('and it is one step that undoes', async ({ page }) => {
        await chooseEverything(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        const grouped = await outlines(page);

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await page.locator('#flattenInstance').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Flatten/, { timeout: 15000 });

        await page.locator('#undoEdit').click();

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(grouped);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/);
    });
});

test.describe('afterwards', () => {
    test('a made cell is in the file that is downloaded', async ({ page }) => {
        await chooseEverything(page);

        const before = await outlines(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        //The same picture, out of a file that now has a cell in it that it did not have before.
        expect(await outlines(page)).toBe(before);
    });
});

///
///Placing a cell by hand: picked up out of the tree, carried, put down where the pointer is.
///
///Against a cell made here rather than against the placed.gds fixture, and that is the point of doing it
///twice. A cell's origin is wherever the file says; grouping shapes into one keeps the coordinates they
///already had - so CELL, made out of a layout drawn a couple of thousand units from the origin, has an
///origin nowhere near anything in it. The fixture's cell sits on its own origin, so the two anchors
///coincide there and the test that matters most cannot fail.
///
test.describe('placing one', () => {
    ///The tree is where a cell is picked up from, and these open on it shut - see gotoExample.
    async function openTree(page) {
        if (await page.locator('#cellTree').count() === 0)
            await page.locator('#cellTreeButton').click();

        await expect(page.locator('#cellTree')).toBeVisible();
    }

    ///Makes a cell out of everything, so there is something to place. Returns how many shapes were drawn.
    async function withACellToPlace(page) {
        await chooseEverything(page);

        const drawn = await shapeCount(page);

        await page.locator('#makeCell').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });

        return drawn;
    }

    ///The square that picks a named cell up, which only exists for a cell that could go in this one.
    function placeButton(page, name) {
        return page.locator('.cellRowPlace[data-place="' + name + '"]');
    }

    ///Puts what is in hand down on the right of the view, clear of both panels.
    async function dropIt(page) {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width * 0.75), view.y + (view.height * 0.3));
        await page.mouse.down();
        await page.mouse.up();
    }

    ///
    ///**Nothing to place, nothing offered.**
    ///
    ///A file with one cell in it offers nothing, because the only cell there is the one being edited and a
    ///cell cannot be placed inside itself. A control that is refused the moment it is pressed is worse than
    ///no control at all.
    ///
    test('no cell offers to be placed while the only one is the one being edited', async ({ page }) => {
        await openTree(page);

        await expect(page.locator('.cellRowPlace')).toHaveCount(0);
    });

    ///
    ///**A cell that already contains this one is not offered.**
    ///
    ///Placing one would make a hierarchy with no bottom, which the format cannot refuse and no reader can
    ///finish. Leaving the square off that row is better than letting it be pressed and then saying no.
    ///
    test('a cell that would contain itself is not offered', async ({ page }) => {
        await withACellToPlace(page);

        const inside = (await page.locator('.contextCrumbOn').textContent()).trim();

        await openTree(page);

        await expect(placeButton(page, 'CELL')).toHaveCount(1);
        await expect(placeButton(page, inside)).toHaveCount(0);
    });

    ///
    ///Picked up, carried, and put down where the pointer is.
    ///
    ///The whole of what replaced the picker: the cell follows the cursor, and the click that ends it is what
    ///says where the instance goes - which is the part the old button could not do at all, since it dropped
    ///everything at the middle of the view.
    ///
    test('placing draws the cell a second time, where it was put down', async ({ page }) => {
        const drawn = await withACellToPlace(page);

        await openTree(page);
        await placeButton(page, 'CELL').click();

        //In hand, and the bar says so.
        await expect(page.locator('#carryingCell')).toBeVisible();

        await dropIt(page);

        //Everything in CELL is drawn again, through the second instance.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(drawn * 2);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place cell/);
    });

    test('and undoing takes the instance away again', async ({ page }) => {
        const drawn = await withACellToPlace(page);

        await openTree(page);
        await placeButton(page, 'CELL').click();

        await dropIt(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(drawn * 2);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(drawn);
    });

    ///
    ///A carried cell sits under the pointer, not at the cell's own origin.
    ///
    ///**This is the fixture that can tell the difference**, for the reason in the header above.
    ///
    ///Measured on the carried group rather than on what lands. The nearest *placed* shape to the pointer is
    ///not a distinguishing measurement at all: the layout the cell was made from is still drawn underneath,
    ///so something is near the pointer wherever the new instance went. This asks where the thing being
    ///carried actually is, which is the complaint.
    ///
    test('a carried cell sits under the pointer, not at its own origin', async ({ page }) => {
        await withACellToPlace(page);

        await openTree(page);
        await placeButton(page, 'CELL').click();

        const view = await page.locator('#gdsSVG').boundingBox();
        const at = { x: view.x + (view.width * 0.75), y: view.y + (view.height * 0.35) };

        await page.mouse.move(at.x, at.y);

        const box = await page.locator('#carriedCell').boundingBox();

        const offBy = Math.round(Math.hypot((box.x + (box.width / 2)) - at.x, (box.y + (box.height / 2)) - at.y));

        //Held by its middle, so the middle is where the pointer is.
        expect(offBy).toBeLessThan(40);
    });

    ///Escape puts it back, which is the way out of carrying something you did not mean to pick up.
    test('escape puts a carried cell back', async ({ page }) => {
        const drawn = await withACellToPlace(page);

        await openTree(page);
        await placeButton(page, 'CELL').click();

        await expect(page.locator('#carryingCell')).toBeVisible();

        await page.keyboard.press('Escape');

        await expect(page.locator('#carryingCell')).toHaveCount(0);

        //And nothing was placed on the way out.
        expect(await shapeCount(page)).toBe(drawn);
    });
});
