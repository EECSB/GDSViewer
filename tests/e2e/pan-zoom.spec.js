//Moving around the 2D drawing: a drag, the wheel and the middle button move the picture already drawn, and the
//viewBox is put on once the gesture stops.
//
//**Why the picture is moved rather than drawn again.** A new viewBox is a new picture, and the browser rasters
//all of it: on the RGB mixer a few notches in from its fit that was 117 ms a frame, every frame of a drag. A
//picture moved by a CSS transform is the compositor's work and costs a frame nothing - see showViewBox in
//JavaScriptInterOp.js. What these pin is what that changes and what it must not: the layout stays under the
//pointer the whole way, the viewBox is set once per gesture rather than once per move, and what a gesture
//uncovers has the drawing on it.
//
//A frame's cost is not asserted - it depends on the machine, and the suite runs on shared runners. Counting
//the viewBox being set is the same everywhere, and setting it per move is what made a frame cost a raster.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, viewSettled, MOSFET } = require('./helpers');

//The smallest bundled design above Viewer2DSvg.CullAbove, so the view draws only what is near the screen.
const CULLED = 'sky130_ef_ip__rc_osc_500k';

//How far a point may land from where the pointer is, in screen pixels: rounding in the matrix and the attribute.
const NEAR = 2;

///Counts every change to the SVG's viewBox attribute from here on, in the page.
async function countViewBoxChanges(page) {
    await page.evaluate(() => {
        window.__viewBoxChanges = 0;

        new MutationObserver(records => {
            window.__viewBoxChanges += records.length;
        }).observe(document.getElementById('gdsSVG'), { attributes: true, attributeFilter: ['viewBox'] });
    });
}

async function viewBoxChanges(page) {
    return page.evaluate(() => window.__viewBoxChanges);
}

///The layout point under a spot on the screen, through the SVG's own matrix - which includes the shift.
async function layoutAt(page, spot) {
    return page.evaluate((spot) => {
        const svg = document.getElementById('gdsSVG');
        const point = svg.createSVGPoint();

        point.x = spot.x;
        point.y = spot.y;

        const layout = point.matrixTransform(svg.getScreenCTM().inverse());

        return { x: layout.x, y: layout.y };
    }, spot);
}

async function expectUnder(page, layout, spot) {
    const now = await page.evaluate((layout) => {
        const svg = document.getElementById('gdsSVG');
        const point = svg.createSVGPoint();

        point.x = layout.x;
        point.y = layout.y;

        const screen = point.matrixTransform(svg.getScreenCTM());

        return { x: screen.x, y: screen.y };
    }, layout);

    expect(Math.abs(now.x - spot.x)).toBeLessThanOrEqual(NEAR);
    expect(Math.abs(now.y - spot.y)).toBeLessThanOrEqual(NEAR);
}

async function middleOfDrawing(page) {
    const box = await page.locator('#gdsSVG').boundingBox();

    return { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };
}

async function transformOf(page) {
    return page.evaluate(() => document.getElementById('gdsSVG').style.transform);
}

///Notches of the wheel over a spot, dispatched in the page in one go so that they are one burst on any machine.
async function wheelBurst(page, spot, notches, deltaY) {
    await page.evaluate(({ spot, notches, deltaY }) => {
        const wrapper = document.getElementById('svgWrapper');

        for (let i = 0; i < notches; i++)
            wrapper.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: deltaY, clientX: spot.x, clientY: spot.y }));
    }, { spot: spot, notches: notches, deltaY: deltaY });
}

test.describe('a small layout', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
        await viewSettled(page);
    });

    ///
    ///Held part way, the picture has moved and the viewBox has not; let go, the viewBox goes on once and the
    ///picture's shift comes off - and the point the press went down on is under the pointer throughout.
    ///
    test('a drag moves the picture under the pointer, and puts the viewBox on once it is let go', async ({ page }) => {
        const from = await middleOfDrawing(page);
        const to = { x: from.x + 140, y: from.y + 70 };
        const held = await layoutAt(page, from);
        const before = await page.locator('#gdsSVG').getAttribute('viewBox');

        await countViewBoxChanges(page);

        await page.mouse.move(from.x, from.y);
        await page.mouse.down();
        await page.mouse.move(to.x, to.y, { steps: 10 });

        expect(await viewBoxChanges(page)).toBe(0);
        expect(await transformOf(page)).toMatch(/^matrix\(/);
        await expectUnder(page, held, to);

        await page.mouse.up();
        await viewSettled(page);

        expect(await viewBoxChanges(page)).toBe(1);
        expect(await page.locator('#gdsSVG').getAttribute('viewBox')).not.toBe(before);
        await expectUnder(page, held, to);
    });

    ///
    ///**Pan shows an open hand** (asked for 2026-10-30), so it can be told from Select and Move at a glance: a drag
    ///closes it and letting go opens it again, another tool shows its own, and coming back to Pan brings the hand
    ///back. The middle button closes the hand over any tool, and gives the tool its own cursor back after.
    ///
    test('Pan shows an open hand that closes while the view is dragged', async ({ page }) => {
        const cursor = () => page.locator('#gdsSVG').evaluate(svg => getComputedStyle(svg).cursor);
        const from = await middleOfDrawing(page);

        await expect.poll(cursor).toBe('grab');

        await page.mouse.move(from.x, from.y);
        await page.mouse.down();
        await page.mouse.move(from.x + 60, from.y + 30, { steps: 5 });

        expect(await cursor()).toBe('grabbing');

        await page.mouse.up();

        expect(await cursor()).toBe('grab');

        await page.locator('#measureTool').click();
        await expect.poll(cursor).toBe('crosshair');

        await page.locator('#selectTool').click();
        await expect.poll(cursor).toBe('pointer');

        await page.mouse.move(from.x, from.y);
        await page.mouse.down({ button: 'middle' });

        expect(await cursor()).toBe('grabbing');

        await page.mouse.up({ button: 'middle' });

        expect(await cursor()).toBe('pointer');

        await page.locator('#panTool').click();
        await expect.poll(cursor).toBe('grab');
    });

    ///Three notches are one zoom about the pointer, put on as one viewBox rather than three.
    test('the wheel scales the picture about the pointer, and puts the viewBox on once it stops', async ({ page }) => {
        const middle = await middleOfDrawing(page);
        const spot = { x: middle.x - 90, y: middle.y + 40 };
        const held = await layoutAt(page, spot);
        const width = Number((await page.locator('#gdsSVG').getAttribute('viewBox')).split(/\s+/)[2]);

        await countViewBoxChanges(page);

        await wheelBurst(page, spot, 3, -100);

        await expectUnder(page, held, spot);

        await viewSettled(page);

        expect(await viewBoxChanges(page)).toBe(1);
        expect(Number((await page.locator('#gdsSVG').getAttribute('viewBox')).split(/\s+/)[2])).toBeLessThan(width);
        await expectUnder(page, held, spot);
    });

    ///
    ///**The grid is drawn past the edges of the view**, by the margin the shapes are, so a drag uncovers grid
    ///lines as well as shapes rather than bare ground until it is let go.
    ///
    test('the grid reaches past the edges of the view', async ({ page }) => {
        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        const reach = await page.evaluate(() => {
            const svg = document.getElementById('gdsSVG');
            const rect = svg.getBoundingClientRect();
            const inverse = svg.getScreenCTM().inverse();
            const corner = (x, y) => {
                const point = svg.createSVGPoint();

                point.x = x;
                point.y = y;

                return point.matrixTransform(inverse);
            };

            const topLeft = corner(rect.left, rect.top);
            const bottomRight = corner(rect.right, rect.bottom);
            const grid = document.getElementById('gridOverlay').getBBox();
            const width = bottomRight.x - topLeft.x;
            const height = bottomRight.y - topLeft.y;

            return {
                left: (topLeft.x - grid.x) / width,
                right: ((grid.x + grid.width) - bottomRight.x) / width,
                top: (topLeft.y - grid.y) / height,
                bottom: ((grid.y + grid.height) - bottomRight.y) / height
            };
        });

        //Half a view each way, less a grid square's worth of rounding outwards.
        for (const side of ['left', 'right', 'top', 'bottom'])
            expect(reach[side]).toBeGreaterThan(0.4);
    });
});

test.describe('a layout drawn only near the screen', () => {
    test.describe.configure({ timeout: 180000 });

    ///What is drawn, as a list of which elements each path drew - the same list for the same picture.
    async function picture(page) {
        return page.evaluate(() => [...document.querySelectorAll('#gdsSVG path[data-elements]')]
            .map(path => path.getAttribute('data-elements'))
            .join('|'));
    }

    test.beforeEach(async ({ page }) => {
        await gotoExample(page, CULLED, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 120000 }).toBeGreaterThan(0);
        await viewSettled(page);

        //In by a factor of four, so the layout runs well past the view on every side and only part of it is drawn.
        const shown = await picture(page);

        await wheelBurst(page, await middleOfDrawing(page), 12, -100);
        await viewSettled(page);

        await expect.poll(async () => picture(page), { timeout: 60000 }).not.toBe(shown);
    });

    ///
    ///**A middle-button drag is redrawn for, like any other.** It used to end without telling C# where the view
    ///had gone, so a drag past the margin left the picture drawn for where the view had been - and with the
    ///picture moved rather than redrawn, it would also have left the viewBox where it was.
    ///
    ///
    ///Begun and ended inside the view. A pointer let go outside it leaves the SVG afterwards, and the leave is
    ///handled as the end of an ordinary pan - which reported for the middle button by accident, and hid the fault.
    ///
    test('a middle-button drag past the margin is drawn for where the view went', async ({ page }) => {
        const drawn = await picture(page);
        const view = await page.locator('#gdsSVG').boundingBox();
        const from = { x: view.x + (view.width * 0.9), y: view.y + (view.height / 2) };

        await page.mouse.move(from.x, from.y);
        await page.mouse.down({ button: 'middle' });
        await page.mouse.move(view.x + (view.width * 0.1), from.y, { steps: 10 });
        await page.mouse.up({ button: 'middle' });

        await viewSettled(page);

        await expect.poll(async () => picture(page), { timeout: 60000 }).not.toBe(drawn);
    });

    ///
    ///**A pan that stays inside what was drawn draws nothing again.** The picture is drawn half a view past every
    ///edge for exactly this, and every pan rebuilt it all the same: the box arrived grown and was compared with the
    ///last grown box, and any pan at all moves a grown box out of the last one on its leading edge. On the RGB mixer
    ///that was a tenth of a second and more after every drag, however short.
    ///
    ///A drawing built again for the moved view would be a different list, since what is near its edges changes;
    ///the same list is the picture left alone. Given a second and a half to change, which a rebuild never needs.
    ///
    test('a pan that stays inside the margin keeps the picture it has', async ({ page }) => {
        const drawn = await picture(page);
        const view = await page.locator('#gdsSVG').boundingBox();
        const from = { x: view.x + (view.width * 0.6), y: view.y + (view.height / 2) };

        //A fifth of the view across and a tenth down, well inside half a view.
        await page.mouse.move(from.x, from.y);
        await page.mouse.down({ button: 'middle' });
        await page.mouse.move(from.x - (view.width * 0.2), from.y - (view.height * 0.1), { steps: 6 });
        await page.mouse.up({ button: 'middle' });

        await viewSettled(page);
        await page.waitForTimeout(1500);

        expect(await picture(page)).toBe(drawn);
    });

    ///
    ///**And so does a zoom out that stays inside it**, which leaves the picture holding shapes that have fallen under
    ///a pixel since - more than needs drawing rather than less. A zoom in draws again, since a shape left out for being
    ///under a pixel may be over one now.
    ///
    test('a zoom out inside the margin keeps the picture, and a zoom in draws again', async ({ page }) => {
        const drawn = await picture(page);
        const middle = await middleOfDrawing(page);

        //Two notches out about the middle is a view a quarter bigger, well inside one drawn twice its size.
        await wheelBurst(page, middle, 2, 100);
        await viewSettled(page);
        await page.waitForTimeout(1500);

        expect(await picture(page)).toBe(drawn);

        //And four back in, past where it was drawn.
        await wheelBurst(page, middle, 4, -100);
        await viewSettled(page);

        await expect.poll(async () => picture(page), { timeout: 60000 }).not.toBe(drawn);
    });
});
