//Placing a label, and typing it where it lands.
//
//The words used to be typed into a box in the toolbar and then clicked into place, which split one decision
//across the screen: you typed here, looked there, and had to remember which half you had already done. A
//click now puts a label down reading "label" and opens a box over it, so the name is typed where the name
//is going.
//
//What the records look like is covered in LayoutEditTests. What is only checkable here is the wiring: that
//the box opens where the label is drawn, that Enter and Escape mean what they say, and that placing and
//naming is one press of undo rather than two.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, CLEAR_OF_PANEL, snapToGrid, chooseShape, openedOnItsOwn } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await enterCellAndDraw(page);

    //
    //Snapping off, which is how every test in this file was written and what they still mean.
    //
    //It is on out of the box now. At the default pitch of a micron, and this view fitted at roughly seven
    //database units to the pixel, a gesture of a few dozen pixels is a fraction of one grid step - so two
    //clicks meant to be apart land on the same crossing and the shape, path or reading collapses. These
    //are about the tools rather than about the grid, so the grid is taken out of them.
    //
    await snapToGrid(page, false);
});

///Into a cell, since there is nowhere to put anything until something has said which cell it goes in.
async function enterCellAndDraw(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await page.locator('#drawTool').click();
    await chooseShape(page, '#labelShape');
}

async function clickAt(page, x, y) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(view.x + x, view.y + y);
}

///Mosfet.gds draws three labels of its own, so what matters is how many more there are.
async function labelCount(page) {
    return page.locator('#gdsSVG text').count();
}

const BOX = '#canvasLabelText';

///Puts a label down and names it, which is the whole gesture.
async function placeLabel(page, x, y, says) {
    await clickAt(page, x, y);

    await expect(page.locator(BOX)).toBeVisible();

    await page.locator(BOX).fill(says);
    await page.locator(BOX).press('Enter');

    await expect(page.locator(BOX)).toHaveCount(0);
}

test.describe('the tool', () => {
    ///
    ///Which shape is chosen, read off the menu the pencil opens.
    ///
    ///Pointed at first: the setup turns snapping off after choosing Label, and pressing anything outside the
    ///menu puts it away. That is the menu working rather than a problem - it is a question asked once, and
    ///the pencil is how it is asked again.
    ///
    test('is one of the shapes', async ({ page }) => {
        await page.locator('#drawTool').hover();

        await expect(page.locator('#labelShape')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#rectangleShape')).not.toHaveClass(/shapePickOn/);
    });

    ///It is not a tool, so it must not count towards the one tool that is lit.
    test('choosing it leaves Draw as the tool that is on', async ({ page }) => {
        await expect(page.locator('#toolGroup .toolButton.toolButtonOn')).toHaveCount(1);
        await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/);
    });

    ///The words belong where the label is going, not in a bar at the top of the screen.
    test('there is no box in the toolbar to type into first', async ({ page }) => {
        await expect(page.locator('#labelText')).toHaveCount(0);
        await expect(page.locator('#drawHint')).toContainText('click to place a label and type it where it lands');
    });
});

test.describe('placing one', () => {
    ///
    ///It says something before it is typed.
    ///
    ///Placing an empty one and typing into nothing would mean an element invisible in every view for as long
    ///as the box is open - and if the box were abandoned, one findable only by reading the records.
    ///
    test('a click puts a label down and opens a box over it', async ({ page }) => {
        const before = await labelCount(page);

        await clickAt(page, 220, 220);

        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before + 1);

        await expect(page.locator(BOX)).toBeVisible();
        await expect(page.locator(BOX)).toHaveValue('label');
        await expect(page.locator('#gdsSVG text').last()).toHaveText('label');
    });

    ///Over the label rather than anywhere: the box is positioned from where the label was drawn.
    test('the box opens on the label it belongs to', async ({ page }) => {
        await clickAt(page, 260, 240);

        await expect(page.locator(BOX)).toBeVisible();

        const box = await page.locator(BOX).boundingBox();
        const view = await page.locator('#gdsSVG').boundingBox();

        //Near where the click was, rather than parked in a corner of the window.
        expect(Math.abs(box.x - (view.x + 260))).toBeLessThan(120);
        expect(Math.abs(box.y - (view.y + 240))).toBeLessThan(120);
    });

    test('typing it and pressing Enter names it', async ({ page }) => {
        await placeLabel(page, 220, 220, 'VDD');

        await expect(page.locator('#gdsSVG text', { hasText: 'VDD' })).toHaveCount(1);
    });

    ///
    ///**One press of undo, not two.**
    ///
    ///Placing a label and naming it is one gesture. It is two edits underneath - the element goes in, then
    ///its string is written - and somebody who has placed one label should not have to press undo twice to
    ///be rid of it. See commitRetype.
    ///
    test('placing and naming is one step on the undo stack', async ({ page }) => {
        const before = await labelCount(page);

        await placeLabel(page, 220, 220, 'GND');

        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before + 1);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Label/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before);
    });

    ///Escape after placing takes the label away, because what was there before it is nothing.
    test('Escape takes back a label that was just placed', async ({ page }) => {
        const before = await labelCount(page);

        await clickAt(page, 220, 220);

        await expect(page.locator(BOX)).toBeVisible();
        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before + 1);

        await page.locator(BOX).press('Escape');

        await expect(page.locator(BOX)).toHaveCount(0);
        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before);
    });

    ///GDSII holds plain ASCII, and the encoder turns anything else into a question mark rather than saying
    ///so. Dropped where it is typed instead, so what lands on screen is what went into the file.
    test('anything the format cannot hold is dropped rather than mangled', async ({ page }) => {
        await placeLabel(page, 220, 220, '2 µm gap');

        await expect(page.locator('#gdsSVG text', { hasText: '2 m gap' })).toHaveCount(1);
    });

    ///An empty label is invisible in every view, so clearing the box means stop rather than erase.
    test('clearing the box keeps what was there', async ({ page }) => {
        await clickAt(page, 220, 220);

        await expect(page.locator(BOX)).toBeVisible();

        await page.locator(BOX).fill('');
        await page.locator(BOX).press('Enter');

        await expect(page.locator(BOX)).toHaveCount(0);
        await expect(page.locator('#gdsSVG text').last()).toHaveText('label');
    });
});

test.describe('retyping one', () => {
    ///
    ///Double rather than single, because a single click has to go on meaning "choose this" - which is what
    ///moving a label and deleting one both start with.
    ///
    test('a double-click opens the box on a label already there', async ({ page }) => {
        await placeLabel(page, CLEAR_OF_PANEL + 60, 200, 'SEL');

        //Out of the Label shape first. A click there places a label, so a double-click there places two -
        //retyping one is a double-click with any other tool in hand.
        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'SEL' }).boundingBox();

        await page.mouse.dblclick(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator(BOX)).toBeVisible();
        await expect(page.locator(BOX)).toHaveValue('SEL');
    });

    ///Its own edit, unlike naming one that was just placed - this label was already in the file.
    test('retyping is a step of its own, and says so', async ({ page }) => {
        await placeLabel(page, CLEAR_OF_PANEL + 60, 200, 'SEL');

        //Out of the Label shape first. A click there places a label, so a double-click there places two -
        //retyping one is a double-click with any other tool in hand.
        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'SEL' }).boundingBox();

        await page.mouse.dblclick(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator(BOX)).toBeVisible();

        await page.locator(BOX).fill('CLK');
        await page.locator(BOX).press('Enter');

        await expect(page.locator('#gdsSVG text', { hasText: 'CLK' })).toHaveCount(1);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Retype/);

        //And taking it back leaves the label, since placing it was the step before.
        await page.locator('#undoEdit').click();

        await expect(page.locator('#gdsSVG text', { hasText: 'SEL' })).toHaveCount(1);
    });

    test('Escape leaves an existing label as it was', async ({ page }) => {
        await placeLabel(page, CLEAR_OF_PANEL + 60, 200, 'SEL');

        //Out of the Label shape first. A click there places a label, so a double-click there places two -
        //retyping one is a double-click with any other tool in hand.
        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'SEL' }).boundingBox();

        await page.mouse.dblclick(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator(BOX)).toBeVisible();

        await page.locator(BOX).fill('NOPE');
        await page.locator(BOX).press('Escape');

        await expect(page.locator(BOX)).toHaveCount(0);
        await expect(page.locator('#gdsSVG text', { hasText: 'SEL' })).toHaveCount(1);
        await expect(page.locator('#gdsSVG text', { hasText: 'NOPE' })).toHaveCount(0);
    });
});

test.describe('afterwards', () => {
    test('a placed label is in the file that is downloaded', async ({ page }) => {
        const before = await labelCount(page);

        await placeLabel(page, 220, 220, 'PIN1');

        await expect.poll(async () => labelCount(page), { timeout: 15000 }).toBe(before + 1);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await page.locator('#fileUpload').setInputFiles(path);

        await openedOnItsOwn(page);

        await expect.poll(async () => labelCount(page), { timeout: 60000 }).toBe(before + 1);
        await expect(page.locator('#gdsSVG text', { hasText: 'PIN1' })).toHaveCount(1);
    });

    ///Chosen like anything else, so it can be moved and deleted with the same gestures.
    test('a placed label can be picked out and says it is one', async ({ page }) => {
        await placeLabel(page, 240, 240, 'SEL');

        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'SEL' }).boundingBox();

        await page.mouse.click(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator('#selectionPanel')).toContainText('label');
    });

    ///Handles are for corners, and a label has none - it is one point with a word at it.
    test('no vertex handles are put on a label', async ({ page }) => {
        await placeLabel(page, 240, 240, 'SEL');

        await page.locator('#selectTool').click();

        const placed = await page.locator('#gdsSVG text', { hasText: 'SEL' }).boundingBox();

        await page.mouse.click(placed.x + (placed.width / 2), placed.y + (placed.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#vertexHandles')).toHaveCount(0);
    });
});
