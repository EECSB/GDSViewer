//The keyboard.
//
//Every shortcut reaches the same method the button beside it calls, so what it does is already covered
//wherever that button is. What is only checkable here is the keyboard itself: that the keys arrive, that
//they stay out of the way of anything being typed into, and that they stop at the edge of this view.
//
//The last two are the ones that matter. A shortcut that fires while somebody is naming a label eats the
//letter, and one that survives the view eats Ctrl+Z in the text editor.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, elementPoints, shapesAndLabels, snapToGrid, showGrid , pitchInUnits , usePitch, chooseShape, selectView } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

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

async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Picks one shape the cell holds. Returns its corners, so a nudge can be measured against them.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        if (await elementPoints(page, index) !== null)
            return { index: index, corners: await cornersAt(page, index) };
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

async function cornersAt(page, index) {
    return elementPoints(page, index);
}

test.describe('the tools', () => {
    for (const [key, tool] of [['p', 'Pan'], ['m', 'measureTool'], ['s', 'selectTool']]) {
        test(`${key} chooses ${tool}`, async ({ page }) => {
            //Off the tool it starts on, so choosing it again is a change rather than a no-op.
            await page.locator('#selectTool').click();

            await page.keyboard.press(key);

            if (tool === 'Pan')
                await expect(page.locator('#selectTool')).not.toHaveClass(/toolButtonOn/);
            else
                await expect(page.locator(`#${tool}`)).toHaveClass(/toolButtonOn/);
        });
    }

    test('d chooses Draw, once there is a cell to draw into', async ({ page }) => {
        await enterCell(page);

        await page.keyboard.press('d');

        await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/);
    });

    test('g shows the grid and hides it again', async ({ page }) => {
        //From off, since the grid starts on now and this is about the key flipping it either way.
        await showGrid(page, false);
        await expect(page.locator('#gridOverlay')).toHaveCount(0);

        await page.keyboard.press('g');

        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        await page.keyboard.press('g');

        await expect(page.locator('#gridOverlay')).toHaveCount(0);
    });
});

test.describe('editing', () => {
    test('Delete removes what is chosen', async ({ page }) => {
        await enterCell(page);

        const before = await shapeCount(page);

        await chooseAShape(page);

        await page.keyboard.press('Delete');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);
    });

    ///Backspace as well as Delete, because which one somebody reaches for is a habit rather than a rule.
    test('and so does Backspace', async ({ page }) => {
        await enterCell(page);

        const before = await shapeCount(page);

        await chooseAShape(page);

        await page.keyboard.press('Backspace');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);
    });

    for (const redo of ['Control+y', 'Control+Shift+z']) {
        test(`Ctrl+Z takes it back and ${redo} puts it again`, async ({ page }) => {
            await enterCell(page);

            const before = await shapeCount(page);

            await chooseAShape(page);

            await page.keyboard.press('Delete');

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before - 1);

            await page.keyboard.press('Control+z');

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before);

            await page.keyboard.press(redo);

            await expect.poll(async () => shapeCount(page), { timeout: 15000 })
                .toBe(before - 1);
        });
    }

    test('Ctrl+C then Ctrl+V copies into the cell', async ({ page }) => {
        await enterCell(page);

        const before = await shapeCount(page);

        await chooseAShape(page);

        await page.keyboard.press('Control+c');
        await page.keyboard.press('Control+v');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);
    });

    test('Ctrl+A chooses everything the cell holds', async ({ page }) => {
        await enterCell(page);

        //Labels counted in: a label is a thing the cell holds, and Ctrl+A takes everything it holds.
        const held = (await shapesAndLabels(page, 'inContext')).length;

        expect(held).toBeGreaterThan(1);

        await page.keyboard.press('Control+a');

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(held);
    });

    test('Escape lets go of what is chosen', async ({ page }) => {
        await enterCell(page);

        await chooseAShape(page);

        await expect(page.locator('#selectionPanel')).toBeVisible();

        await page.keyboard.press('Escape');

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });
});

test.describe('nudging', () => {
    ///
    ///One grid pitch a press, ten with shift - a database unit is a nanometer on this file, and an arrow key
    ///that moved a shape by one would not be an editor.
    ///
    ///Exactly one step because the shape is already on the grid. One that is not gets brought onto it
    ///first, which is the test below.
    ///
    test('an arrow key moves what is chosen by one grid pitch', async ({ page }) => {
        await enterCell(page);

        const chosen = await chooseAShape(page);

        const leftOf = points => Math.min(...points.trim().split(/[\s,]+/)
            .map(Number)
            .filter((_, at) => at % 2 === 0));

        const before = leftOf(chosen.corners);

        //One pitch, read from the app: the file chooses it now, so a thousand was the old default and
        //not the behavior this names.
        const step = await pitchInUnits(page);

        await page.keyboard.press('ArrowRight');

        await expect.poll(async () => leftOf(await cornersAt(page, chosen.index)), { timeout: 15000 })
            .toBe(before + step);
    });

    test('and shift moves it ten', async ({ page }) => {
        await enterCell(page);

        const chosen = await chooseAShape(page);

        const topOf = points => Math.min(...points.trim().split(/[\s,]+/)
            .map(Number)
            .filter((_, at) => at % 2 === 1));

        const before = topOf(chosen.corners);

        const step = await pitchInUnits(page);

        //Down the screen is a larger Y here.
        await page.keyboard.press('Shift+ArrowDown');

        await expect.poll(async () => topOf(await cornersAt(page, chosen.index)), { timeout: 15000 })
            .toBe(before + (step * 10));
    });

    ///
    ///**A shape that is not on the grid is brought onto it**, rather than carried along beside it.
    ///
    ///Both ends of a drag are snapped, so the distance between them is always a whole number of steps -
    ///which meant a shape drawn on some other grid kept its offset for ever, however often it was moved.
    ///Snapping was preserving the file's old grid instead of applying the one in force. The result is
    ///snapped now, not the distance.
    ///
    ///A micron here, which this file's geometry is emphatically not on: its coordinates divide by five,
    ///and the shape below starts at something that is not a multiple of a thousand. Asserted as landing
    ///*on* the grid rather than at a particular number, since which line it lands on is the rule and the
    ///number is the file's business.
    ///
    test('a shape off the grid is pulled onto it rather than moved beside it', async ({ page }) => {
        await enterCell(page);

        const chosen = await chooseAShape(page);

        //Snapping back on, against this file's beforeEach: it is off there because these specs were written
        //before it was a default, and this is the one test here that is about snapping.
        await snapToGrid(page, true);

        //A micron, set after the shape is in hand: this file's geometry is emphatically not on it.
        await usePitch(page, 1);

        const step = await pitchInUnits(page);

        const numbers = points => points.trim().split(/[\s,]+/).map(Number);
        const leftOf = points => Math.min(...numbers(points).filter((_, at) => at % 2 === 0));
        const bottomOf = points => Math.min(...numbers(points).filter((_, at) => at % 2 === 1));

        const before = leftOf(chosen.corners);
        const beforeDown = bottomOf(chosen.corners);

        //The premise: it is not on this grid to begin with, or there is nothing here to show.
        expect(Number.isFinite(before)).toBe(true);
        expect(before % step).not.toBe(0);

        await page.keyboard.press('ArrowRight');

        await expect.poll(async () => leftOf(await cornersAt(page, chosen.index)) % step, { timeout: 15000 })
            .toBe(0);

        //And only the way it was nudged: pulling the other axis on at the same time would slide a shape
        //down the screen on a press of Right, which is not what that key means.
        expect(bottomOf(await cornersAt(page, chosen.index))).toBe(beforeDown);
    });

    ///The selection stays, which is what lets a shape be walked into place one press at a time.
    test('the shape stays chosen, so it can be nudged again', async ({ page }) => {
        await enterCell(page);

        const chosen = await chooseAShape(page);

        await page.keyboard.press('ArrowRight');

        await expect.poll(async () => cornersAt(page, chosen.index), { timeout: 15000 })
            .not.toBe(chosen.corners);

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });
});

test.describe('staying out of the way', () => {
    ///
    ///**Nothing fires while something is being typed into.**
    ///
    ///There is a box for what a label says, one for the grid pitch and four for an array. A "d" typed into
    ///any of them has to be a letter rather than the Draw tool, and a Backspace has to delete a character
    ///rather than a shape.
    ///
    ///Puts a label down, which opens the box over it with the placeholder selected. See label.spec.
    async function openLabelBox(page) {
        await page.locator('#drawTool').click();
        await chooseShape(page, '#labelShape');

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 260, view.y + 260);

        await expect(page.locator('#canvasLabelText')).toBeVisible();
    }

    test('a letter typed into the label box is a letter', async ({ page }) => {
        await enterCell(page);
        await openLabelBox(page);

        const gridBefore = await page.locator('#gridOverlay').count();

        //Replacing the placeholder, which is selected when the box opens.
        await page.locator('#canvasLabelText').fill('');
        await page.keyboard.type('pmsdg');

        await expect(page.locator('#canvasLabelText')).toHaveValue('pmsdg');

        //And neither the tool nor the grid changed under it - the d and the g in that word are both
        //shortcuts, so this is the whole point of the test. Against what the grid was before the typing
        //rather than against a number, since it is on out of the box now and was not always.
        await expect(page.locator('#drawTool')).toHaveClass(/toolButtonOn/);
        await expect(page.locator('#gridOverlay')).toHaveCount(gridBefore);
    });

    test('backspace in the label box deletes a character, not a shape', async ({ page }) => {
        await enterCell(page);

        const before = await shapeCount(page);

        await openLabelBox(page);

        await page.locator('#canvasLabelText').fill('VDD');
        await page.keyboard.press('Backspace');

        await expect(page.locator('#canvasLabelText')).toHaveValue('VD');

        //The label itself is on the layout by now, so what must not have gone is a shape.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
    });

    test('and the same in a number box', async ({ page }) => {
        //However it got there - it starts on, and pressing g would take it away rather than bring it.
        await showGrid(page, true);

        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        await page.locator('#gridPitch').click();
        await page.keyboard.press('g');

        //Still showing: the g went into the box rather than to the switch.
        await expect(page.locator('#gridOverlay')).toHaveCount(1);
    });

    ///
    ///**And nothing fires once the view has gone.**
    ///
    ///The listener is on the window, because an SVG cannot take focus - and the window outlives the view.
    ///Without the check, Ctrl+Z in the text editor would undo a shape instead of a line of typing.
    ///
    test('the shortcuts stop at the edge of this view', async ({ page }) => {
        await enterCell(page);

        const before = await shapeCount(page);

        await chooseAShape(page);
        await page.keyboard.press('Delete');

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before - 1);

        await selectView(page, 'ViewText');

        await expect(page.locator('#gdsSVG')).toHaveCount(0);

        //Pressing it here must not reach the editor that is no longer on screen.
        await page.keyboard.press('Control+z');
        await page.keyboard.press('Delete');

        await page.waitForTimeout(800);

        await selectView(page, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBe(before - 1);
    });
});
