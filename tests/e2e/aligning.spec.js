//Lining up and spacing out what is chosen.
//
//The arithmetic is covered in AligningTests, where boxes can be written down directly: equal gaps, ends that
//do not move, an order that is not the order they sit in, a middle that falls between two units.
//
//What is only checkable here is which button means which edge. This view draws the layout's Y downwards
//where the format counts it upwards, so the edge that looks like the top is the least Y - and nothing but a
//browser can say whether the word on the button matches the side of the screen things move to.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, shapesAndLabels } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCell(page);
});

///Into a cell, since only what the current cell holds may be changed.
async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Catches everything the cell holds, with a band from one corner of the view to the other.
async function chooseEverything(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + 5, view.y + 5);
    await page.mouse.down();
    await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
    await page.mouse.up();

    await expect(page.locator('#selectionPanel')).toContainText('shapes');
}

///
///Everything the cell holds, as boxes in the layout's own coordinates.
///
///**Labels count.** A label is a thing in the cell with a position, so lining one up with the shapes around
///it is a real thing to want - and it takes part in spacing out as a box with no width. Reading only the
///polygons made this test disagree with the app about which shape was outermost, and it looked like the ends
///had moved when what had actually happened was that a label was one of them.
///
async function boxes(page) {
    return (await shapesAndLabels(page, 'inContext')).map(shape => {
        const numbers = shape.points.flat();
        const xs = numbers.filter((_, at) => at % 2 === 0);
        const ys = numbers.filter((_, at) => at % 2 === 1);

        return {
            left: Math.min(...xs),
            right: Math.max(...xs),
            top: Math.min(...ys),
            bottom: Math.max(...ys)
        };
    });
}

///Waits for the shapes to have actually moved, since the redraw is a round trip through C#.
async function settled(page, before) {
    await expect.poll(async () => JSON.stringify(await boxes(page)), { timeout: 15000 })
        .not.toBe(JSON.stringify(before));

    return boxes(page);
}

test.describe('the buttons', () => {
    ///Neither means anything about one shape, and the panel for one stays as short as it was.
    test('are not offered for a single shape', async ({ page }) => {
        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#alignLeft')).toHaveCount(0);
        await expect(page.locator('#spaceAcross')).toHaveCount(0);
    });

    test('and are offered once there is more than one', async ({ page }) => {
        await chooseEverything(page);

        await expect(page.locator('#alignLeft')).toBeVisible();
        await expect(page.locator('#alignCenter')).toBeVisible();
        await expect(page.locator('#alignRight')).toBeVisible();
        await expect(page.locator('#alignTop')).toBeVisible();
        await expect(page.locator('#alignMiddle')).toBeVisible();
        await expect(page.locator('#alignBottom')).toBeVisible();
        await expect(page.locator('#spaceAcross')).toBeVisible();
    });
});

test.describe('lining up', () => {
    ///
    ///**The word on the button is the side of the screen things move to.**
    ///
    ///The one thing the library cannot answer, because it names its edges by which coordinate is being made
    ///equal and stays out of the argument about which way is up. Top is the *least* Y in this view, which is
    ///what a layout format would call the bottom.
    ///
    for (const [button, edge, pick] of [
        ['alignLeft', 'left', boxes => Math.min(...boxes.map(box => box.left))],
        ['alignRight', 'right', boxes => Math.max(...boxes.map(box => box.right))],
        ['alignTop', 'top', boxes => Math.min(...boxes.map(box => box.top))],
        ['alignBottom', 'bottom', boxes => Math.max(...boxes.map(box => box.bottom))]
    ]) {
        test(`${button} puts every ${edge} edge on the outermost one`, async ({ page }) => {
            await chooseEverything(page);

            const before = await boxes(page);

            expect(before.length).toBeGreaterThan(1);

            const wanted = pick(before);

            await page.locator(`#${button}`).click();

            const after = await settled(page, before);

            for (const box of after)
                expect(box[edge]).toBe(wanted);
        });
    }

    test('center puts them all on one vertical line', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#alignCenter').click();

        const after = await settled(page, before);

        //Doubled, because a box of odd width has no whole-numbered middle of its own.
        const middles = [...new Set(after.map(box => box.left + box.right))];

        expect(middles).toHaveLength(1);
    });

    test('middle puts them all on one horizontal line', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#alignMiddle').click();

        const after = await settled(page, before);

        expect([...new Set(after.map(box => box.top + box.bottom))]).toHaveLength(1);
    });

    ///Lining up moves nothing sideways, or the button would be doing two things at once.
    test('lining up left leaves every shape at the height it was', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#alignLeft').click();

        const after = await settled(page, before);

        expect(after.map(box => box.top).sort()).toEqual(before.map(box => box.top).sort());
    });
});

test.describe('spacing out', () => {
    ///Doubled, because a shape of odd width has no whole-numbered middle of its own.
    const middlesAcross = shapes => shapes.map(box => box.left + box.right).sort((one, other) => one - other);
    const middlesDown = shapes => shapes.map(box => box.top + box.bottom).sort((one, other) => one - other);

    ///
    ///**Evenly spaced middles, and the two on the ends stay put.**
    ///
    ///Mosfet.gds is a real cell, so its shapes overlap each other the way chip geometry does - which is the
    ///case that decided the convention. Spacing the *edges* has no free space to divide here and would fling
    ///the middle shapes outside the group; spacing the middles cannot.
    ///
    for (const [button, middlesOf] of [['spaceAcross', middlesAcross], ['spaceDown', middlesDown]]) {
        test(`${button} evens the middles and leaves the outermost two where they were`, async ({ page }) => {
            await chooseEverything(page);

            const before = await boxes(page);

            expect(before.length).toBeGreaterThan(2);

            const was = middlesOf(before);

            await page.locator(`#${button}`).click();

            const after = await settled(page, before);
            const now = middlesOf(after);

            //The outermost two are what the spacing was measured between.
            expect(now[0]).toBe(was[0]);
            expect(now[now.length - 1]).toBe(was[was.length - 1]);

            const steps = [];

            for (let i = 1; i < now.length; i++)
                steps.push(now[i] - now[i - 1]);

            expect(Math.max(...steps) - Math.min(...steps)).toBeLessThanOrEqual(2);
        });
    }

    ///Nothing is flung outside the group, which is the whole reason for spacing middles rather than edges.
    test('nothing ends up outside where the group already reached', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#spaceAcross').click();

        const after = await settled(page, before);

        const was = middlesAcross(before);

        for (const middle of middlesAcross(after)) {
            expect(middle).toBeGreaterThanOrEqual(was[0]);
            expect(middle).toBeLessThanOrEqual(was[was.length - 1]);
        }
    });
});

test.describe('afterwards', () => {
    test('it is one step on the undo stack, named for the button', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#alignLeft').click();

        await settled(page, before);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Line up left/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => JSON.stringify(await boxes(page)), { timeout: 15000 })
            .toBe(JSON.stringify(before));

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    ///
    ///**Pressing it twice does nothing the second time.**
    ///
    ///Which is also what stops a row of moves-by-nothing going onto the undo stack: the shapes are already
    ///where they are being asked to go, so there is no edit to make.
    ///
    test('lining up something already in line adds nothing to undo', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);

        await page.locator('#alignLeft').click();

        await settled(page, before);

        const after = await boxes(page);

        await page.locator('#alignLeft').click();

        await page.waitForTimeout(1200);

        expect(await boxes(page)).toEqual(after);

        //One step, not two: the second press found nothing to do.
        await page.locator('#undoEdit').click();

        await expect.poll(async () => JSON.stringify(await boxes(page)), { timeout: 15000 })
            .toBe(JSON.stringify(before));

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    ///The selection stays, so a row can be lined up and then spaced out without choosing it again.
    test('what was chosen is still chosen', async ({ page }) => {
        await chooseEverything(page);

        const before = await boxes(page);
        const marked = await page.locator('#gdsSVG .shapeSelected').count();

        await page.locator('#alignTop').click();

        await settled(page, before);

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(marked);
        await expect(page.locator('#spaceAcross')).toBeVisible();
    });
});
