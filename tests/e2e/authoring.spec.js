//Starting a layout from nothing, and the two lists you build it with: the layers a shape can go on, and
//the rules it is checked against. All three are new ways *in* rather than new ways to change what is open,
//which is why they share a spec - and why every one of them is asserted from an empty file where there is
//nothing to mistake for what the button did.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, openFile, expectLoaded, openLayerSettings, layerPairs, SKY130_CELL, openExamples, filterExamples, exampleRow } = require('./helpers');
const path = require('path');

///
///The layer rows as bare `layer/datatype` text, named or not - which is what a new file's rows are.
///
///The row's own × is dropped: it is a control that happens to be made of text, and leaving it in would make
///every assertion here about the presence of a button rather than about the layer the row names.
///
async function rowText(page) {
    return page.locator('#layerSidebar .layerRow').allTextContents()
        .then(rows => rows.map(row => row.replace('×', '').replace(/\s+/g, ' ').trim()));
}

///
///Presses New.
///
///It used to ask before closing what was open, and this said yes to it. It asks nothing now - the file on screen
///keeps a tab of its own, or gives way when it is the one the app opened for itself - and a yes armed here with
///no question to answer was a yes the next question in the test got, whatever that was.
///
async function startNewLayout(page) {
    await page.locator('#newLayout').click();
}

///Every question the page asks from here on, answered no - so a question that came back would say so.
function questionsAsked(page) {
    const asked = [];

    page.on('dialog', dialog => {
        asked.push(dialog.message());

        return dialog.dismiss();
    });

    return asked;
}

async function addLayer(page, number, dataType) {
    await page.locator('#addLayer').click();

    await page.locator('#newLayerNumber').fill(String(number));
    await page.locator('#newLayerDataType').fill(String(dataType));

    await page.locator('#addLayerConfirm').click();
}

///Set PDK under the empty layer list, and the PDK shown used in the manager it opens - see Viewer.razor's setPdk.
async function startWithPdkLayers(page) {
    await page.locator('#layersSetPdk').click();

    await expect(page.locator('#pdkManagerWindow')).toBeVisible();

    await page.locator('#pdkUse').click();

    await expect(page.locator('#pdkManagerWindow')).toBeHidden();
}

///
///**Opening a file asks nothing, and closes nothing somebody chose.**
///
///New, Open and a row of the Examples list each asked before closing what was open, every time, so that the
///file on screen never went without a word. With tabs it does not go: a file somebody chose keeps its tab - see
///tabs.spec - and the one that gives way is the example the app opened for itself, untouched. That is where
///these start, so what they pin is that the first file opened takes its place without a question or a tab.
///
test.describe('opening over the file the app opened for itself', () => {
    //
    //Through gotoApp rather than gotoExample, so the file on screen is the one the app opened for itself rather
    //than one a link asked for.
    //
    test.beforeEach(async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await expect.poll(async () => openFile(page)).toBe('Mosfet.gds');
    });

    test('New takes its place, and asks nothing', async ({ page }) => {
        const asked = questionsAsked(page);

        await startNewLayout(page);

        await expect.poll(async () => openFile(page), { timeout: 15000 }).toBe('Untitled.gds');
        await expect(page.locator('#fileTabs')).toHaveCount(0);

        expect(asked).toEqual([]);
    });

    test('so does an example chosen from the list', async ({ page }) => {
        const asked = questionsAsked(page);

        await openExamples(page);
        await filterExamples(page, SKY130_CELL);

        await exampleRow(page, `${SKY130_CELL}.gds`).click();

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${SKY130_CELL}.gds`);
        await expect(page.locator('#fileTabs')).toHaveCount(0);

        expect(asked).toEqual([]);
    });

    test('and a file opened from this computer', async ({ page }) => {
        const asked = questionsAsked(page);

        //Straight at the input rather than through uploadFile, which would answer a question for you.
        await page.locator('#fileUpload').setInputFiles(path.join(__dirname, 'fixtures', 'placed.gds'));

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('placed.gds');
        await expect(page.locator('#fileTabs')).toHaveCount(0);

        expect(asked).toEqual([]);
    });
});

test.describe('a layout started from nothing', () => {
    test.beforeEach(async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await startNewLayout(page);

        await expect.poll(async () => openFile(page)).toBe('Untitled.gds');
    });

    ///
    ///One cell and nothing else, which is what "new" has to mean.
    ///
    ///**The layer list is empty on purpose and that is the point of the next test.** A layout's layers are
    ///read off the shapes in it, so a file with no shapes has no layers - and until there was a way to add
    ///one, an empty layout was somewhere you could not put anything.
    ///
    test('is one empty cell with no layers', async ({ page }) => {
        await expect.poll(async () => (await rowText(page)).length).toBe(0);

        //Read off the crumb rather than the cell tree, which is closed unless the address asks for it -
        //and the crumb is the thing that says which cell an edit would land in anyway.
        await expect(page.locator('.contextCrumb.contextCrumbOn')).toHaveText('TOP');
    });

    ///
    ///**A layer added is named by the PDK in use**, the layout being drawn in it: 66/20 is poly in sky130, with poly's
    ///height and role, the way it is on every sky130 file and the way it comes back after a reload. It came in bare
    ///while nothing said what the layout was; the PDK in use says it now. A pair the PDK has no row for comes in bare.
    ///
    test('names a layer added from the PDK in use, and leaves one it has no row for bare', async ({ page }) => {
        await addLayer(page, 66, 20);
        await addLayer(page, 99, 0);

        await expect.poll(async () => await rowText(page)).toEqual(['1.poly (66/20)', '2.99/0']);
    });

    test('takes a layer that nothing is drawn on yet', async ({ page }) => {
        await addLayer(page, 66, 44);
        await addLayer(page, 68, 20);

        await expect.poll(async () => await rowText(page)).toEqual(['1.licon1 (66/44)', '2.met1 (68/20)']);
    });

    ///
    ///**And offers the PDK's layers while it has none** - for sky130, the 21 its rules check with the 16 pins and labels
    ///on them, named and each in a color of its own - so there is something to draw on and to check without a layermap
    ///of anybody's own. Asked for 2026-10-17, and the pins and labels 2026-10-18: a new layout was a list with nothing
    ///in it, and a layermap imported into it named nothing. Through Set PDK and the PDK manager since 2026-10-31, where
    ///the PDK already in use offers its use pill too.
    ///
    test('offers the PDK\'s layers while it has none: sky130\'s drawing layers with their pins and labels', async ({ page }) => {
        await expect(page.locator('#layersEmpty')).toContainText('No layers yet');
        await expect(page.locator('#layersSetPdk')).toHaveText('Set PDK');

        await startWithPdkLayers(page);

        await expect.poll(async () => (await rowText(page)).length, { timeout: 15000 }).toBe(37);

        const rows = (await rowText(page)).join(' | ');

        for (const named of ['nwell (64/20)', 'poly (66/20)', 'licon1 (66/44)', 'met1 (68/20)', 'via4 (71/44)', 'met5 (72/20)', 'lvtn (125/44)', 'met1.pin (68/16)', 'met1.label (68/5)', 'pwell.pin (122/16)'])
            expect(rows).toContain(named);

        //Not the markers and implants nobody starts a layout on.
        expect(rows).not.toContain('(83/44)');
        expect(rows).not.toContain('(89/44)');

        //A color each, rather than thirty-seven rows of the gray a layer added on its own comes in.
        const colors = await page.locator('#layerSidebar .layerRow .layerSwatch').evaluateAll(swatches => swatches.map(each => each.getAttribute('title')));

        expect(new Set(colors).size).toBe(37);
        expect(colors).not.toContain('#808080');

        //And the offer goes once there is a list.
        await expect(page.locator('#layersEmpty')).toHaveCount(0);
    });

    ///
    ///**Kept through a reload, though nothing is drawn on them.** The file's own records cannot carry a layer with no
    ///shape on it, so they come back from the session, which keeps a named one - see applyRestoredLayers. They did not:
    ///the first reload took every one of them away again.
    ///
    test('keeps the PDK\'s layers through a reload, though nothing is drawn on them', async ({ page }) => {
        await startWithPdkLayers(page);

        await expect.poll(async () => (await rowText(page)).length, { timeout: 15000 }).toBe(37);

        const before = await rowText(page);
        const colors = async () => page.locator('#layerSidebar .layerRow .layerSwatch').evaluateAll(swatches => swatches.map(each => each.getAttribute('title')));
        const colorsBefore = await colors();

        //A refresh: no query string, so the session is what opens the file.
        await page.goto('/');

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Untitled.gds');
        await expect.poll(async () => await rowText(page), { timeout: 15000 }).toEqual(before);

        //In the colors they had, rather than the gray an empty layer comes back in.
        expect(await colors()).toEqual(colorsBefore);
    });

    ///
    ///**Set PDK closed without using one changes nothing**: the list stays empty and still offers it. And the manager
    ///opened another way while it does - the PDK row over the list here - picks the same as Set PDK (2026-11-01, asked
    ///for): it said the PDK was in use and offered nothing, over a list still waiting for its layers.
    ///
    test('leaves the list empty when the PDK manager is closed without using a PDK, and picks from the PDK row as from Set PDK', async ({ page }) => {
        await page.locator('#layersSetPdk').click();

        await expect(page.locator('#pdkManagerWindow')).toBeVisible();

        await page.locator('#pdkManagerWindow .closeButton').click();

        await expect(page.locator('#pdkManagerWindow')).toBeHidden();
        await expect(page.locator('#layersSetPdk')).toBeVisible();

        //And the row over the list does not name a PDK whose layers it has not got.
        await expect(page.locator('#sidebarPdkName')).toHaveText('not set');

        await page.locator('#sidebarPdkManager').click();

        await expect(page.locator('#pdkManagerWindow')).toBeVisible();

        expect(await rowText(page)).toEqual([]);

        await page.locator('#pdkUse').click();

        await expect(page.locator('#pdkManagerWindow')).toBeHidden();
        await expect.poll(async () => (await rowText(page)).length, { timeout: 15000 }).toBe(37);
        await expect(page.locator('#sidebarPdkName')).toHaveText('sky130');

        //With layers, the manager opened from the row is for managing: the PDK in use says so, and offers no pill.
        await page.locator('#sidebarPdkManager').click();

        await expect(page.locator('#pdkList .pdkRow').first()).toContainText('in use');
        await expect(page.locator('#pdkUse')).toHaveCount(0);
    });

    ///
    ///A pair already in the list is refused, and says so where it was typed.
    ///
    ///Two rows for one pair is not a thing the layer table can hold - it is keyed by the pair - so this
    ///would either overwrite the row's settings or do nothing at all. Both are worse than being told.
    ///
    test('refuses a layer pair that is already listed', async ({ page }) => {
        await addLayer(page, 66, 44);
        await addLayer(page, 66, 44);

        await expect(page.locator('#addLayerProblem')).toContainText('already in the list');

        //And the list is unchanged, rather than carrying a second row that says the same thing.
        await expect.poll(async () => await rowText(page)).toEqual(['1.licon1 (66/44)']);
    });

    ///
    ///Nothing typed is refused with a reason rather than adding 0/0.
    ///
    ///**Empty is the only bad thing the box can hold**, which is worth saying because the guard behind it
    ///reads more general than that: the field is `type="number"`, so a browser will not put `poly` in it and
    ///Playwright refuses to try. What reaches the parse is a number or an empty string, and an empty string
    ///defaulting to layer zero would be a row nobody asked for on a press that looked like it did nothing.
    ///
    test('refuses an empty layer number, and says why', async ({ page }) => {
        await page.locator('#addLayer').click();

        await page.locator('#addLayerConfirm').click();

        await expect(page.locator('#addLayerProblem')).toContainText('whole number');
        await expect.poll(async () => (await rowText(page)).length).toBe(0);
    });

    ///<summary>Escape closes the boxes without adding, which is the other half of Enter adding.</summary>
    test('closes the boxes on Escape without adding anything', async ({ page }) => {
        await page.locator('#addLayer').click();

        await page.locator('#newLayerNumber').fill('66');
        await page.locator('#newLayerNumber').press('Escape');

        await expect(page.locator('#addLayerConfirm')).toHaveCount(0);
        await expect.poll(async () => (await rowText(page)).length).toBe(0);
    });

    ///
    ///**Even an empty layer is asked about**, because the control is one press at the end of its row.
    ///
    ///It went without a question while it lived behind a gear and two clicks, on the reading that an empty
    ///row is nothing to lose. On a × beside the checkbox it is a mis-click away from the thing next to it,
    ///and a row with a name, a color and a height on it is not nothing even with no shape carrying it.
    ///
    test('asks before removing a layer, even an empty one', async ({ page }) => {
        await addLayer(page, 66, 44);
        await addLayer(page, 68, 20);

        //Said no: the row stays.
        page.once('dialog', dialog => dialog.dismiss());

        await page.locator('.layerRow').first().locator('.layerRemove').click();

        await expect.poll(async () => await rowText(page)).toEqual(['1.licon1 (66/44)', '2.met1 (68/20)']);

        //Said yes: it goes.
        page.once('dialog', dialog => dialog.accept());

        await page.locator('.layerRow').first().locator('.layerRemove').click();

        await expect.poll(async () => await rowText(page)).toEqual(['1.met1 (68/20)']);
    });
});

test.describe('removing a layer that has shapes on it', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, SKY130_CELL, '2d');
    });

    ///
    ///The shapes go with it, everywhere in the file, and the count is said before they do.
    ///
    ///**Said, because the two cases are one press apart.** Taking out an empty row and taking out fifteen
    ///shapes are the same gesture otherwise, and only one of them is worth stopping to think about.
    ///
    test('says how many shapes go with it, and takes them', async ({ page }) => {
        const licon1 = page.locator('#layerSidebar .layerRow').filter({ hasText: 'licon1' });

        await expect(licon1).toHaveCount(1);

        //What the question said, which is where the count lives now that the control is a bare ×.
        let asked = '';

        page.once('dialog', dialog => {
            asked = dialog.message();

            return dialog.accept();
        });

        await licon1.locator('.layerRemove').click();

        await expect(page.locator('#layerSidebar .layerRow').filter({ hasText: 'licon1' })).toHaveCount(0);

        expect(asked).toContain('15 shapes');
        expect(asked).toContain('licon1 (66/44)');
    });

    ///
    ///And an undo brings back the row it *was*, not a bare one.
    ///
    ///Putting an element back registers its layer, so the row returns on its own - with the gray a new layer
    ///gets and no name, no height and no role, because none of that is in the file. The removed row is held
    ///and put back over it; without that, undoing a removal on a mapped file silently lost the mapping for
    ///that one layer, which is the kind of thing nobody notices until the 3D view is wrong.
    ///
    test('and an undo puts the row back as it was, name and all', async ({ page }) => {
        const licon1 = page.locator('#layerSidebar .layerRow').filter({ hasText: 'licon1' });

        //The x at the end of the row, which is where a list is shortened from.

        page.once('dialog', dialog => dialog.accept());

        await licon1.locator('.layerRemove').click();

        await expect(page.locator('#layerSidebar .layerRow').filter({ hasText: 'licon1' })).toHaveCount(0);

        await page.locator('#undoEdit').click();

        //Named again, rather than back as a bare 66/44.
        await expect(page.locator('#layerSidebar .layerRow').filter({ hasText: 'licon1 (66/44)' })).toHaveCount(1);

        //And the pairs are the file's own again, which says the shapes came back too.
        await expect.poll(async () => (await layerPairs(page)).length).toBe(22);
    });
});

test.describe('the rules list', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, SKY130_CELL, '2d');

        await page.getByRole('button', { name: 'Rules', exact: true }).click();

        await expect(page.locator('.rulesRow').first()).toBeVisible();
    });

    ///<summary>A rule typed in the deck's own grammar joins the deck.</summary>
    test('takes a rule typed in the deck grammar', async ({ page }) => {
        const before = await page.locator('.rulesRow').count();

        await page.locator('#addRule').click();
        await page.locator('#newRuleLine').fill('rule met1.mine width met1 200 "A rule typed in the panel"');
        await page.locator('#addRuleConfirm').click();

        await expect(page.locator('.rulesRow').filter({ hasText: 'met1.mine' })).toHaveCount(1);
        await expect(page.locator('.rulesRow')).toHaveCount(before + 1);

        //And the box closes, which is what says it was taken rather than silently ignored.
        await expect(page.locator('#addRuleConfirm')).toHaveCount(0);
    });

    ///
    ///A line the parser cannot read is refused with the parser's own words.
    ///
    ///**The parser's, not this panel's.** A deck's grammar has one description and it is the one the guide
    ///in this panel links to; a second wording invented here would be a second thing to keep in step, and
    ///the one that is wrong would be the one somebody is reading.
    ///
    test('refuses a line the parser cannot read, and says why', async ({ page }) => {
        const before = await page.locator('.rulesRow').count();

        await page.locator('#addRule').click();
        await page.locator('#newRuleLine').fill('rule nonsense');
        await page.locator('#addRuleConfirm').click();

        await expect(page.locator('#addRuleProblem')).toContainText('too short for a rule');

        //Nothing landed, and the box stays open on what was typed.
        await expect(page.locator('.rulesRow')).toHaveCount(before);
        await expect(page.locator('#newRuleLine')).toHaveValue('rule nonsense');
    });

    test('takes a rule back out', async ({ page }) => {
        const before = await page.locator('.rulesRow').count();
        const second = (await page.locator('.rulesRow').nth(1).textContent()).replace(/\s+/g, ' ').trim();

        //Asked, the same as a layer - a x at the end of a row is one press from the row itself.
        let asked = '';

        page.once('dialog', dialog => { asked = dialog.message(); return dialog.accept(); });

        await page.locator('.rulesRow').nth(1).locator('.rulesRemove').click();

        await expect(page.locator('.rulesRow')).toHaveCount(before - 1);

        const now = await page.locator('.rulesRow').allTextContents();

        expect(now.map(row => row.replace(/\s+/g, ' ').trim())).not.toContain(second);
    });

    ///
    ///Removing a rule does not also jump the view to it.
    ///
    ///The row is a link to the first violation under it, and the × sits inside that row - so without the
    ///click being stopped, taking a rule out would be the same press as going to look at a fault it is
    ///about to stop reporting.
    ///
    test('removing a rule does not follow the row it was on', async ({ page }) => {
        page.once('dialog', dialog => dialog.accept());

        await page.locator('.rulesRow').first().locator('.rulesRemove').click();

        //Nothing was selected by it, which is what following the row would have done.
        await expect(page.locator('.rulesRowChosen')).toHaveCount(0);
    });
});

///
///**A layer is added beside the text too** (asked for 2026-11-01), and what is typed in the editor is left as it is: a
///layer with no shapes writes no record, so the text is not made again under it. Taking one out still rewrites the
///text, so the rows' × stays 2D's.
///
test('a layer is added beside the text, and the text typed is kept', async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'text');

    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 60000 });
    await expect.poll(async () => page.locator('#layerSidebar .layerRow').count(), { timeout: 60000 }).toBeGreaterThan(0);

    const before = await page.locator('#layerSidebar .layerRow').count();

    //Something typed and not applied.
    await page.evaluate(() => {
        const model = monaco.editor.getModels().find(one => one.getValue().includes('BGNLIB'));

        model.setValue('typed here, not applied\n' + model.getValue());
    });

    await addLayer(page, 99, 7);

    await expect(page.locator('#layerSidebar .layerRow')).toHaveCount(before + 1);
    await expect(page.locator('#layerSidebar .layerRow').last()).toContainText('99/7');
    await expect(page.locator('.layerRemove')).toHaveCount(0);

    expect(await page.evaluate(() => monaco.editor.getModels().some(one => one.getValue().startsWith('typed here, not applied')))).toBe(true);
});
