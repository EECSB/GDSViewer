//Design rule checking in the 2D view: opening the panel, loading a deck, running it, and the markers.
//
//Driven through the real file picker rather than by calling the engine, because the engine is already
//covered by DrcDeckTests, DrcLayerTests, DrcCheckTests and DrcRunTests. What needs a browser is the upload,
//the panel, the markers landing in the SVG as real elements, and clicking a rule to frame the view. The
//picker is the PDK manager's rules Import since 2026-10-19, when the rules panel's own Import, Export, Clear
//and Example went there: a deck goes into the PDK in use, which checks the file drawn in it.
//
//**The markers being elements at all is the thing worth a browser.** They are appended to the markup the
//view builds rather than put on the DOM by JavaScript, and the first version of this passed a literal
//string where a field was meant - the whole marker set arrived as one text node reading "drcMarkers", which
//every C# test still passed through happily because the markup it produced was perfectly correct. Nothing
//but a real page can tell markup that is right from markup that arrived.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectExample, shapeCount, shapeBox, chooseShape, storedSession, downloadBytes, importRules, bareNumbers, CLEAR_OF_PANEL, MOSFET, SKY130_CELL } = require('./helpers');

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

    await expect(page.locator('.rulesList')).toBeVisible();
}

///
///Picks a deck file through the real input - the PDK manager's, into the PDK in use, sky130 on Mosfet - and waits
///for *that* deck to be the one showing.
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
///needs. And the rows only render once the PDK has been kept and laid over the file again, so this covers
///the persistence and not merely the parse.
///
async function loadDeck(page, contents, name = 'probe.drc') {
    await importRules(page, contents, name);

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
    ///file the app was already holding. It is the real deck rather than an empty one: the whole file, rules
    ///and all.
    ///
    test('a bundled example opens with its rules already loaded', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('.rulesRow')).not.toHaveCount(0);
        await expect(page.locator('#drcRun')).toHaveAttribute('title', /against all \d\d rule/);

        //The PDK's own, so nothing says otherwise.
        await expect(page.locator('#rulesNotPdk')).toHaveCount(0);
    });

    ///
    ///**A file the PDK in use does not check says so, and where rules come from** - the PDK manager over the panel,
    ///since the panel's own Import and Example went there (2026-10-19). It said to import a deck, or take one.
    ///
    test('a file the PDK in use does not check says why the panel is empty', async ({ page }) => {
        await bareNumbers(page);
        await openRules(page);

        await expect(page.locator('#drcRun')).toHaveCount(0);
        await expect(page.locator('#rulesNone')).toHaveText('No rules here: this file is not drawn in New PDK, the PDK in use. Choose or make another in the PDK manager above.');
    });

    ///
    ///A run's markers are about the file it ran on, so opening another takes them off - and the line counting
    ///them goes with them.
    ///
    ///They stayed. An edit dropped the result, and so did a new deck, but nothing did when the file itself
    ///changed - so the next file opened under the last one's markers and its count of faults.
    ///
    test('opening a different file takes the last run off', async ({ page }) => {
        await openRules(page);
        await loadDeck(page, FINDS_SOMETHING);
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await selectExample(page, `${SKY130_CELL}.gds`);

        await expect.poll(() => markerCount(page), { timeout: 30000 }).toBe(0);
        await expect(page.locator('#drcNotice')).toHaveCount(0);
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
    ///Both are PDK data a GDSII file cannot carry, and both are kept with the PDK they went into - so a reload
    ///that remembered one and forgot the other would send somebody back to the file picker for no reason they
    ///could see.
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

    ///
    ///**The deck that fails on purpose says it is not the PDK's**, where the panel's row was, and goes back to the PDK's
    ///rules from there - for every file after it, and through a reload, a deck the PDK did not put there stays until
    ///it is. Clear did that until 2026-10-19, in the row that went to the PDK manager.
    ///
    test('the deck that fails on purpose says it is not the PDK\'s, and gives the PDK\'s rules back', async ({ page }) => {
        await openRules(page);

        await expect(page.locator('#drcRun')).toHaveAttribute('title', /against all 30 rule/, { timeout: 60000 });

        //From the PDK manager's help on rules, which puts the manager away with the deck loaded.
        await page.locator('#sidebarPdkManager').click();
        await page.locator('#pdkRulesTab').click();
        await page.locator('#pdkHelp').click();
        await page.locator('#loadStrictDeck').click();

        await expect(page.locator('#pdkManagerWindow')).toBeHidden();
        await expect(page.locator('#rulesNotPdk')).toContainText('not sky130\'s rules');

        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        //Still there after a reload, which keeps a deck loaded by hand.
        await page.reload();
        await openRules(page);

        await expect(page.locator('#rulesNotPdk')).toBeVisible({ timeout: 60000 });

        await page.locator('#rulesBackToPdk').click();

        await expect(page.locator('#rulesNotPdk')).toHaveCount(0);
        await expect(page.locator('#drcRun')).toHaveAttribute('title', /against all 30 rule/);

        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('no violations');
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
    ///no marks, no message, and a deck listed above saying the rules were loaded. It took "check on every edit"
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
    ///and the flagged rule exactly where they were - an edit, another file or another deck is what takes those off.
    ///
    test('the message closes without taking the markers with it', async ({ page }) => {
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);

        await page.locator('#drcNoticeClose').click();

        await expect(page.locator('#drcNotice')).toHaveCount(0);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
        await expect(page.locator('.rulesRowBroken')).toHaveCount(1);
    });
});

///
///A rule switched off, with the eye a layer row carries, does not run.
///
///Two rules, one that finds something and one that finds nothing, so what goes when one is switched off can be
///told from what a run would have found anyway. Switching one with a result on screen runs the check again: a
///rule switched back on has nothing to show until it has been measured.
///
test.describe('switching rules off', () => {
    const TWO_RULES = [
        'layer poly 66/20',
        'rule poly.1a width poly 100000 "Absurd poly width"',
        'rule poly.1b width poly 100 "Poly width"'
    ].join('\n');

    ///The row of the rule with that id.
    function ruleRow(page, id) {
        return page.locator('.rulesRow').filter({ has: page.locator('.rulesId', { hasText: id }) }).first();
    }

    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');
        await openRules(page);
        await loadDeck(page, TWO_RULES);
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    test('a rule switched off is left out of the check, and back on is in it again', async ({ page }) => {
        const eye = ruleRow(page, 'poly.1a').locator('.rulesEyeButton');

        await eye.click();

        await expect(eye).toHaveAttribute('aria-pressed', 'false');
        await expect(ruleRow(page, 'poly.1a')).toHaveClass(/rulesRowOff/);
        await expect.poll(() => markerCount(page)).toBe(0);
        await expect(page.locator('.rulesRowBroken')).toHaveCount(0);
        await expect(page.locator('#drcNotice')).toContainText('1 switched off');

        await eye.click();

        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
        await expect(page.locator('.rulesRowBroken')).toHaveCount(1);
    });

    ///
    ///The header's eye does every rule at once - all off while none is, all on while any is - and a run with
    ///every rule off says it checked nothing, in the attention color rather than the green of a clean layout.
    ///
    test('the header switches every rule, and a run of none says nothing was checked', async ({ page }) => {
        const all = page.locator('#allRulesOn');

        await all.click();

        await expect(all).toHaveAttribute('aria-pressed', 'false');
        await expect(page.locator('.rulesRowOff')).toHaveCount(2);
        await expect.poll(() => markerCount(page)).toBe(0);
        await expect(page.locator('#drcNotice')).toContainText('nothing checked');
        await expect(page.locator('#drcNotice')).not.toHaveClass(/drawHintClear/);

        //One switched back on by hand is a mixed column, and the header's press then switches them all on.
        await ruleRow(page, 'poly.1b').locator('.rulesEyeButton').click();
        await expect(all).toHaveAttribute('aria-pressed', 'mixed');

        await all.click();

        await expect(page.locator('.rulesRowOff')).toHaveCount(0);
        await expect.poll(() => markerCount(page)).toBeGreaterThan(0);
    });

    ///Asked first, and only the rules go: the deck stays loaded, with somewhere to add the next one.
    test('the header removes every rule, after asking', async ({ page }) => {
        page.once('dialog', dialog => dialog.dismiss());
        await page.locator('#removeAllRules').click();

        await expect(page.locator('.rulesRow')).toHaveCount(2);

        page.once('dialog', dialog => dialog.accept());
        await page.locator('#removeAllRules').click();

        await expect(page.locator('.rulesRow')).toHaveCount(0);
        await expect(page.locator('#drcRun')).toBeVisible();
        await expect(page.locator('#addRule')).toBeVisible();
    });

    test('a rule switched off stays off across a reload', async ({ page }) => {
        await ruleRow(page, 'poly.1a').locator('.rulesEyeButton').click();
        await expect(ruleRow(page, 'poly.1a')).toHaveClass(/rulesRowOff/);

        await page.reload();
        await openRules(page);

        await expect(ruleRow(page, 'poly.1a')).toHaveClass(/rulesRowOff/, { timeout: 60000 });
        await expect(ruleRow(page, 'poly.1b')).not.toHaveClass(/rulesRowOff/);
    });

    ///
    ///The header's eye and × stand over the columns of them, which stand flush right on every row - they had
    ///auto margins of their own, so the gear sat a different distance from the × on every row.
    ///
    test('the header lines up over the rows, and the rows over each other', async ({ page }) => {
        const lefts = await page.evaluate(() => {
            const x = element => Math.round(element.getBoundingClientRect().left);
            const rows = [...document.querySelectorAll('.rulesRow')];

            return {
                eyes: [...new Set(rows.map(row => x(row.querySelector('.rulesEyeButton'))))],
                removes: [...new Set(rows.map(row => x(row.querySelector('.rulesRemove'))))],
                headerEye: x(document.querySelector('#allRulesOn')),
                headerRemove: x(document.querySelector('#removeAllRules'))
            };
        });

        expect(lefts.eyes).toEqual([lefts.headerEye]);
        expect(lefts.removes).toEqual([lefts.headerRemove]);
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

///
///A fault can be waived - looked at and let stand - from the line a rule's row steps it into.
///
///Three li1 faults on the transistor under a width nothing meets, so one can be waived with two left standing.
///What takes a browser: the row walking the view through the faults, the marker going gray, the notice leaving
///it out of the count, the session keeping it across a reload, and the report the panel hands over.
///
test.describe('waivers', () => {
    const THREE_FAULTS = [
        'layer li1 67/20',
        'rule li1.1 width li1 100000 "Absurd li1 width"'
    ].join('\n');

    ///Which fault the marker picked out as the one stepped to is, by its place in the run - or null for none.
    async function currentMarker(page) {
        return page.evaluate(() => document.querySelector('#gdsSVG .drcMarkerCurrent')?.getAttribute('data-violation') ?? null);
    }

    async function waivedMarkers(page) {
        return page.evaluate(() => document.querySelectorAll('#gdsSVG .drcMarkerWaived').length);
    }

    ///Steps into the rule's first fault and waives it, and waits for the button to say it can be taken back.
    async function waiveTheFirst(page) {
        await page.locator('.rulesRowBroken').first().click();
        await page.locator('#drcWaive').click();

        await expect(page.locator('#drcWaive')).toHaveText('Unwaive');
    }

    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');
        await openRules(page);
        await loadDeck(page, THREE_FAULTS);
        await runDeck(page);

        await expect.poll(() => markerCount(page)).toBe(3);
    });

    ///A press on the row goes to its first fault and the next press to the next, and the line under the list says which.
    test('a rule row steps through its faults', async ({ page }) => {
        const row = page.locator('.rulesRowBroken').first();

        await row.click();

        await expect(page.locator('#drcCurrent')).toContainText('li1.1 1 of 3');
        await expect(row.locator('.rulesCount')).toHaveText('1/3');
        await expect.poll(() => currentMarker(page)).toBe('0');

        await row.click();

        await expect(page.locator('#drcCurrent')).toContainText('li1.1 2 of 3');
        await expect.poll(() => currentMarker(page)).toBe('1');
    });

    ///Still drawn - it was found - in the waived gray, and out of the count the notice gives.
    test('a waived fault is drawn gray and left out of the count', async ({ page }) => {
        await waiveTheFirst(page);

        await expect(page.locator('#drcCurrent')).toContainText('waived');
        await expect(page.locator('#drcNotice')).toContainText('2 violations');
        await expect(page.locator('#drcNotice')).toContainText('1 waived');

        await expect.poll(() => waivedMarkers(page)).toBe(1);
        await expect.poll(() => markerCount(page)).toBe(3);

        const stroke = await page.evaluate(() => getComputedStyle(document.querySelector('#gdsSVG .drcMarkerWaived')).stroke);

        expect(stroke).toBe('rgb(154, 160, 166)');
    });

    ///
    ///Kept by what the fault is, so a run again finds it waived - after a reload too, where the result itself is
    ///not brought back but the waiver is - and Unwaive counts it again.
    ///
    test('a waiver outlives a reload and a run again, and Unwaive takes it back', async ({ page }) => {
        await waiveTheFirst(page);

        //Saved before the reload, or the reload can beat the save - see loadDeck.
        await expect.poll(() => storedSession(page), { timeout: 60000 }).toContain('"wv":["li1.1@');

        await page.reload();

        await openRules(page);
        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await runDeck(page);

        await expect(page.locator('#drcNotice')).toContainText('2 violations');
        await expect(page.locator('#drcNotice')).toContainText('1 waived');

        //The first fault is the waived one, so its button offers to take the waiver back.
        await page.locator('.rulesRowBroken').first().click();
        await expect(page.locator('#drcWaive')).toHaveText('Unwaive');

        await page.locator('#drcWaive').click();

        await expect(page.locator('#drcWaive')).toHaveText('Waive');
        await expect(page.locator('#drcNotice')).toContainText('3 violations');
        await expect(page.locator('#drcNotice')).not.toContainText('waived');
        await expect.poll(() => waivedMarkers(page)).toBe(0);
    });

    ///A waiver is about a fault in this layout, so a different file opened leaves it behind - in the stored session too.
    test('a different file opened leaves the waiver behind', async ({ page }) => {
        await waiveTheFirst(page);

        await expect.poll(() => storedSession(page), { timeout: 60000 }).toContain('"wv":["li1.1@');

        await selectExample(page, `${SKY130_CELL}.gds`);

        await expect.poll(() => storedSession(page), { timeout: 60000 }).toContain(SKY130_CELL);
        expect(await storedSession(page)).not.toContain('li1.1@');
    });

    ///The report the panel hands over carries the waiver as KLayout's own tag, on the one fault waived.
    test('the report carries the waiver as KLayout tags it', async ({ page }) => {
        await waiveTheFirst(page);

        const started = page.waitForEvent('download');

        await page.locator('#drcReport').click();

        const download = await started;
        const text = (await downloadBytes(download)).toString('utf8');

        expect(download.suggestedFilename()).toMatch(/\.lyrdb$/);
        expect(text.split('<tags>waived</tags>').length - 1).toBe(1);
        expect(text).toContain('<name>waived</name>');
        expect(text.split('<item>').length - 1).toBe(3);
    });
});
