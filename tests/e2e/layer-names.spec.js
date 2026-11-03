//Loading a layer/datatype to name mapping, which is the one thing about a layer that cannot come out of
//the GDS file: the format carries only numbers, so what 65/20 means has to be supplied.
//
//Driven through the real file picker rather than by calling the parser, because the parser is already
//covered by LayerNamesTests - what needs a browser is the upload, the relabeling of the sidebar, and the
//redraw that follows. The picker is the PDK manager's layers Import since 2026-10-19, when the layer panel's
//own Import, Export, Clear and Example went there: a mapping goes into the PDK in use, which names the file.
const fs = require('fs');
const path = require('path');
const { test, expect } = require('@playwright/test');
const {
    gotoApp,
    gotoExample,
    expectLoaded,
    importLayermap,
    layerPairs,
    svgCounts,
    fillsDrawn,
    openFile,
    uploadFile,
    openedOnItsOwn,
    onOtherLayers,
    MOSFET,
    MOSFET_ELSEWHERE,
    MOSFET_POLYGONS
} = require('./helpers');

//A file of the visitor's own: the bundled Mosfet, under another name, which is a file the shipped mapping is not for.
const MOSFET_ON_DISK = path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', 'Mosfet.gds');

//Real sky130 names for the pairs Mosfet.gds uses, in the format the loader reads. The same shape a PDK
//layermap converts to with one substitution.
const MAPPING = [
    '#layer,datatype,name',
    '65,20,diff.drawing',
    '66,20,poly.drawing',
    '66,44,poly.via',
    '67,20,li1.drawing',
    '67,44,li1.via',
    '68,5,met1.label',
    '68,20,met1.drawing'
].join('\n');

///
///Imports a mapping into the PDK in use, sky130 on Mosfet, and gives back what the PDK manager said about how it
///read - see importLayermap. What it says about a pair replaces what sky130 said, and Mosfet is named again by it.
///
///**What reaches the panel is each test's to poll for**: the layers are laid over the file again after the manager
///has said its line. Generous, because the whole suite shares one dev server.
///
async function loadMapping(page, contents, name = 'sky130.csv', mimeType = 'text/csv') {
    return importLayermap(page, { name, mimeType, buffer: Buffer.from(contents, 'utf8') });
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);
});

test('a mapping names the layers and keeps the numbers visible', async ({ page }) => {
    const said = await loadMapping(page, MAPPING);

    //
    //And it says how it read beside the Import, rather than in a dialog.
    //
    //The names are in the panel, which is the whole result - a dialog reporting it stood between somebody and
    //the layers it was describing, and had to be dismissed before they could be looked at.
    //
    expect(said).toBe('Imported: 7 layers.');

    await expect.poll(async () => labelFor(page, '65/20'), { timeout: 60000 }).toContain('diff.drawing (65/20)');

    const pairs = await layerPairs(page);

    //Still one row per pair, in the same order - naming relabels, it does not regroup.
    expect(pairs).toEqual(['65/20', '66/20', '66/44', '67/20', '67/44', '68/5', '68/20', '93/44', '95/20']);

    //The name and the numbers both, the way KLayout's own layer panel shows them.
    const labels = await page.locator('.layerRow .layerName').allTextContents();

    expect(labels.some(text => text.includes('diff.drawing (65/20)'))).toBe(true);

    //The two purposes of layer 66 are named separately, which is what keying on the pair is for.
    expect(labels.some(text => text.includes('poly.drawing (66/20)'))).toBe(true);
    expect(labels.some(text => text.includes('poly.via (66/44)'))).toBe(true);

    //
    //A pair the mapping does not cover keeps whatever name it had rather than going blank.
    //
    //It used to assert bare numbers on 93/44, which the shipped sky130 mapping now covers - it is laid over
    //a bundled example before anything is imported, so "uncovered by MAPPING" stopped meaning "unnamed". The
    //claim being made is that naming *relabels* and does not wipe the rest of the list, so what to check is
    //that the row is still there with its pair on it.
    //
    expect(labels.some(text => text.trim().endsWith('93/44)') || text.trim().startsWith('93/44'))).toBe(true);
});

test('naming layers does not change what is drawn', async ({ page }) => {
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await loadMapping(page, MAPPING);

    await expect.poll(async () => labelFor(page, '65/20'), { timeout: 60000 }).toContain('diff.drawing');

    expect((await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

test('a fourth column recolors the layer', async ({ page }) => {
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    const fillsBefore = await fillsDrawn(page);

    expect(fillsBefore).not.toContain('#00ff00');

    await loadMapping(page, '65,20,diff.drawing,#00ff00');

    await expect.poll(async () => fillsDrawn(page), { timeout: 60000 }).toContain('#00ff00');
});

///
///**A KLayout .lyp is a layermap too**, for the names and the colors it carries - read through the same parser as a
///CSV, which LayerNamesTests covers against KLayout itself. What is here is the browser's half: the PDK manager's
///Import taking the file, with the type a browser gives a .lyp, which is none.
///
test('a KLayout .lyp names the layers and colors them', async ({ page }) => {
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    const lyp = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<layer-properties>',
        ' <properties>',
        '  <fill-color>#00ff00</fill-color>',
        '  <name>lypdiff_m 65/20</name>',
        '  <source>65/20@1</source>',
        ' </properties>',
        '</layer-properties>'
    ].join('\n');

    const said = await loadMapping(page, lyp, 'sky130.lyp', '');

    expect(said).toBe('Imported: 1 layer.');

    //sky130's .lyp names end in their pair, which the label shows already.
    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).some(text => text.includes('lypdiff_m (65/20)'))).toBe(true);
    await expect.poll(async () => fillsDrawn(page)).toContain('#00ff00');
});

test('a mapping with a bad row keeps the good ones and names the line', async ({ page }) => {
    const said = await loadMapping(page, '65,20,diff.drawing\nnonsense\n66,20,poly.drawing');

    expect(said).toContain('Imported: 2 layers.');
    expect(said).toContain('Line 2');

    await expect.poll(async () => labelFor(page, '66/20'), { timeout: 60000 }).toContain('poly.drawing');
});

///
///Opens a layer's settings and returns the name box in them.
///
///**Through the gear, because the row is a readout.** This used to click the row's own label, which turned
///it into a text box - and that made the row's click mean two things, since the draw tool wanted the same
///press to choose which layer a shape went on. Naming moved into the settings, where the box has the width
///of a popup rather than of a sidebar column, and the row's press now isolates the layer instead.
///
///The box keeps the behavior this file is about: Enter applies, Escape puts back what was there, and blank
///clears the name. It starts out holding the pair rather than empty - see commitSettingsName - so typing
///the pair back counts as no name too.
///
///Found by the pair as a substring rather than as the whole label, because a named row reads
///"diffusion (65/20)" - so anchoring on the pair alone would only ever find a row that has not been
///renamed yet, which is exactly the case these tests need to get past.
///
async function startRenaming(page, pair) {
    const row = page.locator('.layerRow')
        .filter({ has: page.locator('.layerName', { hasText: pair }) })
        .first();

    await row.locator('.layerSettingsButton').click();

    const box = page.locator('.layerSettingsName');

    await expect(box).toBeVisible();

    return box;
}

///The name a row shows, whether or not it has one.
async function labelFor(page, pair) {
    const labels = await page.locator('.layerRow .layerName').allTextContents();

    return labels.find(text => text.includes(pair))?.trim();
}

test('a layer can be named from its settings', async ({ page }) => {
    const box = await startRenaming(page, '65/20');

    await box.fill('diffusion');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion (65/20)');
});

test('escape abandons a rename', async ({ page }) => {
    const box = await startRenaming(page, '65/20');

    await box.fill('never applied');
    await box.press('Escape');

    await expect.poll(async () => labelFor(page, '65/20')).not.toContain('never applied');
});

///Blank is the only way to undo one rename without dropping the whole mapping.
test('clearing the box puts that one row back to bare numbers', async ({ page }) => {
    let box = await startRenaming(page, '65/20');
    await box.fill('diffusion');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion');

    box = await startRenaming(page, '65/20');
    await box.fill('');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toBe('65/20');
});

///
///Names are kept per technology rather than per file, which is the useful behavior: the numbers mean the
///same thing across a whole PDK, so naming them while looking at one cell names them everywhere.
///
test('a typed name survives a reload, and carries to another file', async ({ page }) => {
    const box = await startRenaming(page, '65/20');

    await box.fill('diffusion');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion');

    await gotoExample(page, MOSFET);

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion (65/20)');

    //A different file of the same technology, which also draws 65/20.
    await gotoExample(page, 'sky130_fd_sc_hd__nand2_1');

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion (65/20)');
});

///A visit of the visitor's own, with nothing in the address - and then a file of theirs, opened on its own.
async function comeBackAndUpload(page, bytes = fs.readFileSync(MOSFET_ON_DISK)) {
    await page.waitForTimeout(1000);

    await gotoApp(page, '', null);
    await expectLoaded(page);

    await uploadFile(page, { name: 'mine.gds', mimeType: 'application/octet-stream', buffer: bytes });
    await openedOnItsOwn(page);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('mine.gds');
}

///
///**The PDK's names stay off a file from another process, after a reload too.** They name a file drawn in the PDK -
///most of its layers the PDK's own - and a file off somebody's machine could be any technology, where sky130 labels
///would read as right when they are wrong. The shipped names used to reach any upload after a reload: the session
///kept the example's names, and restoring them as the names that carry made them the visitor's own. Mosfet here on
///gf180mcu's numbers, none of them sky130's.
///
test('the PDK\'s names stay off a file from another process the visitor uploads, after a reload as well', async ({ page }) => {
    await expect.poll(async () => labelFor(page, '65/20')).toBe('diff (65/20)');

    await comeBackAndUpload(page, onOtherLayers(fs.readFileSync(MOSFET_ON_DISK), MOSFET_ELSEWHERE));

    await expect.poll(async () => labelFor(page, '22/20')).toBe('22/20');
    await expect.poll(async () => labelFor(page, '34/20')).toBe('34/20');
});

///And a sky130 file of the visitor's own is drawn in the PDK, so it is named by it - the reason the rule is about layers.
test('a sky130 file the visitor uploads is named by the PDK in use, after a reload as well', async ({ page }) => {
    await comeBackAndUpload(page);

    await expect.poll(async () => labelFor(page, '65/20')).toBe('diff (65/20)');
});

///And a name the visitor typed is theirs, so it still reaches their upload after the same reload.
test('a typed name still reaches a file the visitor uploads after a reload', async ({ page }) => {
    const box = await startRenaming(page, '65/20');

    await box.fill('mydiff');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toBe('mydiff (65/20)');

    await comeBackAndUpload(page);

    await expect.poll(async () => labelFor(page, '65/20')).toBe('mydiff (65/20)');
});

///
///**A name with a comma in it survives a reload.** The session keeps names as layermap rows, and the comma used to
///split the row: `poly, gate` came back as `poly`, with its color gone and the rest of the row one column along. It
///is written in quotes now, the way a spreadsheet writes one - see LayerNamesTests.
///
test('a name with a comma in it survives a reload', async ({ page }) => {
    const box = await startRenaming(page, '66/20');

    await box.fill('poly, gate');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '66/20')).toBe('poly, gate (66/20)');

    await page.waitForTimeout(1000);

    await gotoApp(page, '', null);
    await expectLoaded(page);

    await expect.poll(async () => labelFor(page, '66/20')).toBe('poly, gate (66/20)');
});

///
///An example arrives named, without anybody importing anything.
///
///**Because every file in the picker is a sky130 cell.** The app compiles no PDK table in, which is right for
///a file off somebody's machine - it could be any technology - and was never right for the files the app
///itself chose to ship. Left as bare numbers, the one feature that needs a mapping was a grayed-out button on
///every file on offer.
///
test('a bundled example is named from the shipped mapping', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(
        async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
        { timeout: 20000 }).toContain('met1');

    //The roles landed too, which is the half that matters: a name is a label, a role is what the walk
    //follows. Measured through the button, since that is the whole reason for doing this.
    await page.locator('#selectTool').click();

    const at = await page.evaluate(() => {
        const view = document.getElementById('gdsSVG');
        const point = view.createSVGPoint();

        point.x = 1380;
        point.y = 960;

        const on = point.matrixTransform(view.getScreenCTM());

        return { x: on.x, y: on.y };
    });

    await page.mouse.click(at.x, at.y);

    await expect(page.locator('#traceNet')).toBeEnabled({ timeout: 20000 });
});
