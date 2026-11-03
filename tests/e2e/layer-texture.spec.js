//A layer's texture for the 3D view: a picture chosen in its settings, kept in this browser under the file's name,
//drawn tiled over the layer in 3D where the view shows textures - and carried by the layermap, as the picture's name
//in the CSV and the picture beside it in a zip. The format is LayerNamesTests', LayermapPackageTests' and
//PdkPackageTests'; what is here is the browser's half: the file input, the store, the scene and the download. A
//layermap with pictures comes in and goes out with a PDK, in the PDK manager, since the layer panel's Import and
//Export went there (2026-10-19).
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, selectView, captureScene, openLayerSettings, layerPairs, downloadBytes, openPdkManager, closePdkManager, MOSFET } = require('./helpers');

//A picture: one red pixel, as a PNG.
const RED_PIXEL = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64');

///Chooses the picture for the layer whose settings are open, and waits for the popup to say it has it.
async function chooseTexture(page, name = 'copper.png') {
    await page.locator('#layerTextureFile').setInputFiles({ name: name, mimeType: 'image/png', buffer: RED_PIXEL });

    await expect(page.locator('#layerTextureName')).toHaveText(name);
    await expect(page.locator('#layerTextureThumb')).toBeVisible();
}

///What the 3D scene draws the first layer with: the picture it names, whether that is on it, and the repeat.
async function firstLayerTexture(page) {
    return page.evaluate(() => {
        const slabs = [];

        window.__gdsScene.traverse(object => {
            if (object.isMesh && object.userData.words != null)
                slabs.push(object);
        });

        slabs.sort((a, b) => a.userData.stackAt - b.userData.stackAt);

        const material = slabs[0].material;

        return {
            name: material.userData.textureName,
            mapped: material.userData.surface.surfaceMapped.value,
            tile: material.userData.surface.surfaceTile.value,
            surfaced: material.userData.surfaced
        };
    });
}

///Whether this browser keeps a picture by this name - see TextureStore.
async function kept(page, name) {
    return page.evaluate(async key => (await window.gdsStorage.get(key)) != null, `gdsviewer.texture.${name}`);
}

///A CSV row giving the first layer in the list this picture, and nothing else.
async function firstLayerRow(page, picture) {
    const [number, dataType] = (await layerPairs(page))[0].split('/');

    return `${number},${dataType},,,,,,,,,${picture}\n`;
}

///
///A picture chosen in a layer's settings is shown there, kept, and given to the layer - and turns the 3D view's Show
///textures on, so the 3D view draws it the moment it opens: tiled at the size typed, in the layout's units.
///
test('a picture chosen in the layer settings is drawn on that layer in 3D, at the size given', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openLayerSettings(page, 0);

    await expect(page.locator('#layerTextureName')).toHaveText('None');

    await chooseTexture(page);

    await page.locator('#textureSize').fill('500');
    await page.locator('#textureSize').press('Enter');

    await expect.poll(async () => page.evaluate(() => JSON.parse(localStorage.getItem('gdsviewer.view3d') || '{}').textures)).toBe(true);

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => {
        await captureScene(page);

        return page.evaluate(() => window.__gdsScene != null);
    }).toBe(true);

    await expect.poll(async () => (await firstLayerTexture(page)).mapped, { timeout: 30000 }).toBe(1);

    const drawn = await firstLayerTexture(page);

    expect(drawn.name).toBe('copper.png');
    expect(drawn.tile).toBe(500);
    expect(drawn.surfaced).toBe(true);
});

///
///**A picture that arrives after the slabs are drawn is put on them there.** A picture loads after the draw that asked
///for it, so the slabs are already up when it is ready - see Viewer3D.setTexture. Chosen with the 3D view open, and
///read without a second capture: captureScene draws the layers again, and a draw after the picture has loaded puts it
///on by itself, which is what let the first test pass with this half broken.
///
test('a picture that arrives after the 3D view has drawn its layers is put on them', async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => {
        await captureScene(page);

        return page.evaluate(() => window.__gdsScene != null);
    }).toBe(true);

    //met1's picture, given to sky130's row for it in the PDK manager - the layer settings on that row: the layers
    //are drawn again for the PDK before the picture loads.
    await openPdkManager(page);
    await page.locator('#pdkLayersTab').click();
    await page.locator('#pdkLayerTable tr[data-pair="68/20"] .pdkLook').click();
    await chooseTexture(page);
    await closePdkManager(page);

    await expect.poll(async () => page.evaluate(() => {
        const named = [];

        window.__gdsScene.traverse(object => {
            if (object.isMesh && object.material?.userData?.textureName === 'copper.png')
                named.push(object.material.userData.surface.surfaceMapped.value);
        });

        if (named.length === 0)
            return 'no slab names it';

        return named.every(mapped => mapped === 1);
    }), { timeout: 20000 }).toBe(true);
});

///
///Remove takes the layer's picture away, and the layer is drawn without one again.
///
test('Remove takes the picture off the layer', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openLayerSettings(page, 0);
    await chooseTexture(page);

    await page.locator('#layerTextureRemove').click();

    await expect(page.locator('#layerTextureName')).toHaveText('None');
    await expect(page.locator('#layerTextureThumb')).toHaveCount(0);
    await expect(page.locator('#textureSize')).toHaveCount(0);
});

///
///**A PDK's Export writes its pictures beside its layermap**: the CSV, naming the picture in its texture column, and the
///picture in the PDK's folder. And the zip imported into a browser that no longer keeps the picture brings it back -
///into the store, and onto the row it names. A picture chosen on a layer of a file drawn in the PDK goes into the PDK's
///row, which is how this one gets there.
///
test('a PDK goes out as a zip with its picture, and the zip brings the picture back', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openLayerSettings(page, 0);
    await chooseTexture(page);
    await page.locator('.layerSettingsPopup .closeButton').click();

    await openPdkManager(page);

    const download = page.waitForEvent('download');

    await page.locator('#pdkExport').click();

    const file = await download;
    const zip = await downloadBytes(file);

    expect(file.suggestedFilename()).toBe('sky130.zip');

    //A zip, with the two names in it: the entry names are stored as they are, in the headers and the directory.
    expect(zip.subarray(0, 4).toString('binary')).toBe('PK\u0003\u0004');
    expect(zip.includes(Buffer.from('sky130/sky130 layers.csv'))).toBe(true);
    expect(zip.includes(Buffer.from('sky130/copper.png'))).toBe(true);

    //As though the picture had never been kept here.
    await page.evaluate(() => window.gdsStorage.remove('gdsviewer.texture.copper.png'));

    expect(await kept(page, 'copper.png')).toBe(false);

    await page.locator('#pdkPackageImport').setInputFiles({ name: 'sky130.zip', mimeType: 'application/zip', buffer: zip });

    await expect(page.locator('#pdkImportSays')).toHaveText('Added sky130 2: 46 layers and 30 rules.');

    await expect.poll(async () => kept(page, 'copper.png')).toBe(true);

    //On the row of the PDK it came in as, which its layer settings say.
    const [number, dataType] = (await layerPairs(page))[0].split('/');

    await page.locator('#pdkLayersTab').click();
    await page.locator(`#pdkLayerTable tr[data-pair="${number}/${dataType}"] .pdkLook`).click();

    await expect(page.locator('#layerTextureName')).toHaveText('copper.png');
    await expect(page.locator('#layerTextureThumb')).toBeVisible();
    await expect(page.locator('#layerTextureMissing')).toHaveCount(0);
});

///
///A PDK naming a picture that neither came with it nor is kept here says so - which layers will be drawn without one -
///rather than leaving the layer quietly untextured.
///
test('a PDK naming a picture that did not come with it says which', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');
    await openPdkManager(page);

    await page.locator('#pdkPackageImport').setInputFiles({
        name: 'textured.csv',
        mimeType: 'text/csv',
        buffer: Buffer.from('65,20,diff,,,,,,,,missing.png\n', 'utf8')
    });

    await expect(page.locator('#pdkImportSays')).toContainText('missing.png');
});

///
///**A CSV chosen together with its pictures**, the way a folder is imported without zipping it: a PDK, with the picture
///kept by the name the CSV gives it, and said nothing about - it came with the layermap.
///
test('a CSV chosen together with its picture keeps the picture', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    const row = await firstLayerRow(page, 'copper.png');

    await openPdkManager(page);

    await page.locator('#pdkPackageImport').setInputFiles([
        { name: 'textured.csv', mimeType: 'text/csv', buffer: Buffer.from(row, 'utf8') },
        { name: 'copper.png', mimeType: 'image/png', buffer: RED_PIXEL }
    ]);

    await expect(page.locator('#pdkImportSays')).toHaveText('Added textured: 1 layer and 0 rules.');

    await expect.poll(async () => kept(page, 'copper.png')).toBe(true);

    await page.locator('#pdkLayersTab').click();
    await page.locator('#pdkLayerTable tr .pdkLook').first().click();

    await expect(page.locator('#layerTextureName')).toHaveText('copper.png');
    await expect(page.locator('#layerTextureThumb')).toBeVisible();
});

///
///**A layermap an address names fetches its pictures from beside it** - the texture column's names, from the CSV's own
///address - and says in the strip over the view which it could not, rather than in a dialog over somebody else's page.
///Served by the test, on the app's own origin, the way embedding.spec.js serves its layermap.
///
test('a layermap an address names brings its pictures from beside it, and says which did not come', async ({ page, baseURL }) => {
    const folder = `${baseURL}/textured-maps`;

    //Layer 68/20 is MOSFET's met1, and 65/20 its diffusion; one picture is there and one is not.
    await page.route(`${folder}/textured.csv`, route => route.fulfill({
        contentType: 'text/csv',
        body: '68,20,met1,,,,,,,,copper.png\n65,20,diff,,,,,,,,gone.png\n'
    }));

    await page.route(`${folder}/copper.png`, route => route.fulfill({ contentType: 'image/png', body: RED_PIXEL }));
    await page.route(`${folder}/gone.png`, route => route.fulfill({ status: 404, body: '' }));

    await gotoApp(page, `?file=${MOSFET}&view=2d&layermap=${encodeURIComponent(`${folder}/textured.csv`)}`);
    await expectLoaded(page);

    await expect.poll(async () => kept(page, 'copper.png'), { timeout: 20000 }).toBe(true);

    await expect(page.locator('#layerMapNotice')).toContainText('gone.png');
    await expect(page.locator('#layerMapNotice')).not.toContainText('copper.png');

    expect(await kept(page, 'gone.png')).toBe(false);
});
