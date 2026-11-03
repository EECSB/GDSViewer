//A KLayout report laid over the layout it was made from: its categories in the rules panel, its markers in
//the drawing, and the ways one comes in - the toolbar's Open, and a drop on the drawing, which goes through it.
//The rules panel's own Import took one too, until it went to the PDK manager on 2026-10-19.
//
//The report is KLayout's own, a deep run over the 555 timer checked in beside the C# tests that read it:
//57 items in three categories, every one filed under the cell it was found in and placed through that
//cell's reference. DrcReportReadingTests holds the reading to KLayout's flat run of the same check; what
//needs a browser is the panel, the markers arriving in the SVG as elements, and a row taking the view
//somewhere.
const path = require('path');
const { test, expect } = require('@playwright/test');
const { gotoExample, selectExample, uploadFile, openFile, viewSettled, MOSFET } = require('./helpers');

const TIMER = 'tt_um_vaf_555_timer';

const DEEP_REPORT = path.resolve(__dirname, '..', 'GDSViewer.Tests', 'fixtures', 'klayout-deep.lyrdb');

///How many items each of the report's categories holds, in the order the report declares them.
const CATEGORIES = [['gate', 24], ['difftap.2', 6], ['poly.short', 27]];

const ITEMS = 57;

async function openRules(page) {
    await page.locator('#sidebarPanelRules').click();

    await expect(page.locator('.rulesList')).toBeVisible();
}

///Through the toolbar's Open, which puts the rules panel up with the report in it.
async function importReport(page) {
    await uploadFile(page, DEEP_REPORT);

    await expect(page.locator('#reportSection')).toBeVisible({ timeout: 60000 });
}

///The report's markers in the drawing. Polled by the callers - the view draws after it is handed them.
async function reportMarkers(page) {
    return page.evaluate(() => document.querySelectorAll('#gdsSVG #reportMarkers > *').length);
}

async function viewBox(page) {
    await viewSettled(page);

    return page.locator('#gdsSVG').getAttribute('viewBox');
}

test.describe('a KLayout report', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, TIMER, 'View2DSvg');
    });

    ///
    ///Listed a category to a row, with what each holds, and drawn - one marker per item, each a value KLayout
    ///wrote in its own cell's coordinates.
    ///
    ///And the deck the example came with is still there under it: a report is what another engine found, not a
    ///deck, and loading one must not take this app's own rules away.
    ///
    test('comes in, lists its categories and draws its markers', async ({ page }) => {
        await importReport(page);

        await expect(page.locator('#reportSummary')).toContainText(`${ITEMS} items in 3 categories`);

        const rows = page.locator('.reportRow');

        await expect(rows).toHaveCount(CATEGORIES.length);

        for (let i = 0; i < CATEGORIES.length; i++) {
            await expect(rows.nth(i).locator('.rulesId')).toHaveText(CATEGORIES[i][0]);
            await expect(rows.nth(i).locator('.rulesCount')).toHaveText(String(CATEGORIES[i][1]));
        }

        await expect.poll(() => reportMarkers(page), { timeout: 60000 }).toBe(ITEMS);

        //The bundled sky130 deck, untouched.
        await expect(page.locator('#drcRun')).toBeVisible();
        await expect(page.locator('.rulesRow:not(.reportRow)')).not.toHaveCount(0);
    });

    ///
    ///A row is a way through its category: each press takes the view to the next item and marks it as the one
    ///being looked at, and the count says how far through it is.
    ///
    test('a row steps through its category, framing each item in turn', async ({ page }) => {
        await importReport(page);

        const gate = page.locator('.reportRow').first();
        const before = await viewBox(page);

        await gate.click();

        await expect(gate.locator('.rulesCount')).toHaveText('1/24');
        await expect(page.locator('#reportCurrent')).toContainText('gate, in ');
        await expect.poll(() => page.evaluate(() => document.querySelectorAll('#gdsSVG .reportMarkerCurrent').length), { timeout: 30000 }).toBeGreaterThan(0);

        const first = await viewBox(page);

        expect(first).not.toBe(before);

        await gate.click();

        await expect(gate.locator('.rulesCount')).toHaveText('2/24');
        await expect.poll(() => viewBox(page), { timeout: 30000 }).not.toBe(first);
    });

    test('the eye takes a category off the drawing and puts it back', async ({ page }) => {
        await importReport(page);

        await expect.poll(() => reportMarkers(page), { timeout: 60000 }).toBe(ITEMS);

        const shortEdges = page.locator('.reportRow').nth(2);

        await shortEdges.locator('.rulesEyeButton').click();

        await expect(shortEdges).toHaveClass(/rulesRowOff/);
        await expect.poll(() => reportMarkers(page), { timeout: 30000 }).toBe(ITEMS - 27);

        await shortEdges.locator('.rulesEyeButton').click();

        await expect.poll(() => reportMarkers(page), { timeout: 30000 }).toBe(ITEMS);
    });

    ///
    ///**The markers change, and the drawing under them is left as it is.**
    ///
    ///They were appended to the drawing's own markup, so a category switched off or on - or a step through one -
    ///handed the browser the whole drawing again: 116 to 179 ms a press on the RGB mixer, to move forty markers.
    ///They are markup of their own beside it now. A node of the drawing still in the page after the press is the
    ///drawing not replaced, which is the part a count of markers cannot see.
    ///
    test('the eye changes the markers and leaves the drawing alone', async ({ page }) => {
        await importReport(page);

        await expect.poll(() => reportMarkers(page), { timeout: 60000 }).toBe(ITEMS);

        await page.evaluate(() => {
            window.drawnBefore = document.querySelector('#gdsSVG > path[data-elements]');
        });

        expect(await page.evaluate(() => window.drawnBefore != null)).toBe(true);

        await page.locator('.reportRow').nth(2).locator('.rulesEyeButton').click();

        await expect.poll(() => reportMarkers(page), { timeout: 30000 }).toBe(ITEMS - 27);

        expect(await page.evaluate(() => window.drawnBefore.isConnected)).toBe(true);
    });

    ///
    ///Through the toolbar's Open, which a drop on the drawing goes through too: laid over the layout that is open
    ///rather than opened in its place - the same file is open afterwards - and its list put up.
    ///
    test('opened from the toolbar, it is laid over the layout rather than replacing it', async ({ page }) => {
        const before = await openFile(page);

        await uploadFile(page, DEEP_REPORT);

        await expect(page.locator('#reportSection')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#sidebarPanelRules')).toHaveAttribute('aria-pressed', 'true');
        await expect.poll(() => reportMarkers(page), { timeout: 60000 }).toBe(ITEMS);

        expect(await openFile(page)).toBe(before);
    });

    test('its cross takes it off and leaves the deck', async ({ page }) => {
        await importReport(page);

        await page.locator('#reportClose').click();

        await expect(page.locator('#reportSection')).toHaveCount(0);
        await expect.poll(() => reportMarkers(page), { timeout: 30000 }).toBe(0);
        await expect(page.locator('#drcRun')).toBeVisible();
    });

    ///A report is what KLayout found in one file, so a different file takes it off - its line over the drawing too.
    test('opening a different file takes it off', async ({ page }) => {
        await importReport(page);

        await expect.poll(() => reportMarkers(page), { timeout: 60000 }).toBe(ITEMS);
        await expect(page.locator('#drcNotice')).toContainText('klayout-deep.lyrdb');

        await selectExample(page, `${MOSFET}.gds`);

        await openRules(page);

        await expect(page.locator('#reportSection')).toHaveCount(0);
        await expect.poll(() => reportMarkers(page), { timeout: 30000 }).toBe(0);
        await expect(page.locator('#drcNotice')).toHaveCount(0);
    });
});
