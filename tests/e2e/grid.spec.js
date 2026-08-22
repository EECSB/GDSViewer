//The grid, and landing on it.
//
//Two switches and a distance. What can only be checked in a browser is the part that depends on how much
//layout is on screen: the pitch is set in microns and drawn in database units, the fine lines drop out as
//they get too close together to be anything but a wash of color, and snapping is what makes a drag come out
//as a whole number of steps.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapePoints, shapeBox, showGrid, snapToGrid, pitchInUnits, usePitch, chooseShape, openGridMenu } = require('./helpers');

//Mosfet.gds has a UNITS record saying a database unit is a nanometer, so a micron is a thousand of them.
const UNITS_PER_MICRON = 1000;

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///How many lines each set of the grid is drawing, coarsest last. Empty when there is no grid.
async function gridSets(page) {
    return page.locator('#gridOverlay path').evaluateAll(nodes =>
        nodes.map(node => (node.getAttribute('d').match(/M/g) || []).length));
}

///
///Puts the view at an exact width and redraws, for the cases that are about how much layout is on screen.
///
///Set rather than scrolled to. A wheel notch is a fixed step, so reaching a zoom forty times out by
///scrolling is a loop whose length depends on the notch size - and what these are actually asking about is
///the width, not the scrolling.
///
async function atWidth(page, width) {
    await page.evaluate(units => {
        const svg = document.getElementById('gdsSVG');

        svg.setAttribute('viewBox', `${-units / 2} ${-units / 2} ${units} ${units}`);

        drawGrid();
    }, width);
}

test.describe('showing it', () => {
    ///
    ///The grid starts on, which is the opposite of what this file used to say.
    ///
    ///This is an editor and a layout is placed on a pitch: beginning with no grid means every first shape
    ///is drawn freehand and then has to be found and fixed.
    ///
    ///Snapping is on with it, and only because the pitch comes from the file too: at a fixed micron one
    ///grid step was 145 screen pixels at the opening fit, so any smaller gesture put both ends on the same
    ///crossing and the shape collapsed. On this file a step is 13 pixels. A grid drawn but not snapped to
    ///is a picture of a rule nothing follows, so the two belong on together or not at all.
    ///
    test('the grid is drawn and snapped to before anything is asked for', async ({ page }) => {
        await expect(page.locator('#gridOverlay')).toHaveCount(1);
        await openGridMenu(page);
        await expect(page.locator('#gridToggle')).toHaveClass(/shapePickOn/);
        await openGridMenu(page);
        await expect(page.locator('#snapToggle')).toHaveClass(/shapePickOn/);

        //And on a pitch the file chose: its own five-unit grid, raised one decade to be worth drawing.
        expect(await pitchInUnits(page)).toBe(50);
    });

    ///A fine set and a heavy one every tenth, which is what makes a grid readable rather than countable.
    test('the switch draws lines, and a heavier one every tenth', async ({ page }) => {
        //Off and on again rather than straight on, since the switch is what this is about.
        await showGrid(page, false);
        await openGridMenu(page);
        await page.locator('#gridToggle').click();

        await expect(page.locator('#gridOverlay')).toBeVisible();

        expect(await gridSets(page)).toHaveLength(2);

        //Fewer heavy lines than fine ones, and each of them thicker and darker - which is the whole of what
        //"every tenth" means to somebody looking at it.
        const drawn = await page.locator('#gridOverlay path').evaluateAll(nodes => nodes.map(node => ({
            lines: (node.getAttribute('d').match(/M/g) || []).length,
            width: Number(node.getAttribute('stroke-width')),
            opacity: Number(node.getAttribute('opacity'))
        })));

        expect(drawn[1].lines).toBeLessThan(drawn[0].lines);
        expect(drawn[1].width).toBeGreaterThan(drawn[0].width);
        expect(drawn[1].opacity).toBeGreaterThan(drawn[0].opacity);
    });

    ///Behind the layout, or it would be a grid drawn over the thing it is there to measure.
    test('the grid is drawn under the shapes', async ({ page }) => {
        await showGrid(page);

        await expect(page.locator('#gridOverlay')).toBeVisible();

        const first = await page.locator('#gdsSVG').evaluate(svg => svg.firstElementChild.id);

        expect(first).toBe('gridOverlay');
    });

    test('the switch takes it away again', async ({ page }) => {
        await showGrid(page);
        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        await openGridMenu(page);
        await page.locator('#gridToggle').click();
        await expect(page.locator('#gridOverlay')).toHaveCount(0);
    });

    ///
    ///**Lines closer together than a few pixels are not a grid, they are a color.**
    ///
    ///The fine set goes first and the heavy one carries on alone, and then that goes too - so zooming out
    ///ends with the layout rather than with a gray rectangle over it.
    ///
    test('the fine lines go as they get too close, and then the rest', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await showGrid(page);

        await expect.poll(async () => (await gridSets(page)).length, { timeout: 15000 }).toBe(2);

        //
        //Wide enough that the fine lines are under a pixel apart and the heavy ones are still several.
        //
        //Five hundred microns rather than a thousand, which used to sit here: how many pixels a line is
        //from the next is worked out from the browser's own matrix now, and the box is fitted to the
        //*height* of a view wider than it is tall - so the same width is more units a pixel than the old
        //arithmetic thought, and a thousand microns takes the heavy set under the threshold too.
        //
        await atWidth(page, 500 * 1000);

        expect(await gridSets(page)).toHaveLength(1);

        //And ten more microns out, the heavy ones are the same problem.
        await atWidth(page, 10000 * 1000);

        await expect(page.locator('#gridOverlay')).toHaveCount(0);

        //Back in, and it comes back rather than staying gone.
        await atWidth(page, 4000);

        expect(await gridSets(page)).toHaveLength(2);
    });
});

test.describe('the pitch', () => {
    ///
    ///Set in microns and drawn in database units, which is the one place the two meet. A pitch stored in
    ///database units would mean a thousand times more or less on a file whose UNITS say something different.
    ///
    test('is given in microns and says what that is in the file own units', async ({ page }) => {
        //What Mosfet opens on, which is fifty of its own units rather than a round micron.
        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /0.05 µm is 50 database units/);

        await usePitch(page, 0.5);

        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /0\.5 µm is 500 database units/);
    });

    ///
    ///**And says what grid the file itself was drawn on**, which nothing in a GDSII file records.
    ///
    ///It is recoverable - every coordinate divides by it, so the greatest common divisor of the lot is it -
    ///and it is the finest placement the file is built to, which is worth knowing while laying out on it.
    ///
    ///Said rather than snapped to. Defaulting the pitch to it was tried: on this file it is five database
    ///units, which drew 178 lines across the view where a micron draws six, and took the tenth-line reading
    ///and the level of detail out of sight at the default zoom. The reason for doing it did not survive
    ///either - a micron is a thousand units and a thousand divides by five, so a point snapped to the old
    ///grid was already on the file's own.
    ///
    test('and says what grid the file itself was drawn on', async ({ page }) => {
        await expect(page.locator('#gridUnit'))
            .toHaveAttribute('title', /This file is drawn on a grid of 0\.005 µm \(5 units\)/);
    });

    test('a finer pitch draws more lines over the same view', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await showGrid(page);

        await expect.poll(async () => (await gridSets(page)).length, { timeout: 15000 }).toBe(2);

        const coarse = (await gridSets(page))[0];

        await page.locator('#gridPitch').fill('0.25');
        await page.locator('#gridPitch').blur();

        await expect.poll(async () => (await gridSets(page))[0], { timeout: 15000 }).toBeGreaterThan(coarse);
    });
});

///
///The unit beside the pitch is a choice, and choosing one only changes how the pitch is written.
///
///Worth its own set because the box and the grid can disagree in two directions, and only one of them is
///visible from either side alone: a conversion that ran the wrong way would show a plausible number over a
///grid at a thousand times the pitch, and a conversion that never reached the grid would show the right
///number over lines that had not moved when they should have.
///
test.describe('the unit it is written in', () => {
    ///The same distance, said differently - so the box changes and not one line of the grid.
    test('re-expresses the pitch without moving the grid', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await showGrid(page);

        await expect.poll(async () => (await gridSets(page)).length, { timeout: 15000 }).toBe(2);

        const drawn = await gridSets(page);

        await page.locator('#gridUnit').selectOption('Nanometer');

        await expect(page.locator('#gridPitch')).toHaveValue(String(UNITS_PER_MICRON));
        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /1000 nm is 1,000 database units/);

        //Read after the box has caught up, which is the render that follows the redraw - so this is the
        //grid as it stands with nanometers chosen, not the one from before the change.
        expect(await gridSets(page)).toEqual(drawn);

        await page.locator('#gridUnit').selectOption('Millimeter');

        await expect(page.locator('#gridPitch')).toHaveValue('0.001');

        expect(await gridSets(page)).toEqual(drawn);
    });

    ///
    ///And the arrows move by something that means anything in it.
    ///
    ///A tenth is a sensible nudge in microns and a hundred femtometers in nanometers, and a database unit
    ///is a whole number in the file - so the step and the floor follow the unit rather than staying at what
    ///suits one of them.
    ///
    test('takes the box floor and step with it', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await expect(page.locator('#gridPitch')).toHaveAttribute('step', '0.1');

        await page.locator('#gridUnit').selectOption('Nanometer');

        await expect(page.locator('#gridPitch')).toHaveAttribute('step', '1');
        await expect(page.locator('#gridPitch')).toHaveAttribute('min', '0.1');

        await page.locator('#gridUnit').selectOption('DatabaseUnit');

        await expect(page.locator('#gridPitch')).toHaveAttribute('step', '1');
        await expect(page.locator('#gridPitch')).toHaveAttribute('min', '1');
    });

    ///
    ///Database units are in the list because the file's own grid is quoted in them - the readout says this
    ///one is drawn on five, and typing five where you can see that number is the point of offering it.
    ///
    test('offers the file own units, and says that reading the other way round', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await page.locator('#gridUnit').selectOption('DatabaseUnit');

        await expect(page.locator('#gridPitch')).toHaveValue(String(UNITS_PER_MICRON));

        //"1,000 database units is 1 µm", not "1,000 database units is 1,000 database units".
        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /1,000 database units is 1 µm/);
    });

    ///
    ///And it reads as part of the box rather than as something parked beside it.
    ///
    ///A number and what it is in are one control read left to right. It used to float at two thirds the
    ///height, centered, with a gap in front of it, which puts it among the things on the bar instead of on
    ///the box it belongs to. Against it, and as tall as it - stretched rather than given a height, so it
    ///follows the input.
    ///
    test('sits against the box and matches its height', async ({ page }) => {
        const box = await page.locator('#gridPitch').boundingBox();
        const unit = await page.locator('#gridUnit').boundingBox();

        //Touching: the unit starts where the number ends.
        expect(Math.abs(unit.x - (box.x + box.width))).toBeLessThanOrEqual(1);

        //And the same height, top and bottom.
        expect(Math.abs(unit.height - box.height)).toBeLessThanOrEqual(1);
        expect(Math.abs(unit.y - box.y)).toBeLessThanOrEqual(1);

        //No wider than the widest thing it has to say. A select is already that wide, so what this rules
        //out is a width being handed to it - "units" is 5 characters at 0.85em plus the arrow.
        expect(unit.width).toBeLessThan(box.width);
    });

    ///A number typed in is read in whatever is chosen, which is the whole reason for choosing one.
    test('reads what is typed in the unit that is chosen', async ({ page }) => {
        //A micron, set rather than assumed: the pitch the file chooses is finer, and this is about what
        //happens at a particular one rather than about whichever the file picked.
        await usePitch(page, 1);

        await page.locator('#gridUnit').selectOption('Nanometer');

        await page.locator('#gridPitch').fill('500');
        await page.locator('#gridPitch').blur();

        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /500 nm is 500 database units/);

        //Half a micron, arrived at by typing nanometers.
        await page.locator('#gridUnit').selectOption('Micron');

        await expect(page.locator('#gridPitch')).toHaveValue('0.5');
        await expect(page.locator('#gridUnit')).toHaveAttribute('title', /0\.5 µm is 500 database units/);
    });
});

test.describe('snapping', () => {
    ///
    ///**Drawn corners land on the grid.**
    ///
    ///The whole point of the feature, and the only way to see it is to draw something and read the numbers
    ///back. A rectangle dragged out anywhere with snapping on has corners that are whole multiples of the
    ///pitch - which for this file is a thousand database units to the micron.
    ///
    test('a drawn rectangle lands on whole steps of the pitch', async ({ page }) => {
        await snapToGrid(page);

        //Into a cell, since there is nowhere to put a new shape until something says which.
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await page.locator('#drawTool').click();

        const before = await shapeCount(page);

        const view = await page.locator('#gdsSVG').boundingBox();

        //Deliberately not on anything round.
        await page.mouse.move(view.x + 137, view.y + 143);
        await page.mouse.down();
        await page.mouse.move(view.x + 291, view.y + 268, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        //Whatever pitch the file opened on, rather than a micron: this is about landing on the grid in
        //force, and which one that is belongs to the file.
        const step = await pitchInUnits(page);

        //The one just added is the last drawn.
        const points = await shapePoints(page, -1);

        //A whole number of steps, asked that way round rather than through a remainder - a coordinate of
        //zero comes back from the browser as negative zero, and that is not equal to zero.
        for (const number of points.split(/[ ,]/).filter(part => part.length > 0))
            expect(Number.isInteger(Number(number) / step)).toBe(true);
    });

    test('and without it they do not', async ({ page }) => {
        //Snapping is on out of the box, and this is the case where it is not.
        await snapToGrid(page, false);

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await page.locator('#drawTool').click();

        const before = await shapeCount(page);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 137, view.y + 143);
        await page.mouse.down();
        await page.mouse.move(view.x + 291, view.y + 268, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 })
            .toBe(before + 1);

        const points = await shapePoints(page, -1);
        const numbers = points.split(/[ ,]/).filter(part => part.length > 0).map(Number);

        //Landing on the grid by chance at every corner is not credible; landing off it at any is enough.
        expect(numbers.some(number => number % UNITS_PER_MICRON !== 0)).toBe(true);
    });

    ///Showing a grid and landing on one are different wants, and the switches are separate.
    ///
    ///**A shape can still be picked out while snapping is on**, which it could not be.
    ///
    ///The hit test was asked at the snapped point. Snapping decides where a point *goes*; it has no business
    ///deciding what was clicked - and with a micron pitch over shapes a few tenths across, the crossing
    ///nearest the middle of a shape is usually outside it, so a click selected nothing at all. Unseen for as
    ///long as snapping started off, and the first thing anybody would hit once it did not.
    ///
    test('a shape can still be picked out while snapping is on', async ({ page }) => {
        await snapToGrid(page);

        //
        //A coarse pitch on purpose, which is what makes this decisive.
        //
        //At the default micron the crossing nearest the middle of a shape is sometimes still inside it,
        //so the bug shows on some shapes and not others - a first version of this test clicked one of the
        //others and passed with the bug put back. Five microns is wider than anything in this file, so the
        //snapped point is certainly outside whatever was aimed at.
        //
        await page.locator('#gridPitch').fill('5');
        await page.locator('#gridPitch').blur();

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });

    test('the grid can be shown without snapping to it', async ({ page }) => {
        await showGrid(page);
        await snapToGrid(page, false);

        await expect(page.locator('#gridOverlay')).toHaveCount(1);
        await openGridMenu(page);
        await expect(page.locator('#snapToggle')).not.toHaveClass(/shapePickOn/);
    });

    test('and snapped to without being shown', async ({ page }) => {
        await snapToGrid(page);
        await showGrid(page, false);

        await openGridMenu(page);
        await expect(page.locator('#snapToggle')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#gridOverlay')).toHaveCount(0);
    });
});

///
///A pitch is how you work on a file rather than what your hand is doing this second, so it comes back -
///unlike the tool, which deliberately does not.
///
test.describe('coming back', () => {
    test('the grid and its pitch survive a reload', async ({ page }) => {
        await showGrid(page);
        await snapToGrid(page);
        await page.locator('#gridPitch').fill('2.5');
        await page.locator('#gridPitch').blur();

        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        //Without the query string, so the session is what opens the file rather than the address.
        await page.goto('/');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        await openGridMenu(page);
        await expect(page.locator('#gridToggle')).toHaveClass(/shapePickOn/);
        await openGridMenu(page);
        await expect(page.locator('#snapToggle')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#gridPitch')).toHaveValue('2.5');
        await expect(page.locator('#gridOverlay')).toHaveCount(1);
    });

    ///
    ///And so does the unit, which is saved beside a pitch held in microns.
    ///
    ///Both, because either one alone comes back wrong: the pitch without the unit reads 0.25 where 250 was
    ///typed, and the unit without the pitch reads 1000 where it should read 250.
    ///
    test('the unit comes back with it', async ({ page }) => {
        await page.locator('#gridUnit').selectOption('Nanometer');

        await page.locator('#gridPitch').fill('250');
        await page.locator('#gridPitch').blur();

        await page.goto('/');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 })
            .toBeGreaterThan(0);

        await expect(page.locator('#gridUnit')).toHaveValue('Nanometer');
        await expect(page.locator('#gridPitch')).toHaveValue('250');
    });
});

///
///Clicking a square fills it, without dragging one out.
///
///Dragging is the way to draw a rectangle of any size and stays that way. But the common thing to want on
///a grid is one square, and dragging across a single cell is a fiddly way to ask for it - so a click, which
///asked for nothing at all before, now fills the cell it landed in.
///
test.describe('clicking a square', () => {
    test.beforeEach(async ({ page }) => {
        //Into the cell, since a shape has to go somewhere.
        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await showGrid(page);
        await snapToGrid(page);

        await page.locator('#drawTool').click();
        await chooseShape(page, '#rectangleShape');
    });

    test('fills exactly one cell of the grid', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();
        const before = await shapeCount(page);

        await page.mouse.click(view.x + 250, view.y + 250);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before + 1);

        const drawn = await shapePoints(page, -1);
        const numbers = drawn.trim().split(/[\s,]+/).map(Number);
        const xs = numbers.filter((_, at) => at % 2 === 0);
        const ys = numbers.filter((_, at) => at % 2 === 1);

        const step = await pitchInUnits(page);

        //One pitch across and one down, on the grid at both ends - whatever the file opened on.
        expect(Math.max(...xs) - Math.min(...xs)).toBe(step);
        expect(Math.max(...ys) - Math.min(...ys)).toBe(step);

        //Abs, because a negative multiple leaves -0 and Object.is tells that from 0.
        expect(Math.abs(Math.min(...xs) % step)).toBe(0);
        expect(Math.abs(Math.min(...ys) % step)).toBe(0);
    });

    ///
    ///The cell the pointer was inside, not the crossing it was nearest.
    ///
    ///A snapped point sits on a corner shared by four squares and cannot say which was meant, so this is
    ///worked out from where the pointer really was. Two clicks either side of a line have to fill different
    ///squares, which is the whole of what that distinction buys.
    ///
    test('fills the square the pointer was in, either side of a line', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 250, view.y + 250);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBeGreaterThan(18);

        const first = await shapePoints(page, -1);

        //A long way off, so it is unambiguously another square.
        await page.mouse.click(view.x + 420, view.y + 250);

        await expect.poll(async () => shapePoints(page, -1), { timeout: 15000 }).not.toBe(first);
    });

    ///Dragging still draws a rectangle of whatever size, which is what the click is beside rather than instead of.
    test('and dragging still draws a bigger one', async ({ page }) => {
        const view = await page.locator('#gdsSVG').boundingBox();

        //Far enough to be several cells at this zoom, so the assertion is about the drag rather than about
        //happening to span exactly one.
        await page.mouse.move(view.x + 200, view.y + 200);
        await page.mouse.down();
        await page.mouse.move(view.x + 600, view.y + 500, { steps: 8 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBeGreaterThan(18);

        const drawn = await shapePoints(page, -1);
        const numbers = drawn.trim().split(/[\s,]+/).map(Number);
        const xs = numbers.filter((_, at) => at % 2 === 0);

        expect(Math.max(...xs) - Math.min(...xs)).toBeGreaterThan(UNITS_PER_MICRON);
    });
});

///
///The grid reaches the edges of the view, whatever shape the window is.
///
///**The viewBox is not what is on screen.** The SVG sets no `preserveAspectRatio`, so a box that is not the
///element's shape is fitted inside it and centered - a square box in a wide view leaves a band of layout
///down each side that the box says nothing about. Drawing the grid over the box left those bands bare, and
///it looked like the grid had simply stopped.
///
///Checked at two window shapes, because a square one hides it completely: the bug only appears when the
///element and the box disagree.
///
test.describe('covering the view', () => {
    for (const [name, size] of [['a wide window', { width: 1400, height: 700 }], ['a tall one', { width: 700, height: 1000 }]]) {
        test(`the grid reaches every edge in ${name}`, async ({ page }) => {
            await page.setViewportSize(size);

            await showGrid(page);

            await expect(page.locator('#gridOverlay')).toHaveCount(1);

            const covered = await page.evaluate(() => {
                const view = document.getElementById('gdsSVG').getBoundingClientRect();
                const grid = document.getElementById('gridOverlay').getBoundingClientRect();

                return {
                    left: grid.left <= view.left,
                    right: grid.right >= view.right,
                    top: grid.top <= view.top,
                    bottom: grid.bottom >= view.bottom
                };
            });

            expect(covered).toEqual({ left: true, right: true, top: true, bottom: true });
        });
    }
});

///
///**The fit frames the layout, not the grid.**
///
///fitToDrawing measured svg.getBBox(), which is every child of the SVG - and the grid is drawn across
///whatever is on screen rather than around the geometry. So with the grid on when a file opened, the fit
///framed the grid: measured on this file, a box 40,000 units square against a layout 2,800 by 1,500, for a
///viewBox of 22,000 where 3,080 was wanted. The layout came up seven times too small in an empty view and
///nothing could be clicked, because no shape was where it looked like being.
///
///It reads as a bug about the grid and is a bug about the fit - the grid was measured when it should not
///have been, and so were the ruler and the rest of the furniture drawn over the layout.
///
///Fitted again with the grid taken away rather than reloading, which would only put the default back. The
///claim is that the two agree, so both readings come from the same page a moment apart.
///
test('the fit frames the layout whether the grid is drawn or not', async ({ page }) => {
    //The grid is on out of the box, so this is the reading that used to be wrong.
    await expect(page.locator('#gridOverlay')).toHaveCount(1);

    const withGrid = await page.locator('#gdsSVG').getAttribute('viewBox');

    //A layout 2,800 units across with a tenth of a margin is 3,080. The old reading was 22,000.
    expect(Number(withGrid.split(' ')[2])).toBeLessThan(4000);

    await showGrid(page, false);

    await expect(page.locator('#gridOverlay')).toHaveCount(0);

    //Framed again, with nothing over the layout to measure by mistake.
    await page.evaluate(() => fitToDrawing());

    expect(await page.locator('#gdsSVG').getAttribute('viewBox')).toBe(withGrid);
});

///
///The three switches go when the pointer leaves, and can be reached without them going first.
///
///They used to stay up until something was pressed, which for a menu you open by pointing at an icon is a
///panel you have to remember to put down. Every other panel in the bar closes on the way out; this is the
///one that did not.
///
///The reachable half is the part that is easy to get wrong: the menu hangs twelve pixels below the column,
///and if that twelve is a gap rather than part of the menu's own box then crossing it *is* leaving, and the
///menu shuts on the way in. Stepped, or the move is one event at the destination and jumps over it.
///
test.describe('the grid menu goes when the pointer does', () => {
    test('the pointer can travel from the icon onto the menu', async ({ page }) => {
        const icon = await page.locator('#gridMenu').boundingBox();

        await openGridMenu(page);

        const menu = await page.locator('#gridPicker').boundingBox();

        await page.mouse.move(icon.x + (icon.width / 2), menu.y + 30, { steps: 20 });

        await expect(page.locator('#gridPicker')).toBeVisible();

        //And the switches are live once you get there.
        await expect(page.locator('#snapToggle')).toBeVisible();
    });

    test('moving away puts them down', async ({ page }) => {
        await openGridMenu(page);

        await page.mouse.move(4, 4);

        await expect(page.locator('#gridPicker')).toHaveCount(0);

        //And pointing at the icon brings them back, so leaving is not a one-way door.
        await openGridMenu(page);

        await expect(page.locator('#gridPicker')).toBeVisible();
    });
});
