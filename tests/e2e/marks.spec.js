//Marks made from numbers, from the Draw menu's Generator row - Mark until 2026-10-21, with its Place button Pick up: a
//die frame, alignment marks, test structures, a ring resonator, a channel - and since 2026-11-01 the passives of an
//analog and an RF circuit, antennas, a clock tree, pads and fill.
//
//The shapes themselves - a frame's area, a window's gap round its cross, a ring a waveguide's width wide - are
//C#'s and measured in MarksTests. What is here is the part only the editor has: a panel built from the
//library's own list of settings, a mark carried under the pointer at the size the numbers say, put down on the
//layer being drawn on as one press of undo, and a mark the numbers cannot make said rather than carried.
//
//Carried marks are read off their own drawing, which is in database units with the mark's middle at the
//origin - so its extent there is its size. Mosfet's unit is a nanometer.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, openShapeSettings, chooseShape } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#drawTool')).toBeEnabled({ timeout: 30000 });

    await page.locator('#drawTool').click();
});

///What is in hand: its extent in database units and how many outlines it is.
async function carried(page) {
    return page.evaluate(() => {
        const path = document.querySelector('#carriedCell path');

        if (path == null)
            return null;

        //Its corners, read back from the steps the drawing is written in.
        const drawn = path.getAttribute('d');
        const numbers = window.viewGeometry.subpaths(drawn).flatMap(run => run.corners);
        const xs = numbers.filter((_, i) => i % 2 === 0);
        const ys = numbers.filter((_, i) => i % 2 === 1);

        return {
            width: Math.max(...xs) - Math.min(...xs),
            height: Math.max(...ys) - Math.min(...ys),
            outlines: (drawn.match(/M/g) || []).length
        };
    });
}

///Sets one of the kind's numbers, by the name the library gives it.
async function setting(page, name, value) {
    const box = page.locator(`#markPanel input[data-setting="${name}"]`);

    await box.fill(String(value));
    await box.blur();
}

async function dropAt(page, across, down) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + (view.width * across), view.y + (view.height * down));
    await page.mouse.down();
    await page.mouse.up();
}

test('the panel lists every kind, each in its group, with its own numbers', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    await expect(page.locator('#markKind optgroup')).toHaveCount(9);
    await expect(page.locator('#markKind option')).toHaveCount(38);

    //A die frame to start with, which is a width, a height and a line.
    await expect(page.locator('#markPanel input[data-setting]')).toHaveCount(3);

    await page.locator('#markKind').selectOption('ring');

    await expect(page.locator('#markPanel input[data-setting="radius"]')).toBeVisible();
    await expect(page.locator('#markPanel input[data-setting]')).toHaveCount(4);
});

///
///**The panel stays open when the kind is changed** (2026-11-01, asked for). A kind chosen from the list leaves the
///pointer wherever it was in the list - over the drawing, as often as not - and the first move from there shut the
///whole menu, so every kind was a trip back to the pencil. The list keeps the keyboard, which holds the menu open
///now; a press anywhere else still puts it away.
///
test('choosing another kind keeps the panel open when the pointer moves off it, until a press elsewhere', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    const kind = page.locator('#markKind');

    await kind.focus();
    await kind.selectOption('ratrace');

    //Where an option low in the list leaves the pointer: over the drawing, well clear of the menu.
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + (view.width * 0.5), view.y + (view.height * 0.85));
    await page.waitForTimeout(500);

    await expect(page.locator('#shapePicker')).toHaveCount(1);
    await expect(page.locator('#markPanel')).toBeVisible();
    await expect(page.locator('#markFigure')).toHaveAttribute('data-kind', 'ratrace');

    //Its numbers are typed into from there, the pointer still out on the drawing.
    await page.locator('#markPanel input[data-setting="radius"]').fill('500');
    await page.locator('#markPanel input[data-setting="radius"]').press('Tab');

    await expect(page.locator('#shapePicker')).toHaveCount(1);

    await page.mouse.down();
    await page.mouse.up();

    await expect(page.locator('#shapePicker')).toHaveCount(0);
});

///
///**Every kind drawn with its numbers on it** (2026-11-01, asked for): a box for each number and a dimension for each
///box, every box on the drawing and clear of the others. Measured here, at the size the browser's fonts make a box,
///which MarkFiguresTests can only take as given.
///
test('every kind is drawn with a box for each of its numbers, every box on the drawing and clear of the rest', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    const kinds = await page.locator('#markKind option').evaluateAll(options => options.map(option => option.value));

    expect(kinds.length).toBe(38);

    for (const kind of kinds) {
        await page.locator('#markKind').selectOption(kind);
        await expect(page.locator('#markFigure')).toHaveAttribute('data-kind', kind);

        const laid = await page.evaluate(() => {
            const figure = document.querySelector('#markFigure');
            const sheet = figure.getBoundingClientRect();
            const boxes = [...figure.querySelectorAll('.markChip')].map(chip => chip.getBoundingClientRect());
            let inside = true;
            let clear = true;

            for (const box of boxes) {
                if (box.left < sheet.left || box.top < sheet.top || box.right > sheet.right || box.bottom > sheet.bottom)
                    inside = false;
            }

            for (let first = 0; first < boxes.length; first++) {
                for (let second = first + 1; second < boxes.length; second++) {
                    const a = boxes[first];
                    const b = boxes[second];

                    if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom)
                        clear = false;
                }
            }

            return { boxes: boxes.length, dimensions: figure.querySelectorAll('g.markDim').length, inside, clear };
        });

        expect(laid.boxes, kind).toBeGreaterThan(0);
        expect(laid.dimensions, kind).toBe(laid.boxes);
        expect(laid.inside, `${kind}: a box runs off the drawing`).toBe(true);
        expect(laid.clear, `${kind}: two boxes overlap`).toBe(true);
    }
});

///Pointing at a number's box lights the part of the drawing it sets, and only that.
test('pointing at a number lights its dimension on the drawing', async ({ page }) => {
    await openShapeSettings(page, '#markShape');
    await page.locator('#markKind').selectOption('ratrace');

    const color = async (index) => page.evaluate((index) => getComputedStyle(document.querySelector(`#markFigure .markDim${index}`)).color, index);
    const quiet = await color(0);

    await page.locator('#markPanel input[data-setting="radius"]').hover();

    await expect.poll(async () => color(0)).toBe('rgb(232, 89, 12)');
    expect(await color(1)).toBe(quiet);
});

///Rounded, like every field and button in a popup or a window (2026-11-01, asked for).
test('the panel\'s fields and its Place button are rounded like the rest of the app', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    for (const control of ['#markKind', '#markPanel input[data-setting="width"]', '#pickUpMark'])
        await expect(page.locator(control)).toHaveCSS('border-radius', '3px');
});

///
///A die frame two microns square with a line of a tenth: four bars, two thousand nanometers across, put down
///on the layer being drawn on as one step named for what it is - and still in hand for the next die.
///
test('a die frame is carried at its size and put down as one step', async ({ page }) => {
    const was = await shapeCount(page);

    await openShapeSettings(page, '#markShape');

    await setting(page, 'width', 2);
    await setting(page, 'height', 2);
    await setting(page, 'line', 0.1);

    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toContainText('Die frame');

    const frame = await carried(page);

    expect(frame.outlines).toBe(4);
    expect(frame.width).toBe(2000);
    expect(frame.height).toBe(2000);

    await dropAt(page, 0.3, 0.8);

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 4);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place die frame/);

    await expect(page.locator('#carryingStamp')).toBeVisible();

    await page.keyboard.press('Escape');

    await expect(page.locator('#carryingStamp')).toHaveCount(0);
});

///A number changed while the mark is in hand changes what is in hand.
test('a cross in hand follows the numbers typed for it', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    await page.locator('#markKind').selectOption('cross');

    await setting(page, 'arm', 1);
    await setting(page, 'width', 0.2);

    await page.locator('#pickUpMark').click();

    await expect.poll(async () => (await carried(page))?.width, { timeout: 15000 }).toBe(1000);
    expect((await carried(page)).outlines).toBe(1);

    await openShapeSettings(page, '#markShape');
    await setting(page, 'arm', 2);

    await expect.poll(async () => (await carried(page))?.width, { timeout: 15000 }).toBe(2000);
});

///
///**Choosing another shape or tool puts the structure back** (2026-10-21, asked for). What is carried takes every press
///before any tool does, so choosing Text with a cross in hand left the cross in hand and the click meant for writing put
///another cross down; choosing Select left it too. See emptyHand.
///
test('choosing Text with a structure in hand puts it back, and the click writes', async ({ page }) => {
    await openShapeSettings(page, '#markShape');
    await page.locator('#markKind').selectOption('cross');
    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toBeVisible();
    await expect(page.locator('#markShape')).toHaveClass(/shapePickOn/);

    //Lit beside it, a shape row would say two things were in hand.
    await expect(page.locator('#shapePicker .shapePickOn')).toHaveCount(1);

    await chooseShape(page, '#textShape');

    await expect(page.locator('#carryingStamp')).toHaveCount(0);
    await expect(page.locator('#carriedCell')).toHaveCount(0);

    await dropAt(page, 0.5, 0.75);

    await expect(page.locator('#canvasWritingText')).toBeFocused();
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place text/);
});

test('choosing Select with a structure in hand puts it back, and a click picks a shape out', async ({ page }) => {
    await openShapeSettings(page, '#markShape');
    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toBeVisible();

    await page.locator('#selectTool').click();

    await expect(page.locator('#carryingStamp')).toHaveCount(0);
    await expect(page.locator('#carriedCell')).toHaveCount(0);
    await expect(page.locator('#chosenLayer')).toHaveCount(0);

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    //Chosen, and nothing put down - the press went to Select rather than to a structure still in hand.
    await expect(page.locator('#chosenLayer')).toBeVisible();
    await expect(page.locator('#undoEdit')).toHaveCount(0);
});

///
///**A mark the numbers cannot make is said, not carried.** A frame whose line is as wide as half the die has no
///die inside it - the library says so, the panel shows it, and nothing follows the pointer.
///
test('a frame with no room inside it is refused, and says why', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    await setting(page, 'width', 2);
    await setting(page, 'height', 2);
    await setting(page, 'line', 1);

    await page.locator('#pickUpMark').click();

    await expect(page.locator('#markProblem')).toContainText('no die inside it');
    await expect(page.locator('#carryingStamp')).toHaveCount(0);
});

///
///**A structure on several layers is a mark a layer, from one set of numbers** (2026-11-01, asked for with the analog,
///RF, antenna, digital and chip kinds): the turns typed for a spiral inductor are its underpass's turns too, so the two
///put down at one point meet. The panel says what the numbers come to where the kind can, and an angle is in degrees.
///
test('a spiral inductor and its underpass share their numbers, and the panel says what they come to', async ({ page }) => {
    await openShapeSettings(page, '#markShape');

    await page.locator('#markKind').selectOption('spiral');

    const note = page.locator('#markNote');

    await expect(note).toHaveText("About 2.07 nH, by Mohan's formula for a square spiral.");

    await setting(page, 'turns', 2);

    await expect(note).not.toHaveText(/2\.07 nH/);

    await page.locator('#markKind').selectOption('underpass');

    await expect(page.locator('#markPanel input[data-setting="turns"]')).toHaveValue('2');
    await expect(note).toHaveCount(0);

    //Picked up from the spiral's numbers: one bar.
    await page.locator('#pickUpMark').click();

    await expect(page.locator('#carryingStamp')).toContainText('Inductor underpass');
    await expect.poll(async () => (await carried(page))?.outlines, { timeout: 15000 }).toBe(1);

    await page.keyboard.press('Escape');

    await openShapeSettings(page, '#markShape');
    await page.locator('#markKind').selectOption('bowtie');

    await expect(page.locator('#markPanel input[data-setting="angle"] + .markChipUnit')).toHaveText('°');
});
