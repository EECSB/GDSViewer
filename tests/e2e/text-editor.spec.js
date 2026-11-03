//The text view and its save path: Monaco loads its own assets, shows the record dump, and an edit made
//there goes back into the file - or is refused by the line, leaving the file alone.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    selectView,
    editorText,
    expectEditorLoaded,
    saveEditorText,
    editorSaveFailed,
    layerNumbers,
    svgCounts,
    selectExample,
    uploadFile,
    openFile,
    MOSFET,
    MOSFET_POLYGONS,
    SKY130_CELL,
    previewPicture,
    importRules
} = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');

    //Monaco is fetched lazily through its own AMD loader, so its assets are part of what this covers.
    await expectEditorLoaded(page);
});

test('shows the record dump, one record per line', async ({ page }) => {
    const text = await editorText(page);

    expect(text.split('\n')[0]).toMatch(/^HEADER: /);
    expect(text).toContain('LIBNAME: mosfet');
    expect(text).toContain('ENDLIB');
});

test('numbers are written so they can be read back anywhere', async ({ page }) => {
    const text = await editorText(page);

    //A decimal point rather than whatever the browser's locale would use, because this dump is a data
    //format that Deserialize has to read.
    expect(text).toMatch(/UNITS: 0\.001 /);
});

test('an edit is saved back into the file and the rest of the app follows', async ({ page }) => {
    const text = await editorText(page);

    const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    expect(messages.join(' ')).toContain('Saved');

    //The layer list beside the text was rebuilt from the edited file, which is what makes the change visible at all.
    await expect.poll(async () => layerNumbers(page)).toContain(200);
    await expect.poll(async () => layerNumbers(page)).not.toContain(65);

    //And so was every other view's.
    await selectView(page, 'View2DSvg');

    await expect.poll(async () => layerNumbers(page)).toContain(200);
});

test('a line that cannot be read is refused by number, and the file is untouched', async ({ page }) => {
    const text = await editorText(page);

    const messages = await saveEditorText(page, text.replace('LIBNAME: mosfet ', 'WIBBLE: mosfet '));

    expect(messages.join(' ')).toContain('unknown record type');
    expect(messages.join(' ')).toContain('WIBBLE');

    //Nothing was applied: the file still draws exactly as it did.
    await selectView(page, 'View2DSvg');
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

test('a structurally broken edit names the record and where', async ({ page }) => {
    const text = await editorText(page);

    //Deleting a record an element needs shifts every later one into the wrong slot.
    const messages = await saveEditorText(page, text.replace('LAYER: 65 \n', ''));

    expect(messages.join(' ')).toMatch(/Record \d+ is \w+ where LAYER was expected/);
});

///
///A refusal is red, and a save is not.
///
///The point of moving this out of a dialog: the message stays next to the text it is about, and it has to
///be tellable at a glance which of the two it is. saveEditorText already fails outright if a dialog
///appears, so that half is covered on every save in this file.
///
test('a refused save is reported in red, in the page', async ({ page }) => {
    const text = await editorText(page);

    await saveEditorText(page, text.replace('LIBNAME: mosfet ', 'WIBBLE: mosfet '));

    await expect(page.locator('.editorMessage')).toBeVisible();
    expect(await editorSaveFailed(page)).toBe(true);

    //Red, read off what is actually painted rather than off the class name alone.
    const color = await page.locator('.editorMessageText').evaluate(node => getComputedStyle(node).color);
    const [red, green, blue] = color.match(/\d+/g).map(Number);

    expect(red).toBeGreaterThan(green + 40);
    expect(red).toBeGreaterThan(blue + 40);
});

test('a save that works is reported too, and not in red', async ({ page }) => {
    const text = await editorText(page);

    const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    expect(messages.join(' ')).toContain('Saved');
    expect(await editorSaveFailed(page)).toBe(false);
});

///The strip is dismissible, and a second save replaces the first one's message rather than stacking.
test('the message can be dismissed, and the next save replaces it', async ({ page }) => {
    const text = await editorText(page);

    await saveEditorText(page, text.replace('LIBNAME: mosfet ', 'WIBBLE: mosfet '));
    await expect(page.locator('.editorMessage')).toHaveCount(1);

    await page.locator('.editorMessageDismiss').click();
    await expect(page.locator('.editorMessage')).toHaveCount(0);

    //A good save now, which should leave exactly one strip and not the red one.
    const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    await expect(page.locator('.editorMessage')).toHaveCount(1);
    expect(messages.join(' ')).toContain('Saved');
    expect(await editorSaveFailed(page)).toBe(false);
});

///Opening a different file clears it - the message would otherwise be about something no longer on screen.
test('opening another file clears the message', async ({ page }) => {
    const text = await editorText(page);

    await saveEditorText(page, text.replace('LIBNAME: mosfet ', 'WIBBLE: mosfet '));
    await expect(page.locator('.editorMessage')).toBeVisible();

    await selectExample(page, `${SKY130_CELL}.gds`);

    await expect(page.locator('.editorMessage')).toHaveCount(0);
});

///
///The file's cells beside the text, and a press on one going to where it begins.
///
///Which line that is comes from where the cell's records are, handed to the editor once as the text goes in; the
///editor marks it and moves the mark with what is typed. So these check the line it lands on against the text as
///it stands, after typing as well as before.
///
test.describe('the cell list', () => {
    test.beforeEach(async ({ page }) => {
        //A leaf with one square, placed three times by a top: two cells, one inside the other.
        await uploadFile(page, 'e2e/fixtures/placed.gds');

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('placed.gds');
        await expect.poll(async () => editorText(page), { timeout: 60000 }).toContain('STRNAME: LEAF');

        //Shut by the address the first file was opened with - see gotoExample - and opened by the bar's switch.
        await page.locator('#cellTreeButton').click();

        await expect(page.locator('#textCells')).toBeVisible();
    });

    ///The line a cell begins on in the editor's text as it is now: its BGNSTR, the line before its name.
    async function beginsOn(page, name) {
        return (await editorText(page)).split('\n').findIndex(line => line.startsWith(`STRNAME: ${name}`));
    }

    ///The line the cursor is on, which the editor numbers in its gutter.
    async function cursorLine(page) {
        return Number(await page.locator('.monaco-editor .line-numbers.active-line-number').first().textContent());
    }

    async function goTo(page, name) {
        await page.locator(`#textCells .textCellRow[data-cell="${name}"]`).click();
    }

    test('lists the file\'s cells, each under what places it', async ({ page }) => {
        const rows = await page.locator('#textCells .cellRowPair').evaluateAll(pairs => pairs.map(pair => `${pair.dataset.depth}:${pair.textContent.trim()}`));

        expect(rows).toEqual(['0:TOP', '1:LEAF']);
    });

    test('a press goes to where the cell begins', async ({ page }) => {
        for (const name of ['LEAF', 'TOP']) {
            await goTo(page, name);

            await expect.poll(async () => cursorLine(page)).toBe(await beginsOn(page, name));
        }
    });

    ///
    ///**And still does after lines are typed above it.** The marks move with the text; nothing reads the text again
    ///to find the cells, which is what keeps a press a lookup on a file of a hundred thousand lines.
    ///
    test('and still does after lines are typed above it', async ({ page }) => {
        //Whichever of the two the file defines first.
        let first = 'TOP';
        let second = 'LEAF';

        if (await beginsOn(page, 'LEAF') < await beginsOn(page, 'TOP')) {
            first = 'LEAF';
            second = 'TOP';
        }

        const was = await beginsOn(page, second);

        //At the start of the first cell's first line, so it and everything after it move down three.
        await goTo(page, first);
        await page.keyboard.press('Enter');
        await page.keyboard.press('Enter');
        await page.keyboard.press('Enter');

        await expect.poll(async () => beginsOn(page, second)).toBe(was + 3);

        for (const name of [second, first]) {
            await goTo(page, name);

            await expect.poll(async () => cursorLine(page)).toBe(await beginsOn(page, name));
        }
    });

    ///A cell added in the text and saved is a cell of the file, so it is listed - and can be gone to.
    test('a cell added in the text and saved is listed, and gone to', async ({ page }) => {
        const text = await editorText(page);
        const stamps = text.split('\n').find(line => line.startsWith('BGNSTR: '));

        const messages = await saveEditorText(page, text.replace('ENDLIB: ', `${stamps}\nSTRNAME: ADDED \nENDSTR: \nENDLIB: `));

        expect(messages.join(' ')).toContain('Saved');

        await expect(page.locator('#textCells .textCellRow[data-cell="ADDED"]')).toHaveCount(1);

        await goTo(page, 'ADDED');

        await expect.poll(async () => cursorLine(page)).toBe(await beginsOn(page, 'ADDED'));
    });

    ///The lines lit as the placement gone to, by the decoration that lights them - or null when none is.
    async function litPlacement(page) {
        return page.evaluate(() => {
            const lit = window.monaco.editor.getModels()[0].getAllDecorations().find(one => one.options.className === 'textPlacedLines');

            if (lit == null)
                return null;

            return { from: lit.range.startLineNumber, to: lit.range.endLineNumber };
        });
    }

    ///
    ///**A placed cell opens to where it is placed**: LEAF, under the TOP that places it three times, opens to a row for
    ///each - where it is - and a press on one goes to that one's lines, its SREF to its ENDEL, lit. Marked as the text
    ///went in, so still found after lines are typed above it.
    ///
    test('a placed cell opens to its placements, and a press goes to that one\'s lines', async ({ page }) => {
        const fold = page.locator('#textCells .cellRowPair[data-depth="1"] .cellRowFold');

        //TOP is placed by nothing, so it has nothing to open.
        await expect(page.locator('#textCells .cellRowPair[data-depth="0"] .cellRowFold')).toHaveCount(0);
        await expect(fold).toHaveAttribute('aria-expanded', 'false');
        await expect(page.locator('#textCells .textPlacement')).toHaveCount(0);

        await fold.click();

        const placed = page.locator('#textCells .textPlacement');

        await expect(placed).toHaveCount(3);

        expect((await placed.allTextContents()).map(one => one.trim())).toEqual(['(600, 0)', '(1200, 0)', '(1800, 0)']);

        await placed.nth(1).click();

        await expect.poll(async () => litPlacement(page)).not.toBeNull();

        let lines = (await editorText(page)).split('\n');
        const lit = await litPlacement(page);

        expect(lines[lit.from - 1]).toMatch(/^SREF:/);
        expect(lines[lit.to - 1]).toMatch(/^ENDEL:/);
        expect(lines.slice(lit.from - 1, lit.to).join('\n')).toContain('XY: 1200 0');

        //Two lines typed at the very top, and the same placement is found two lines further down.
        await page.evaluate(() => {
            window.monaco.editor.getModels()[0].applyEdits([{ range: new window.monaco.Range(1, 1, 1, 1), text: '\n\n' }]);
        });

        await placed.nth(1).click();

        await expect.poll(async () => (await litPlacement(page))?.from).toBe(lit.from + 2);

        lines = (await editorText(page)).split('\n');

        expect(lines.slice(lit.from + 1, lit.to + 2).join('\n')).toContain('XY: 1200 0');

        //And it shuts again.
        await fold.click();

        await expect(placed).toHaveCount(0);
    });

    ///
    ///**The cell pointed at is drawn under the list**, as the 2D tree draws it, and the frame empties again as the
    ///pointer leaves the list.
    ///
    test('pointing at a cell draws it under the list', async ({ page }) => {
        const frame = page.locator('#textCellPreview');

        await expect(frame).toHaveText(/Point at a cell/);

        await page.locator('#textCells .textCellRow[data-cell="LEAF"]').hover();

        await expect(frame.locator('img.cellPreview')).toBeVisible({ timeout: 15000 });
        await expect.poll(async () => (await previewPicture(page, '#textCellPreview img.cellPreview'))?.drawn ?? 0, { timeout: 15000 })
            .toBeGreaterThan(0);

        const editor = await page.locator('#gdsTextEditor').boundingBox();

        await page.mouse.move(editor.x + (editor.width / 2), editor.y + (editor.height / 2));

        await expect(frame).toHaveText(/Point at a cell/);
    });

    ///The cross on the list puts it away, and so does the switch it shares with the 2D view's tree.
    test('the cross and the switch in the bar both put it away', async ({ page }) => {
        await page.locator('#textCellsClose').click();

        await expect(page.locator('#textCells')).toHaveCount(0);
        await expect(page.locator('#cellTreeButton')).toHaveAttribute('aria-pressed', 'false');

        await page.locator('#cellTreeButton').click();

        await expect(page.locator('#textCells')).toBeVisible();

        await page.locator('#cellTreeButton').click();

        await expect(page.locator('#textCells')).toHaveCount(0);
    });
});

///
///**The layers and the rules beside the text**, so an edit to the records can be checked against the rules where it
///was made. The list names the layer numbers the records are written in, and is not edited from here: a layer taken
///out would rewrite the text under the editor, over whatever had been typed.
///
test.describe('the layers and the rules beside the text', () => {
    ///A deck naming Mosfet's poly with a width nothing could satisfy, so every poly shape is a fault.
    const EVERY_POLY = ['layer poly 66/20', 'rule poly.1a width poly 100000 "Absurd poly width"'].join('\n');

    ///Into the PDK in use, through the PDK manager - where a deck goes since the rules panel's Import went there.
    async function loadDeck(page) {
        await page.locator('#sidebarPanelRules').click();
        await importRules(page, EVERY_POLY);

        await expect(page.locator('.rulesRow')).toHaveCount(1, { timeout: 60000 });
        await expect(page.locator('#drcRun')).toBeVisible();
    }

    ///The lines lit as the shape a fault is on, by the decoration that lights them - or null when none is.
    async function litLines(page) {
        return page.evaluate(() => {
            const lit = window.monaco.editor.getModels()[0].getAllDecorations().find(one => one.options.className === 'textFaultLines');

            if (lit == null)
                return null;

            return { from: lit.range.startLineNumber, to: lit.range.endLineNumber };
        });
    }

    test('the layer list stands beside the text, and a save keeps the layers\' names', async ({ page }) => {
        await expect(page.locator('#layersToggle')).toBeEnabled();
        await expect(page.locator('#layerSidebar')).toBeVisible();
        await expect(page.locator('#layerSidebar .layerName').first()).toHaveText('diff (65/20)');
        await expect(page.locator('#sidebarPanelRules')).toBeVisible();

        //Not edited from here.
        await expect(page.locator('#layerSidebar .layerRemove')).toHaveCount(0);

        //A save read every record in again, and built the layers from them alone: diff (65/20) came back as 65/20.
        const messages = await saveEditorText(page, await editorText(page));

        expect(messages.join(' ')).toContain('Saved');

        await expect(page.locator('#layerSidebar .layerName').first()).toHaveText('diff (65/20)');
    });

    ///
    ///**DRC Check saves what was typed first** - the rules are checked on the file, which typing has not reached until
    ///it is saved - and says what it found over the text. Checked as it opens, every poly shape is a fault; with every
    ///one typed onto another layer, the check of the file as saved finds none.
    ///
    test('DRC Check saves what was typed before it checks, and says what it found', async ({ page }) => {
        await loadDeck(page);
        await page.locator('#drcRun').click();

        await expect(page.locator('#drcNotice')).toContainText('poly.1a');
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintFault/);

        const text = await editorText(page);

        await page.evaluate(contents => window.SetMonacoContent(contents), text.replaceAll('LAYER: 66 ', 'LAYER: 65 '));
        await page.locator('#drcRun').click();

        await expect(page.locator('.editorMessageText')).toContainText('Saved');
        await expect(page.locator('#drcNotice')).toHaveClass(/drawHintClear/);
        await expect(page.locator('.rulesRowBroken')).toHaveCount(0);
    });

    ///
    ///**A fault goes to its shape in the text**: the lines from its first record to its ENDEL, lit - the 2D view frames
    ///the shape instead. Counted from where its cell begins, so lines typed above the cell first do not throw it off.
    ///
    test('a fault goes to its shape\'s lines in the text, after typing above it too', async ({ page }) => {
        await loadDeck(page);
        await page.locator('#drcRun').click();

        await expect(page.locator('#drcNotice')).toContainText('poly.1a');

        await page.locator('.rulesRowBroken').first().click();

        await expect.poll(async () => litLines(page)).not.toBeNull();

        let lines = (await editorText(page)).split('\n');
        let lit = await litLines(page);

        expect(lines[lit.from - 1]).toMatch(/^BOUNDARY:/);
        expect(lines[lit.to - 1]).toMatch(/^ENDEL:/);
        expect(lines.slice(lit.from - 1, lit.to).join('\n')).toContain('LAYER: 66 ');

        //A press steps on to the rule's next fault, so a round of them - the count says how many, as 1/n - comes back.
        await expect(page.locator('.rulesRowBroken .rulesCount')).toHaveText(/^1\/\d+$/);

        const faults = Number((await page.locator('.rulesRowBroken .rulesCount').textContent()).split('/')[1]);

        //Three lines typed at the very top - above every cell - and the same shape is found three lines further down.
        await page.evaluate(() => {
            const model = window.monaco.editor.getModels()[0];

            model.applyEdits([{ range: new window.monaco.Range(1, 1, 1, 1), text: '\n\n\n' }]);
        });

        const before = lit.from;

        for (let press = 0; press < faults; press++)
            await page.locator('.rulesRowBroken').first().click();

        await expect(page.locator('.rulesRowBroken .rulesCount')).toHaveText(`1/${faults}`);
        await expect.poll(async () => (await litLines(page))?.from).toBe(before + 3);

        lines = (await editorText(page)).split('\n');
        lit = await litLines(page);

        expect(lines[lit.from - 1]).toMatch(/^BOUNDARY:/);
    });
});
