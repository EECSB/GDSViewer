//Turning and mirroring what is chosen.
//
//That the arithmetic is exact - four quarters returning the file byte for byte, a turn through a placed and
//mirrored cell going the way it was asked to - is covered in TurningTests, where a cell can be built at any
//angle and the bytes compared.
//
//What is only checkable here is which way it looks on screen. This view draws the layout's Y downwards where
//the format counts it upwards, so the button and the arithmetic disagree about what "right" means by exactly
//one reflection - and nothing but a browser can say whether the arrow matches what happens.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, allPoints, openedOnItsOwn } = require('./helpers');

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

///
///Picks out one shape the cell holds, and hands back which one it is along with its corners.
///
///**By index, not by position in the list.** A click lands on whichever shape is drawn on top at that point,
///which in a layout of overlapping rectangles is often not the one whose middle was aimed at - so what was
///actually chosen is read back off the highlight rather than assumed. The index survives a turn, because
///turning changes no element's place in the file.
///
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        //Only what the current cell holds can be turned, and only a polygon has corners to compare.
        if (await page.locator('#turnRight').count() === 0)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        if (await elementPoints(page, index) !== null)
            return { index: index, corners: await cornersAt(page, index) };
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

async function cornersAt(page, index) {
    const points = await elementPoints(page, index);

    return points.trim().split(/\s+/).map(pair => {
        const [x, y] = pair.split(',').map(Number);

        return { x: x, y: y };
    });
}

function boxOf(corners) {
    const xs = corners.map(corner => corner.x);
    const ys = corners.map(corner => corner.y);

    return {
        left: Math.min(...xs),
        right: Math.max(...xs),
        top: Math.min(...ys),
        bottom: Math.max(...ys),
        width: Math.max(...xs) - Math.min(...xs),
        height: Math.max(...ys) - Math.min(...ys)
    };
}

///Waits for the shape to have actually changed, since the redraw is a round trip through C#.
async function turnedInto(page, index, before) {
    await expect.poll(async () => JSON.stringify(await cornersAt(page, index)), { timeout: 15000 })
        .not.toBe(JSON.stringify(before));

    return cornersAt(page, index);
}

test.describe('the buttons', () => {
    test('are offered only for what the cell holds', async ({ page }) => {
        await chooseAShape(page);

        await expect(page.locator('#turnLeft')).toBeVisible();
        await expect(page.locator('#turnRight')).toBeVisible();
        await expect(page.locator('#mirrorAcross')).toBeVisible();
        await expect(page.locator('#mirrorDown')).toBeVisible();
    });

    ///
    ///Nothing chosen, nothing to turn.
    ///
    ///Escape first, because entering a cell now leaves the shape that took you there chosen - which is the
    ///whole point of clicking to edit, and it means "nothing is chosen" has to be arranged rather than
    ///assumed.
    ///
    test('and not when nothing is chosen', async ({ page }) => {
        await page.keyboard.press('Escape');

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
        await expect(page.locator('#turnRight')).toHaveCount(0);
    });
});

test.describe('turning', () => {
    ///
    ///**A corner to the right of the middle ends up below it.**
    ///
    ///Which is what "turn right" means to somebody looking at the screen, and the exact thing the format's
    ///upward Y would get backwards. The corner is followed rather than the box, because a box tells you the
    ///shape turned and not which way.
    ///
    test('right takes the rightmost corner to the bottom', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;
        const box = boxOf(before);

        const middleX = (box.left + box.right) / 2;
        const middleY = (box.top + box.bottom) / 2;

        const rightmost = before.reduce((far, corner) => {
            if (corner.x > far.x)
                return corner;

            return far;
        });

        await page.locator('#turnRight').click();

        const after = await turnedInto(page, chosen.index, before);

        //Where it should have gone: right of middle becomes below middle.
        const wantedX = middleX - (rightmost.y - middleY);
        const wantedY = middleY + (rightmost.x - middleX);

        expect(after.some(corner =>
            Math.abs(corner.x - wantedX) <= 1 && Math.abs(corner.y - wantedY) <= 1)).toBe(true);
    });

    test('and left takes it to the top', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;
        const box = boxOf(before);

        const middleX = (box.left + box.right) / 2;
        const middleY = (box.top + box.bottom) / 2;

        const rightmost = before.reduce((far, corner) => {
            if (corner.x > far.x)
                return corner;

            return far;
        });

        await page.locator('#turnLeft').click();

        const after = await turnedInto(page, chosen.index, before);

        const wantedX = middleX + (rightmost.y - middleY);
        const wantedY = middleY - (rightmost.x - middleX);

        expect(after.some(corner =>
            Math.abs(corner.x - wantedX) <= 1 && Math.abs(corner.y - wantedY) <= 1)).toBe(true);
    });

    test('a quarter turn swaps how wide it is for how tall', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;
        const was = boxOf(before);

        await page.locator('#turnRight').click();

        const now = boxOf(await turnedInto(page, chosen.index, before));

        expect(Math.abs(now.width - was.height)).toBeLessThanOrEqual(1);
        expect(Math.abs(now.height - was.width)).toBeLessThanOrEqual(1);
    });

    ///Left then right is nothing, which is the smallest statement that the two are each other's opposite.
    test('left then right leaves the shape where it was', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;

        await page.locator('#turnLeft').click();

        await turnedInto(page, chosen.index, before);

        await page.locator('#turnRight').click();

        await expect.poll(async () => JSON.stringify(await cornersAt(page, chosen.index)), { timeout: 15000 })
            .toBe(JSON.stringify(before));
    });
});

test.describe('mirroring', () => {
    test('across leaves the box where it was and turns the shape over', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;
        const was = boxOf(before);

        await page.locator('#mirrorAcross').click();

        const after = await turnedInto(page, chosen.index, before);
        const now = boxOf(after);

        //Same ground covered, different shape on it.
        expect(now).toEqual(was);
        expect(JSON.stringify(after)).not.toBe(JSON.stringify(before));
    });

    test('and doing it twice puts it back', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;

        await page.locator('#mirrorDown').click();

        await turnedInto(page, chosen.index, before);

        await page.locator('#mirrorDown').click();

        await expect.poll(async () => JSON.stringify(await cornersAt(page, chosen.index)), { timeout: 15000 })
            .toBe(JSON.stringify(before));
    });
});

test.describe('afterwards', () => {
    test('it is one step on the undo stack, named for the button', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;

        await page.locator('#mirrorAcross').click();

        await turnedInto(page, chosen.index, before);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Mirror across/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => JSON.stringify(await cornersAt(page, chosen.index)), { timeout: 15000 })
            .toBe(JSON.stringify(before));
    });

    ///
    ///**Several shapes turn about one point, not each about its own.**
    ///
    ///Turning each in place would leave the arrangement exactly as it was and only spin the pieces, which is
    ///not what anybody means by turning a selection.
    ///
    test('a band of shapes turns about the middle of all of them', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + view.height - 5, { steps: 10 });
        await page.mouse.up();

        await expect(page.locator('#selectionPanel')).toContainText('shapes');

        const before = await allPoints(page, 'inContext');

        await page.locator('#turnRight').click();

        await expect.poll(async () => {
            const now = await allPoints(page, 'inContext');

            return JSON.stringify(now);
        }, { timeout: 15000 }).not.toBe(JSON.stringify(before));

        //One step for the lot, or it is not one gesture.
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Turn right \d+ shapes/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => {
            const now = await allPoints(page, 'inContext');

            return JSON.stringify(now);
        }, { timeout: 15000 }).toBe(JSON.stringify(before));

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });

    test('a turn is in the file that is downloaded', async ({ page }) => {
        const chosen = await chooseAShape(page);
        const before = chosen.corners;

        await page.locator('#turnRight').click();

        const after = await turnedInto(page, chosen.index, before);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        const drawn = await allPoints(page);

        expect(drawn).toContain(after.map(corner => `${corner.x},${corner.y}`).join(' '));
    });
});
