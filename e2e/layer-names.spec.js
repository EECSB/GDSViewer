//Loading a layer/datatype to name mapping, which is the one thing about a layer that cannot come out of
//the GDS file: the format carries only numbers, so what 65/20 means has to be supplied.
//
//Driven through the real file picker rather than by calling the parser, because the parser is already
//covered by LayerNamesTests - what needs a browser is the upload, the relabeling of the sidebar, and the
//redraw that follows.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    layerPairs,
    svgCounts,
    fillsDrawn,
    selectExample,
    openFile,
    MOSFET,
    MOSFET_POLYGONS
} = require('./helpers');

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
///Picks a mapping file, waits for it to land, and gives back anything the app said about it.
///
///**A mapping that works says nothing**, so waiting for a dialog would be waiting for one that is never
///coming. What is waited on instead is either effect: the panel rewritten, or the app speaking up. One of
///the two always happens, and which one it was is the thing the individual tests are about.
///
///Generous, because the whole suite shares one dev server: at six workers the read, the apply and the
///redraw have been seen to take past 15s, which showed up as this spec alone failing in a full run and
///passing on its own.
///
async function loadMapping(page, contents, name = 'sky130.csv') {
    const said = [];

    await page.exposeFunction('reportAlert', message => said.push(String(message)));
    await page.evaluate(() => { window.alert = message => window.reportAlert(message); });

    const labels = () => page.locator('.layerRow .layerName').allTextContents();

    const before = (await labels()).join('|');

    await page.locator('#layerNamesImport').setInputFiles({
        name,
        mimeType: 'text/csv',
        buffer: Buffer.from(contents, 'utf8')
    });

    await expect.poll(async () => said.length > 0 || (await labels()).join('|') !== before, { timeout: 60000 }).toBe(true);

    return said.join(' ');
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);
});

test('a mapping names the layers and keeps the numbers visible', async ({ page }) => {
    const said = await loadMapping(page, MAPPING);

    //
    //And it said nothing, because there was nothing to say.
    //
    //The names are in the panel one line away, which is the whole result - a dialog reporting it stood
    //between somebody and the layers it was describing, and had to be dismissed before they could be
    //looked at. What the app still speaks up about is the two cases the panel cannot show, below.
    //
    expect(said).toBe('');

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

    expect((await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

test('a fourth column recolors the layer', async ({ page }) => {
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    const fillsBefore = await fillsDrawn(page);

    expect(fillsBefore).not.toContain('#00ff00');

    await loadMapping(page, '65,20,diff.drawing,#00ff00');

    const fillsAfter = await fillsDrawn(page);

    expect(fillsAfter).toContain('#00ff00');
});

test('clearing puts the bare numbers and the palette colors back', async ({ page }) => {
    await loadMapping(page, '65,20,diff.drawing,#00ff00');

    await page.getByTitle('Drop every layer name and put the palette colors back, leaving the bare numbers').click();

    await expect.poll(async () => {
        const labels = await page.locator('.layerRow .layerName').allTextContents();

        return labels.some(text => text.includes('diff.drawing'));
    }).toBe(false);

    const fills = await fillsDrawn(page);

    expect(fills).not.toContain('#00ff00');
});

///
///The case that would otherwise look like the feature being broken: every row read, none of them matching.
///
test('a mapping for another technology says so rather than failing quietly', async ({ page }) => {
    const said = await loadMapping(page, '1,0,metal1\n2,0,metal2');

    expect(said).toContain('none of them match a layer this file uses');

    expect(await layerPairs(page)).toContain('65/20');
});

test('a mapping with a bad row keeps the good ones and names the line', async ({ page }) => {
    const said = await loadMapping(page, '65,20,diff.drawing\nnonsense\n66,20,poly.drawing');

    expect(said).toContain('Updated 2');
    expect(said).toContain('Line 2');
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

test('clear drops the stored names too, not just the ones on screen', async ({ page }) => {
    const box = await startRenaming(page, '65/20');

    await box.fill('diffusion');
    await box.press('Enter');

    await expect.poll(async () => labelFor(page, '65/20')).toContain('diffusion');

    await page.getByTitle('Drop every layer name and put the palette colors back, leaving the bare numbers').click();

    await gotoExample(page, MOSFET);

    await expect.poll(async () => labelFor(page, '65/20')).toBe('65/20');
});

///
///Export closes the loop: it is the open file's own layers, in the format the loader reads, so what is on
///screen can be edited in a spreadsheet and loaded back.
///
///By id rather than by the button's title, which is UI copy - matching that tied this test to the wording,
///and it broke the moment the pair was renamed from Names and Template to Import and Export.
///
test('export writes this file own layers as a mapping, every column filled', async ({ page }) => {
    const download = page.waitForEvent('download');

    await page.locator('#layerNamesExport').click();

    const file = await download;
    const stream = await file.createReadStream();

    let text = '';
    for await (const chunk of stream)
        text += chunk.toString('utf8');

    expect(file.suggestedFilename()).toBe('Mosfet layers.csv');
    expect(text).toContain('#layer,datatype,name,color,height,thickness');

    //Every pair the sidebar lists, so nothing has to be typed from scratch.
    for (const pair of ['65,20', '66,20', '66,44', '67,20', '67,44', '68,5', '68,20', '93,44', '95,20'])
        expect(text).toContain(pair);

    //
    //And every row filled as far as the six the stack needs.
    //
    //The bug this was written for is a row of *four* columns under a six-column header: building a stack
    //then meant knowing to type two columns that were not there. Six was the whole row when it was written,
    //and it is a floor now - a bundled example arrives with the shipped sky130 mapping over it, so the rows
    //carrying a role or a fill write the columns those live in as well. See carryLayerNamesOver.
    //
    const rows = text.split('\n').filter(row => row.length > 0 && !row.startsWith('#'));

    expect(rows).toHaveLength(9);

    for (const row of rows)
        expect(row.split(',').length).toBeGreaterThanOrEqual(6);
});

///
///An example arrives named, without anybody importing anything.
///
///**Because every file in the picker is a sky130 cell.** The app compiles no PDK table in, which is right for
///a file off somebody's machine - it could be any technology - and was never right for the files the app
///itself chose to ship. Left as bare numbers, the one feature that needs a mapping was a greyed-out button on
///every file on offer.
///
test('a bundled example is named from the shipped mapping', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
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

///
///Clear means it for the file it was made on, and stops there.
///
///Both halves matter and they pull opposite ways. Dropping the names has to survive a reload, or the panel
///would fill straight back up and Clear would read as a button that does nothing. But it used to survive
///everything: one Clear, and every example opened afterwards arrived with bare numbers - the app
///withholding a mapping it ships, for a PDK it knows the file belongs to, on the strength of something said
///about a different layout. Nothing on screen explained it and the way back was behind a hover.
///
///The Example offer is what says which state the panel is in: it is there only while nothing is named.
///
test('a Clear holds for its own file and not for the next one', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
        { timeout: 20000 }).toContain('met1');

    await page.locator('.layerSidebarClear').click();

    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
        { timeout: 20000 }).not.toContain('met1');

    await expect(page.locator('#layerExampleOffer')).toBeVisible();

    //The same file again: still bare, because that is what was asked for about this one.
    await page.reload();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    await expect(page.locator('#layerExampleOffer')).toBeVisible({ timeout: 60000 });

    expect((await page.locator('.layerRow .layerName').allTextContents()).join(' ')).not.toContain('met1');

    //Another example, through the picker, which is a fresh start on a file whose technology is known.
    await selectExample(page, 'sky130_fd_sc_hd__nand2_1.gds');

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('sky130_fd_sc_hd__nand2_1.gds');

    await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
        { timeout: 20000 }).toContain('met1');

    await expect(page.locator('#layerExampleOffer')).toHaveCount(0);
});
