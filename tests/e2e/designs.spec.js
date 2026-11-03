//
//The bundled designs: whole open-source chips, listed above the cells, each one shown with whose it is.
//
//DesignTests holds designs.json to the folder and to its credits; what only a browser can show is the page
//holding to designs.json - the rows, the credit under the list and its links, and a design actually drawn.
//
const { test, expect } = require('@playwright/test');
const path = require('path');
const fs = require('fs');
const { gotoApp, gotoExample, openExamples, exampleRow, filterExamples, openFile, svgCounts, layerLabel, stackOnScreen, onScreenSaid, cameraPosition, openViewSettings } = require('./helpers');

const DESIGNS = require(path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'designs.json'));

//GF180's designs, which are listed and credited the same way and shipped not at all: each is downloaded from its own
//repository when it is opened (2026-11-02, asked for).
const GF180_DESIGNS = require(path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'gf180-designs.json'));

//A GF180 cell off disk, served in GitHub's place - so a spec opening a GF180 design reaches nothing off this machine.
const GF180_CELL = path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'GF180 GDS', 'gf180mcu_fd_sc_mcu7t5v0__inv_1.gds');

//IHP's the same way, and one of IHP's cells to serve in GitHub's place.
const IHP_DESIGNS = require(path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'ihp-designs.json'));
const IHP_CELL = path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'IHP GDS', 'sg13g2_inv_1.gds');

//The smallest of them, so a spec waits on the app rather than on a big file - and analog, so it carries
//layers no standard cell has: met4's pins and labels, and the fill generator's markers.
const SMALL = 'tt_um_oscillating_bones';

//A design whose name says sky130, which is what used to decide whether the PDK's cell links were shown.
const NAMED_SKY130 = 'sky130_ef_ip__opamp';

function listed(name) {
    return DESIGNS.find(design => design.file === `${name}.gds`);
}

///
///How many outlines a redraw of the 3D scene builds, counted until the scene goes quiet.
///
///threeCounts gives the redraw half a second, which is plenty for one cell and nowhere near enough for a
///design on the dev server - so this counts until a second passes with nothing added.
///
///Outlines rather than meshes, because a mesh is a whole layer: counting meshes would say how many layers
///were built, which a design that came out thin would pass. Each mesh says how many outlines went into it.
///
async function outlinesBuilt(page) {
    return page.evaluate(async () => {
        const THREE = await import('three');

        let outlines = 0;
        let lastAdded = performance.now();

        const original = THREE.Object3D.prototype.add;

        //The slabs, which are the meshes carrying outlines - a layer's labels are a mesh too.
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh && object.userData.outlines != null) {
                    outlines += object.userData.outlines;
                    lastAdded = performance.now();
                }
            }

            return original.apply(this, objects);
        };

        //The spacing slider is the cheapest way to ask for a redraw from outside - see threeCounts.
        const slider = document.getElementById('layerSpacing');

        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        const started = performance.now();

        while (performance.now() - started < 30000) {
            await new Promise(resolve => setTimeout(resolve, 250));

            if (outlines > 0 && performance.now() - lastAdded > 1000)
                break;
        }

        THREE.Object3D.prototype.add = original;

        return outlines;
    });
}

///
///Between the test file and the cells, every one of them, each saying what it is.
///
///A design's file name is its maker's - tt_um_mcs4_cpu - so the row's tooltip carries the title and the
///author, which is what tells somebody pointing along the list what they are about to open.
///
test('the designs are listed under their own heading, each saying what it is', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    const headings = page.locator('.examplePickerHeading');

    await expect(headings.nth(0)).toHaveText('Test GDS Files');
    await expect(headings.nth(1)).toHaveText('Sky130 Designs');
    await expect(headings.nth(2)).toHaveText('GF180 Designs');
    await expect(headings.nth(3)).toHaveText('IHP Designs');

    //The cells' headings follow, below the window of rows the virtualized list draws once IHP's designs joined - the
    //filtered specs below and in app-launch.spec hold their order.

    for (const design of DESIGNS) {
        await filterExamples(page, design.file.replace('.gds', ''));

        await expect(exampleRow(page, design.file)).toHaveCount(1);
        await expect(exampleRow(page, design.file)).toHaveAttribute('title', `${design.title}, by ${design.author}`);
    }

    //Narrowed to one, the designs' heading stands over it alone.
    await filterExamples(page, SMALL);

    await expect(headings).toHaveCount(1);
    await expect(headings.nth(0)).toHaveText('Sky130 Designs');
});

///
///A design opens from a link the way a cell does, drawn, and with the sky130 mapping laid over it.
///
///The mapping is what names its layers, and these files carry pairs no cell does - met4's label among them,
///which was given its height when the designs arrived.
///
test('a design opens by its name in a link, drawn and with its layers named', async ({ page }) => {
    await gotoExample(page, SMALL, '2d');

    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBeGreaterThan(1000);

    await expect.poll(async () => layerLabel(page, '71/5'), { timeout: 30000 }).toContain('met4.label');
});

///
///With a design open, the Examples popup says whose it is and links back to where it came from.
///
///A design rather than a cell gets this instead of the PDK's cell documentation, even when its name says
///sky130 - those links are about the standard cells, and this is somebody else's work under its own license.
///
test('a design open is credited under the list, with links to its project, its file and its license', async ({ page }) => {
    const design = listed(NAMED_SKY130);

    await gotoExample(page, NAMED_SKY130, '2d');
    await openExamples(page);

    const credit = page.locator('.designCredit');

    await expect(credit).toContainText(design.title);
    await expect(credit).toContainText(design.author);
    await expect(credit).toContainText(design.about);

    //Out to the project and to the exact file, each in a tab of its own.
    await expect(credit.locator(`a[href="${design.project}"]`)).toHaveAttribute('target', '_blank');
    await expect(credit.locator(`a[href="${design.gds}"]`)).toHaveAttribute('target', '_blank');

    //And the license as text this app serves itself, so the terms travel with the design.
    const license = credit.locator('a', { hasText: design.license });
    const served = await page.request.get(new URL(await license.getAttribute('href'), page.url()).toString());

    expect(served.ok()).toBe(true);
    expect(await served.text()).toContain('Apache License');

    await expect(page.getByText('Example PDK NAND2 Cell Docs')).toHaveCount(0);
});

///
///Auto-generate preview at the far end of the credit's first line, and the popup still not scrolling.
///
///The box was under the picture, a line of its own. It moved to the line under the popup, which is there for
///every file - a line more than the popup had, in a popup capped twice, its own height and the list's. On a tall
///window the two caps put it over and it scrolled inside itself; measurePopupRoom takes the line out of the
///popup's cap now. A design's credit is three lines, the tallest that line gets.
///
test('Auto-generate preview ends the first line of a design\'s credit, and the popup does not scroll', async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 1400 });

    await gotoExample(page, NAMED_SKY130, '2d');
    await openExamples(page);

    await expect(page.locator('.designCredit')).toBeVisible();

    await expect.poll(() => page.evaluate(() => {
        const popup = document.querySelector('.popupDiv.popupUnder');
        const first = document.querySelector('.designCredit p').getBoundingClientRect();
        const box = document.getElementById('examplesAutoPreview').closest('label').getBoundingClientRect();
        const line = document.querySelector('.examplesFoot').getBoundingClientRect();

        return {
            scrolls: popup.scrollHeight > popup.clientHeight,
            level: Math.abs((first.top + first.bottom) / 2 - (box.top + box.bottom) / 2) < 6,
            atTheEnd: Math.abs(line.right - box.right) < 2
        };
    })).toEqual({ scrolls: false, level: true, atTheEnd: true });
});

///And a cell still gets the PDK's links, and no credit - the cells are one library with one license.
test('a cell open still gets the PDK links, and no credit', async ({ page }) => {
    await gotoExample(page, 'sky130_fd_sc_hd__nand2_1', '2d');
    await openExamples(page);

    await expect(page.getByText('Example PDK NAND2 Cell Docs')).toBeVisible();
    await expect(page.locator('.designCredit')).toHaveCount(0);
});

///
///A design stands up in the 3D view, its layers given heights by the mapping.
///
///Without a height a layer is left out of the scene entirely, so a design whose layers the map did not cover
///would come out thin rather than fail - which is why this counts what is built rather than asking whether
///anything was.
///
test('a design is built in 3D', async ({ page }) => {
    await gotoExample(page, SMALL, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    await expect.poll(async () => outlinesBuilt(page), { timeout: 90000 }).toBeGreaterThan(1000);
});

///
///**A design's labels are drawn a layer at a time**, not one by one.
///
///At a sprite each - a draw call apiece on every frame - the RGB mixer's 19,005 labels took its frames from the
///17 ms its slabs need to 43 on a GPU, and the VGA clock and the 4004 CPU went the same way. This design carries
///171 of them on six layers. Counted the way outlinesBuilt counts, until the scene goes quiet, and asserted as
///a ratio rather than as those two numbers, which are the layer map's to change: well over a hundred labels,
///and a tenth as many draws at the very most.
///
test('a design\'s labels are drawn a layer at a time', async ({ page }) => {
    await gotoExample(page, SMALL, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    const drawn = async () => page.evaluate(async () => {
        const THREE = await import('three');

        let labels = 0;
        let meshes = 0;
        let lastAdded = performance.now();

        const original = THREE.Object3D.prototype.add;

        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh)
                    lastAdded = performance.now();

                if (object.isMesh && object.userData.labels != null) {
                    labels += object.userData.labels;
                    meshes++;
                }
            }

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');

        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        const started = performance.now();

        while (performance.now() - started < 30000) {
            await new Promise(resolve => setTimeout(resolve, 250));

            if (labels > 0 && performance.now() - lastAdded > 1000)
                break;
        }

        THREE.Object3D.prototype.add = original;

        return { labels: labels, meshes: meshes };
    });

    await expect.poll(async () => (await drawn()).labels, { timeout: 90000 }).toBeGreaterThan(100);

    const seen = await drawn();

    expect(seen.labels).toBeGreaterThan(100);
    expect(seen.meshes).toBeGreaterThan(0);
    expect(seen.meshes).toBeLessThanOrEqual(seen.labels / 10);
});

///
///**And it opens in view: in frame, between the near and far planes, and seen from above.**
///
///Every bundled design opened on an empty view. The planes were fixed at 1 and 50,000, which suits a cell, and a
///design is framed from further than that - so all of it was past the far plane, while the framing spec, which
///ran on a cell and checked only across and up, passed. And from the angle it opened at, looking straight across,
///pushing the far plane back only showed the chip edge-on.
///
test('a design opens in 3D in view, looking down on it', async ({ page }) => {
    await gotoExample(page, SMALL, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    const on = await stackOnScreen(page);

    expect(on.meshes).toBeGreaterThan(10);

    const said = onScreenSaid(on);

    expect(on.nearest, `the design is in front of the near plane: ${said}`).toBeGreaterThanOrEqual(-1);
    expect(on.farthest, `the design is past the far plane: ${said}`).toBeLessThanOrEqual(1);

    expect(Math.max(-on.left, on.right, -on.bottom, on.top), `the design runs out of the frame: ${said}`).toBeLessThanOrEqual(1);
    expect(on.right - on.left, `the design is a speck: ${said}`).toBeGreaterThan(1);

    expect(on.pitch).toBeCloseTo(45, 0);
});

///
///Admire's orbit ran 10,000 units out from the middle of whatever was open, which is inside every design - it
///circled over the middle of the chip, looking at a few cells at a time. It is in proportion now.
///
test('Admire circles a design from outside it', async ({ page }) => {
    await gotoExample(page, SMALL, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    const on = await stackOnScreen(page);

    expect(on.camera).toBe(true);

    const before = await cameraPosition(page);

    //A switch in the view's settings, under the cube.
    await openViewSettings(page);
    await page.locator('#viewAdmire').check();

    //Driven from the render loop, so it takes frames rather than a call to move.
    await expect.poll(async () => {
        const now = await cameraPosition(page);

        return now.x !== before.x || now.y !== before.y || now.z !== before.z;
    }, { timeout: 15000 }).toBe(true);

    const now = await cameraPosition(page);
    const out = Math.hypot(now.x - on.middle.x, now.y - on.middle.y, now.z - on.middle.z);

    expect(out, `${Math.round(out)} from the middle of a design that reaches ${Math.round(on.radius)} from it`).toBeGreaterThan(on.radius);

    await page.locator('#viewAdmire').uncheck();
});

///
///**A GF180 design is a link, downloaded when it is opened** (2026-11-02, asked for): listed and credited like the sky130
///ones, it ships in nothing, so its row says how big it is, pointing at it fetches nothing until the frame's button is
///pressed, and opening it fetches the raw file at the commit its credit names - and opens it under gf180mcu, which it
///is drawn in. GitHub is stood in for by a GF180 cell off disk, and everything else off this machine is refused.
///
test('a GF180 design is downloaded from the commit its credit names when it is opened, and opens under gf180mcu', async ({ page }) => {
    const design = GF180_DESIGNS.find(one => one.file === 'tt_um_vga_clock.oas');
    const fetched = [];

    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort('blockedbyclient'));
    await page.route('https://raw.githubusercontent.com/**', async route => {
        fetched.push(route.request().url());

        await route.fulfill({
            status: 200,
            headers: { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/octet-stream' },
            body: fs.readFileSync(GF180_CELL)
        });
    });

    await gotoApp(page);
    await openExamples(page);
    await filterExamples(page, 'tt_um_vga_clock');

    //sky130's VGA clock beside it, a file of the same name in another process.
    await expect(exampleRow(page, 'tt_um_vga_clock.gds')).toHaveCount(1);

    const row = exampleRow(page, design.file);

    await expect(row).toHaveAttribute('title', `${design.title}, by ${design.author} - downloaded from GitHub when opened, 97 KB`);
    await expect(row.locator('.exampleDownloadSize')).toHaveText('97 KB');

    //Pointed at, it waits for a press rather than fetching for a row nobody chose.
    await row.hover();

    await expect(page.locator('#examplePreviewGenerate')).toContainText('Download 97 KB and preview');

    expect(fetched).toEqual([]);

    await row.click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(design.file);

    expect(fetched).toEqual([design.download]);

    await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is gf180mcu/);
    await expect.poll(async () => layerLabel(page, '34/0'), { timeout: 30000 }).toContain('Metal1');

    //Credited under the list, its file the commit it came from.
    await openExamples(page);

    const credit = page.locator('.designCredit');

    await expect(credit).toContainText(design.title);
    await expect(credit.locator(`a[href="${design.gds}"]`)).toHaveAttribute('target', '_blank');
});

///
///**An IHP design is a link the same way** (2026-11-02, asked for), and opens under ihp-sg13g2. GitHub is stood in for
///by an IHP cell off disk.
///
test('an IHP design is downloaded from the commit its credit names when it is opened, and opens under ihp-sg13g2', async ({ page }) => {
    const design = IHP_DESIGNS.find(one => one.file === '50_ghz_mpa_core.gds');
    const fetched = [];

    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort('blockedbyclient'));
    await page.route('https://raw.githubusercontent.com/**', async route => {
        fetched.push(route.request().url());

        await route.fulfill({
            status: 200,
            headers: { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/octet-stream' },
            body: fs.readFileSync(IHP_CELL)
        });
    });

    await gotoApp(page);
    await openExamples(page);
    await filterExamples(page, '50_ghz_mpa');

    const row = exampleRow(page, design.file);

    await expect(row).toHaveAttribute('title', `${design.title}, by ${design.author} - downloaded from GitHub when opened, 250 KB`);
    await expect(row.locator('.exampleDownloadSize')).toHaveText('250 KB');

    //Pointed at, it waits for a press rather than fetching for a row nobody chose.
    await row.hover();

    await expect(page.locator('#examplePreviewGenerate')).toContainText('Download 250 KB and preview');

    expect(fetched).toEqual([]);

    await row.click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(design.file);

    expect(fetched).toEqual([design.download]);

    await expect(page.locator('#sidebarPdkManager')).toHaveAttribute('title', /^The PDK in use is ihp-sg13g2/);
    await expect.poll(async () => layerLabel(page, '8/0'), { timeout: 30000 }).toContain('Metal1');
});

///And GF180's cells and IHP's are groups of their own, at the end, with sky130's of the same name over them.
test('GF180 and IHP cells are listed under their own headings', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);
    await filterExamples(page, 'inv_1.gds');

    const headings = page.locator('.examplePickerHeading');

    await expect(headings).toHaveText(['Sky130 GDS Examples', 'GF180 GDS Examples', 'IHP GDS Examples']);
    await expect(exampleRow(page, 'gf180mcu_fd_sc_mcu7t5v0__inv_1.gds')).toHaveCount(1);
    await expect(exampleRow(page, 'gf180mcu_fd_sc_mcu7t5v0__inv_1.gds')).toHaveText('gf180mcu_fd_sc_mcu7t5v0__inv_1.gds');
    await expect(exampleRow(page, 'sg13g2_inv_1.gds')).toHaveCount(1);
});
