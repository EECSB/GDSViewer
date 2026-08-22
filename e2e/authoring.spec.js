//Starting a layout from nothing, and the two lists you build it with: the layers a shape can go on, and
//the rules it is checked against. All three are new ways *in* rather than new ways to change what is open,
//which is why they share a spec - and why every one of them is asserted from an empty file where there is
//nothing to mistake for what the button did.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, openFile, expectLoaded, openLayerSettings, layerPairs, SKY130_CELL, uploadFile, openExamples, filterExamples, exampleRow } = require('./helpers');

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
///Presses New, and says yes to the prompt.
///
///New asks before it closes what is open - see discardsWhatIsOpen in Viewer.razor - so a press nobody
///answers is a press that does nothing at all. Playwright dismisses a dialog it was not told about, which
///is Cancel, so every one of these has to say so out loud.
///
async function startNewLayout(page) {
    page.once('dialog', dialog => dialog.accept());

    await page.locator('#newLayout').click();
}

async function addLayer(page, number, dataType) {
    await page.locator('#addLayer').click();

    await page.locator('#newLayerNumber').fill(String(number));
    await page.locator('#newLayerDataType').fill(String(dataType));

    await page.locator('#addLayerConfirm').click();
}

///
///**New asks before it closes what is open.**
///
///It sits in the toolbar beside Open and Download and one press wipes the view. What is worth pinning is
///not that a dialog appears but that Cancel is a real answer - the file that was open is still open - and
///that the question names the file, so it can be answered without guessing which one is about to go.
///
test.describe('closing what is open', () => {
    //
    //Through gotoApp rather than gotoExample, so the file on screen is the one the app opened for itself.
    //
    //That is what the Open case needs: mayOfferImport asks a *better* question when there is something to
    //bring across - import this file as a cell, or open it on its own - and taking the second answer is
    //choosing to replace, so the confirm stands aside for it. The app's own opening file is the case it
    //stands aside from, since nothing on screen is worth importing into yet, and it is also the first thing
    //anybody actually does.
    //
    test.beforeEach(async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await expect.poll(async () => openFile(page)).toBe('Mosfet.gds');
    });

    test('Cancel keeps the file that was open', async ({ page }) => {
        let asked = '';

        page.once('dialog', dialog => {
            asked = dialog.message();

            return dialog.dismiss();
        });

        await page.locator('#newLayout').click();

        expect(asked).toContain('Mosfet.gds');

        //Polled rather than read once, because what this guards against is a new layout arriving a moment
        //later rather than not at all.
        await expect.poll(async () => openFile(page), { timeout: 10000 }).toBe('Mosfet.gds');
    });

    test('and saying yes does what the button always did', async ({ page }) => {
        await startNewLayout(page);

        await expect.poll(async () => openFile(page), { timeout: 15000 }).toBe('Untitled.gds');
    });

    ///
    ///**All three ways in ask, not just New.**
    ///
    ///Open and a row of the Examples list replace what is on screen exactly as New does, so a question on
    ///one of them and silence on the other two is a rule nobody can rely on. Each names the file arriving
    ///as well as the one going, which is what makes the question answerable.
    ///
    test('choosing an example asks, and Cancel keeps what was open', async ({ page }) => {
        await openExamples(page);
        await filterExamples(page, SKY130_CELL);

        let asked = '';

        page.once('dialog', dialog => {
            asked = dialog.message();

            return dialog.dismiss();
        });

        await exampleRow(page, `${SKY130_CELL}.gds`).click();

        //Both files named: the one arriving and the one going.
        expect(asked).toContain(SKY130_CELL);
        expect(asked).toContain('Mosfet.gds');

        await expect.poll(async () => openFile(page), { timeout: 10000 }).toBe('Mosfet.gds');

        //And yes opens it.
        page.once('dialog', dialog => dialog.accept());

        await exampleRow(page, `${SKY130_CELL}.gds`).click();

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${SKY130_CELL}.gds`);
    });

    test('opening a file asks, and Cancel keeps what was open', async ({ page }) => {
        let asked = '';

        page.once('dialog', dialog => {
            asked = dialog.message();

            return dialog.dismiss();
        });

        //Driven straight at the input rather than through uploadFile, which answers the question for you.
        await page.locator('#fileUpload').setInputFiles('e2e/fixtures/placed.gds');

        //Polled: the question is asked from C# by way of the interop, so it is not up the instant
        //setInputFiles returns - unlike a click, which does not come back until the dialog is answered.
        await expect.poll(() => asked, { timeout: 15000 }).toContain('placed.gds');

        expect(asked).toContain('Mosfet.gds');

        await expect.poll(async () => openFile(page), { timeout: 10000 }).toBe('Mosfet.gds');
    });

    ///
    ///Saying yes on its own page, rather than after the Cancel above.
    ///
    ///Setting the same path on the input a second time is not a second upload: the file list is already
    ///that file, and what a person does after cancelling is choose again from a dialog rather than re-pick
    ///the same name. A fresh page is the honest version of "and then they said yes".
    ///
    test('and saying yes to that opens it', async ({ page }) => {
        await uploadFile(page, 'e2e/fixtures/placed.gds');

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('placed.gds');
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

    ///<summary>And it is not a bundled example, so no PDK names are guessed onto it.</summary>
    test('gets no layermap guessed at it', async ({ page }) => {
        await addLayer(page, 66, 20);

        //66/20 is poly in sky130. Bare here, because this file is not a sky130 cell and nothing said it was.
        await expect.poll(async () => await rowText(page)).toEqual(['1.66/20']);
    });

    test('takes a layer that nothing is drawn on yet', async ({ page }) => {
        await addLayer(page, 66, 44);
        await addLayer(page, 68, 20);

        await expect.poll(async () => await rowText(page)).toEqual(['1.66/44', '2.68/20']);
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
        await expect.poll(async () => await rowText(page)).toEqual(['1.66/44']);
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

        await expect.poll(async () => await rowText(page)).toEqual(['1.66/44', '2.68/20']);

        //Said yes: it goes.
        page.once('dialog', dialog => dialog.accept());

        await page.locator('.layerRow').first().locator('.layerRemove').click();

        await expect.poll(async () => await rowText(page)).toEqual(['1.68/20']);
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
