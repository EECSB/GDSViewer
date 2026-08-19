//
//The title bar, put away and brought back from the bar.
//
//The banner belongs to the layout and the button belongs to the page, so this only exists once both are
//rendered - the press goes up through a cascade, and nothing in C# can say whether it arrived. What is
//worth checking is that the band actually leaves the page rather than being covered, that the drawing gets
//the room it freed, and that the button is left out where it could not turn anything on.
const { test, expect } = require('@playwright/test');
const { gotoExample, expectLoaded, MOSFET } = require('./helpers');

///The height of the drawing's own box, which is what the banner is taking room from.
async function canvasHeight(page) {
    return await page.evaluate(() => document.querySelector('.viewCanvas').getBoundingClientRect().height);
}

test.describe('hiding the title bar', () => {
    ///
    ///The band goes, rather than being hidden in place.
    ///
    ///Left out of the markup on purpose - see MainLayout - so nothing in it stays tabbable behind a
    ///`display: none`. Which means the count is the assertion and a visibility check would pass either way.
    ///
    test('the button takes the banner out of the page and puts it back', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect(page.locator('.top-row')).toHaveCount(1);

        await page.locator('#bannerToggle').click();

        await expect(page.locator('.top-row')).toHaveCount(0);

        await page.locator('#bannerToggle').click();

        await expect(page.locator('.top-row')).toHaveCount(1);
    });

    ///
    ///And the drawing gets what it freed.
    ///
    ///The point of the button. A banner that left the markup without the view growing would be the same
    ///window with a gap in it.
    ///
    test('the drawing grows by the height of the banner', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        const band = await page.evaluate(() => document.querySelector('.top-row').getBoundingClientRect().height);
        const before = await canvasHeight(page);

        await page.locator('#bannerToggle').click();

        await expect(page.locator('.top-row')).toHaveCount(0);

        const after = await canvasHeight(page);

        expect(band).toBeGreaterThan(20);

        //Within a few pixels of the whole band, rather than merely "bigger".
        expect(after - before).toBeGreaterThan(band - 4);
    });

    ///
    ///The button says which state it is in, not which one it would take you to.
    ///
    test('the button is lit while the banner is showing', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect(page.locator('#bannerToggle')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('#bannerToggle')).toHaveAttribute('title', /Hide the title bar/);

        await page.locator('#bannerToggle').click();

        await expect(page.locator('#bannerToggle')).toHaveAttribute('aria-pressed', 'false');
        await expect(page.locator('#bannerToggle')).toHaveAttribute('title', /Show the title bar/);
    });

    ///
    ///A switch that cannot turn anything on is left out.
    ///
    ///An embed that said banner=false gets no banner however many times the button is pressed - so the
    ///button would be a control with one state, which is not a control.
    ///
    test('an embed that asked for no banner gets no button either', async ({ page }) => {
        await page.goto('/?file=Mosfet&view=2d&banner=false', { waitUntil: 'domcontentloaded' });

        await expectLoaded(page);
        await page.waitForTimeout(1200);

        await expect(page.locator('.top-row')).toHaveCount(0);
        await expect(page.locator('#bannerToggle')).toHaveCount(0);

        //Full screen is still there, so this is the banner button being absent rather than the end of the
        //bar being gone.
        await expect(page.locator('#fullScreen')).toHaveCount(1);
    });

    ///
    ///The two decisions about how much window the drawing gets, side by side but not butted together.
    ///
    ///They are the same kind of choice and they read as a pair, but they are two presses rather than one
    ///control - which is a gap between them, and the same gap the bar's groups use.
    ///
    test('the banner button sits beside full screen with a space between', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        const pair = await page.evaluate(() => {
            const box = one => document.querySelector(one).getBoundingClientRect();

            return {
                gap: box('#fullScreen').left - box('#bannerToggle').right,
                sizes: [box('#bannerToggle').width, box('#fullScreen').width],
                tops: [box('#bannerToggle').top, box('#fullScreen').top]
            };
        });

        //Apart, but as one pair rather than as two ends of the bar.
        expect(pair.gap).toBeGreaterThan(2);
        expect(pair.gap).toBeLessThan(12);

        //The same square as its neighbour, on one line with it.
        expect(pair.sizes[0]).toBeCloseTo(pair.sizes[1], 1);
        expect(pair.tops[0]).toBeCloseTo(pair.tops[1], 1);
    });
});
