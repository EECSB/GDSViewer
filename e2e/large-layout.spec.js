//Opening a layout big enough to hurt.
//
//**The case the suite has never had.** Every bundled example is under 60 KB and the largest count any other
//spec asserts is 74 shapes, against a wall somewhere around twenty thousand - so nothing here has ever been
//measured in a browser, and every performance claim about this app was made about desktop .NET.
//
//`gds bench` measures the library, where a profiler works. This measures the half the library hands over: the
//marshal, the parse of a multi-megabyte markup string, and the layout and raster of however many nodes came
//out of it. Those are the browser's costs, they are the ones nothing here can profile, and on the desktop
//numbers they are where the wall is.
//
//The fixture is generated rather than committed - a half-million-element file does not belong in a repository
//and the interesting question is where the curve bends, which needs a family of sizes rather than one.
const { test, expect } = require('@playwright/test');
const { execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { gotoApp, shapeCount, shapeBox, downloadBytes , otherShapeClearOfPanel, openedOnItsOwn } = require('./helpers');

///
///Where the generated fixtures live: outside the repo, so nothing here is ever committed or swept up by the
///corpus test, and reused across runs because generating one costs a `dotnet run`.
///
const MADE = path.join(os.tmpdir(), 'gdsviewer-bench');

///
///How big to go.
///
///Twenty thousand, because that is roughly where the desktop numbers put the wall and a test that never
///reaches it measures nothing. Kept modest on purpose: this runs on every CI pass, and finding the exact
///cliff is `gds bench`'s job rather than the suite's.
///
const SHAPES = 20000;

function generate(shapes) {
    fs.mkdirSync(MADE, { recursive: true });

    const file = path.join(MADE, `flat-${shapes}.gds`);

    if (fs.existsSync(file))
        return file;

    execFileSync(
        'dotnet',
        ['run', '-c', 'Release', '--project', 'GdsII.Cli', '--', 'generate', '--shapes', String(shapes), '-o', file],
        { cwd: path.join(__dirname, '..'), stdio: 'pipe', timeout: 300000 });

    return file;
}

///
///Opens a generated layout and hands back how long the browser took to draw it.
///
///**Waited on the new count, not on any count.** The app opens the bundled Mosfet on load, so eighteen
///polygons are already there - a poll for "more than none" is satisfied before the upload has done anything
///and reports the time it took to measure the file that was already open.
///
async function openAndTime(page, file, shapes) {
    const started = Date.now();

    await page.locator('#fileUpload').setInputFiles(file);

    await openedOnItsOwn(page, 60000);

    await expect.poll(async () => shapeCount(page), { timeout: 240000 })
        .toBeGreaterThan(shapes / 2);

    return Date.now() - started;
}

test.describe.configure({ timeout: 300000 });

test.describe('a layout of twenty thousand shapes', () => {
    let file;

    test.beforeAll(() => {
        file = generate(SHAPES);
    });

    test.beforeEach(async ({ page }) => {
        await gotoApp(page);
    });

    ///
    ///**It opens at all**, which is the first of the three things this is for and the one nothing has ever
    ///checked. Every shape reaches the DOM today; whether that stays true is what the later work decides, so
    ///this asserts what is drawn is *enough*, not that it is exactly one node per element.
    ///
    test('opens, and draws what is in it', async ({ page }) => {
        const took = await openAndTime(page, file, SHAPES);

        const drawn = await shapeCount(page);

        console.log(`open: ${took} ms, ${drawn} polygons drawn of ${SHAPES}`);

        expect(drawn).toBeGreaterThan(SHAPES / 2);
    });

    ///
    ///What the browser was handed, which the library-side benchmark cannot see: the size of the markup string
    ///and the number of nodes it became. These are the two numbers the byte-reduction work has to move.
    ///
    test('reports what the browser was handed', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        const measured = await page.evaluate(() => {
            const svg = document.getElementById('gdsSVG');

            return {
                characters: svg.innerHTML.length,
                nodes: svg.querySelectorAll('*').length
            };
        });

        console.log(`markup: ${(measured.characters / 1e6).toFixed(1)} M characters, ${measured.nodes} nodes`);

        //
        //**The node count is bounded; the byte count is not.**
        //
        //A layer is one path with a subpath per shape, so the nodes are the layers - single figures for this
        //fixture's eight, against twenty thousand shapes. That is the thing a pan frame is proportional to,
        //and it is the same number on every machine, so it can be asserted where a millisecond cannot.
        //
        //Bounded loosely rather than pinned: what this has to catch is a return to one node per shape, which
        //is three orders of magnitude away, not a layer more or less.
        //
        expect(measured.nodes).toBeGreaterThan(0);
        expect(measured.nodes).toBeLessThan(SHAPES / 100);
    });

    ///
    ///The same pan, timed in the page rather than through the harness.
    ///
    ///**`page.mouse.move` is a round trip per step**, so the figure above carries about six milliseconds of
    ///Playwright with it - which was worth knowing before any of the work that made a frame cheap, because
    ///it might have been the whole of what was being measured. It was not, but the only way to find out was
    ///to measure the frame where it happens.
    ///
    ///What this reports is the gap between one animation frame and the next while the viewBox is moving,
    ///which is what "smooth" actually means. Recorded rather than bounded: a threshold in milliseconds is a
    ///number that depends on the machine, and this suite runs on a shared CI runner. The node count above is
    ///the assertion; this is the figure.
    ///
    test('pans a frame at a time, and says what a frame costs', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        const deltas = await page.evaluate(async (steps) => {
            const svg = document.getElementById('gdsSVG');
            const box = svg.getAttribute('viewBox').split(/\s+/).map(Number);
            const gaps = [];

            await new Promise(resolve => {
                let step = 0;
                let last = performance.now();

                function frame() {
                    //A hundredth of the view a step, so the whole picture stays on screen and every frame
                    //has the same amount to draw as the one before it.
                    const moved = (box[2] / 100) * step;

                    svg.setAttribute('viewBox', `${box[0] + moved} ${box[1]} ${box[2]} ${box[3]}`);

                    const now = performance.now();

                    //The first is thrown away: it carries whatever the page was doing beforehand.
                    if (step > 0)
                        gaps.push(now - last);

                    last = now;
                    step++;

                    if (step > steps) {
                        svg.setAttribute('viewBox', box.join(' '));
                        resolve();

                        return;
                    }

                    requestAnimationFrame(frame);
                }

                requestAnimationFrame(frame);
            });

            return gaps;
        }, 60);

        const sorted = deltas.slice().sort((one, other) => one - other);
        const median = sorted[Math.floor(sorted.length / 2)];

        console.log(`frame: ${median.toFixed(1)} ms median, ${sorted[sorted.length - 1].toFixed(1)} ms worst (${(1000 / median).toFixed(0)} fps)`);

        expect(deltas.length).toBeGreaterThan(0);
    });

    ///
    ///**Panning is pure JS** - a viewBox attribute, no Blazor round trip - so what is measured here is the
    ///browser re-rastering however many nodes are on screen, and nothing else. It is the number that decides
    ///whether culling is needed at all.
    ///
    test('pans, and says how long a frame takes', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width / 2), view.y + (view.height / 2));
        await page.mouse.down();

        const started = Date.now();
        const steps = 20;

        for (let i = 0; i < steps; i++)
            await page.mouse.move(view.x + (view.width / 2) + (i * 8), view.y + (view.height / 2) + (i * 4));

        const perStep = (Date.now() - started) / steps;

        await page.mouse.up();

        console.log(`pan: ${perStep.toFixed(1)} ms per move`);

        expect(perStep).toBeLessThan(2000);
    });

    ///
    ///**A saved image holds the whole layout, not the part that was on screen.**
    ///
    ///The one case only a large layout can show. Above `Viewer2DSvg.CullAbove` the view draws what is in the
    ///viewport and what is bigger than a pixel, and the download used to be a copy of the view - so zoomed
    ///in, it held sixteen shapes of twenty thousand and said nothing about the rest. A viewer that quietly
    ///saves a fraction of a layout is worse than one that refuses to save at all.
    ///
    ///Zoomed in first, because at the fit there is nothing to leave out and the bug is invisible.
    ///
    test('saves the whole layout, not the part on screen', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width / 2), view.y + (view.height / 2));

        for (let i = 0; i < 40; i++)
            await page.mouse.wheel(0, -120);

        //Culled down to a handful, which is what makes the assertion below mean something.
        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeLessThan(SHAPES / 10);

        const started = page.waitForEvent('download');

        await page.locator('#downloadImage').click();

        const svg = (await downloadBytes(await started)).toString('utf8');

        //A subpath per shape, so the moves are the shapes.
        const saved = svg.split('M').length - 1;

        console.log(`saved: ${saved} shapes of ${SHAPES}, ${(svg.length / 1e6).toFixed(1)} M characters`);

        expect(saved).toBe(SHAPES);
    });

    ///An edit re-flattens and rebuilds; this is the number the duplicate-work phase has to move.
    test('takes an edit, and says how long it took', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#contextBar')).toBeVisible({ timeout: 60000 });

        //One the selection panel is not covering. The click that entered the cell opened it over the
        //top-left of the view, and it takes its own clicks - so a drag starting behind it never begins.
        const chosen = await otherShapeClearOfPanel(page, 'inContext');

        expect(chosen, 'every shape is behind the panel').not.toBeNull();

        await page.mouse.click(chosen.x, chosen.y);

        await expect(page.locator('#selectionPanel')).toBeVisible({ timeout: 60000 });

        const started = Date.now();

        await page.mouse.move(chosen.x, chosen.y);
        await page.mouse.down();
        await page.mouse.move(chosen.x + 60, chosen.y, { steps: 4 });
        await page.mouse.up();

        await expect(page.locator('#undoEdit')).toBeVisible({ timeout: 240000 });

        console.log(`edit: ${Date.now() - started} ms from drop to redrawn`);
    });
});
