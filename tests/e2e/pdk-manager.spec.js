//The PDK manager: the PDKs kept, each with its layers and rules, and the one in use naming and checking every file drawn
//in it. Asked for 2026-10-18. The layermap edits underneath are covered without a browser in PdkTests, a whole PDK's zip
//in PdkPackageTests and the rule lines the form writes in DrcDeckTests; what is here is the window - opened from the
//bar, in the middle, and from the link over the layer panel that took the place of its rows of controls (2026-10-19) -
//the two panels editing the PDK in use, a row styled through the layer panel's own settings, the rule form, a PDK
//coming in and going out as one zip, and which files a PDK lays itself over.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, layerPairs, onOtherLayers, downloadBytes, selectExample, MOSFET, SKY130_CELL } = require('./helpers');
const fs = require('fs');
const path = require('path');

///The layer rows as their text, named or not, without the row's own ×.
async function rowText(page) {
    return page.locator('#layerSidebar .layerRow .layerName').allTextContents()
        .then(rows => rows.map(row => row.replace(/\s+/g, ' ').trim()));
}

///The name a row shows for a pair, whether or not it has one.
async function labelFor(page, pair) {
    return (await rowText(page)).find(text => text.includes(pair));
}

///Opens the PDK manager from Settings, the way somebody would find it first.
async function openPdkManager(page) {
    await page.locator('#settingsMenu').click();
    await page.locator('#pdkManagerItem').click();

    await expect(page.locator('#pdkManagerWindow')).toBeVisible();
}

function pdkRow(page, pair) {
    return page.locator(`#pdkLayerTable tr[data-pair="${pair}"]`);
}

///Renames a layer from its settings in the layer panel - the gear on its row, and the box at the top.
async function renameInPanel(page, pair, name) {
    const row = page.locator('.layerRow').filter({ has: page.locator('.layerName', { hasText: pair }) }).first();

    await row.locator('.layerSettingsButton').click();

    const box = page.locator('.layerSettingsName');

    await expect(box).toBeVisible();

    await box.fill(name);
    await box.press('Enter');
}

test.describe('the window', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
    });

    test('opens on the PDK in use, sky130, with its layers and its rules', async ({ page }) => {
        await openPdkManager(page);

        //The three built-ins, sky130 in use and gf180mcu and ihp-sg13g2 under it (2026-11-02).
        await expect(page.locator('#pdkList .pdkRow')).toHaveCount(3);
        await expect(page.locator('#pdkList .pdkRow').first()).toContainText('sky130');
        await expect(page.locator('#pdkList .pdkRow').first()).toContainText('in use');
        await expect(page.locator('#pdkList .pdkRow').nth(1)).toContainText('gf180mcu');
        await expect(page.locator('#pdkList .pdkRow').nth(1)).not.toContainText('in use');
        await expect(page.locator('#pdkList .pdkRow').nth(2)).toContainText('ihp-sg13g2');

        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (46)');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');

        await expect(pdkRow(page, '68/20').locator('.pdkCellName')).toHaveValue('met1');
        await expect(pdkRow(page, '68/20').locator('.pdkRole')).toHaveValue('Conductor');
        await expect(pdkRow(page, '68/20').locator('.pdkNumber').first()).toHaveValue('1370');

        //The built-in as it ships: nothing to reset, and it keeps its name - a double-click on it opens no box.
        await expect(page.locator('#pdkReset')).toBeDisabled();
        await page.locator('#pdkList .pdkRowName').first().dblclick();
        await expect(page.locator('#pdkName')).toHaveCount(0);

        //Named when the link over the layer panel is pointed at, which opens it too.
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is sky130: it names this file's layers and checks it/);
    });

    ///
    ///**The bar's chip and "PDK", in front of the layer panel's names**, in place of each panel's own row of Import,
    ///Export, Clear, Example and the PDK's name - asked for 2026-10-19, over the names first and then in their row. So it
    ///is there on both lists, and it opens the window like the chip at the end of the bar. The info icon that was beside
    ///the names is the PDK manager's now. Since 2026-11-01 at the top of the white list, over a line and "All layers", as
    ///wide as the list, saying "PDK:" and the PDK in use - and the names have icons in the room it left.
    ///
    test('the PDK button at the top of the layer panel list opens it, on either list, where the rows of controls were', async ({ page }) => {
        const link = page.locator('#sidebarPdkManager');

        await expect(link).toHaveText(/^\s*PDK:\s*sky130\s*$/);
        await expect(page.locator('#sidebarPdkName')).toHaveText('sky130');
        await expect(link.locator('svg')).toHaveCount(2);
        await expect(link.locator('.sidebarPdkChange')).toBeVisible();
        await expect(page.locator('#sidebarPanelLayers svg')).toBeVisible();
        await expect(page.locator('#sidebarPanelRules svg')).toBeVisible();

        //In the list, over its header, and nearly as wide as the panel.
        await expect(page.locator('#layerSidebar .layerList > .sidebarPdkRow + .listHeader')).toHaveCount(1);

        const linkBox = await link.boundingBox();
        const headerBox = await page.locator('#layerSidebar .layerListHeader').boundingBox();
        const sideBox = await page.locator('#layerSidebar').boundingBox();

        expect(linkBox.y + linkBox.height).toBeLessThanOrEqual(headerBox.y + 1);
        expect(linkBox.x - sideBox.x).toBeLessThan(16);
        expect(linkBox.width).toBeGreaterThan(sideBox.width - 32);

        await expect(page.locator('#layerSidebar .layerSidebarControls')).toHaveCount(0);
        await expect(page.locator('#sidebarInfo')).toHaveCount(0);

        await link.click();

        await expect(page.locator('#pdkManagerWindow')).toBeVisible();

        await page.locator('#pdkManagerWindow .closeButton').click();

        await expect(page.locator('#pdkManagerWindow')).toBeHidden();

        //And on the rules, the same.
        await page.locator('#sidebarPanelRules').click();

        await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });
        await expect(page.locator('#layerSidebar .layerSidebarControls')).toHaveCount(0);

        await link.click();

        await expect(page.locator('#pdkManagerWindow')).toBeVisible();
    });

    ///
    ///**Its layers and rules are a tree under the PDK**, in place of the tabs over the table and their own Import and
    ///Export - asked for 2026-10-19. What is picked is what the squares under the tree act on: the PDK as one zip, or a
    ///list of it, written out as its file or emptied after asking - which Reset puts back on the built-in.
    ///
    test('its layers and rules are picked in a tree, and the squares act on what is picked', async ({ page }) => {
        await openPdkManager(page);

        await expect(page.locator('#pdkList .pdkBranch')).toHaveCount(2);

        //The built-in picked: no bin, its Reset in the bin's place, with nothing to put back yet.
        await expect(page.locator('#pdkDelete')).toHaveCount(0);
        await expect(page.locator('#pdkReset')).toBeDisabled();

        let downloading = page.waitForEvent('download');

        //The list under the name, with no line over it naming what the tree already shows picked.
        await page.locator('#pdkLayersTab').click();
        await expect(page.locator('#pdkLayerTable')).toBeVisible();
        await expect(page.locator('.pdkDetail')).not.toContainText("sky130's layers");
        await page.locator('#pdkExport').click();

        expect((await downloading).suggestedFilename()).toBe('sky130 layers.csv');

        downloading = page.waitForEvent('download');

        await page.locator('#pdkRulesTab').click();
        await expect(page.locator('.pdkRules .pdkRulesBar')).toBeVisible();
        await expect(page.locator('#pdkLayerTable')).toBeHidden();
        await expect(page.locator('#pdkImport')).toHaveAttribute('for', 'pdkRulesImport');
        await page.locator('#pdkExport').click();

        expect((await downloading).suggestedFilename()).toBe('sky130 rules.drc');

        //The rules, emptied after asking, and put back by Reset.
        await page.locator('#pdkDelete').click();
        await expect(page.locator('.pdkDeleteAsk')).toContainText("Delete sky130's rules - 30 rules");
        await page.locator('#pdkDeleteSure').click();

        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (0)');
        await expect(page.locator('#pdkDelete')).toBeDisabled();

        //The PDK picked again: Import is a whole PDK's, and the built-in's Reset is where the bin was.
        await page.locator('#pdkList .pdkRow').first().click();

        await expect(page.locator('#pdkImport')).toHaveAttribute('for', 'pdkPackageImport');
        await expect(page.locator('#pdkDelete')).toHaveCount(0);

        await page.locator('#pdkReset').click();

        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');
        await expect(page.locator('#pdkReset')).toBeDisabled();
    });

    ///
    ///**What each list is about, from the help square under the tree** - the layer panel's info icon until 2026-10-19,
    ///then an (i) over the list, then the square. In the place of the list while it is up, switched with the tree, and
    ///put away by the square again. Since 2026-11-01 the PDK, its layers and its rules, one section after another, the
    ///same whatever the tree has picked; it was the picked one's alone.
    ///
    test('the help square says what a PDK, its layers and its rules are, in place of the list', async ({ page }) => {
        await openPdkManager(page);

        //At the end of the squares, and nothing else over the list but the name.
        await expect(page.locator('.pdkSideButtons > :last-child')).toHaveId('pdkHelp');

        await page.locator('#pdkHelp').click();

        const titles = ['A PDK', 'Layers', 'Design rules'];

        await expect(page.locator('#pdkAbout .pdkAboutTitle')).toHaveText(titles);
        await expect(page.locator('#pdkAboutPdk')).toContainText('README.md');
        await expect(page.locator('#pdkAboutLayers #downloadLayermapGuide')).toBeVisible();
        await expect(page.locator('#pdkAboutRules #loadStrictDeck')).toBeVisible();
        await expect(page.locator('#pdkInfo')).toHaveCount(0);

        await page.locator('#pdkLayersTab').click();

        await expect(page.locator('#pdkAbout .pdkAboutTitle')).toHaveText(titles);
        await expect(page.locator('#pdkLayerTable')).toBeHidden();

        await page.locator('#pdkRulesTab').click();

        await expect(page.locator('#pdkAbout .pdkAboutTitle')).toHaveText(titles);

        await page.locator('#pdkHelp').click();

        await expect(page.locator('#pdkAbout')).toHaveCount(0);
        await expect(page.locator('#pdkRuleList')).toBeVisible();
    });

    test('a layer renamed in it is renamed on the file, kept, and put back by Reset', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkLayersTab').click();

        await pdkRow(page, '68/20').locator('.pdkCellName').fill('metal one');
        await pdkRow(page, '68/20').locator('.pdkCellName').press('Tab');

        await expect.poll(async () => labelFor(page, '68/20')).toBe('metal one (68/20)');

        //Reset is the PDK's, so it is under the PDK picked rather than its layers.
        await page.locator('#pdkList .pdkRow[data-pdk="sky130"]').click();

        await expect(page.locator('#pdkReset')).toBeEnabled();

        //Kept: a reload brings the PDK back as it was left, and the file named by it.
        await page.goto('/');

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);
        await expect.poll(async () => labelFor(page, '68/20'), { timeout: 15000 }).toBe('metal one (68/20)');

        await page.locator('#sidebarPdkManager').click();
        await page.locator('#pdkReset').click();

        await expect.poll(async () => labelFor(page, '68/20')).toBe('met1 (68/20)');
        await expect(page.locator('#pdkReset')).toBeDisabled();
    });

    ///
    ///**The layer panel edits the PDK in use**, so a layer renamed there is renamed on the next file drawn in it - which
    ///is what the names that carried from file to file used to do, and is now the PDK's to do.
    ///
    test('a layer renamed in the layer panel goes into the PDK, and reaches the next file', async ({ page }) => {
        await renameInPanel(page, '68/20', 'M1');

        await expect.poll(async () => labelFor(page, '68/20')).toBe('M1 (68/20)');

        await page.locator('#sidebarPdkManager').click();

        await expect(pdkRow(page, '68/20').locator('.pdkCellName')).toHaveValue('M1');

        //Its height and role stay the PDK's: the row took the name, not a row that knows nothing else.
        await expect(pdkRow(page, '68/20').locator('.pdkRole')).toHaveValue('Conductor');
        await expect(pdkRow(page, '68/20').locator('.pdkNumber').first()).toHaveValue('1370');

        await gotoExample(page, SKY130_CELL, '2d');

        await expect.poll(async () => labelFor(page, '68/20'), { timeout: 15000 }).toBe('M1 (68/20)');
    });

    ///<summary>Its rules are the deck's own text, and applied they are the rules the panel checks with.</summary>
    test('its rules, edited and applied, are the rules the rules panel checks', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkRulesTab').click();
        await page.locator('#pdkRulesAsText').click();

        await expect(page.locator('#pdkRulesEditor .monaco-editor')).toBeVisible({ timeout: 30000 });

        await page.evaluate(() => {
            const host = document.querySelector('#pdkRulesEditor');
            const editor = monaco.editor.getEditors().find(each => host.contains(each.getDomNode()));

            editor.setValue(editor.getValue() + '\nrule extra.1 width met1 777 "A rule of my own"\n');
        });

        await page.locator('#pdkRulesApply').click();

        await expect(page.locator('#pdkRulesSays')).toHaveText('Applied: 31 rules.');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (31)');

        //And in the table, which reads the same text.
        await page.locator('#pdkRulesAsTable').click();

        await expect(page.locator('#pdkRuleList [data-rule="extra.1"]')).toBeVisible();

        await page.locator('#pdkManagerWindow .closeButton').click();
        await page.locator('#sidebarPanelRules').click();

        await expect(page.locator('#drcRun')).toHaveAttribute('title', /31 rule/);
        await expect(page.locator('#layerSidebar')).toContainText('extra.1');
    });

    ///
    ///**A comment is colored as one only on a line of its own**, which is how the deck is read: a # after a rule is
    ///marked invalid rather than shown as skipped, since the parser refuses the line (the 2026-10-21 audit).
    ///
    test('the rules editor colors a comment only on a line of its own', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkRulesTab').click();
        await page.locator('#pdkRulesAsText').click();

        await expect(page.locator('#pdkRulesEditor .monaco-editor')).toBeVisible({ timeout: 30000 });

        const kinds = await page.evaluate(() => {
            const lines = monaco.editor.tokenize('# a note\nrule x.1 width met1 140 "Width" # not a note', 'gdsdeck');

            return lines.map(line => line.map(token => token.type));
        });

        expect(kinds[0]).toEqual(['comment.gdsdeck']);
        expect(kinds[1]).toContain('invalid.gdsdeck');
        expect(kinds[1]).not.toContain('comment.gdsdeck');
    });

    ///
    ///**A layer is added from the table's own last row**, held to the bottom of the list while it scrolls - asked for
    ///2026-10-19, when it was a row under the table. Folded to Add new layer until it is pressed (2026-11-01), as the
    ///rules' is. Enter adds, the row stays open for the next, and the row added is brought into view clear of it.
    ///
    test('the add row is the table\'s last, always in view, and Enter adds from it', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkLayersTab').click();

        //Folded to a plus until pressed; then its fields, the caret in the first.
        await expect(page.locator('#pdkLayerTable tfoot #pdkLayerAdd')).toHaveText('Add new layer');
        await expect(page.locator('#pdkAddLayer')).toHaveCount(0);

        await page.locator('#pdkLayerAdd').click();

        await expect(page.locator('#pdkAddLayer')).toBeFocused();

        const scroller = page.locator('.pdkLayers .pdkLayerScroll');
        const inView = async selector => {
            const box = await scroller.boundingBox();
            const row = await page.locator(selector).boundingBox();

            return row.y >= box.y && row.y + row.height <= box.y + box.height + 1;
        };

        //Forty-six rows, more than fit: the add row is at the bottom of what shows, at the top of the list and at its end.
        await expect.poll(async () => scroller.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
        await expect(page.locator('#pdkLayerTable tfoot #pdkAddLayer')).toBeVisible();
        expect(await inView('#pdkAddLayer')).toBe(true);

        await scroller.evaluate(element => { element.scrollTop = element.scrollHeight; });
        expect(await inView('#pdkAddLayer')).toBe(true);

        await scroller.evaluate(element => { element.scrollTop = 0; });

        await page.locator('#pdkAddLayer').fill('2');
        await page.locator('#pdkAddDataType').fill('0');
        await page.locator('#pdkAddName').fill('probe');
        await page.locator('#pdkAddName').press('Enter');

        await expect(pdkRow(page, '2/0').locator('.pdkCellName')).toHaveValue('probe');
        await expect(page.locator('#pdkAddName')).toHaveValue('');
        await expect.poll(async () => inView('#pdkLayerTable tr[data-pair="2/0"]')).toBe(true);

        //Not under the add row either.
        const added = await pdkRow(page, '2/0').boundingBox();
        const adder = await page.locator('#pdkLayerTable tfoot tr').boundingBox();

        expect(added.y + added.height).toBeLessThanOrEqual(adder.y + 1);

        //Its X folds it again.
        await page.locator('#pdkAddClose').click();

        await expect(page.locator('#pdkLayerAdd')).toBeVisible();
        await expect(page.locator('#pdkAddLayer')).toHaveCount(0);
    });

    ///
    ///**The layers as text, as the rules are** (asked for 2026-11-01): the layermap's own CSV in an editor, Apply making
    ///it the PDK's and saying how it read, and the table read from it again.
    ///
    test('the layers are shown as the layermap\'s text, and text applied is the table', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkNew').click();
        await page.locator('#pdkName').press('Enter');
        await page.locator('#pdkLayersTab').click();

        await page.locator('#pdkLayersAsText').click();

        await expect(page.locator('#pdkLayersAsText')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('#pdkLayerTable')).toBeHidden();
        await expect(page.locator('#pdkLayersEditor .monaco-editor')).toBeVisible({ timeout: 30000 });

        await page.evaluate(() => monaco.editor.getEditors().find(editor => editor.getContainerDomNode().closest('#pdkLayersEditor')).setValue('68,20,met1,#0000ff,,,conductor\n69,20,met2\n70,x,bad\n'));
        await page.locator('#pdkLayersApply').click();

        await expect(page.locator('#pdkLayersSays')).toContainText('Applied: 2 layers. 1 line it could not use');

        //Colored as the layermap is read (asked for 2026-11-01): the pair and the name before the rest - a layer named
        //via is a name, its role a keyword - a color with its swatch, and a comment on a line of its own.
        const tokens = await page.evaluate(() => monaco.editor.tokenize('#a comment\n68,44,via,#0000ff,1730,270,via', 'gdslayermap')
            .map(line => line.map(token => token.type.replace('.gdslayermap', ''))));

        expect(tokens[0]).toEqual(['comment']);
        expect(tokens[1]).toEqual(['type.pair', 'delimiter', 'type.pair', 'delimiter', 'layername', 'delimiter', 'color', 'delimiter', 'number', 'delimiter', 'number', 'delimiter', 'keyword']);
        await expect(page.locator('#pdkLayersEditor .colorpicker-color-decoration')).toHaveCount(1);
        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (2)');

        await page.locator('#pdkLayersAsTable').click();

        await expect(pdkRow(page, '68/20').locator('.pdkRole')).toHaveValue('Conductor');
        await expect(pdkRow(page, '69/20').locator('.pdkCellName')).toHaveValue('met2');

        //A row added in the table is in the text when it is shown again.
        await page.locator('#pdkLayerAdd').click();
        await page.locator('#pdkAddLayer').fill('70');
        await page.locator('#pdkAddDataType').fill('20');
        await page.locator('#pdkAddName').fill('met3');
        await page.locator('#pdkAddName').press('Enter');
        await page.locator('#pdkLayersAsText').click();

        await expect(page.locator('#pdkLayersEditor')).toContainText('70,20,met3');
    });

    ///
    ///**Named in the tree** - the box over the list that did it went 2026-10-19. A PDK made is named next, its box open
    ///on "New PDK" with it selected; a double-click opens it again, Enter keeps what is typed and Esc does not.
    ///
    test('a PDK is renamed in the tree, straight after it is made and by a double-click', async ({ page }) => {
        await openPdkManager(page);

        await page.locator('#pdkNew').click();

        await expect(page.locator('#pdkName')).toBeFocused();
        await expect(page.locator('#pdkName')).toHaveValue('New PDK');

        await page.keyboard.type('Mine');
        await page.keyboard.press('Enter');

        await expect(page.locator('#pdkName')).toHaveCount(0);
        await expect(page.locator('#pdkList .pdkRowOpen .pdkRowName')).toHaveText('Mine');

        //Esc keeps the name it had, and the window stays up.
        await page.locator('#pdkList .pdkRowOpen .pdkRowName').dblclick();
        await page.locator('#pdkName').fill('Not this');
        await page.keyboard.press('Escape');

        await expect(page.locator('#pdkList .pdkRowOpen .pdkRowName')).toHaveText('Mine');
        await expect(page.locator('#pdkManagerWindow')).toBeVisible();

        //Not in use, so its row offers to put it in use.
        await expect(page.locator('#pdkList .pdkRowOpen #pdkUse')).toHaveText('use');
    });

    ///
    ///**A PDK of your own**: made empty, put in use, and Mosfet - a sky130 cell, one of whose nine layers it has - keeps
    ///its numbers and loses sky130's rules. Deleted, and sky130 is in use again, naming Mosfet.
    ///
    test('a PDK of your own is made, put in use and deleted, and names only the files drawn in it', async ({ page }) => {
        await openPdkManager(page);

        await page.locator('#pdkNew').click();

        await expect(page.locator('#pdkName')).toHaveValue('New PDK');

        //A PDK made new has nothing to say about itself yet, and its layers are under it.
        await expect(page.locator('#pdkNoReadme')).toContainText('No README for this PDK');

        await page.locator('#pdkLayersTab').click();

        await expect(page.locator('#pdkNoLayers')).toBeVisible();

        await page.locator('#pdkLayerAdd').click();
        await page.locator('#pdkAddLayer').fill('65');
        await page.locator('#pdkAddDataType').fill('20');
        await page.locator('#pdkAddName').fill('diffusion');
        await page.locator('#pdkAdd').click();

        await expect(pdkRow(page, '65/20').locator('.pdkCellName')).toHaveValue('diffusion');

        await page.locator('#pdkUse').click();

        //No Use for the one in use: the tree's pill says it.
        await expect(page.locator('#pdkUse')).toHaveCount(0);
        await expect(page.locator('#pdkList .pdkRowOpen .pdkInUse')).toBeVisible();

        //One of Mosfet's nine layers is this PDK's: not drawn in it, so it names nothing and checks nothing here.
        await expect.poll(async () => labelFor(page, '65/20')).toBe('65/20');
        await expect.poll(async () => labelFor(page, '68/20')).toBe('68/20');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /not mostly New PDK's/);

        //The PDK picked, for the bin to take it rather than empty its layers.
        await page.locator('#pdkList .pdkRowOpen').click();
        await page.locator('#pdkDelete').click();
        await page.locator('#pdkDeleteSure').click();

        await expect(page.locator('#pdkList .pdkRow')).toHaveCount(3);
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is sky130:/);
        await expect.poll(async () => labelFor(page, '68/20')).toBe('met1 (68/20)');
    });

    ///
    ///**In the middle of the window** - asked for the same day, with the window larger than the AI windows' 820 it
    ///began at - and the bin cannot take the built-in away. From the PDK button over the layer panel, which is its way
    ///in since the chip at the end of the bar went (2026-10-19).
    ///
    test('the PDK button opens it in the middle of the window, and the bar has no chip for it', async ({ page }) => {
        await expect(page.locator('#pdkManagerButton')).toHaveCount(0);

        await page.locator('#sidebarPdkManager').click();

        await expect(page.locator('#pdkManagerWindow')).toBeVisible();

        const placed = await page.evaluate(() => {
            const box = document.querySelector('#pdkManagerWindow').getBoundingClientRect();

            return {
                across: Math.abs(box.left - ((window.innerWidth - box.width) / 2)),
                down: Math.abs(box.top - ((window.innerHeight - box.height) / 2)),
                width: box.width
            };
        });

        expect(placed.across).toBeLessThanOrEqual(1);
        expect(placed.down).toBeLessThanOrEqual(1);
        expect(placed.width).toBeGreaterThan(1000);

        await expect(page.locator('#pdkReset')).toBeDisabled();
    });

    ///
    ///**A row is styled through the layer panel's own settings**, opened from its swatch: every switch the gear on a
    ///layer row offers but the labels, which a layermap has no column for, written into the PDK's row - and onto the
    ///file, which sky130 is laid over. It was a bare color picker.
    ///
    test('a row is styled through the layer panel settings, and the file drawn in the PDK takes it', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkLayersTab').click();

        await pdkRow(page, '68/20').locator('.pdkLook').click();

        await expect(page.locator('#layerSettingsPdk')).toHaveText('in sky130');
        await expect(page.locator('.layerSettingsName')).toHaveValue('met1');
        await expect(page.locator('.layerLabelsToggle')).toHaveCount(0);

        await page.locator('.layerFillChoice[data-fill="Diagonal"]').click();

        await expect(pdkRow(page, '68/20').locator('.pdkLook')).toHaveAttribute('title', /diagonal/);
        await expect(pdkRow(page, '68/20').locator('.pdkLookSwatch')).toBeVisible();

        const metal = page.locator('.layerRow').filter({ has: page.locator('.layerName', { hasText: '68/20' }) }).first();

        await expect(metal.locator('svg.layerSwatchFilled title')).toContainText('Diagonal');

        //And its height, the same way.
        await page.locator('.layerSettingsHeight').fill('1400');
        await page.locator('.layerSettingsHeight').press('Tab');

        await expect(pdkRow(page, '68/20').locator('.pdkNumber').first()).toHaveValue('1400');
    });

    ///
    ///**The PDK itself picked shows what it says about itself** (asked for 2026-11-01): sky130's README, drawn from its
    ///Markdown, where its layers' table stood - the lists only when they are picked under it. A PDK with none says so and
    ///takes one; markup in a README is shown as words, an HTML one is sandboxed, and the README goes out in the zip.
    ///
    test('a PDK picked shows its README, and one is added, exported and taken out', async ({ page }) => {
        await openPdkManager(page);

        const readme = page.locator('#pdkReadme');

        await expect(readme.locator('h1')).toHaveText('SkyWater SKY130');
        await expect(readme.locator('td', { hasText: '68/20' })).toBeVisible();
        await expect(page.locator('#pdkReadmeName')).toHaveText('README.md');
        await expect(page.locator('#pdkLayerTable')).toBeHidden();

        //A list picked shows the list; the PDK picked again, its README.
        await page.locator('#pdkLayersTab').click();

        await expect(page.locator('#pdkLayerTable')).toBeVisible();
        await expect(page.locator('#pdkInfo')).toHaveCount(0);

        await page.locator('#pdkList .pdkRow[data-pdk="sky130"]').click();

        await expect(readme.locator('h1')).toHaveText('SkyWater SKY130');

        //A PDK of one's own has none until one is given; markup in it is its words.
        await page.locator('#pdkNew').click();
        await page.locator('#pdkName').press('Enter');

        await expect(page.locator('#pdkNoReadme')).toContainText('No README for this PDK');

        await page.locator('#pdkReadmeImport').setInputFiles({
            name: 'README.md',
            mimeType: 'text/markdown',
            buffer: Buffer.from('# Mine\n\nA process of my own. <script>window.__ran = true</script>\n\n- met1\n- met2\n')
        });

        await expect(readme.locator('h1')).toHaveText('Mine');
        await expect(readme.locator('li')).toHaveText(['met1', 'met2']);
        await expect(readme).toContainText('<script>window.__ran = true</script>');
        expect(await page.evaluate(() => window.__ran === true)).toBe(false);

        //It goes out in the zip, beside the layers and the rules.
        const downloading = page.waitForEvent('download');

        await page.locator('#pdkExport').click();

        const zip = await downloadBytes(await downloading);

        expect(zip.includes(Buffer.from('New PDK/README.md'))).toBe(true);

        //An HTML one is drawn in a frame that runs nothing.
        await page.locator('#pdkReadmeImport').setInputFiles({
            name: 'readme.html',
            mimeType: 'text/html',
            buffer: Buffer.from('<h1>Framed</h1><script>parent.__ran = true</script>')
        });

        await expect(readme).toHaveAttribute('sandbox', '');
        await expect(page.frameLocator('#pdkReadme').locator('h1')).toHaveText('Framed');
        expect(await page.evaluate(() => window.__ran === true)).toBe(false);

        await page.locator('#pdkReadmeRemove').click();

        await expect(page.locator('#pdkNoReadme')).toBeVisible();
    });

    ///
    ///**A rule made by choosing, not by writing its line** - asked for the same day. The form shows the line it writes, a
    ///check of two layers asks for the second, and the x asks twice.
    ///
    test('a rule is added, changed and taken out with the form', async ({ page }) => {
        await openPdkManager(page);
        await page.locator('#pdkRulesTab').click();

        await expect(page.locator('#pdkRuleList tr.pdkRule')).toHaveCount(30);

        //Add new rule at the table's foot, as New layer is at the layers', and its form under the last rule (asked for 2026-11-01).
        await expect(page.locator('#pdkRuleList tfoot #pdkRuleAdd')).toBeVisible();

        await page.locator('#pdkRuleAdd').click();

        await expect(page.locator('#pdkRuleList tbody > tr:last-child')).toHaveClass(/pdkRuleFormRow/);
        await expect(page.locator('#pdkRuleAdd')).toHaveCount(0);

        //A cross in the form's corner closes it, in place of a Cancel beside Add (asked for 2026-11-01).
        await page.locator('#pdkRuleClose').click();

        await expect(page.locator('#pdkRuleForm')).toHaveCount(0);

        await page.locator('#pdkRuleAdd').click();

        //The description is one line until the words run past it, and a line break typed is a space - the deck writes it on one.
        const description = page.locator('#pdkRuleDescription');

        await description.fill('Short');

        const oneLine = (await description.boundingBox()).height;

        await description.fill('A description long enough to run past the end of its box, so that the box takes a second line for it');

        await expect.poll(async () => (await description.boundingBox()).height).toBeGreaterThan(oneLine + 5);

        await description.fill('');
        await description.pressSequentially('Met1');
        await description.press('Enter');
        await description.pressSequentially('wider');

        await expect(description).toHaveValue('Met1 wider');

        await page.locator('#pdkRuleId').fill('met1.x');
        await page.locator('#pdkRuleLayer').selectOption('met1');
        await page.locator('#pdkRuleValue').fill('150');
        await page.locator('#pdkRuleDescription').fill('Met1 wider');

        await expect(page.locator('#pdkRuleLine')).toHaveText('rule met1.x width met1 150 "Met1 wider"');

        await page.locator('#pdkRuleSave').click();

        await expect(page.locator('#pdkRulesSays')).toHaveText('Added met1.x.');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (31)');

        const added = page.locator('#pdkRuleList tr[data-rule="met1.x"]');

        await expect(added).toBeVisible();

        await added.locator('.pdkRuleEdit').click();
        await page.locator('#pdkRuleCheck').selectOption('Enclosure');
        await page.locator('#pdkRuleSave').click();

        await expect(page.locator('#pdkRuleProblem')).toHaveText('Choose the layer it is inside.');

        await page.locator('#pdkRuleSecond').selectOption('nwell');
        await page.locator('#pdkRuleSave').click();

        await expect(page.locator('#pdkRulesSays')).toHaveText('Changed met1.x.');
        await expect(added).toContainText('met1 inside nwell');

        await added.locator('.pdkRemove').click();
        await added.locator('.pdkRuleRemoveSure').click();

        await expect(page.locator('#pdkRulesSays')).toHaveText('Removed met1.x.');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');
    });

    ///
    ///**A layer the deck does not name yet is offered from the PDK's own**, and saving a rule on it declares it - so a
    ///PDK of your own gets its rules without a layer line typed.
    ///
    test('a rule on one of the PDK layers declares that layer in the deck', async ({ page }) => {
        await openPdkManager(page);

        await page.locator('#pdkNew').click();
        await page.locator('#pdkLayersTab').click();
        await page.locator('#pdkLayerAdd').click();
        await page.locator('#pdkAddLayer').fill('65');
        await page.locator('#pdkAddDataType').fill('20');
        await page.locator('#pdkAddName').fill('diffusion');
        await page.locator('#pdkAdd').click();

        await page.locator('#pdkRulesTab').click();

        await expect(page.locator('#pdkNoRules')).toBeVisible();

        await page.locator('#pdkRuleAdd').click();
        await page.locator('#pdkRuleId').fill('diff.1');
        await page.locator('#pdkRuleLayer').selectOption('diffusion');
        await page.locator('#pdkRuleValue').fill('150');
        await page.locator('#pdkRuleSave').click();

        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (1)');
        await expect(page.locator('#pdkRulesProblems')).toHaveCount(0);

        await page.locator('#pdkRulesAsText').click();

        await expect(page.locator('#pdkRulesEditor .monaco-editor')).toBeVisible({ timeout: 30000 });

        const text = await page.evaluate(() => {
            const host = document.querySelector('#pdkRulesEditor');

            return monaco.editor.getEditors().find(each => host.contains(each.getDomNode())).getValue();
        });

        expect(text).toBe('layer diffusion 65/20\nrule diff.1 width diffusion 150 ""\n');
    });

    ///
    ///**A whole PDK as one zip** - its layers, its rules and its textures' pictures - out with Export, and back in through
    ///Import or dropped on the list, each time under a name of its own. Asked for the same day: a PDK brought in whole
    ///rather than a file at a time.
    ///
    test('a PDK goes out as one zip and comes back in, chosen or dropped', async ({ page }) => {
        await openPdkManager(page);

        const downloading = page.waitForEvent('download');

        await page.locator('#pdkExport').click();

        const download = await downloading;

        expect(download.suggestedFilename()).toBe('sky130.zip');

        const zip = fs.readFileSync(await download.path());

        await page.locator('#pdkPackageImport').setInputFiles({ name: 'sky130.zip', mimeType: 'application/zip', buffer: zip });

        await expect(page.locator('#pdkImportSays')).toHaveText('Added sky130 2: 46 layers and 30 rules.');
        await expect(page.locator('#pdkList .pdkRowOpen .pdkRowName')).toHaveText('sky130 2');

        const carried = await page.evaluateHandle(content => {
            const bytes = Uint8Array.from(atob(content), character => character.charCodeAt(0));
            const transfer = new DataTransfer();

            transfer.items.add(new File([bytes], 'dropped.zip', { type: 'application/zip' }));

            return transfer;
        }, zip.toString('base64'));

        await page.dispatchEvent('#pdkList', 'dragover', { dataTransfer: carried });

        await expect(page.locator('.pdkSide.fileDropOver')).toHaveCount(1);

        await page.dispatchEvent('#pdkList', 'drop', { dataTransfer: carried });

        await expect(page.locator('#pdkImportSays')).toHaveText('Added sky130 3: 46 layers and 30 rules.');
        await expect(page.locator('#pdkList .pdkRow')).toHaveCount(5);

        //The bin, asked first, takes the one shown.
        await page.locator('#pdkDelete').click();
        await page.locator('#pdkDeleteSure').click();

        await expect(page.locator('#pdkList .pdkRow')).toHaveCount(4);
    });
});

test.describe('which files a PDK names', () => {
    test.beforeEach(async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);
    });

    ///
    ///**A sky130 upload is named and checked by sky130** - the built-in used to name the bundled examples only, and an
    ///upload came in as numbers even when it was sky130. Most of its layers being sky130's is what says it is.
    ///
    test('an upload drawn in sky130 is named and checked by it', async ({ page }) => {
        await page.locator('#fileUpload').setInputFiles(path.join(__dirname, 'fixtures', 'placed.gds'));

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('placed.gds');
        await expect.poll(async () => labelFor(page, '65/20'), { timeout: 15000 }).toBe('diff (65/20)');

        await page.locator('#sidebarPanelRules').click();

        await expect(page.locator('#drcRun')).toBeVisible();
    });

    ///
    ///**And a file from another process keeps its numbers**: sky130 labels laid over it would read as right when they are
    ///wrong, which is why the bundled names stopped at the examples. The same fixture, on gf180mcu's numbers for COMP and
    ///Metal1 but sky130's datatype, 20 - a pair no PDK kept here has, so nothing else is put in use for it either.
    ///
    test('an upload from another process keeps its numbers and is not checked by sky130', async ({ page }) => {
        await page.locator('#fileUpload').setInputFiles({
            name: 'other.gds',
            mimeType: 'application/octet-stream',
            buffer: onOtherLayers(fs.readFileSync(path.join(__dirname, 'fixtures', 'placed.gds')), { 65: 22, 67: 34 })
        });

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('other.gds');
        await expect.poll(async () => layerPairs(page), { timeout: 15000 }).toEqual(['22/20', '34/20']);

        expect(await rowText(page)).toEqual(['22/20', '34/20']);

        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /not mostly sky130's/);

        await page.locator('#sidebarPanelRules').click();

        await expect(page.locator('#drcRun')).toHaveCount(0);
        await expect(page.locator('#rulesNone')).toContainText('not drawn in sky130');
    });
});

//
//**gf180mcu, built in beside sky130** (2026-11-02, asked for): GlobalFoundries' 180 nm PDK as its gf180mcuD stack - its
//layers from the design manual's table, a starter deck, and a README with the PDK's own cross-section - kept like sky130,
//with Reset in place of the bin and its name its own. Its files are held to the foundry's tables without a browser, in
//Gf180LayermapTests and DrcDeckTests; what is here is the window, and a GF180 file putting it in use.
//
test.describe('gf180mcu', () => {
    ///
    ///Mosfet moved onto gf180mcu's pairs: its layers onto GF180's - COMP, Poly2, Contact, Metal1, Nplus and SAB - and its
    ///datatypes onto GF180's, 10 for its labels and 0 for everything else. A file drawn in gf180mcu as far as its layers
    ///can say, and in nothing else kept here.
    ///
    function onGf180(bytes) {
        const copy = onOtherLayers(bytes, { 65: 22, 66: 30, 67: 33, 68: 34, 93: 32, 95: 49 });

        for (let at = 0; at + 4 <= copy.length; ) {
            const length = copy.readUInt16BE(at);

            if (length < 4)
                break;

            //DATATYPE is record 0x0E and TEXTTYPE 0x16, each holding one two-byte integer.
            if ((copy[at + 2] === 0x0E || copy[at + 2] === 0x16) && copy[at + 3] === 0x02) {
                if (copy.readInt16BE(at + 4) === 5)
                    copy.writeInt16BE(10, at + 4);
                else
                    copy.writeInt16BE(0, at + 4);
            }

            at += length;
        }

        return copy;
    }

    test('is built in beside sky130, with its README, its layers and its rules', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await openPdkManager(page);

        await page.locator('#pdkList .pdkRow[data-pdk="gf180mcu"]').click();

        //Its README, and the cross-section beside it drawn in.
        const readme = page.locator('#pdkReadme');

        await expect(readme.locator('h1')).toHaveText('GlobalFoundries GF180MCU');
        await expect(readme.locator('td', { hasText: '81/0' })).toBeVisible();
        await expect.poll(() => readme.locator('img').evaluate(picture => picture.naturalWidth)).toBeGreaterThan(0);

        //A built-in as it ships: nothing to reset, no bin, and its name its own.
        await expect(page.locator('#pdkReset')).toBeDisabled();
        await expect(page.locator('#pdkDelete')).toHaveCount(0);
        await page.locator('#pdkList .pdkRowOpen .pdkRowName').dblclick();
        await expect(page.locator('#pdkName')).toHaveCount(0);

        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (104)');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (56)');

        await page.locator('#pdkLayersTab').click();

        await expect(pdkRow(page, '34/0').locator('.pdkCellName')).toHaveValue('Metal1');
        await expect(pdkRow(page, '34/0').locator('.pdkRole')).toHaveValue('Conductor');
        await expect(pdkRow(page, '34/0').locator('.pdkNumber').first()).toHaveValue('910');
        await expect(pdkRow(page, '81/0').locator('.pdkCellName')).toHaveValue('Metal5');
        await expect(pdkRow(page, '41/0').locator('.pdkRole')).toHaveValue('Via');

        //Put in use, it names no sky130 file: Mosfet keeps its numbers under it.
        await page.locator('#pdkList .pdkRow[data-pdk="gf180mcu"]').click();
        await page.locator('#pdkList .pdkRowOpen #pdkUse').click();

        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is gf180mcu/);
        await expect.poll(async () => labelFor(page, '68/20')).toBe('68/20');
    });

    ///
    ///**A GF180 file of your own is named by the PDK in use**, as any upload is: under sky130 it keeps its numbers, and
    ///gf180mcu put in use names it. Only an example the app lists puts its PDK in use by itself - see the next test; a
    ///file off somebody's machine is judged by the PDK in use alone, since a guess across every PDK kept named a DXF
    ///drawing as GF180 (dxf.spec).
    ///
    test('a file drawn in it, uploaded under sky130, keeps its numbers until gf180mcu is put in use', async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await page.locator('#fileUpload').setInputFiles({
            name: 'gf180.gds',
            mimeType: 'application/octet-stream',
            buffer: onGf180(fs.readFileSync(path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', 'Mosfet.gds')))
        });

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('gf180.gds');
        await expect.poll(async () => labelFor(page, '34/0'), { timeout: 15000 }).toBe('34/0');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /not mostly sky130's/);

        await openPdkManager(page);
        await page.locator('#pdkList .pdkRow[data-pdk="gf180mcu"]').click();
        await page.locator('#pdkList .pdkRowOpen #pdkUse').click();

        await expect.poll(async () => labelFor(page, '34/0'), { timeout: 15000 }).toBe('Metal1 (34/0)');

        expect(await labelFor(page, '22/0')).toBe('COMP (22/0)');
        expect(await labelFor(page, '34/10')).toBe('Metal1.label (34/10)');

        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is gf180mcu: it names this file's layers and checks it/);
    });

    ///
    ///**An example drawn in the other built-in puts that one in use** - a GF180 cell from the picker while sky130 is in
    ///use, named, checked and clean, a signed-off cell under its deck - and a sky130 cell after it puts sky130 back.
    ///
    test('a GF180 cell from the examples puts it in use, named, checked and clean, and a sky130 cell puts sky130 back', async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await selectExample(page, 'gf180mcu_fd_sc_mcu7t5v0__nand2_1.gds');

        await expect.poll(async () => labelFor(page, '34/0'), { timeout: 15000 }).toBe('Metal1 (34/0)');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is gf180mcu: it names this file's layers and checks it/);

        await page.locator('#sidebarPanelRules').click();
        await page.locator('#drcRun').click();

        await expect(page.locator('#drcNotice')).toContainText('no violations', { timeout: 60000 });
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintClear/);

        //Back to the layers, whose rows say what names a file.
        await page.locator('#sidebarPanelLayers').click();
        await selectExample(page, `${SKY130_CELL}.gds`);

        await expect.poll(async () => labelFor(page, '68/20'), { timeout: 15000 }).toBe('met1 (68/20)');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is sky130:/);
    });

    ///
    ///**But never away from a PDK somebody made.** One they put in use is one they chose, so a GF180 example opened
    ///under it stays under it, as its numbers.
    ///
    test('a GF180 example opened under a PDK of one\'s own stays under that one', async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await openPdkManager(page);
        await page.locator('#pdkNew').click();
        await page.locator('#pdkName').press('Enter');
        await page.locator('#pdkList .pdkRowOpen #pdkUse').click();

        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is New PDK/);

        await page.locator('#pdkManagerWindow .closeButton').click();

        await selectExample(page, 'gf180mcu_fd_sc_mcu7t5v0__nand2_1.gds');

        await expect.poll(async () => labelFor(page, '34/0'), { timeout: 15000 }).toBe('34/0');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is New PDK/);
    });
});

//
//**IHP SG13G2, built in after gf180mcu** (2026-11-02, asked for): its README with the stack drawn to scale, its 195 layers
//and its 68 rules - and its cells, which put it in use from the picker and pass its deck.
//
test.describe('ihp-sg13g2', () => {
    test('is built in after gf180mcu, with its README, its layers and its rules', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');
        await openPdkManager(page);

        await page.locator('#pdkList .pdkRow[data-pdk="ihp-sg13g2"]').click();

        //Its README, and the stack beside it drawn in.
        const readme = page.locator('#pdkReadme');

        await expect(readme.locator('h1')).toHaveText('IHP SG13G2');
        await expect(readme.locator('td', { hasText: '134/0' })).toBeVisible();
        await expect.poll(() => readme.locator('img').evaluate(picture => picture.naturalWidth)).toBeGreaterThan(0);

        //A built-in as it ships: nothing to reset, no bin.
        await expect(page.locator('#pdkReset')).toBeDisabled();
        await expect(page.locator('#pdkDelete')).toHaveCount(0);

        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (195)');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (68)');

        await page.locator('#pdkLayersTab').click();

        await expect(pdkRow(page, '8/0').locator('.pdkCellName')).toHaveValue('Metal1');
        await expect(pdkRow(page, '8/0').locator('.pdkRole')).toHaveValue('Conductor');
        await expect(pdkRow(page, '8/0').locator('.pdkNumber').first()).toHaveValue('640');
        await expect(pdkRow(page, '8/25').locator('.pdkCellName')).toHaveValue('Metal1.text');
        await expect(pdkRow(page, '134/0').locator('.pdkCellName')).toHaveValue('TopMetal2');
        await expect(pdkRow(page, '125/0').locator('.pdkRole')).toHaveValue('Via');
    });

    ///
    ///**An IHP cell from the picker puts ihp-sg13g2 in use**, named, checked and clean - a signed-off cell under its deck -
    ///and a sky130 cell after it puts sky130 back.
    ///
    test('an IHP cell from the examples puts it in use, named, checked and clean, and a sky130 cell puts sky130 back', async ({ page }) => {
        await gotoApp(page);
        await expectLoaded(page);

        await selectExample(page, 'sg13g2_nand2_1.gds');

        await expect.poll(async () => labelFor(page, '8/0'), { timeout: 15000 }).toBe('Metal1 (8/0)');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is ihp-sg13g2: it names this file's layers and checks it/);

        expect(await labelFor(page, '8/25')).toBe('Metal1.text (8/25)');

        await page.locator('#sidebarPanelRules').click();
        await page.locator('#drcRun').click();

        await expect(page.locator('#drcNotice')).toContainText('no violations', { timeout: 60000 });
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintClear/);

        await page.locator('#sidebarPanelLayers').click();
        await selectExample(page, `${SKY130_CELL}.gds`);

        await expect.poll(async () => labelFor(page, '68/20'), { timeout: 15000 }).toBe('met1 (68/20)');
        await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is sky130:/);
    });
});

//
//**Rules written by an AI model** - asked for 2026-10-19 as a box in the PDK manager, and since 2026-10-31 in the AI assistant:
//Write with AI opens it on the PDK it was pressed on, and the assistant reads that PDK's deck with read_rules and adds to
//it with add_rules, a card waiting on each change. No provider is reached: Anthropic's API is answered from a script, as
//in ask-ai.spec, and everything else off this machine is refused.
//
test.describe('rules written with AI', () => {
    const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };

    function says(text) {
        return { content: [{ type: 'text', text }], stop_reason: 'end_turn' };
    }

    function calls(id, name, input) {
        return { content: [{ type: 'tool_use', id, name, input }], stop_reason: 'tool_use' };
    }

    async function fakeClaude(page, replies) {
        const sent = [];

        await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort('blockedbyclient'));
        await page.route('https://api.anthropic.com/**', async route => {
            const request = route.request();

            if (request.method() === 'OPTIONS') {
                await route.fulfill({ status: 204, headers: CORS });

                return;
            }

            sent.push(request.postDataJSON());

            await route.fulfill({
                status: 200,
                headers: { ...CORS, 'Content-Type': 'application/json' },
                body: JSON.stringify(replies[Math.min(sent.length - 1, replies.length - 1)])
            });
        });

        return sent;
    }

    test.beforeEach(async ({ page }) => {
        await page.addInitScript(stored => {
            try {
                window.localStorage.setItem('gdsviewer.llmConnections', 'r' + stored);
            }
            catch (e) { }
        }, JSON.stringify({ Claude: { Name: 'Claude', ProviderType: 'Anthropic', ApiKey: 'test-key', MaxTokens: 8192 } }));
    });

    ///
    ///**Write with AI opens the AI assistant, offered words about this PDK**, where a box in the manager was: the assistant reads the
    ///deck, its layers and the grammar, and adds lines to it - a card that waits for Approve on the rung the window opens on,
    ///showing the deck's diff - and the PDK's rules are those lines more.
    ///
    test('Write with AI opens the AI assistant, which reads the PDK\'s deck and adds rules to it once approved', async ({ page }) => {
        const sent = await fakeClaude(page, [
            calls('toolu_1', 'read_rules', {}),
            calls('toolu_2', 'add_rules', { lines: 'layer met9 99/20\nrule m9.1 width met9 300 "Met9 width"\nrule m9.2 space met9 300 "Met9 spacing"' }),
            says('Added m9.1 and m9.2.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openPdkManager(page);
        await page.locator('#pdkRulesTab').click();

        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');
        await expect(page.locator('#pdkAiWriter')).toHaveCount(0);

        await page.locator('#pdkRulesAi').click();

        await expect(page.locator('#askAiWindow')).toBeVisible();
        await expect(page.locator('#aiPrompt')).toHaveValue(/^Add these rules to sky130's deck: met1 at least 0\.14/);

        await page.locator('#aiPrompt').fill('met9 at least 0.3 um wide and apart');
        await page.locator('#aiSend').click();

        //The change waits, and nothing is in the deck until it is approved.
        const card = page.locator('.aiCard').filter({ hasText: 'add rules' });

        await expect(card.locator('.aiApprove')).toBeVisible({ timeout: 30000 });
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');

        await card.locator('.aiApprove').click();

        await expect(page.locator('#aiTranscript')).toContainText('Added m9.1 and m9.2.');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (32)');
        await expect(page.locator('#pdkRuleList tr[data-rule="m9.1"]')).toHaveCount(1);

        //What it was told: the tools offered, and the PDK's deck, layers and grammar in read_rules' answer.
        const offered = sent[0].tools.map(tool => tool.name);

        expect(offered).toContain('read_rules');
        expect(offered).toContain('add_rules');
        expect(JSON.stringify(sent[0].system)).toContain('Design rules are written into the PDK sky130');

        const read = JSON.stringify(sent[1].messages);

        expect(read).toContain('The PDK is sky130.');
        expect(read).toContain('met1 68/20');
        expect(read).toContain('derive <name>');
    });

    ///
    ///**Write with AI over the layers too** (asked for 2026-11-01): the assistant reads the layermap and its format, and
    ///sets rows in it - a card waiting for Approve - laid over what is there, so a layer renamed keeps its color.
    ///
    test('Write with AI over the layers reads the layermap and sets layers in it once approved', async ({ page }) => {
        const sent = await fakeClaude(page, [
            calls('toolu_1', 'read_layers', {}),
            calls('toolu_2', 'set_layers', { rows: '99,20,met6,#ff8800,7700,1260,conductor\n68,20,metal1' }),
            says('Added met6 and renamed met1.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openPdkManager(page);
        await page.locator('#pdkLayersTab').click();

        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (46)');

        //Blue, as the rules' is.
        const blue = await page.locator('#pdkLayersAi').evaluate(element => getComputedStyle(element).backgroundColor);

        expect(blue).toBe(await page.locator('#pdkLayersApply').evaluate(element => getComputedStyle(element).backgroundColor));

        await page.locator('#pdkLayersAi').click();

        await expect(page.locator('#aiPrompt')).toHaveValue(/^Add these layers to sky130's layermap/);

        await page.locator('#aiPrompt').fill('met6 on 99/20');
        await page.locator('#aiSend').click();

        const card = page.locator('.aiCard').filter({ hasText: 'set layers' });

        await expect(card.locator('.aiApprove')).toBeVisible({ timeout: 30000 });
        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (46)');

        await card.locator('.aiApprove').click();

        await expect(page.locator('#aiTranscript')).toContainText('Added met6 and renamed met1.');
        await expect(page.locator('#pdkLayersTab')).toHaveText('Layers (47)');
        await expect(pdkRow(page, '99/20').locator('.pdkCellName')).toHaveValue('met6');
        await expect(pdkRow(page, '68/20').locator('.pdkCellName')).toHaveValue('metal1');
        await expect(pdkRow(page, '68/20').locator('.pdkRole')).toHaveValue('Conductor');

        const offered = sent[0].tools.map(tool => tool.name);

        expect(offered).toContain('read_layers');
        expect(offered).toContain('set_layers');
        expect(JSON.stringify(sent[0].system)).toContain('read_layers gives it and its format');

        const read = JSON.stringify(sent[1].messages);

        expect(read).toContain('68,20,met1');
        expect(read).toContain('You are writing a layermap');
    });

    ///A rule the deck cannot read is refused, with why, and nothing goes in.
    test('rules the deck cannot read are refused to the model, and nothing goes in', async ({ page }) => {
        const sent = await fakeClaude(page, [
            calls('toolu_1', 'add_rules', { lines: 'rule m1.x overlap met1 100 "Not a check this engine has"' }),
            says('That check is not one the deck has.')
        ]);

        await gotoExample(page, MOSFET, '2d');
        await openPdkManager(page);
        await page.locator('#pdkRulesTab').click();
        await page.locator('#pdkRulesAi').click();

        await page.locator('#aiMode').selectOption('AutoReadWrite');
        await page.locator('#aiPrompt').fill('an overlap rule');
        await page.locator('#aiSend').click();

        await expect(page.locator('#aiTranscript')).toContainText('That check is not one the deck has.');
        await expect(page.locator('#pdkRulesTab')).toHaveText('Rules (30)');

        expect(JSON.stringify(sent[1].messages)).toContain('No rule in those lines');
    });
});
