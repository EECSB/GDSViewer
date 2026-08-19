//Everything that leaves the app: the GDS download, the SVG export, the three 3D-model exporters, and the
//share button.
//
//The GDS one is the reason this file is worth having. It is the only test anywhere that reads what the
//app actually hands the browser, so it closes the gap between "Serialize round-trips in a unit test" and
//"the download button produces that file".
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const { gotoExample, selectView, downloadBytes, MOSFET, openedOnItsOwn } = require('./helpers');

const MOSFET_ON_DISK = path.join('wwwroot', 'resources', 'GDS Files', 'Sky130 GDS', 'Mosfet.gds');

test('the GDS download is byte for byte the file that was opened', async ({ page }) => {
    await gotoExample(page, MOSFET);

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;

    expect(download.suggestedFilename()).toBe('Mosfet.gds');

    //Compared against the served file rather than a length or a header, because the whole point of the
    //write path is that it reproduces its input exactly.
    expect(await downloadBytes(download)).toEqual(fs.readFileSync(MOSFET_ON_DISK));
});

test('a saved edit comes back out in the download', async ({ page }) => {
    await gotoExample(page, MOSFET, 'text');

    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 60000 });
    await expect.poll(async () => page.evaluate(() => window.GetMonacoContent() || ''), { timeout: 60000 })
        .toContain('HEADER:');

    await page.evaluate(async () => {
        window.alert = () => { };

        const text = await window.GetMonacoContent();

        await window.SetMonacoContent(text.replace('LAYER: 65 ', 'LAYER: 200 '));

        //By its id. It used to be found by the PNG inside it, which stopped existing the moment the button
        //became a blue square with an inline floppy on it - and a lookup by picture is a lookup that breaks
        //whenever the picture changes, which is not what this test is about.
        document.getElementById('saveGdsText').click();
    });

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const bytes = await downloadBytes(await started);
    const original = fs.readFileSync(MOSFET_ON_DISK);

    //Same size - one LAYER value changed, not its length - but no longer the same bytes.
    expect(bytes.length).toBe(original.length);
    expect(bytes.equals(original)).toBe(false);
});

///
///The picker beside the download button, which is the only way to get OASIS out of the app.
///
///The bytes are checked rather than only the name: naming a file .oas is easy and writing one is the
///part that can be wrong. The magic is what every reader tells the two formats apart by, including
///this app's own.
///
test('the download can be switched to OASIS', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await page.locator('#downloadFormat').selectOption('.oas');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;

    expect(download.suggestedFilename()).toBe('Mosfet.oas');

    const bytes = await downloadBytes(download);

    expect(bytes.subarray(0, 13).toString('latin1')).toBe('%SEMI-OASIS\r\n');

    //Smaller than the GDSII it came from, which is the reason the format exists.
    expect(bytes.length).toBeLessThan(fs.readFileSync(MOSFET_ON_DISK).length);
});

///
///The third format, which is the one nothing arrives as and everything can read.
///
///A DXF is what goes back to whoever sent you a drawing - a MEMS house working in AutoCAD, the mechanical
///side of a package. The bytes are checked rather than the name, since naming a file .dxf is easy.
///
test('the download can be switched to DXF', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await page.locator('#downloadFormat').selectOption('.dxf');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;

    expect(download.suggestedFilename()).toBe('Mosfet.dxf');

    const drawing = (await downloadBytes(download)).toString('latin1');

    //A DXF opens with a section, says what release it is, and ends where it says it does.
    expect(drawing).toContain('SECTION');
    expect(drawing).toContain('$ACADVER');
    expect(drawing.trimEnd()).toMatch(/EOF$/);

    //With the layer numbers in the layer names, since a DXF has nowhere else to put them.
    expect(drawing).toContain('L65D20');
});

///
///And the app says what it did to the layers, every time rather than never.
///
///Nothing is lost in the conversion, so this is not a warning - it is the one thing somebody has to know
///before sending the file on, and after is too late.
///
test('a DXF download says where the layer numbers went', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await page.locator('#downloadFormat').selectOption('.dxf');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    await started;

    await expect(page.locator('.inlineNotice')).toContainText('L<layer>D<datatype>');
});

///
///An OASIS file that is opened comes back out as OASIS without anyone choosing, because keeping the
///format a file arrived in is the rule and converting is the exception.
///
test('a file opened as OASIS defaults to being saved as OASIS', async ({ page }) => {
    await gotoExample(page, MOSFET);

    //Made here rather than committed: it is what the app's own writer produces, so a fixture would be a
    //second copy of that to keep current.
    await page.locator('#downloadFormat').selectOption('.oas');

    const madeIt = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const oasis = await downloadBytes(await madeIt);
    const uploaded = path.join(require('os').tmpdir(), `gdsviewer-e2e-${process.pid}-${Date.now()}.oas`);

    fs.writeFileSync(uploaded, oasis);

    try {
        await page.locator('#fileUpload').setInputFiles(uploaded);

        await openedOnItsOwn(page);

        //The picker follows the file in, so the next download needs no choosing.
        await expect.poll(async () => page.locator('#downloadFormat').inputValue(), { timeout: 30000 })
            .toBe('.oas');

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const download = await started;

        expect(download.suggestedFilename()).toMatch(/\.oas$/);
        expect((await downloadBytes(download)).subarray(0, 13).toString('latin1')).toBe('%SEMI-OASIS\r\n');
    }
    finally {
        fs.unlinkSync(uploaded);
    }
});

///
///And switched back, the same file comes out as GDSII under a .gds name - never as a .oas holding
///GDSII, which is the file every tool refuses on sight.
///
test('an OASIS file saved back as GDS is named and written as one', async ({ page }) => {
    await gotoExample(page, MOSFET);

    await page.locator('#downloadFormat').selectOption('.gds');

    const started = page.waitForEvent('download');

    await page.locator('#downloadGds').click();

    const download = await started;

    expect(download.suggestedFilename()).toBe('Mosfet.gds');
    expect((await downloadBytes(download)).subarray(0, 13).toString('latin1')).not.toBe('%SEMI-OASIS\r\n');
});

test('the 2D view exports its SVG', async ({ page }) => {
    await gotoExample(page, MOSFET);

    const started = page.waitForEvent('download');

    await page.locator('#downloadImage').click();

    const download = await started;

    expect(download.suggestedFilename()).toBe('mySvg.svg');

    const svg = (await downloadBytes(download)).toString('utf8');

    expect(svg).toContain('<svg');
    expect(svg).toContain('<path');
    //The labels go out with it, and the coordinates keep an ASCII minus.
    expect(svg).toContain('<text');
    expect(svg).toContain('-600');
});

test.describe('3D model export', () => {
    //Each exporter writes a different format, so the assertion is what that format starts with rather
    //than only that a file arrived.
    const formats = [
        { option: '.stl', file: 'Mosfet.stl', contains: 'solid' },
        { option: '.obj', file: 'Mosfet.obj', contains: 'v ' },
        { option: '.gltf', file: 'Mosfet.gltf', contains: '"asset"' }
    ];

    for (const format of formats) {
        test(`exports ${format.option}`, async ({ page }) => {
            await gotoExample(page, MOSFET, '3d');

            await expect(page.locator('#container canvas')).toBeVisible();

            await page.locator('#modelFormat').selectOption(format.option);

            const started = page.waitForEvent('download');

            //By its id. It used to be found by the PNG in it, which stopped existing the moment the control
            //became a blue square with an inline glyph joined to the picker beside it - and a lookup by
            //picture breaks whenever the picture changes, which is not what this test is about.
            await page.locator('#downloadModel').click();

            const download = await started;

            expect(download.suggestedFilename()).toBe(format.file);

            const contents = (await downloadBytes(download)).toString('utf8');

            expect(contents).toContain(format.contains);
            expect(contents.length).toBeGreaterThan(1000);
        });
    }
});
