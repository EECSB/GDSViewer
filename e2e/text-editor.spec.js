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
    MOSFET,
    MOSFET_POLYGONS,
    SKY130_CELL
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

    //Read from a view that draws the sidebar: the text view does not render one.
    await selectView(page, 'View2DSvg');

    //It was rebuilt from the edited file, which is what makes the change visible at all.
    await expect.poll(async () => layerNumbers(page)).toContain(200);
    await expect.poll(async () => layerNumbers(page)).not.toContain(65);
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
