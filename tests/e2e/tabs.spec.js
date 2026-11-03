//Several files open at once, a tab each, over the one view they take turns in.
//
//What takes a browser: a file going into a tab and coming back out of one as it was left - its layers, its edits
//and its undo - through the same saved state a reload uses; the strip appearing only once there are two; a tab
//closed; and the tabs themselves outlasting a reload. The one file that gives its place up rather than keeping a
//tab - the example the app opens for itself - is authoring.spec's.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectExample, openFile, expectLoaded, hideLayer, svgCounts, shapeCount, shapeBox, elementPoints, snapToGrid, uploadFile, MOSFET, MOSFET_POLYGONS, SKY130_CELL } = require('./helpers');

const MOSFET_FILE = `${MOSFET}.gds`;
const SKY130_FILE = `${SKY130_CELL}.gds`;

///The tabs as their names, the one on screen marked with a star - or an empty list when there is no strip.
async function tabsShown(page) {
    return page.evaluate(() => [...document.querySelectorAll('#fileTabs .fileTab')]
        .map(tab => {
            let mark = '';

            if (tab.classList.contains('fileTabOn'))
                mark = '*';

            return mark + tab.querySelector('.fileTabName').textContent;
        }));
}

async function pressTab(page, name) {
    await page.locator('#fileTabs .fileTab').filter({ has: page.locator('.fileTabName', { hasText: name }) }).click();
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

///Mosfet opened by a link, which is a file somebody asked for - so it keeps its tab when another is opened.
test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => openFile(page)).toBe(MOSFET_FILE);
});

///One file is no tabs: a strip of one would push the drawing down and say nothing its name does not.
test('one file open shows no tabs', async ({ page }) => {
    await expect(page.locator('#fileTabs')).toHaveCount(0);
});

///A second file opens in a tab of its own, asking nothing, and the first is a press away.
test('a second file gets a tab of its own, and the first is a press away', async ({ page }) => {
    const asked = questionsAsked(page);

    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    await pressTab(page, MOSFET_FILE);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect.poll(async () => tabsShown(page)).toEqual([`*${MOSFET_FILE}`, SKY130_FILE]);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    expect(asked).toEqual([]);
});

///The layers as they were left come back with the tab - it is the same saved state a reload puts back.
test('a file comes back to screen as it was left', async ({ page }) => {
    await hideLayer(page);

    const hidden = (await svgCounts(page)).polygons;

    expect(hidden).toBeLessThan(MOSFET_POLYGONS);

    await selectExample(page, SKY130_FILE);
    await pressTab(page, MOSFET_FILE);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(hidden);
});

///
///And an edit with its undo: a shape deleted, the file put away and brought back, the shape still gone - and
///Ctrl+Z still bringing it back, since the undo goes into the tab with the file.
///
test('an edit and its undo come back with the tab', async ({ page }) => {
    await snapToGrid(page, false);

    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    const before = await shapeCount(page);

    await chooseAShape(page);
    await page.keyboard.press('Delete');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 1);

    await selectExample(page, SKY130_FILE);
    await pressTab(page, MOSFET_FILE);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);

    //Still gone: the file came back as it was left, not as it was opened.
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before - 1);

    await page.keyboard.press('Control+z');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(before);
});

///Picks one shape of the cell entered on its own, the way the shortcut spec does.
async function chooseAShape(page) {
    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        if (await elementPoints(page, index) !== null)
            return;
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

///Closing the tab on screen puts its neighbor there; one left is no strip.
test('closing a tab puts the one beside it on screen', async ({ page }) => {
    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    await page.locator('#fileTabs .fileTabOn .fileTabClose').click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect(page.locator('#fileTabs')).toHaveCount(0);
});

///An example already open, untouched, is gone back to rather than opened a second time beside itself.
test('an example already in a tab is that tab', async ({ page }) => {
    await selectExample(page, SKY130_FILE);
    await selectExample(page, MOSFET_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([`*${MOSFET_FILE}`, SKY130_FILE]);
});

///The tabs outlast a reload, the one on screen still on screen and the other still a press away.
test('the tabs come back after a reload', async ({ page }) => {
    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    //Written before the reload, or the reload can beat the write.
    await expect.poll(() => page.evaluate(async () => (await window.gdsStorage.get('gdsviewer.tabs')) !== null), { timeout: 30000 }).toBe(true);

    await page.reload();
    await expectLoaded(page);

    await expect.poll(async () => tabsShown(page), { timeout: 60000 }).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    await pressTab(page, MOSFET_FILE);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
});

///
///A file brought from this computer gets a tab too. Brought onto a file somebody chose, it is offered as a cell to
///bring in first - see import.spec - and opening it on its own is the other answer, which is a tab of its own now.
///
test('a file opened from this computer gets a tab of its own', async ({ page }) => {
    await uploadFile(page, 'e2e/fixtures/placed.gds');

    await page.locator('#importAsFile').click();

    await expect.poll(async () => tabsShown(page), { timeout: 60000 }).toEqual([MOSFET_FILE, '*placed.gds']);
});

///The names the History popup lists, read with it open and put away again.
async function historyNames(page) {
    await page.locator('#historyButton').click();

    await expect(page.locator('#historyPicker')).toBeVisible({ timeout: 60000 });

    const names = await page.locator('#historyPicker .historyRow').evaluateAll(rows => rows.map(row => row.getAttribute('data-file')));

    await page.mouse.move(4, 4);

    await expect(page.locator('#historyPicker')).toHaveCount(0);

    return names;
}

async function startRenaming(page, name) {
    await page.locator('#fileTabs .fileTabName', { hasText: name }).dblclick();

    await expect(page.locator('#fileTabRename')).toBeFocused();
}

///
///**A double-click on a tab's name renames its file** (asked for 2026-10-31): the name in a box, selected so what is
///typed replaces it, and the extension after the box rather than in it. Enter keeps the name; the tab, the download
///and the file the shell says is open all go by it. An example renamed is the person's own from then on, so it
///carries its bytes and comes back under its new name after a reload, and the history list keeps it under that name.
///
test('a double-click on a tab renames its file, the extension kept, and the name outlasts a reload', async ({ page }) => {
    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    await startRenaming(page, SKY130_FILE);

    const box = page.locator('#fileTabRename');

    await expect(box).toHaveValue(SKY130_CELL);
    await expect(page.locator('#fileTabs .fileTabExtension')).toHaveText('.gds');
    expect(await box.evaluate(element => [element.selectionStart, element.selectionEnd])).toEqual([0, SKY130_CELL.length]);

    const tabsBefore = await page.evaluate(() => window.gdsStorage.get('gdsviewer.tabs'));

    await page.keyboard.type('my inverter');
    await page.keyboard.press('Enter');

    await expect(box).toHaveCount(0);
    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, '*my inverter.gds']);
    await expect.poll(async () => openFile(page)).toBe('my inverter.gds');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    expect((await started).suggestedFilename()).toBe('my inverter.gds');

    await expect.poll(async () => historyNames(page), { timeout: 30000 }).toContain('my inverter.gds');

    //Written before the reload, or the reload can beat the write - compressed, so it is told by having changed.
    await expect.poll(() => page.evaluate(() => window.gdsStorage.get('gdsviewer.tabs')), { timeout: 30000 }).not.toBe(tabsBefore);

    await page.reload();
    await expectLoaded(page);

    await expect.poll(async () => tabsShown(page), { timeout: 60000 }).toEqual([MOSFET_FILE, '*my inverter.gds']);
    await expect.poll(async () => openFile(page)).toBe('my inverter.gds');
});

///
///**A file with a history row keeps the one row, under its new name** - written under the new name and then taken from
///the old, rather than left behind as a second file. A name typed with its extension is the name without it.
///
test('a file renamed moves its history row to the new name, and an extension typed is not doubled', async ({ page }) => {
    await hideLayer(page);
    await selectExample(page, SKY130_FILE);
    await pressTab(page, MOSFET_FILE);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect.poll(async () => historyNames(page), { timeout: 30000 }).toContain(MOSFET_FILE);

    await startRenaming(page, MOSFET_FILE);
    await page.keyboard.type('gate.gds');
    await page.keyboard.press('Enter');

    await expect.poll(async () => tabsShown(page)).toEqual(['*gate.gds', SKY130_FILE]);
    await expect.poll(async () => historyNames(page), { timeout: 30000 }).toContain('gate.gds');

    expect(await historyNames(page)).not.toContain(MOSFET_FILE);
});

///
///**A name that cannot be keeps the box open, red, and says why**: none at all, a character no file name may hold, or
///another tab's name. Esc puts the old name back, and so does leaving the box on a name that cannot be.
///
test('a name that cannot be is refused in the box, and Esc or leaving it keeps the old one', async ({ page }) => {
    await selectExample(page, SKY130_FILE);
    await startRenaming(page, SKY130_FILE);

    const box = page.locator('#fileTabRename');

    for (const [typed, said] of [[' ', 'A file needs a name'], ['a/b', 'cannot hold'], [MOSFET, `Another tab holds a file called ${MOSFET_FILE}`]]) {
        await box.fill(typed);
        await page.keyboard.press('Enter');

        await expect(box).toHaveClass(/fileTabRenameBad/);
        await expect.poll(() => box.getAttribute('title')).toContain(said);
    }

    await page.keyboard.press('Escape');

    await expect(box).toHaveCount(0);
    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    await startRenaming(page, SKY130_FILE);
    await box.fill('a:b');
    await page.locator('#viewContainer').click({ position: { x: 5, y: 5 } });

    await expect(box).toHaveCount(0);
    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);
});

///A double-click on a tab that is not on screen puts it there - the first click does - and then opens its box.
test('a double-click on a tab put away brings it on screen and renames it', async ({ page }) => {
    await selectExample(page, SKY130_FILE);

    await page.locator('#fileTabs .fileTabName', { hasText: MOSFET_FILE }).dblclick();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect(page.locator('#fileTabRename')).toBeFocused({ timeout: 60000 });
    await expect(page.locator('#fileTabRename')).toHaveValue(MOSFET);
});
