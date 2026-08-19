//Design rule checking in the 2D view: opening the panel, loading a deck, running it, and the markers.
//
//Driven through the real file picker rather than by calling the engine, because the engine is already
//covered by DrcDeckTests, DrcLayerTests, DrcCheckTests and DrcRunTests. What needs a browser is the upload,
//the panel, the markers landing in the SVG as real elements, and clicking a rule to frame the view.
//
//**The markers being elements at all is the thing worth a browser.** They are appended to the markup the
//view builds rather than put on the DOM by JavaScript, and the first version of this passed a literal
//string where a field was meant - the whole marker set arrived as one text node reading "drcMarkers", which
//every C# test still passed through happily because the markup it produced was perfectly correct. Nothing
//but a real page can tell markup that is right from markup that arrived.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, chooseShape, CLEAR_OF_PANEL, MOSFET } = require('./helpers');

///A deck naming one layer Mosfet.gds uses, with a width nothing could satisfy - so it always finds a fault.
const FINDS_SOMETHING = [
    'layer poly 66/20',
    'rule poly.1a width poly 100000 "Absurd poly width"'
].join('\n');

///The same layer with a width everything satisfies, so the run comes back clean.
const FINDS_NOTHING = [
    'layer poly 66/20',
    'rule poly.1a width poly 100 "Poly width"'
].join('\n');

///A deck asking for a check this build cannot measure, which must not come back looking clean.
const CANNOT_MEASURE = [
    'layer poly 66/20',
    'rule poly.4 spaceparallel poly 75 "Parallel edges only"'
].join('\n');

///Puts the rules up in the side panel, which is where every control below it lives.
async function openRules(page) {
    await page.locator('#rulesToggle').click();

    await expect(page.locator('label[for="drcDeckImport"]')).toBeVisible();
}

///Picks a deck file through the real input.
async function loadDeck(page, contents, name = 'probe.drc') {
    await page.locator('#drcDeckImport').setInputFiles({
        name,
        mimeType: 'text/plain',
        buffer: Buffer.from(contents, 'utf8')
    });

    //The read and the parse go through .NET before the Check button appears. Generous for the same reason
    //the layermap spec is: the suite shares one dev server and this round trip is not quick under load.
    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
}

///Runs the loaded deck and waits for what it found to reach the view.
async function runDeck(page) {
    await page.locator('#drcRun').click();

    await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
}

///How many marker elements are in the drawing. Polled rather than read once - the view is drawn after it
///is mounted, so a single read races it.
async function markerCount(page) {
    return page.evaluate(() => document.querySelectorAll('#gdsSVG .drcMarker, #gdsSVG .drcMarkerPoint').length);
}

test.describe('the rules panel', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');
    });

    ///
    ///The panel is the layer panel showing something else, which is what the button switches.
    ///
    ///Worth asserting both halves: a button that put the rules up *beside* the layers would pass a test
    ///that only looked for the rules, and would be a second panel rather than the one the design asks for.
    ///
    test('the rules button turns the layer panel into the rules panel', async ({ page }) => {
        await expect(page.locator('#layerSidebar .layerList')).toBeVisible();

        await openRules(page);

        await expect(page.locator('.rulesList')).toBeVisible();

        //And the layer rows are gone rather than pushed down.
        await expect(page.locator('#layerSidebar input[type=checkbox]')).toHaveCount(0);
    });

    ///Pressing the layer button while the rules are up brings the layers back rather than closing anything.
    test('the layer button brings the layers back', async ({ page }) => {
        await openRules(page);

        await page.locator('#layersToggle').click();

        await expect(page.locator('.rulesList')).toHaveCount(0);
        await expect(page.locator('#layerSidebar .layerList')).toBeVisible();
    });

    test('asks for a deck before it offers to check anything', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('label[for="drcDeckImport"]')).toBeVisible();

        //Nothing to run until a deck says what the rules are.
        await expect(page.locator('#drcRun')).toHaveCount(0);
    });

    test('a deck arrives, is listed rule by rule, and offers to be run', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);

        await expect(page.locator('#drcRun')).toBeVisible();
        await expect(page.locator('.rulesRow')).toHaveCount(1);
        await expect(page.locator('.rulesRow')).toContainText('poly.1a');
    });

    ///
    ///A deck outlives a reload, the way a layermap does.
    ///
    ///Both are PDK data a GDSII file cannot carry and both arrive from a file the user picks, which is the
    ///argument for the two controls sitting in the same place - so a reload that remembered one and forgot
    ///the other sent somebody back to the file picker for no reason they could see.
    ///
    test('a deck outlives a reload', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);

        await page.reload();

        await openRules(page);

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

        //And it is the deck that came back, not merely a button: it still runs.
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    ///
    ///What a reload deliberately does *not* bring back is the result.
    ///
    ///A run belongs to the layout it was run against, and the file can be edited between one visit and the
    ///next - so markers restored beside a changed layout would be pointing at where a fault used to be. The
    ///deck comes back and the Check button with it, which is one gesture and an honest one.
    ///
    test('the violations a reload does not bring back', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await page.reload();

        await openRules(page);

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#drcNotice')).toHaveCount(0);
        await expect.poll(() => markerCount(page)).toBe(0);
    });

    ///
    ///The starter deck ships beside the examples and is one press away.
    ///
    ///**Because the examples it is written for are one press away.** A deck kept only in the repository is
    ///one you have to go and find, and somebody looking at a bundled sky130 cell in a browser has no
    ///repository in front of them.
    ///
    test('the deck that ships with the examples can be picked', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('#drcBundled')).toBeVisible();

        await page.locator('#drcBundled').click();

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

        //It is the real deck rather than an empty one - the whole file, rules and all.
        await expect(page.locator('#drcRun')).toHaveAttribute('title', /Check against \d\d rule/);
    });

    ///
    ///And running it over the bundled transistor finds nothing, which is the first thing anybody will do.
    ///
    ///Worth a test of its own rather than trusting the parse: a starter deck whose own demonstration
    ///reports faults on a correct layout teaches nobody to trust it, and this deck has twice shipped rules
    ///that did exactly that.
    ///
    test('the deck that ships finds nothing wrong with the example it ships beside', async ({ page }) => {
        await openRules(page);

        await page.locator('#drcBundled').click();

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('no violations');
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintClear/);
        await expect.poll(() => markerCount(page)).toBe(0);
    });

    ///Once it is the deck in hand there is nothing left to pick, so the control stands aside.
    test('the bundled control goes once its deck is the one loaded', async ({ page }) => {
        await openRules(page);

        await page.locator('#drcBundled').click();

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#drcBundled')).toHaveCount(0);
    });

    test('a clean run says so rather than leaving the view silent', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, FINDS_NOTHING);
        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('no violations');
        await expect.poll(() => markerCount(page)).toBe(0);
    });

    ///The test the whole design is for: nothing found, and nothing may be concluded from that.
    test('a rule that could not run is said before anything else', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, CANNOT_MEASURE);
        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('not fully checked');
        await expect(page.locator('#drcNotice')).toContainText('poly.4');

        //Said as a fault rather than in the green that means all clear.
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintFault/);
        await expect(page.locator('#drcNotice')).not.toContainText('rule(s) ran');
    });

    ///A rule that was refused is still listed, or the panel would claim the deck is smaller than it is.
    test('a rule this build cannot measure is listed as refused', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, CANNOT_MEASURE);

        await expect(page.locator('.rulesRowRefused')).toContainText('poly.4');
    });
});

///
///Gets inside the cell, which the Draw tool needs before it will do anything.
///
///The same gesture combining.spec.js uses on this file: the tool is disabled until there is a context to
///draw into, and clicking a shape of the top cell is what makes one.
///
async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#drawTool')).toBeEnabled({ timeout: 30000 });
}

///
///Draws one rectangle into the layout, which is the smallest edit there is.
///
///Clear of the selection panel for the reason combining.spec.js gives: the panel opens over the top-left
///the moment a shape lands, and it takes its own clicks.
///
async function drawOne(page) {
    const was = await shapeCount(page);

    await page.locator('#drawTool').click();
    await chooseShape(page, '#rectangleShape');

    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + CLEAR_OF_PANEL, view.y + 160);
    await page.mouse.down();
    await page.mouse.move(view.x + CLEAR_OF_PANEL + 70, view.y + 230, { steps: 6 });
    await page.mouse.up();

    await expect.poll(async () => shapeCount(page), { timeout: 30000 }).toBe(was + 1);
}

test.describe('checking as the layout changes', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

        await enterCell(page);

        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);
    });

    ///Turning it on answers immediately rather than waiting for an edit that may not come.
    test('turning it on checks straight away', async ({ page }) => {
        await expect(page.locator('#drcNotice')).toHaveCount(0);

        await page.locator('#drcContinuous').check();

        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    ///With it on there is nothing left for the button to do, and it says so.
    test('the Check button steps aside while it is on', async ({ page }) => {
        await expect(page.locator('#drcRun')).toBeEnabled();

        await page.locator('#drcContinuous').check();

        await expect(page.locator('#drcRun')).toBeDisabled();
    });

    ///
    ///Without it, an edit takes the markers off rather than leaving them where a fault used to be.
    ///
    ///The important half of the feature: a marker is a claim about where something is, and the moment the
    ///layout under it moves the claim is about a layout that no longer exists.
    ///
    test('an edit clears a stale result when it is off', async ({ page }) => {
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await drawOne(page);

        await expect(page.locator('#drcNotice')).toHaveCount(0, { timeout: 60000 });
        await expect.poll(() => markerCount(page)).toBe(0);
    });

    ///
    ///And with it on, the same edit gets a fresh answer rather than a cleared one.
    ///
    ///Which is the whole point of the switch: the result under a layout being worked on is either current
    ///or absent, and this is the setting that chooses current.
    ///
    test('an edit is rechecked when it is on', async ({ page }) => {
        await page.locator('#drcContinuous').check();

        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });

        await drawOne(page);

        //Still up afterwards, which is what a recheck looks like and a clear does not.
        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });
});

test.describe('the markers', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');
        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);
        await runDeck(page);
    });

    ///
    ///Real SVG elements in the drawing, not a string that happens to look like them.
    ///
    ///This is the assertion that would have caught the literal-string bug, and it is why it checks the
    ///namespace and the computed stroke rather than only that a node exists: a marker in the HTML namespace
    ///would be in the DOM, would match a class selector, and would draw nothing at all.
    ///
    test('are drawn into the layout as SVG elements', async ({ page }) => {
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        const marker = await page.evaluate(() => {
            const one = document.querySelector('#gdsSVG .drcMarker');

            return {
                tag: one.tagName,
                namespace: one.namespaceURI,
                rule: one.getAttribute('data-rule'),
                stroke: getComputedStyle(one).stroke,
                fill: getComputedStyle(one).fill
            };
        });

        expect(marker.tag).toBe('polygon');
        expect(marker.namespace).toBe('http://www.w3.org/2000/svg');
        expect(marker.rule).toBe('poly.1a');

        //Stroked and hollow, so the geometry underneath stays readable.
        expect(marker.stroke).toBe('rgb(255, 109, 0)');
        expect(marker.fill).toBe('none');
    });

    ///Nothing is left in the drawing as loose text, which is what the literal-string bug produced.
    test('leave no stray text behind in the drawing', async ({ page }) => {
        const stray = await page.evaluate(() =>
            [...document.getElementById('gdsSVG').childNodes]
                .filter(node => node.nodeType === 3 && node.nodeValue.trim().length > 0)
                .map(node => node.nodeValue.trim()));

        expect(stray).toEqual([]);
    });

    ///
    ///A redraw rebuilds the whole drawing, and the markers are part of what is built.
    ///
    ///The selection highlight is put up by JavaScript and has to be put up again afterwards through a flag
    ///the render checks. Markers are not, and this is what says so: hiding a layer redraws everything, and
    ///they are still there.
    ///
    test('survive a redraw', async ({ page }) => {
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        //The checkboxes are on the layer list, which is the panel the rules replaced.
        await page.locator('#layersToggle').click();

        await page.locator('#layerSidebar input[type=checkbox]').last().click();

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    ///
    ///The count and the marks are one fact, and the count goes where the drawing hint goes.
    ///
    ///Not a panel of its own: it is in the hint's place, which means a result and a drawing instruction
    ///can never be stacked on each other.
    ///
    test('are counted in the message the drawing hint uses', async ({ page }) => {
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHint/);
        await expect(page.locator('#drcNotice')).toContainText('poly.1a');
        await expect(page.locator('#drcNotice')).toContainText('violation');
    });

    ///Clicking the rule that was broken puts the view on the first fault under it.
    test('clicking a broken rule frames the view on one', async ({ page }) => {
        const before = await page.evaluate(() => document.getElementById('gdsSVG').getAttribute('viewBox'));

        await page.locator('.rulesRowBroken').first().click();

        await expect
            .poll(() => page.evaluate(() => document.getElementById('gdsSVG').getAttribute('viewBox')))
            .not.toBe(before);
    });

    ///And a rule that found nothing is not marked as one that did.
    test('only the broken rules are marked', async ({ page }) => {
        await expect(page.locator('.rulesRowBroken')).toHaveCount(1);
        await expect(page.locator('.rulesCount')).toHaveText(/^\d+$/);
    });

    ///Clearing takes the markers off and keeps the deck, so the next run needs no second trip to the picker.
    test('clearing takes them off without dropping the deck', async ({ page }) => {
        await page.locator('#drcClear').click();

        await expect.poll(() => markerCount(page)).toBe(0);
        await expect(page.locator('#drcNotice')).toHaveCount(0);
        await expect(page.locator('#drcRun')).toBeVisible();
    });
});
