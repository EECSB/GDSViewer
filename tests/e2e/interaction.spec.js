//The pointer and the keyboard while something is being drawn or dragged.
//
//These are the three things a browser does on its own that an editor has to take back: it selects text
//under a drag, it stops sending pointer events to an element the drag has wandered out of, and it has no
//opinion about Escape. Each of them looks like the app misbehaving rather than like the browser doing
//something reasonable, which is why they are worth pinning.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, shapePoints, chooseShape } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    //Into the cell, so drawing and editing are allowed.
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
});

///
///A drag across the labels selects none of them.
///
///The pin names are `<text>`, so without saying otherwise the browser treats the layout as a document and
///a drag as a selection - which highlights the names, and can hand the gesture to the browser's own drag
///machinery partway through.
///
test('dragging over the labels selects no text', async ({ page }) => {
    const view = await page.locator('#gdsSVG').boundingBox();

    //
    //Started clear of the selection panel, which the click above opened.
    //
    //The panel is HTML text over the top left of the view, and text in it selects the way text in a page
    //does - which is fine, and is not what this is about. Beginning inside it made the drag a drag on the
    //panel, so the labels underneath were never crossed at all and the spec passed on the wrong thing until
    //a row of numbers moved under the path it took.
    //
    const panel = await page.locator('#selectionPanel').boundingBox();

    let from = view.x + 60;

    if (panel !== null && panel.y < view.y + (view.height / 2) && panel.y + panel.height > view.y + (view.height / 2))
        from = panel.x + panel.width + 20;

    await page.mouse.move(from, view.y + (view.height / 2));
    await page.mouse.down();

    for (let step = 1; step <= 6; step++)
        await page.mouse.move(from + (step * 60), view.y + (view.height / 2) + (step * 8));

    await page.mouse.up();

    const selected = await page.evaluate(() => (window.getSelection()?.toString() ?? '').trim());

    expect(selected).toBe('');
});

test.describe('Escape while drawing', () => {
    test.beforeEach(async ({ page }) => {
        await page.locator('#drawTool').click();
    });

    ///The case that had no way out but to finish the shape and undo it.
    test('abandons a rectangle being dragged out', async ({ page }) => {
        await chooseShape(page, '#rectangleShape');

        const view = await page.locator('#gdsSVG').boundingBox();
        const before = await shapeCount(page);

        await page.mouse.move(view.x + 200, view.y + 200);
        await page.mouse.down();
        await page.mouse.move(view.x + 380, view.y + 340, { steps: 6 });

        await page.keyboard.press('Escape');

        //The preview goes at once.
        await expect(page.locator('#drawPreview')).toHaveCount(0);

        await page.mouse.up();

        //And letting go afterwards adds nothing, which is the half that matters.
        await page.waitForTimeout(800);

        expect(await shapeCount(page)).toBe(before);
        await expect(page.locator('#undoEdit')).toHaveCount(0);
    });

    test('and a polygon part way through its corners', async ({ page }) => {
        await chooseShape(page, '#polygonShape');

        const view = await page.locator('#gdsSVG').boundingBox();
        const before = await shapeCount(page);

        await page.mouse.click(view.x + 200, view.y + 200);
        await page.mouse.click(view.x + 300, view.y + 200);
        await page.mouse.click(view.x + 300, view.y + 300);

        await expect(page.locator('#drawPreview')).toHaveCount(1);

        await page.keyboard.press('Escape');

        await expect(page.locator('#drawPreview')).toHaveCount(0);

        expect(await shapeCount(page)).toBe(before);
    });
});


///
///**The selection panel is a surface, not a window.**
///
///It was `pointer-events: none` back when it was a readout, and each control added since put pointer events
///back on itself. The gaps between them did not, so a press on the panel's own background went through to
///the layout and chose whatever was behind it - reaching for a button and missing by two pixels threw away
///the selection the panel was describing.
///
test('the selection panel takes its own clicks rather than passing them through', async ({ page }) => {
    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#selectionPanel')).toBeVisible();

    const chosen = await page.locator('#gdsSVG .shapeSelected').count();

    expect(chosen).toBeGreaterThan(0);

    //A point on the panel where no control is - which is the case that used to fall through.
    const bare = await page.evaluate(() => {
        const panel = document.getElementById('selectionPanel');
        const box = panel.getBoundingClientRect();

        for (let y = box.top + 4; y < box.bottom - 4; y += 3) {
            for (let x = box.left + 4; x < box.right - 4; x += 3) {
                if (document.elementFromPoint(x, y) === panel)
                    return { x, y };
            }
        }

        return null;
    });

    expect(bare, 'the panel has no bare background to press').not.toBeNull();

    await page.mouse.click(bare.x, bare.y);

    //Still whatever was chosen before, rather than something found underneath the panel.
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(chosen);
    await expect(page.locator('#selectionPanel')).toBeVisible();
});

///
///**The middle button pans, whatever tool is in hand.**
///
///Panning is how you get to the part of the layout you want to use a tool on, and going up to the toolbar
///to move the view and again to come back is an interruption in the middle of the thing you were doing.
///
test.describe('the middle button', () => {
    const boxOf = (page) => page.locator('#gdsSVG').getAttribute('viewBox');

    async function middleDrag(page) {
        const view = await page.locator('#gdsSVG').boundingBox();
        const at = { x: view.x + (view.width / 2), y: view.y + (view.height / 2) };

        await page.mouse.move(at.x, at.y);
        await page.mouse.down({ button: 'middle' });
        await page.mouse.move(at.x + 120, at.y + 90, { steps: 5 });
        await page.mouse.up({ button: 'middle' });
    }

    test('pans while the Select tool is out, and leaves the selection alone', async ({ page }) => {
        await page.locator('#selectTool').click();

        const before = await boxOf(page);

        //Something is already chosen: entering the cell is a click on a shape. That is the point - the
        //drag must not disturb it.
        const chosen = await page.locator('#gdsSVG .shapeSelected').count();

        expect(chosen).toBeGreaterThan(0);

        await middleDrag(page);

        expect(await boxOf(page)).not.toBe(before);

        //The tool never heard the press, so it must not read the release as a click on wherever the pan
        //happened to finish.
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(chosen);
    });

    test('and while the Draw tool is out, without drawing anything', async ({ page }) => {
        const was = await shapeCount(page);

        await page.locator('#drawTool').click();
        await chooseShape(page, '#rectangleShape');

        const before = await boxOf(page);

        await middleDrag(page);

        expect(await boxOf(page)).not.toBe(before);
        expect(await shapeCount(page)).toBe(was);
    });

    ///The left button still belongs to the tool, so panning with it is still Pan's job alone.
    test('leaves the left button to the tool', async ({ page }) => {
        await page.locator('#selectTool').click();

        const before = await boxOf(page);
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width / 2), view.y + (view.height / 2));
        await page.mouse.down();
        await page.mouse.move(view.x + (view.width / 2) + 120, view.y + (view.height / 2) + 90, { steps: 5 });
        await page.mouse.up();

        expect(await boxOf(page)).toBe(before);
    });
});
