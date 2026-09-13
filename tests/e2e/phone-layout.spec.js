//A phone: the drawing gets the whole width, the page keeps no margins, and the sidebars go under the view.
//
//Below 768 pixels the layer sidebar and the cell tree stop sharing a row with the drawing - see the phone
//blocks in wwwroot/css/app.css. Before them a phone drew nothing at all: at 375 wide the two sidebars wanted
//496 pixels of a 345-pixel row, the drawing was left zero, and the page could be dragged four pixels sideways.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    layerCheckbox,
    svgCounts,
    MOSFET,
    MOSFET_POLYGONS
} = require('./helpers');

const PHONE = { width: 375, height: 812 };

///
///Where an element is, in page coordinates rather than window ones, so "under" survives a scroll.
///
///null for an element that is not there, which is what a sidebar the text view does not have looks like.
///
async function box(page, selector) {
    return page.evaluate((selector) => {
        const element = document.querySelector(selector);

        if (element === null)
            return null;

        const rect = element.getBoundingClientRect();

        return {
            left: rect.left + scrollX,
            top: rect.top + scrollY,
            right: rect.right + scrollX,
            bottom: rect.bottom + scrollY,
            width: rect.width,
            height: rect.height
        };
    }, selector);
}

///How much wider the page is than the window. Zero when nothing hangs past an edge.
async function sidewaysOverflow(page) {
    return page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
}

///A dimension of an element that may not be there yet, as 0 until it is.
async function sizeOf(page, selector, dimension) {
    const found = await box(page, selector);

    if (found === null)
        return 0;

    return found[dimension];
}

test.describe('on a phone', () => {
    test.use({ viewport: PHONE, isMobile: true, hasTouch: true });

    ///
    ///The case that drew nothing: the 2D view with the cell tree open, beside the layer sidebar.
    ///
    ///Polled, since the SVG is sized after the view mounts. A few pixels short of the window for the pane's
    ///own border, and more than half the window tall - it is given 70svh.
    ///
    test('the drawing takes the width of the screen, where it used to be squeezed to nothing', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d', true);

        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

        await expect.poll(async () => sizeOf(page, '#gdsSVG', 'width')).toBeGreaterThanOrEqual(PHONE.width - 4);
        await expect.poll(async () => sizeOf(page, '#gdsSVG', 'height')).toBeGreaterThan(PHONE.height / 2);

        expect(await sidewaysOverflow(page)).toBe(0);
    });

    ///
    ///The four rems above and below and the side padding are gone, and so is the 90% cap - the bar runs from
    ///edge to edge, straight under the banner.
    ///
    test('the page keeps no margins of its own', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const padding = await page.locator('.content').evaluate((content) => {
            const style = getComputedStyle(content);

            return [style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft];
        });

        expect(padding).toEqual(['0px', '0px', '0px', '0px']);

        const banner = await box(page, '.top-row');
        const bar = await box(page, '#mainAppContainer > .viewBorderBackground');

        expect(bar.left).toBe(0);
        expect(bar.width).toBe(PHONE.width);
        expect(Math.round(bar.top)).toBe(Math.round(banner.bottom));
    });

    ///
    ///The drawing, then the cell tree, then the layer sidebar - each as wide as the screen, and each starting
    ///where the one above it ends. The page is taller than the window, since scrolling is how the sidebars
    ///are reached, and the edges that drag a sidebar wider are gone from a panel already as wide as the window.
    ///
    test('the cell tree goes under the drawing, and the layer sidebar under the view', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d', true);

        await expect.poll(async () => sizeOf(page, '#gdsSVG', 'height')).toBeGreaterThan(PHONE.height / 2);

        const canvas = await box(page, '.viewCanvas');
        const tree = await box(page, '#cellTree');
        const pane = await box(page, '#viewPane');
        const sidebar = await box(page, '#layerSidebar');

        expect(tree.top).toBeGreaterThanOrEqual(canvas.bottom - 1);
        expect(tree.width).toBeGreaterThanOrEqual(PHONE.width - 4);

        expect(sidebar.top).toBeGreaterThanOrEqual(pane.bottom - 1);
        expect(sidebar.width).toBe(PHONE.width);

        expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeGreaterThan(PHONE.height);

        await expect(page.locator('.sidebarGrabber').first()).toBeHidden();

        //The picture of the cell under the pointer, which a finger is never over.
        await expect(page.locator('#cellTree .cellTreePreviewFrame')).toBeHidden();
    });

    ///
    ///Under the view, the view's bottom edge is the line between them - so the sidebar draws no top border of
    ///its own, and takes back the left one it gives up beside the view. Each line drawn once. The other side of
    ///the rule in layout-fit.spec.js, which holds the arrangement beside the view down to 768.
    ///
    test('under the view, the sidebar draws the line between them once, and its own left edge', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await expect(page.locator('#layerSidebar')).toBeVisible();

        await expect.poll(async () => {
            return page.evaluate(() => {
                const bar = getComputedStyle(document.querySelector('#layerSidebar'));
                const pane = getComputedStyle(document.querySelector('.viewPane'));

                return {
                    dividerOnce: parseFloat(bar.borderTopWidth) === 0 && parseFloat(pane.borderBottomWidth) > 0,
                    leftDrawn: parseFloat(bar.borderLeftWidth) > 0
                };
            });
        }).toEqual({ dividerOnce: true, leftDrawn: true });
    });

    ///
    ///The eye and the padlock at a size a thumb can pick between, where they were 18 pixels six apart. Read off
    ///the row that is drawn rather than off the rule, so a later rule that wins over it still fails this.
    ///
    test('a layer row\'s switches are big enough for a thumb', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const eye = await page.locator('.layerRow').first().locator('.layerEyeButton').boundingBox();
        const lock = await page.locator('.layerRow').first().locator('.layerLockButton').boundingBox();

        expect(eye.width).toBeGreaterThanOrEqual(32);
        expect(eye.height).toBeGreaterThanOrEqual(32);
        expect(lock.width).toBeGreaterThanOrEqual(32);
        expect(lock.height).toBeGreaterThanOrEqual(32);
    });

    ///
    ///Every row of the wrapped toolbar starts at the same edge.
    ///
    ///The first group gives up its left gutter so a bar on one line is inset by the bar's own padding alone, and
    ///a phone's bar is never on one line - every later row starts with a group that kept its gutter, so the first
    ///row's buttons sat ten pixels left of all the rows under them. Lines are told apart by where their groups
    ///end, since the groups are bottom-aligned.
    ///
    test('every row of the toolbar starts at the same edge', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const starts = await page.evaluate(() => {
            const lines = new Map();

            for (const item of document.querySelectorAll('.viewToolbar > .toolbarItem')) {
                const box = item.getBoundingClientRect();
                const control = [...item.querySelectorAll('button, select, input')].find(shown => shown.getBoundingClientRect().width > 0);

                if (box.width === 0 || control === undefined)
                    continue;

                const line = Math.round(box.bottom / 6);

                if (!lines.has(line) || box.left < lines.get(line).itemLeft)
                    lines.set(line, { itemLeft: box.left, controlLeft: Math.round(control.getBoundingClientRect().left) });
            }

            return [...lines.values()].map(line => line.controlLeft);
        });

        expect(starts.length).toBeGreaterThan(1);
        expect(new Set(starts).size).toBe(1);
    });

    ///
    ///Scrolled to and tapped, rather than clicked where it sits: the row is below the fold on a phone, and a
    ///tap is what a thumb sends.
    ///
    test('a layer is switched off with a tap, from under the drawing', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

        const eye = layerCheckbox(page, 0);

        await eye.scrollIntoViewIfNeeded();
        await eye.tap();

        await expect(eye).toHaveAttribute('aria-pressed', 'false');
        await expect.poll(async () => (await svgCounts(page)).polygons).toBeLessThan(MOSFET_POLYGONS);
    });

    ///
    ///Neither of the other two views can take "what the window has left" any more either, so each is given a
    ///height of its own. Without one each would be a percentage of a box only as tall as its contents, which
    ///is nothing.
    ///
    test('the 3D view and the text view are given a height of their own', async ({ page }) => {
        await gotoExample(page, MOSFET, '3d');

        await expect.poll(async () => sizeOf(page, '#container canvas', 'height')).toBeGreaterThan(PHONE.height / 2);
        expect(await sizeOf(page, '#container canvas', 'width')).toBeGreaterThanOrEqual(PHONE.width - 4);
        expect(await sidewaysOverflow(page)).toBe(0);

        await gotoExample(page, MOSFET, 'text');

        await expect.poll(async () => sizeOf(page, '#gdsTextEditor', 'height')).toBeGreaterThan(PHONE.height / 2);
        expect(await sizeOf(page, '#gdsTextEditor', 'width')).toBeGreaterThanOrEqual(PHONE.width - 4);
    });
});

///
///The edge itself, from both sides: 767 is a phone, 768 is not.
///
///768 keeps the sidebar beside the view, level with it and to its right - the layout every other spec is
///written against, which is the point of pinning where it stops.
///
test.describe('where the phone layout starts', () => {
    test('767 pixels wide puts the sidebar under the view', async ({ page }) => {
        await page.setViewportSize({ width: 767, height: 900 });
        await gotoExample(page, MOSFET, '2d');

        await expect.poll(async () => sizeOf(page, '#gdsSVG', 'height')).toBeGreaterThan(0);

        const pane = await box(page, '#viewPane');
        const sidebar = await box(page, '#layerSidebar');

        expect(sidebar.top).toBeGreaterThanOrEqual(pane.bottom - 1);
    });

    test('768 pixels wide keeps it beside the view', async ({ page }) => {
        await page.setViewportSize({ width: 768, height: 900 });
        await gotoExample(page, MOSFET, '2d');

        await expect.poll(async () => sizeOf(page, '#gdsSVG', 'height')).toBeGreaterThan(0);

        const pane = await box(page, '#viewPane');
        const sidebar = await box(page, '#layerSidebar');

        expect(sidebar.left).toBeGreaterThanOrEqual(pane.right - 1);
        expect(Math.round(sidebar.top)).toBe(Math.round(pane.top));
    });
});
