//The history list: what gets into it, what order it is in, that a file comes back out of it as it was
//left, and that a row can be thrown away.
//
//Only a browser can show this. The list and every file in it are in IndexedDB, the writes run through .NET
//interop, and what is actually being tested is a round trip through storage rather than a function's return
//value - a history that saves and never restores would pass any test that did not reload.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, layerCheckbox, openLayerSettings, layerNameBox, svgCounts, selectExample, editorText, saveEditorText, expectEditorLoaded, selectView, MOSFET, SKY130_CELL, previewShapeCount, openedOnItsOwn } = require('./helpers');

//The picker lists files by their whole name; ?file= and the history both name them the same way. MOSFET
//and SKY130_CELL are slugs, so the extension goes back on for anything that goes through the list.
const MOSFET_FILE = `${MOSFET}.gds`;
const SKY130_FILE = `${SKY130_CELL}.gds`;

///Opens the History popup and waits for it to be up.
async function openHistory(page) {
    await page.locator('#historyButton').click();

    await expect(page.locator('#historyPicker')).toBeVisible({ timeout: 60000 });
}

///
///Puts it away by taking the pointer off it, since there is nothing to press.
///
///The list hangs off its button and closes when the pointer leaves the two of them - a corner of the
///window is somewhere neither of them is, and is not under anything a click would rather not land on.
///
async function closeHistory(page) {
    await page.mouse.move(4, 4);

    await expect(page.locator('#historyPicker')).toHaveCount(0);
}

///The files listed, in the order they are listed in.
async function historyNames(page) {
    return page.locator('#historyPicker .historyRow').evaluateAll(rows =>
        rows.map(row => row.getAttribute('data-file')));
}

///
///Polls the list rather than reading it once.
///
///A write goes through .NET to IndexedDB and back, and the popup is drawn from what the page last read -
///so a single read straight after the change that should have caused it races the save.
///
async function expectHistory(page, names) {
    await expect.poll(async () => historyNames(page), { timeout: 60000 }).toEqual(names);
}

///
///Presses Clear History and answers the confirmation it asks first.
///
///Registered before the click, and once: with no handler at all Playwright dismisses a dialog by itself,
///which would make every one of these read as the user having said no.
///
async function clearHistory(page, { answer = 'accept' } = {}) {
    const asked = new Promise(resolve => {
        page.once('dialog', dialog => {
            resolve(dialog.message());

            if (answer === 'accept')
                dialog.accept();
            else
                dialog.dismiss();
        });
    });

    await page.locator('#clearHistoryButton').click();

    return asked;
}

///Puts a file on the page that did not come from the bundled list, the way a person would.
async function upload(page, name, bytes) {
    await page.locator('#fileUpload').setInputFiles({ name, mimeType: 'application/octet-stream', buffer: bytes });

    await openedOnItsOwn(page, 60000);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(name);
}

///
///The bytes of a bundled example, to upload back under another name.
///
///Off the disk rather than fetched through the page. Fetching was the first version of this, and a wrong
///path answered 404 with an empty body - which uploads as a zero-byte file, is refused by the parser, and
///leaves a test that looks like the history failing to record an upload that never happened.
///
function exampleBytes(fileName) {
    return require('fs').readFileSync(require('path').join(__dirname, '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', fileName));
}

///
///Grows a GDSII file to at least the size given, by repeating the elements inside its structure.
///
///Valid GDSII rather than padding. The parser refuses anything that is not well formed and would be right
///to, so a file padded with filler would test the refusal rather than the size. A structure is allowed as
///many elements as it likes, so the block between STRNAME and ENDSTR can simply be repeated - no record
///has to be rewritten and no name has to be made unique.
///
function grownGds(bytes, targetSize) {
    const STRNAME = 0x0606;
    const ENDSTR = 0x0700;

    let elementStart = -1;
    let elementEnd = -1;

    for (let at = 0; at + 4 <= bytes.length;) {
        const length = (bytes[at] << 8) | bytes[at + 1];
        const type = (bytes[at + 2] << 8) | bytes[at + 3];

        if (length < 4)
            throw new Error(`Not a GDSII record at offset ${at}`);

        if (type === STRNAME)
            elementStart = at + length;

        if (type === ENDSTR && elementStart >= 0) {
            elementEnd = at;

            break;
        }

        at += length;
    }

    if (elementStart < 0 || elementEnd < 0)
        throw new Error('No structure to grow in this file');

    const block = bytes.subarray(elementStart, elementEnd);
    const copies = Math.max(1, Math.ceil((targetSize - bytes.length) / block.length));

    return Buffer.concat([
        bytes.subarray(0, elementEnd),
        ...Array.from({ length: copies }, () => block),
        bytes.subarray(elementEnd)
    ]);
}

///
///A file larger than the browser's default upload limit still opens.
///
///IBrowserFile.OpenReadStream refuses anything over 512 KB unless told otherwise, and it was not being
///told - so a real layout of a megabyte was refused with a message about a limit nobody had chosen. Every
///bundled example is under 60 KB, which is why nothing in the corpus or the rest of this suite met it.
///
///The file is built rather than found: 897 sample files and not one of them is big enough to show this.
///
test('a file larger than the browser default upload limit still opens', async ({ page }) => {
    await gotoExample(page, MOSFET);

    const big = grownGds(exampleBytes('Mosfet.gds'), 550 * 1024);
    expect(big.length).toBeGreaterThan(512000);

    await upload(page, 'big.gds', big);

    //Proof it was actually read, not merely named.
    //
    //The shell sets the file's name before the parse, so waiting on that alone would pass against a file
    //that was refused. A history row is only written after a successful parse, and the drawing only has
    //polygons in it if the records came out - so between them they say the parse worked.
    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBeGreaterThan(18);

    await openHistory(page);
    await expectHistory(page, ['big.gds']);
});

///
///An example that is only looked at is deliberately not kept.
///
///The rule the whole list rests on. Without it, opening the app once and clicking through a few cells fills
///the history with files nobody did anything to, and the ones that matter are lost among them.
///
test('an example that is only opened is not kept', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);

    await selectExample(page, SKY130_FILE);

    await openHistory(page);

    await expectHistory(page, []);
});

///
///An uploaded file is kept whether or not it is touched.
///
///It exists in this tab and nowhere else - there is no list to find it in again and no address that names
///it - so this is the one kind of file where not keeping it means losing it.
///
test('a file opened from this computer is kept even though nothing was changed', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);

    await expectHistory(page, ['uploaded.gds']);
});

test('changing an example puts it in the history', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);

    await layerCheckbox(page).uncheck();

    await openHistory(page);

    await expectHistory(page, ['Mosfet.gds']);
});

///
///Newest first, and a file seen again moves up rather than being listed twice.
///
test('the most recently changed file is at the top', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await selectExample(page, SKY130_FILE);
    await layerCheckbox(page).uncheck();

    await openHistory(page);
    await expectHistory(page, [SKY130_FILE, 'Mosfet.gds']);
    await closeHistory(page);

    //Back to the first one and change it again, which should move it up rather than add a second row.
    await selectExample(page, MOSFET_FILE);
    await openLayerSettings(page);
    await layerNameBox(page).fill('renamed');
    await layerNameBox(page).blur();

    await openHistory(page);
    await expectHistory(page, ['Mosfet.gds', SKY130_FILE]);
});

///
///The point of the whole feature: a file comes back as it was left.
///
///Through a reload, so this is a round trip through storage rather than through the page's own memory -
///which would pass whether or not anything was ever written.
///
test('a file comes back out of the history in the state it was left in', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);

    await layerCheckbox(page).uncheck();

    const hidden = (await svgCounts(page)).polygons;
    expect(hidden).toBeLessThan(18);

    //Somewhere else entirely, so what comes back has to have come from the history.
    await selectExample(page, SKY130_FILE);
    await expect.poll(async () => openFile(page)).toBe(SKY130_FILE);

    await gotoApp(page);
    await expectLoaded(page);

    await openHistory(page);
    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowName').click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(hidden);
});

///
///Including an edit to the records, which is the state that cannot be got back any other way.
///
test('an edited file comes back edited', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');
    await expectEditorLoaded(page);

    const text = await editorText(page);
    const messages = await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    expect(messages.join(' ')).toContain('Saved');

    //Away to a different file, then back through the history.
    await selectExample(page, SKY130_FILE);
    await expect.poll(async () => openFile(page)).toBe(SKY130_FILE);

    await openHistory(page);
    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowName').click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');

    //The popup stays up after a row is chosen - see the Examples one, which does the same on purpose - and
    //it sits over the bar. Choosing a view is a real press on a real button now, so it has to be reachable.
    await closeHistory(page);

    await selectView(page, 'ViewText');
    await expectEditorLoaded(page);

    await expect.poll(async () => editorText(page), { timeout: 60000 }).toContain('LAYER: 200');
});

///
///Opening the bundled version of a file must not write over the edited copy of it in the history.
///
///A row is identified by its name, which is what makes opening the same file again move it up rather than
///list it twice - and it also means the bundled Mosfet and somebody's edited Mosfet are the same row. The
///rule that keeps that safe is that only a file which was uploaded, changed, or opened out of the history
///writes to one; a fresh copy off the server does not, however it is named.
///
test('opening the bundled example does not overwrite an edited copy of it', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');
    await expectEditorLoaded(page);

    const text = await editorText(page);
    await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    //The pristine cell, straight out of the example list, under the same name.
    await selectExample(page, MOSFET_FILE);
    await expect.poll(async () => openFile(page)).toBe(MOSFET_FILE);

    //Something that saves, so that if the rule were wrong the row would be written over here.
    await selectView(page, 'View3D');

    await openHistory(page);
    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowName').click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');

    //The popup stays up after a row is chosen - see the Examples one, which does the same on purpose - and
    //it sits over the bar. Choosing a view is a real press on a real button now, so it has to be reachable.
    await closeHistory(page);

    await selectView(page, 'ViewText');
    await expectEditorLoaded(page);

    //Still the edit, rather than the pristine cell that was opened over the top of it.
    await expect.poll(async () => editorText(page), { timeout: 60000 }).toContain('LAYER: 200');
});

///An edited file is marked as such, since it is named after the one it came from.
test('a file whose records were changed says so', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');
    await expectEditorLoaded(page);

    const text = await editorText(page);
    await saveEditorText(page, text.replace('LAYER: 65 ', 'LAYER: 200 '));

    await openHistory(page);

    await expect(page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowEdited')).toBeVisible();
});

///
///Pointing at a row draws it, without opening it.
///
///The same preview the example list has. Worth its own check because it reads the file back out of storage
///rather than fetching it, which is a different path to the same picture.
///
test('pointing at a row draws it without opening it', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await selectExample(page, SKY130_FILE);
    await expect.poll(async () => openFile(page)).toBe(SKY130_FILE);

    await openHistory(page);
    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"]').hover();

    //Drawn...
    await expect.poll(async () => previewShapeCount(page, '.examplePreview'), { timeout: 60000 })
        .toBeGreaterThan(0);

    //...and the file on screen is still the other one.
    expect(await openFile(page)).toBe(SKY130_FILE);
});

test('a row can be thrown away on its own', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds', 'Mosfet.gds']);

    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowForget').click();

    await expectHistory(page, ['uploaded.gds']);
});

///Deleting a row must not also open it - the delete button sits inside the row that opens the file.
test('throwing a row away does not open it', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await page.locator('#historyPicker .historyRow[data-file="Mosfet.gds"] .historyRowForget').click();

    await expectHistory(page, ['uploaded.gds']);

    //Still on what was open before, rather than on the file that was just deleted.
    expect(await openFile(page)).toBe('uploaded.gds');
});

test('the whole history can be cleared', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds', 'Mosfet.gds']);

    //Says how many are going, so the number on screen is the number being agreed to.
    const question = await clearHistory(page);
    expect(question).toContain('2 files');

    await expectHistory(page, []);

    //And it stays cleared, rather than the list only having been emptied on screen.
    await gotoApp(page);
    await expectLoaded(page);

    await openHistory(page);
    await expectHistory(page, []);
});

///
///Saying no leaves it alone.
///
///The reason the confirmation is there at all: an uploaded file's only copy in the browser is the one in
///this list, so a misplaced click on Clear is a file gone. A prompt that asked and then cleared anyway
///would be worse than no prompt, because it reads as a safety net.
///
test('declining the confirmation leaves the history alone', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds']);

    await clearHistory(page, { answer: 'dismiss' });

    //Still listed, and still stored - not merely still drawn.
    await expectHistory(page, ['uploaded.gds']);

    expect(await page.evaluate(async () =>
        window.gdsStorage.get('gdsviewer.history.uploaded.gds'))).not.toBeNull();
});

///
///The filter, the same as the example list's.
///
///The names in here are long and near-identical for a run of cells out of one library, which is the case
///it is for - twenty rows is short enough to read but not short enough to scan for one character.
///
test('the filter narrows the list', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds', 'Mosfet.gds']);

    await page.locator('.historyPickerFilter').fill('mos');
    await expectHistory(page, ['Mosfet.gds']);

    //Matches anywhere in the name, and ignores case - "load" is in the middle of "uploaded".
    await page.locator('.historyPickerFilter').fill('LOAD');
    await expectHistory(page, ['uploaded.gds']);

    //A filter matching nothing says so rather than looking like an empty history, which means something
    //quite different: one is "nothing here", the other is "nothing here *yet*".
    await page.locator('.historyPickerFilter').fill('nothing like this');
    await expectHistory(page, []);
    await expect(page.locator('#historyPicker .examplePickerNone')).toContainText('Nothing matches');

    await page.locator('.historyPickerFilter').fill('');
    await expectHistory(page, ['uploaded.gds', 'Mosfet.gds']);
});

///Filtering hides rows; it must not delete them.
test('a filtered-out file is still in the history', async ({ page }) => {
    await gotoExample(page, MOSFET);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(18);
    await layerCheckbox(page).uncheck();

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await page.locator('.historyPickerFilter').fill('mos');
    await expectHistory(page, ['Mosfet.gds']);

    await gotoApp(page);
    await expectLoaded(page);

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds', 'Mosfet.gds']);
});

///
///The files, not only the list.
///
///Clearing has to remove what each row points at as well, or the layouts stay in the browser's storage
///forever with nothing left pointing at them - which is not what "clear" means to somebody deleting files.
///
test('clearing removes the stored files, not just the rows', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds']);

    const before = await page.evaluate(async () => window.gdsStorage.get('gdsviewer.history.uploaded.gds'));
    expect(before).not.toBeNull();

    await clearHistory(page);
    await expectHistory(page, []);

    await expect.poll(async () =>
        page.evaluate(async () => window.gdsStorage.get('gdsviewer.history.uploaded.gds'))).toBeNull();
});

///The list survives the browser being closed, which is the whole reason it is in IndexedDB.
test('the list is still there after the browser is closed', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await upload(page, 'uploaded.gds', exampleBytes('Mosfet.gds'));

    await openHistory(page);
    await expectHistory(page, ['uploaded.gds']);

    //A second page in the same context: same origin, same storage, no shared JavaScript - which is what
    //closing a tab and opening a new one actually is.
    const revived = await page.context().newPage();

    await page.close();

    await gotoApp(revived);
    await expectLoaded(revived);

    await openHistory(revived);
    await expectHistory(revived, ['uploaded.gds']);
});
