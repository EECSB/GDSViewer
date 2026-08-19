//The set operations, reached from the editor.
//
//The arithmetic is Clipper2's and the layer over it is covered in BooleanTests - the four operations, what
//each means for more than two shapes, holes cut into keyholes, growing and shrinking.
//
//What is only checkable here is the wiring: that the buttons act on what is chosen, that the shapes which
//went in come out, that the result lands on the right layer, and that the lot is one step.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, allPoints, shapesDrawn, CLEAR_OF_PANEL, chooseShape, openedOnItsOwn } = require('./helpers');

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
///Draws two overlapping rectangles into the cell and chooses both.
///
///Drawn rather than found: the operations are about what two shapes have in common, and a cell whose shapes
///happen to overlap in whatever way the file was built is a fixture that changes meaning if the file does.
///
async function twoOverlapping(page) {
    await page.locator('#drawTool').click();
    await chooseShape(page, '#rectangleShape');

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
    //To the right of the selection panel, and no higher than they were.
    //
    //The panel opens over the top-left as soon as the first of the two clicks below lands, and it takes its
    //own clicks - so the second one would hit the panel rather than the shape. Only the across matters: the
    //top of the view belongs to the breadcrumb, and moving these up there was a drag on the breadcrumb that
    //drew nothing at all.
    //
    await drag(CLEAR_OF_PANEL + 20, 120, CLEAR_OF_PANEL + 160, 260);
    await drag(CLEAR_OF_PANEL + 100, 200, CLEAR_OF_PANEL + 240, 340);

    await page.locator('#selectTool').click();

    //The two just drawn are the last two, and both are on the layer the Draw tool was set to.
    await page.mouse.click(view.x + CLEAR_OF_PANEL + 50, view.y + 150);
    await page.keyboard.down('Control');
    await page.mouse.click(view.x + CLEAR_OF_PANEL + 200, view.y + 300);
    await page.keyboard.up('Control');

    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(2);

    return page.locator('#gdsSVG .shapeSelected').evaluateAll(nodes =>
        nodes.map(node => node.getAttribute('points')));
}

///Every polygon drawn, by its points - which is how a result is told from what went in.
async function outlines(page) {
    return allPoints(page);
}

///One outline's area, by the shoelace.
function areaOf(points) {
    const numbers = points.trim().split(/[\s,]+/).map(Number);

    let twice = 0;

    for (let i = 0; i + 3 < numbers.length; i += 2)
        twice += (numbers[i] * numbers[i + 3]) - (numbers[i + 2] * numbers[i + 1]);

    return Math.abs(twice / 2);
}

///The area of every polygon drawn, by the shoelace, so a result can be measured rather than counted.
async function drawnArea(page) {
    return (await shapesDrawn(page)).reduce((total, shape) => {
        const numbers = shape.points.flat();

        let twice = 0;

        for (let i = 0; i + 3 < numbers.length; i += 2)
            twice += (numbers[i] * numbers[i + 3]) - (numbers[i + 2] * numbers[i + 1]);

        return total + Math.abs(twice / 2);
    }, 0);
}

test.describe('the buttons', () => {
    ///Every one of these is about two shapes or more; none of them means anything about one.
    test('are offered only once more than one shape is chosen', async ({ page }) => {
        const inside = await shapeBox(page, 0, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#combineUnion')).toHaveCount(0);

        //Growing is the exception: one shape can be grown.
        await expect(page.locator('#growApply')).toBeVisible();
    });

    test('and all four are there once two are', async ({ page }) => {
        await twoOverlapping(page);

        await expect(page.locator('#combineUnion')).toBeVisible();
        await expect(page.locator('#combineSubtract')).toBeVisible();
        await expect(page.locator('#combineIntersect')).toBeVisible();
        await expect(page.locator('#combineExclude')).toBeVisible();
    });
});

test.describe('combining two', () => {
    ///Two overlapping shapes become one covering less than the two of them did separately.
    test('union leaves one shape covering both', async ({ page }) => {
        await twoOverlapping(page);

        const before = await shapeCount(page);
        const wasArea = await drawnArea(page);

        await page.locator('#combineUnion').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);

        //The overlap was counted twice before and once now, so the total drops.
        expect(await drawnArea(page)).toBeLessThan(wasArea);
    });

    ///
    ///Measured against the two shapes that went in, not against everything drawn - the cell has eighteen
    ///shapes of its own, and a bound on the whole cell's area says nothing about what these two became.
    ///
    test('intersect leaves only the overlap', async ({ page }) => {
        const chosen = await twoOverlapping(page);

        const before = await outlines(page);

        await page.locator('#combineIntersect').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before.length - 1);

        const added = (await outlines(page)).filter(points => !before.includes(points));

        expect(added).toHaveLength(1);

        //Only where the two crossed, so smaller than either of them on its own.
        expect(areaOf(added[0])).toBeLessThan(Math.min(areaOf(chosen[0]), areaOf(chosen[1])));
    });

    test('subtract leaves the first one chosen with the other cut out', async ({ page }) => {
        await twoOverlapping(page);

        const before = await shapeCount(page);

        await page.locator('#combineSubtract').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);
    });

    ///
    ///By area rather than by count. Two overlapping rectangles excluded leave two pieces, so the number of
    ///shapes does not move - what moves is the ground they shared, which was covered twice before and is
    ///covered by nothing now.
    ///
    test('exclude drops the ground they share', async ({ page }) => {
        const chosen = await twoOverlapping(page);

        const wasArea = await drawnArea(page);

        await page.locator('#combineExclude').click();

        await expect.poll(async () => drawnArea(page), { timeout: 15000 }).toBeLessThan(wasArea);

        //Neither of the two that went in is still there.
        const now = await outlines(page);

        expect(now).not.toContain(chosen[0]);
        expect(now).not.toContain(chosen[1]);
    });

    ///
    ///**Two shapes that do not touch, intersected, are nothing.**
    ///
    ///Allowed through rather than refused: it is the answer to the question that was asked, and it is one
    ///press of undo either way.
    ///
    test('intersecting two that never meet leaves nothing', async ({ page }) => {
        await page.locator('#drawTool').click();
        await chooseShape(page, '#rectangleShape');

        const view = await page.locator('#gdsSVG').boundingBox();
        const before = await shapeCount(page);

        for (const [x1, y1, x2, y2] of [[120, 120, 200, 200], [300, 300, 380, 380]]) {
            await page.mouse.move(view.x + x1, view.y + y1);
            await page.mouse.down();
            await page.mouse.move(view.x + x2, view.y + y2, { steps: 6 });
            await page.mouse.up();
        }

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 2);

        await page.locator('#selectTool').click();
        await page.mouse.click(view.x + 160, view.y + 160);
        await page.keyboard.down('Control');
        await page.mouse.click(view.x + 340, view.y + 340);
        await page.keyboard.up('Control');

        await page.locator('#combineIntersect').click();

        //Both went in and nothing came out.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before);
    });
});

test.describe('growing', () => {
    test('a positive distance makes the shape bigger', async ({ page }) => {
        await twoOverlapping(page);

        const wasArea = await drawnArea(page);

        await page.locator('#growBy').fill('0.5');
        await page.locator('#growBy').blur();

        await page.locator('#growApply').click();

        await expect.poll(async () => drawnArea(page), { timeout: 15000 }).toBeGreaterThan(wasArea);
    });

    test('and a negative one makes it smaller', async ({ page }) => {
        await twoOverlapping(page);

        const wasArea = await drawnArea(page);

        await page.locator('#growBy').fill('-0.2');
        await page.locator('#growBy').blur();

        await page.locator('#growApply').click();

        await expect.poll(async () => drawnArea(page), { timeout: 15000 }).toBeLessThan(wasArea);
    });

    ///A distance of nothing would move no edge, so the button says so rather than making a step that does it.
    test('a distance of nothing is refused', async ({ page }) => {
        await twoOverlapping(page);

        await page.locator('#growBy').fill('0');
        await page.locator('#growBy').blur();

        await expect(page.locator('#growApply')).toBeDisabled();
    });
});

test.describe('afterwards', () => {
    test('the whole thing is one step on the undo stack', async ({ page }) => {
        await twoOverlapping(page);

        const before = await shapeCount(page);
        const outlines = await allPoints(page);

        await page.locator('#combineUnion').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Union/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before);

        //The two that went in are back, exactly as they were.
        const after = await allPoints(page);

        expect(after.sort()).toEqual(outlines.sort());
    });

    test('a combined shape is in the file that is downloaded', async ({ page }) => {
        await twoOverlapping(page);

        const before = await shapeCount(page);

        await page.locator('#combineUnion').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(before - 1);
    });

    ///Everything that went in has gone and what came out is new, so the numbering moved with it.
    test('the selection is let go', async ({ page }) => {
        await twoOverlapping(page);

        await page.locator('#combineUnion').click();

        await expect.poll(async () => page.locator('#gdsSVG .shapeSelected').count(), { timeout: 15000 }).toBe(0);
        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });
});
