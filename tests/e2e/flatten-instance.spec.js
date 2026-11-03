//Flattening an instance that is more than a plain one: turned, mirrored and magnified, holding a placement
//of its own - or an array.
//
//What the copied records say is covered in FlatteningTests, where the library can be read directly. What is
//only checkable here is that the button reaches it: the placement a click came through, found and replaced,
//and the picture the same afterwards.
//
//e2e/fixtures/nested.gds: TOP holds a square of its own, MID placed mirrored, magnified twice and turned a
//quarter, and LEAF as an array of three columns by two rows. MID holds a square, a placement of LEAF and a
//path, so flattening it has a placement to turn, a width to magnify and a shape to do neither to.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, shapesMarked, openedOnItsOwn, uploadFile } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);

    await uploadFile(page, 'e2e/fixtures/nested.gds');

    await openedOnItsOwn(page);

    //TOP's square, MID's three and the array's six.
    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(10);

    await page.locator('#selectTool').click();

    await editTop(page);
});

///
///Where everything is drawn, as one string.
///
///**Each shape's corners sorted, not only the shapes.** A path is outlined from its centerline wherever it is
///drawn, so the same rectangle comes back starting at another corner once the path has been put through a
///mirror - and that is the same picture.
///
async function outlines(page) {
    const shapes = await shapesMarked(page);

    return shapes.map(shape => shape.points.map(point => point.join(',')).sort().join(' ')).sort().join(' | ');
}

///What the selection panel says about the shape last clicked.
async function said(page) {
    return (await page.locator('#selectionPanel').textContent()) ?? '';
}

///Opens TOP for editing, by way of the one shape it holds itself - the only one whose chain names nothing else.
async function editTop(page) {
    const many = await shapeCount(page);

    for (let nth = 0; nth < many; nth++) {
        const box = await shapeBox(page, nth);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const panel = await said(page);

        if (panel.includes('MID') || panel.includes('LEAF'))
            continue;

        await expect.poll(
            async () => (await page.locator('.contextCrumbOn').textContent()).trim(),
            { timeout: 15000 }).toBe('TOP');

        return;
    }

    throw new Error('no shape of TOP\'s own was found');
}

///
///Clicks shapes in the layout's order until one is reached through exactly `chain`, passing over the first
///`skip` of them.
///
///Exactly, because "TOP > MID" is also how "TOP > MID > LEAF" starts.
///
async function clickChain(page, chain, skip = 0) {
    const many = await shapeCount(page);
    let passed = 0;

    for (let nth = 0; nth < many; nth++) {
        const box = await shapeBox(page, nth);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const panel = await said(page);

        if (!panel.includes(chain) || panel.includes(chain + ' >'))
            continue;

        if (passed === skip)
            return;

        passed++;
    }

    throw new Error(`no shape was reached through ${chain}`);
}

async function flatten(page) {
    await expect(page.locator('#flattenInstance')).toBeVisible();

    await page.locator('#flattenInstance').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Flatten/, { timeout: 15000 });
}

test.describe('flattening an instance', () => {
    ///
    ///**The placement inside it turns with it.**
    ///
    ///Moving every coordinate through the instance is enough for a shape, and not for a placement: its
    ///point lands in the right place and the cell it places is drawn the way it was before it was moved -
    ///unturned, unmirrored, at its own size. A path's width is a length as well as a point, so it doubles.
    ///
    test('one turned, mirrored and magnified draws the same afterwards', async ({ page }) => {
        const before = await outlines(page);

        await clickChain(page, 'TOP > MID');

        await flatten(page);

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);
    });

    test('and undoing it puts the instance back', async ({ page }) => {
        const before = await outlines(page);

        await clickChain(page, 'TOP > MID');

        await flatten(page);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);

        await expect(page.locator('#undoEdit')).toBeDisabled();
    });
});

test.describe('flattening an array', () => {
    ///
    ///**Every place in it, from whichever place was clicked.**
    ///
    ///An array is one placement however many places it has, so flattening it puts all of them into the cell.
    ///The one clicked here is the second, because the first is the one whose point the array is written at -
    ///so it is the one a lookup by that point finds whether or not it can find the others.
    ///
    test('from a place that is not its first, draws the same afterwards', async ({ page }) => {
        const before = await outlines(page);

        await clickChain(page, 'TOP > LEAF', 1);

        await flatten(page);

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);
    });

    test('from its first place, every place stays', async ({ page }) => {
        const before = await outlines(page);

        await clickChain(page, 'TOP > LEAF');

        await flatten(page);

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);
    });
});

///
///The other things a placement can have done to it, on one that is an array.
///
///**An array is one placement**, which every one of these used to forget: turning one kept a single place of
///it, and the Array button wrote a second grid into it and stopped the app on reading it back.
///
test.describe('acting on an array placement', () => {
    test('a place of one is chosen as the whole array, and says so', async ({ page }) => {
        await clickChain(page, 'TOP > LEAF', 1);

        await expect(page.locator('#instanceNote')).toContainText('array of LEAF');
    });

    test('turning one keeps every place in it', async ({ page }) => {
        await clickChain(page, 'TOP > LEAF', 1);

        await page.locator('#turnRight').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Turn/, { timeout: 15000 });

        //Six places of LEAF, as before - turned about the place that was clicked, all of them.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    test('and undoing the turn puts it back where it was', async ({ page }) => {
        const before = await outlines(page);

        await clickChain(page, 'TOP > LEAF', 1);

        await page.locator('#turnRight').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Turn/, { timeout: 15000 });

        await expect.poll(async () => outlines(page), { timeout: 15000 }).not.toBe(before);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => outlines(page), { timeout: 15000 }).toBe(before);
    });

    test('Array is not offered on one, since the format has no array of arrays', async ({ page }) => {
        await clickChain(page, 'TOP > LEAF');

        await expect(page.locator('#instanceNote')).toBeVisible();
        await expect(page.locator('#arrayOpen')).toHaveCount(0);
    });

    test('nor on a placement inside a placed cell, which is not the cell being edited', async ({ page }) => {
        await clickChain(page, 'TOP > MID > LEAF');

        await expect(page.locator('#arrayOpen')).toHaveCount(0);
    });

    test('but still is on a single instance the cell holds', async ({ page }) => {
        await clickChain(page, 'TOP > MID');

        await expect(page.locator('#arrayOpen')).toHaveCount(1);
    });
});
