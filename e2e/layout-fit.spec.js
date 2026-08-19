//The app fits the window rather than the window fitting the app.
//
//The view used to be 55em tall - a height picked to look right on one screen, which on anything shorter
//pushed the page into a scrollbar and ran the drawing off the bottom of it. It is sized by what is left
//over now, which is a chain of flex rules through five elements: break any link and the page grows a
//scrollbar again, and nothing but a real browser lays that out.
//
//The canvas is the other half of it. A drawing surface has a size of its own that CSS does not touch, so
//fitting the box it sits in is something the view has to be told to do - and now that the box moves for
//reasons other than the window moving, being told by a window event is not enough.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, MOSFET } = require('./helpers');

///Whether the page as a whole overflows the window. A pixel of slack for sub-pixel layout.
async function pageScrolls(page) {
    return page.evaluate(() => document.documentElement.scrollHeight > window.innerHeight + 1);
}

///
///Overflow in both directions at once, against the *client* box rather than the window.
///
///Both, because the two make each other: twelve pixels past the right edge is a horizontal scrollbar,
///that bar takes fifteen pixels off the bottom, and the page that fitted exactly is now fifteen too tall.
///Measured against clientWidth and clientHeight, which is what is left once a bar has appeared - reading
///innerWidth hides the very thing this is looking for.
///
async function pageOverflow(page) {
    return page.evaluate(() => {
        const root = document.documentElement;

        return {
            across: root.scrollWidth - root.clientWidth,
            down: root.scrollHeight - root.clientHeight
        };
    });
}

///
///What is left of the container once the canvas is in it, measured sub-pixel on every side.
///
///The other helper here rounds - offsetWidth and clientHeight are integers - which is exactly the kind of
///reading that let this go wrong: the canvas was sized from a rounded box with three pixels taken off it
///"to account for border", on a container that has no border, and the strip of gray under the scene was
///2.8 pixels. Anything that rounds cannot see that, and neither can a check with four pixels of slack.
///
async function canvasGaps(page) {
    return page.evaluate(() => {
        const container = document.getElementById('container');
        const canvas = container?.querySelector('canvas');

        if (canvas == null)
            return null;

        const box = container.getBoundingClientRect();
        const drawn = canvas.getBoundingClientRect();

        return {
            top: drawn.top - box.top,
            left: drawn.left - box.left,
            right: box.right - drawn.right,
            bottom: box.bottom - drawn.bottom
        };
    });
}

///The box a view is given, and the box the drawing surface in it actually takes.
async function canvasFit(page) {
    return page.evaluate(() => {
        const container = document.getElementById('container');
        const canvas = container?.querySelector('canvas');

        if (canvas == null)
            return null;

        return {
            container: { width: container.offsetWidth, height: container.offsetHeight },
            canvas: { width: canvas.clientWidth, height: canvas.clientHeight }
        };
    });
}

test('the page fits the window at any height, without a scrollbar', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    //A tall window, a laptop, and something shorter than the old fixed height by a long way.
    for (const height of [1000, 700, 480]) {
        await page.setViewportSize({ width: 1400, height });

        await expect.poll(async () => pageScrolls(page)).toBe(false);
    }
});

///
///The view is what gives way, not the page.
///
///Asserted as a *change* rather than against a number: the point is that the space is shared out again
///when there is less of it, and a spec that demanded a particular pixel height would have to be rewritten
///every time a control above the view changed size.
///
test('the view shrinks with the window instead of overflowing it', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    await page.setViewportSize({ width: 1400, height: 1000 });

    const tall = await page.locator('.viewPane').evaluate(node => node.clientHeight);

    await page.setViewportSize({ width: 1400, height: 600 });

    await expect.poll(async () => page.locator('.viewPane').evaluate(node => node.clientHeight))
        .toBeLessThan(tall - 300);
});

///
///Full screen, which is the page's own margins rather than the browser's.
///
///The four rems above and below the content and the one and a half at each side are the only padding in
///the chain between the window and the view, and on a laptop they are a fifth of the height. Taking them
///away is the whole of the feature - so this asserts the view *grew*, not that it grew by a number, and
///that the bar above it is still there, since a full screen that swallowed the toolbar would be a
///different thing entirely.
///
test('full screen gives the view the page margins, and gives them back', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });

    await gotoExample(page, MOSFET, '2d');
    await expect(page.locator('#gdsSVG')).toBeVisible();

    const before = await page.locator('#gdsSVG').boundingBox();
    const bar = await page.locator('.top-row').boundingBox();

    await page.locator('#fullScreen').click();

    //Both dimensions, because the padding is on all four sides.
    await expect.poll(async () => {
        const now = await page.locator('#gdsSVG').boundingBox();

        return now.height > before.height && now.width > before.width;
    }).toBe(true);

    //The bar it is named for keeping, unmoved.
    await expect(page.locator('.top-row')).toBeVisible();
    expect(await page.locator('.top-row').boundingBox()).toEqual(bar);

    //And no scrollbar bought with the extra room.
    expect(await pageScrolls(page)).toBe(false);

    await page.locator('#fullScreen').click();

    await expect.poll(async () => {
        const now = await page.locator('#gdsSVG').boundingBox();

        return now.height === before.height && now.width === before.width;
    }).toBe(true);
});

///
///All the way to the edges, which is not what taking the padding away gets you on its own.
///
///`#mainAppContainer` is capped at `max-width: 80%`, and that is what held the app off the sides - at 1905
///wide it left 181 pixels of bare page either side, in full screen and out of it. The padding was never the
///thing. Asserted against the window rather than against a number, since the whole claim is "the window".
///
test('full screen reaches the sides of the window', async ({ page }) => {
    await page.setViewportSize({ width: 1400, height: 800 });

    await gotoExample(page, MOSFET, '2d');
    await expect(page.locator('#gdsSVG')).toBeVisible();

    await page.locator('#fullScreen').click();

    //The view's left edge at the window's, and the sidebar's right edge at the window's. Two pixels of
    //slack for the view's own border, which is drawn inside it.
    await expect.poll(async () => {
        const edges = await page.evaluate(() => {
            const view = document.querySelector('#gdsSVG').getBoundingClientRect();
            const sidebar = document.querySelector('#layerSidebar').getBoundingClientRect();

            return { left: view.left, right: document.documentElement.clientWidth - sidebar.right };
        });

        return edges.left <= 2 && edges.right <= 2;
    }).toBe(true);
});

///
///The button sits on the bar's line, at the far end of it, inset by what the first control is inset by.
///
///It was in the corner - top-aligned and with its gutter taken off - on the reading that a button about the
///window belongs where nothing else is. That held while the bar was a mix of heights and stopped holding
///once the rest became one row of squares of one size: what it produced then was one control a size and a
///line away from every other, and a bar with 22px of air at one end and 12 at the other.
///
///A wide window on purpose. The bar wraps when it runs out of room, and every statement here is about a
///bar that is one row - which is the state e2e/app-launch.spec.js pins across the widths that matter.
///
test('the full-screen button ends the bar the way the first control starts it', async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 800 });

    await gotoExample(page, MOSFET, '2d');
    await expect(page.locator('#fullScreen')).toBeVisible();

    const placed = await page.evaluate(() => {
        const bar = document.querySelector('.viewToolbar').getBoundingClientRect();

        const button = document.querySelector('#fullScreen').getBoundingClientRect();
        const open = document.querySelector('label[for="fileUpload"]').getBoundingClientRect();

        return {
            bottoms: [Math.round(button.bottom), Math.round(open.bottom)],
            ends: [Math.round(open.left - bar.left), Math.round(bar.right - button.right)],

            //Still at the far end: every other column ends before it starts. Its own column is left out -
            //that one is 10px wider than the button, since the gutter it keeps is inside it.
            last: [...document.querySelector('.viewToolbar').children]
                .filter(node => !node.classList.contains('toolbarEnd'))
                .every(node => node.getBoundingClientRect().right <= button.left)
        };
    });

    //On the line the rest of the bar sits on.
    expect(placed.bottoms[0]).toBe(placed.bottoms[1]);

    //Inset from its end by what Open is inset from the other.
    expect(placed.ends[0]).toBe(placed.ends[1]);

    expect(placed.last).toBe(true);
});

///
///And brings no scrollbar with it, on the file and the window that produced one.
///
///Bootstrap lays a `.row` out on negative margins and relies on an ancestor's padding to absorb them, so
///taking the padding away put the outermost row twelve pixels past the right edge. That is a horizontal
///scrollbar; a horizontal scrollbar costs fifteen pixels of height; and the opacity slider at the foot of
///the sidebar went under it. Reported as "it overflows vertically", which it did - as a consequence.
///
///A twenty-two layer cell rather than Mosfet's nine, and a short window, because the sidebar is the tallest
///thing on the page and the bottom of it is what gets clipped.
///
test('full screen brings no scrollbar, in either direction', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 600 });

    await gotoExample(page, 'sky130_fd_sc_hd__a21bo_2', '2d');
    await expect(page.locator('#gdsSVG')).toBeVisible();

    await page.locator('#fullScreen').click();

    await expect.poll(async () => pageOverflow(page)).toEqual({ across: 0, down: 0 });

    //And the control at the very bottom of the sidebar is inside the window rather than under a bar.
    const opacity = await page.locator('.layerOpacity').boundingBox();
    const room = await page.evaluate(() => document.documentElement.clientHeight);

    expect(opacity.y + opacity.height).toBeLessThanOrEqual(room);
});

///
///The 3D canvas matches the box it is in.
///
///It is sized by three, in pixels, at the moment the viewer is built - so it keeps whatever size the box
///had then. The box is laid out by the window now, and .viewPane clips the overflow, which is what made
///this worth a test: a canvas hundreds of pixels too wide looks like nothing at all until you notice the
///drawing is cropped and the perspective is stretched.
///
test('the 3D canvas follows its box when the window is resized', async ({ page }) => {
    await page.setViewportSize({ width: 1400, height: 900 });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => {
        const fit = await canvasFit(page);

        return fit && Math.abs(fit.canvas.width - fit.container.width);
    }).toBeLessThanOrEqual(4);

    await page.setViewportSize({ width: 900, height: 620 });

    await expect.poll(async () => {
        const fit = await canvasFit(page);

        return fit && Math.abs(fit.canvas.width - fit.container.width);
    }).toBeLessThanOrEqual(4);
});

///
///And when the box changes for a reason the window knows nothing about.
///
///This is the case a window listener cannot cover, and the reason the view watches its own container
///instead. Leaving the view and coming back re-mounts it inside whatever the layout is by then; the
///sidebar widening under a longer layer name does the same thing without any navigation at all.
///
test('the 3D canvas follows its box when the layout changes without the window', async ({ page }) => {
    await page.setViewportSize({ width: 1200, height: 800 });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    //Take room away from the view without touching the window.
    await page.addStyleTag({ content: '#layerSidebar { width: 420px !important; }' });

    await expect.poll(async () => {
        const fit = await canvasFit(page);

        return fit && Math.abs(fit.canvas.width - fit.container.width);
    }).toBeLessThanOrEqual(4);

    //And give it back.
    await page.evaluate(() => {
        for (const style of document.querySelectorAll('style'))
            if (style.textContent.includes('#layerSidebar'))
                style.remove();
    });

    await expect.poll(async () => {
        const fit = await canvasFit(page);

        return fit && Math.abs(fit.canvas.width - fit.container.width);
    }).toBeLessThanOrEqual(4);
});

///
///A view that is not on screen measures zero, and 0/0 is a NaN the camera never comes back from.
///
///Switching away hides the 3D view rather than always unmounting it, and an observer reports that as a
///size of nothing. Feeding it through would put a NaN in the projection matrix and the scene would simply
///be gone on the way back - so the last good size is kept instead.
///
test('the 3D scene survives the view being hidden and shown', async ({ page }) => {
    await page.setViewportSize({ width: 1200, height: 800 });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    await selectView(page, 'View2DSvg');
    await expect(page.locator('#gdsSVG')).toBeVisible();

    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => {
        const fit = await canvasFit(page);

        if (fit == null)
            return null;

        return fit.canvas.width > 0 && Math.abs(fit.canvas.width - fit.container.width) <= 4;
    }).toBe(true);
});

///
///The 3D canvas fills its container exactly, leaving no strip of it showing.
///
///Separate from the two above, which allow four pixels of slack and read rounded sizes - deliberately, so
///they survive sub-pixel layout while asking "does the canvas follow its box". This one asks the narrower
///question they cannot: is there any of the container left over. There was 2.8 pixels of it under the
///scene, from a hardcoded `- 3` on a rounded height, and four pixels of tolerance would never have said so.
///
///After a resize as well as at rest, since the size is applied in two places - once directly when the view
///is built, and once from the ResizeObserver - and only the second one runs when the box moves.
///
test('the 3D canvas leaves no part of its container showing', async ({ page }) => {
    await page.setViewportSize({ width: 1301, height: 733 });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    //A pixel of slack for sub-pixel layout, and no more - the bug this catches was 2.8.
    const fits = gaps => gaps !== null
        && Math.abs(gaps.top) <= 1 && Math.abs(gaps.left) <= 1
        && Math.abs(gaps.right) <= 1 && Math.abs(gaps.bottom) <= 1;

    await expect.poll(async () => fits(await canvasGaps(page)), { timeout: 15000 }).toBe(true);

    //An odd size, where a rounded reading is most likely to be a pixel out.
    await page.setViewportSize({ width: 1207, height: 641 });

    await expect.poll(async () => fits(await canvasGaps(page)), { timeout: 15000 }).toBe(true);
});
