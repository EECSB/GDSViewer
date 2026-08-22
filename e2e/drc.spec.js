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
const { gotoExample, selectExample, shapeCount, shapeBox, chooseShape, CLEAR_OF_PANEL, MOSFET, SKY130_CELL } = require('./helpers');

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
///
///The heading is a pair of names with the live one lit; pressing "Rules" shows the rules.
async function openRules(page) {
    await page.locator('#sidebarPanelRules').click();

    await expect(page.locator('label[for="drcDeckImport"]')).toBeVisible();
}

///
///Picks a deck file through the real input, and waits for *that* deck to be the one showing.
///
///**Waiting for the Check button was waiting for something already true.** A sky130 example arrives with a
///bundled deck of thirty rules, so `#drcRun` is visible before the upload even starts - measured:
///`isVisible()` is already true on the line above this one. So the wait passed instantly and loadDeck
///returned with the read, the parse and the save all still in flight.
///
///That is harmless until something acts immediately afterwards. `a deck outlives a reload` calls
///`page.reload()` on the very next line, and a reload that beats the save takes the page down before the
///deck is written - so what comes back is the *bundled* deck, which finds nothing. The failure surfaced two
///assertions later as a run with no markers, which looks nothing like a deck that went missing. About one
///run in a hundred and sixty, because the round trip after this is normally enough for the import to land.
///
///Counting the deck's own `rule` lines rather than asserting "not thirty", so it says what it means and
///holds for a deck of any size. A refused rule renders as a `.rulesRow` too, which is what `CANNOT_MEASURE`
///needs. And the rows only render when the handler completes - which is after the `saveSession` it
///awaits - so this covers the persistence and not merely the parse.
///
async function loadDeck(page, contents, name = 'probe.drc') {
    await page.locator('#drcDeckImport').setInputFiles({
        name,
        mimeType: 'text/plain',
        buffer: Buffer.from(contents, 'utf8')
    });

    const rules = contents.split('\n').filter(line => line.trim().startsWith('rule ')).length;

    //Generous for the same reason the layermap spec is: the suite shares one dev server and this round trip
    //is not quick under load.
    await expect(page.locator('.rulesRow')).toHaveCount(rules, { timeout: 60000 });

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

///
///Drops whatever deck is loaded, which a bundled example now arrives with.
///
///Most of these start from an empty panel, and since the deck comes automatically that is a Clear away
///rather than the state a file opens in.
///
async function clearDeck(page) {
    await page.locator('#drcClear').click();

    await expect(page.locator('#drcRun')).toHaveCount(0);
}

///
///Loads the bundled deck through the Example offer.
///
///The button lives inside a popup the wrapper opens on hover, so it has to be hovered rather than clicked
///straight away - `hover()` drives the real CSS :hover, which is what the popup is built on.
///
async function loadBundledDeck(page) {
    await page.locator('#drcExampleOffer').hover();

    //The popup waits a third of a second before opening, so a pointer passing over cannot trip it.
    await expect(page.locator('#drcBundled')).toBeVisible();

    await page.locator('#drcBundled').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
}

test.describe('the rules panel', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');
    });

    ///
    ///The panel is the layer panel showing something else, which is what the name pair switches.
    ///
    ///Worth asserting both halves: a control that put the rules up *beside* the layers would pass a test
    ///that only looked for the rules, and would be a second panel rather than the one the design asks for.
    ///
    test('the name pair turns the layer panel into the rules panel', async ({ page }) => {
        await expect(page.locator('#layerSidebar .layerList')).toBeVisible();
        await expect(page.locator('#sidebarPanelLayers')).toHaveClass(/sidebarPanelChoiceOn/);

        await openRules(page);

        await expect(page.locator('#sidebarPanelRules')).toHaveClass(/sidebarPanelChoiceOn/);
        await expect(page.locator('.rulesList')).toBeVisible();

        //And the layer rows are gone rather than pushed down.
        await expect(page.locator('#layerSidebar .layerList input[type=checkbox]')).toHaveCount(0);
    });

    ///Pressing the other name goes back, which is what makes the pair a switch.
    test('the other name switches back to the layers', async ({ page }) => {
        await openRules(page);

        await page.locator('#sidebarPanelLayers').click();

        await expect(page.locator('#sidebarPanelLayers')).toHaveClass(/sidebarPanelChoiceOn/);
        await expect(page.locator('.rulesList')).toHaveCount(0);
        await expect(page.locator('#layerSidebar .layerList')).toBeVisible();
    });

    ///
    ///And pressing the name you are already on leaves you there.
    ///
    ///The pair names both lists, so "Rules" means show the rules - not toggle them away. A swap would make
    ///the lit half a trap: the one control whose label does not describe what pressing it does.
    ///
    test('pressing the name already showing is not a toggle', async ({ page }) => {
        await openRules(page);

        await page.locator('#sidebarPanelRules').click();

        await expect(page.locator('#sidebarPanelRules')).toHaveClass(/sidebarPanelChoiceOn/);
        await expect(page.locator('.rulesList')).toBeVisible();
    });

    ///
    ///There is no second button in the toolbar for it.
    ///
    ///There was, and it was a third icon in a crowded bar that said nothing about where its panel would
    ///appear. The panel names both its lists now, so the switch lives on the panel it changes.
    ///
    test('the toolbar has no separate rules button', async ({ page }) => {
        await expect(page.locator('#rulesToggle')).toHaveCount(0);
    });

    ///
    ///A bundled example arrives with the deck already on it.
    ///
    ///Every file this app ships is a sky130 cell and the deck for them ships beside the layermap that is
    ///already applied the same way - so opening one and finding the panel empty sent somebody to fetch a
    ///file the app was already holding. The empty state is what a Clear leaves behind, and this checks
    ///both halves of that.
    ///
    test('a bundled example opens with its rules already loaded', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('.rulesRow')).not.toHaveCount(0);

        //Nothing to offer while it is loaded.
        await expect(page.locator('#drcExampleOffer')).toHaveCount(0);

        await clearDeck(page);

        //And with it gone, Import and the Example offer are what is left.
        await expect(page.locator('label[for="drcDeckImport"]')).toBeVisible();
        await expect(page.locator('#drcExampleOffer')).toBeVisible();
        await expect(page.locator('#drcRun')).toHaveCount(0);
    });

    ///
    ///A Clear speaks for the file it was made on, and stops there.
    ///
    ///It has to outlast a reload, or the panel would fill straight back up and Clear would read as a button
    ///that does nothing. But it was outlasting everything: one Clear while looking round the examples, and
    ///every example opened afterwards arrived with an empty panel - for a PDK this app ships the deck for
    ///and knows the file belongs to. Nothing on screen explained it, and the way back was behind a hover.
    ///
    ///Through the picker rather than the address, because the picker is where it was reported from.
    ///
    test('clearing the deck does not follow you to the next example', async ({ page }) => {
        await openRules(page);
        await clearDeck(page);

        await expect(page.locator('#drcExampleOffer')).toBeVisible();

        await selectExample(page, `${SKY130_CELL}.gds`);

        await openRules(page);

        //A different sky130 cell, so the deck for it applies again.
        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('.rulesRow')).not.toHaveCount(0);
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
        await clearDeck(page);

        await expect(page.locator('#drcExampleOffer')).toBeVisible();

        await loadBundledDeck(page);

        //It is the real deck rather than an empty one - the whole file, rules and all.
        await expect(page.locator('#drcRun')).toHaveAttribute('title', /against all \d\d rule/);
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

        //Already loaded, since it is a bundled example.
        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('no violations');
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintClear/);
        await expect.poll(() => markerCount(page)).toBe(0);
    });

    ///Once it is the deck in hand there is nothing left to pick, so the offer stands aside.
    test('the bundled control goes once its deck is the one loaded', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#drcExampleOffer')).toHaveCount(0);
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

    ///
    ///The button stays live with the switch on, and still runs.
    ///
    ///It used to go disabled, on the argument that a control with nothing left to do should say so. But a
    ///check runs on an *edit*, and plenty worth checking is not one - a deck imported, a cell flattened, or
    ///simply wanting the marks back after reading them away. Disabling it took the only manual run away
    ///because an automatic one existed, which left no way to ask for the thing the panel is for.
    ///
    test('the Check button still runs while it is on', async ({ page }) => {
        await expect(page.locator('#drcRun')).toBeEnabled();

        await page.locator('#drcContinuous').check();

        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#drcRun')).toBeEnabled();

        //Read away, then asked for again - the gesture the disabled button had no answer for.
        await page.locator('#drcNoticeClose').click();

        await expect(page.locator('#drcNotice')).toHaveCount(0);

        await page.locator('#drcRun').click();

        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
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
    ///**The message is read away first, so what comes back is this edit's answer.** Without that this
    ///passed while the recheck did nothing at all - the notice and the markers were both left over from
    ///turning the switch on, and neither of them moves when a run silently returns. It was green through
    ///the whole life of the bug the button test below is named for.
    ///
    test('an edit is rechecked when it is on', async ({ page }) => {
        await page.locator('#drcContinuous').check();

        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });

        await page.locator('#drcNoticeClose').click();

        await expect(page.locator('#drcNotice')).toHaveCount(0);

        await drawOne(page);

        //Back up afterwards, which only a run that happened can do.
        await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    ///
    ///And the button goes on working after an edit, which is what it stopped doing.
    ///
    ///The shell drops its flattened layout when the library is changed in place, and the check read that
    ///null as "nothing to check" and returned. So **from the first edit onward DRC Check did nothing** -
    ///no marks, no message, and a deck listed above saying the rules were loaded. It took "check on edit"
    ///with it, since that runs through the same method. It works a layout out now rather than returning.
    ///
    test('the button still checks after an edit', async ({ page }) => {
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await drawOne(page);

        //The switch is off here, so the edit takes the stale result away. That is the state the button
        //could not get out of.
        await expect.poll(() => markerCount(page)).toBe(0);

        await runDeck(page);

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

        //The eyes are on the layer list, so switch the panel back to it. Hiding a layer is a redraw, which
        //is all this needs - the markers have to survive being rebuilt into the new picture.
        await page.locator('#sidebarPanelLayers').click();

        await page.locator('#layerSidebar .layerList .layerEyeButton').last().click();

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


    ///
    ///The message can be put away, and putting it away is not the same as clearing the result.
    ///
    ///The drawing hints above it come and go with the tool in hand; this one is about a run that has
    ///finished, so nothing else takes it off the drawing it is sitting over. Closing it leaves the marks
    ///and the flagged rule exactly where they were - Clear in the panel is what takes those off.
    ///
    test('the message closes without taking the markers with it', async ({ page }) => {
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await page.locator('#drcNoticeClose').click();

        await expect(page.locator('#drcNotice')).toHaveCount(0);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
        await expect(page.locator('.rulesRowBroken')).toHaveCount(1);
    });

    ///
    ///Clearing drops the deck as well as the marks, the way Clear does one panel over.
    ///
    ///The two Clears sit in the same place in the same row, so they mean the same thing: the layer one
    ///drops an imported layermap rather than merely what it is drawing, and this drops an imported deck.
    ///It used to keep the deck, which made one a reset and the other a tidy-up.
    ///
    test('clearing drops the deck along with the marks', async ({ page }) => {
        await page.locator('#drcClear').click();

        await expect.poll(() => markerCount(page)).toBe(0);
        await expect(page.locator('#drcNotice')).toHaveCount(0);

        //No deck, so nothing to run and the empty state is back with its offer.
        await expect(page.locator('#drcRun')).toHaveCount(0);
        await expect(page.locator('.rulesRow')).toHaveCount(0);
        await expect(page.locator('#drcExampleOffer')).toBeVisible();
    });
});

///
///One rule's settings, behind the same gear a layer row carries.
///
///**A rule is a line of the deck**, which is what these all turn on. The deck's text is what gets parsed,
///saved, exported and read back - so the popup edits that line rather than a form of its parts, and a
///DrcRule carries more than a form would show it: an except clause, a window, a step, a metric, each on
///some rules and not others. Composing a line back from boxes would drop whichever this rule used and
///leave it listed while checking something narrower than it says.
///
test.describe('a rule\'s settings', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await openRules(page);
        await loadDeck(page, FINDS_NOTHING);

        //
        //**Waited for by row count, not by the Check button appearing.**
        //
        //A deck outlives a reload - there is a test above saying so - so this page can arrive with one
        //already loaded and briefly show its rows while the probe deck is still going through .NET. Every
        //test below reaches for "the" gear and "the" limit, which means the one row this deck has; without
        //this they can land on the thirty the bundled deck has and fail on strict mode rather than on
        //anything real.
        //
        await expect(page.locator('.rulesRow')).toHaveCount(1);
    });

    ///The gear is on every row, the way it is on every layer row.
    test('every rule row offers one', async ({ page }) => {
        await expect(page.locator('.rulesRow')).toHaveCount(1);
        await expect(page.locator('.rulesSettingsButton')).toHaveCount(1);
    });

    ///
    ///What it opens is a readout of the parsed rule and the line it came from.
    ///
    ///The readout is not editable on purpose: every one of those values is decided by the line, and showing
    ///them as boxes would offer to change a thing that is really changed somewhere else.
    ///
    test('it opens on the rule as the deck holds it', async ({ page }) => {
        await page.locator('.rulesSettingsButton').click();

        await expect(page.locator('.ruleSettingsPopup')).toBeVisible();

        const shown = await page.locator('.ruleSettingsValue').allTextContents();

        expect(shown).toEqual(['poly.1a', 'width', 'poly', '100']);

        await expect(page.locator('.ruleSettingsLine')).toHaveValue(
            'rule poly.1a width poly 100 "Poly width"');
    });

    ///
    ///A typed line is applied by putting it back where it came from and reading the whole deck again.
    ///
    test('a changed line changes the rule', async ({ page }) => {
        await page.locator('.rulesSettingsButton').click();

        await page.locator('.ruleSettingsLine').fill('rule poly.1a width poly 250 "Poly width"');
        await page.getByRole('button', { name: 'Apply', exact: true }).click();

        //Closed, because it was accepted.
        await expect(page.locator('.ruleSettingsPopup')).toHaveCount(0);

        //The row says the new limit, and there is still exactly one rule.
        await expect(page.locator('.rulesRow')).toHaveCount(1);
        await expect(page.locator('.rulesLimit')).toHaveText('250');
    });

    ///
    ///**A line the parser will not take leaves the deck alone and says why.**
    ///
    ///Refused rather than half-applied, which is the same pair of refusals adding a rule can hit: a line the
    ///parser complains about, and one it understands but this build cannot measure.
    ///
    test('a line that cannot be read is refused and nothing changes', async ({ page }) => {
        await page.locator('.rulesSettingsButton').click();

        await page.locator('.ruleSettingsLine').fill('rule poly.1a spaceparallel poly 75 "Not measurable"');
        await page.getByRole('button', { name: 'Apply', exact: true }).click();

        //Still open, with a complaint in it.
        await expect(page.locator('.ruleSettingsPopup')).toBeVisible();
        await expect(page.locator('.ruleSettingsProblem')).not.toHaveText('');

        //And the rule is exactly as it was.
        await expect(page.locator('.rulesLimit')).toHaveText('100');
    });

    ///Cancel leaves the deck alone whatever was typed into the box.
    test('cancel keeps the rule', async ({ page }) => {
        await page.locator('.rulesSettingsButton').click();

        await page.locator('.ruleSettingsLine').fill('rule poly.1a width poly 999 "Poly width"');
        await page.getByRole('button', { name: 'Cancel', exact: true }).click();

        await expect(page.locator('.ruleSettingsPopup')).toHaveCount(0);
        await expect(page.locator('.rulesLimit')).toHaveText('100');
    });
});
