//Opening a layout big enough to hurt.
//
//**The case the suite had never had.** Every bundled example was under 60 KB and the largest count any
//other spec asserted was 74 shapes, against a wall somewhere around twenty thousand - so nothing here had
//ever been measured in a browser, and every performance claim about this app was made about desktop .NET.
//The designs bundled since are past that wall, but only at the sizes they happen to be; this makes the
//size it needs.
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
const { gotoApp, shapeCount, shapeBox, downloadBytes , otherShapeClearOfPanel, uploadOpenedOnItsOwn, saveImage, selectView } = require('./helpers');

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

///How long one generation may take, and so how old a lock can get before it is taken to be left over.
const GENERATION_TIMEOUT = 300000;

///
///**Made once, however many workers ask for it.** The tests below run in parallel and every worker runs this
///describe's beforeAll, so on a run where the file was not in tmp yet each of them started its own `dotnet run`
///of the CLI at the same moment. Several MSBuilds building one project into one obj/ folder is a build that
///fails: two of six did on 2026-09-23, each saying nothing but "The build failed", while the ones that got
///through were writing the same file at once.
///
///So the first worker to create the lock file makes the fixture, and the rest wait for the lock to go. It is
///written under a name of its own and renamed into place, so what a waiting worker finds is always whole. A
///lock older than a generation is allowed to take is one a killed run left behind, and is cleared.
///
async function generate(shapes) {
    fs.mkdirSync(MADE, { recursive: true });

    const file = path.join(MADE, `flat-${shapes}.gds`);
    const lock = `${file}.lock`;

    while (!fs.existsSync(file)) {
        let held = null;

        try {
            held = fs.openSync(lock, 'wx');
        }
        catch (error) {
            if (error.code !== 'EEXIST')
                throw error;
        }

        if (held == null) {
            const taken = fs.statSync(lock, { throwIfNoEntry: false });

            if (taken != null && Date.now() - taken.mtimeMs > GENERATION_TIMEOUT)
                fs.rmSync(lock, { force: true });

            await new Promise(resolve => setTimeout(resolve, 1000));

            continue;
        }

        const partial = path.join(MADE, `flat-${shapes}.${process.pid}.partial.gds`);
        const started = Date.now();

        try {
            //Asked again with the lock in hand: whoever held it last may have finished in between.
            if (!fs.existsSync(file)) {
                execFileSync(
                    'dotnet',
                    ['run', '-c', 'Release', '--project', 'GdsII.Cli', '--', 'generate', '--shapes', String(shapes), '-o', partial],
                    { cwd: path.join(__dirname, '..', '..'), stdio: 'pipe', timeout: GENERATION_TIMEOUT });

                fs.renameSync(partial, file);

                console.log(`fixture: generated ${path.basename(file)} in ${Math.round((Date.now() - started) / 1000)} s`);
            }
        }
        finally {
            fs.rmSync(partial, { force: true });
            fs.closeSync(held);
            fs.rmSync(lock, { force: true });
        }
    }

    return file;
}

///
///Opens a generated layout and hands back how long the browser took to draw it.
///
///**Waited on the new count, not on any count.** The app opens the bundled Mosfet on load, so twenty
///polygons are already there - a poll for "more than none" is satisfied before the upload has done anything
///and reports the time it took to measure the file that was already open.
///
///**And not on a question that is not coming.** Uploaded over the app's own Mosfet, the file is opened without
///asking whether to bring it in - so a minute spent waiting for that question was every open this reported, all
///sixty seconds of it. See uploadOpenedOnItsOwn, which stops waiting once the file is opened.
///
async function openAndTime(page, file, shapes) {
    const started = Date.now();

    await uploadOpenedOnItsOwn(page, file, 60000);

    await expect.poll(async () => shapeCount(page), { timeout: 240000 })
        .toBeGreaterThan(shapes / 2);

    return Date.now() - started;
}

test.describe.configure({ timeout: 300000 });

test.describe('a layout of twenty thousand shapes', () => {
    ///
    ///**Measured locally, not on a runner.** Six tests, each opening the same twenty-thousand-shape file
    ///under a five-minute budget, on two shared cores with no GPU - half an hour of a CI pass, and two of
    ///them ran out of time anyway.
    ///
    ///What that timeout reports is worse than slow, it is misleading. The poll gives up still counting the
    ///twenty polygons of the bundled Mosfet, which is exactly what a file that never uploaded looks like -
    ///so a runner too slow to finish the draw reads as the app failing to open the file at all.
    ///
    ///The numbers are the whole point of this file and they are worth having; they are worth having from a
    ///machine whose speed means something, which a shared runner is not. `process.env.CI` is set by Actions,
    ///the same switch playwright.config.js sizes its worker count with.
    ///
    ///**Putting this back on CI needs the CLI built before the suite starts.** `generate()` shells out to
    ///`dotnet run -c Release --project GdsII.Cli`, and on a runner the fixture is never already in tmp, so
    ///that build lands in the obj/ tree the Playwright web server is running out of - two dotnet builds in
    ///one directory, and the second fails with "The build failed" and no compiler error under it, which
    ///reads as a broken CLI and is nothing of the kind. A `dotnet build GdsII.Cli -c Release` step ahead of
    ///the suite is what stopped that. It was in ci.yml until this skip made it dead weight.
    ///
    test.skip(!!process.env.CI, 'twenty thousand shapes is a local measurement');

    let file;

    test.beforeAll(async () => {
        file = await generate(SHAPES);
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
    ///What this reports is the gap between one animation frame and the next while a drag is moving the view,
    ///which is what "smooth" actually means. The drag is the app's own, a pointer move a frame: it used to set
    ///the viewBox here directly, which was what a pan did until a pan started moving the picture already
    ///drawn instead - see showViewBox in JavaScriptInterOp.js - and from then on it would have been timing a
    ///raster no drag ever asks for. Recorded rather than bounded: a threshold in milliseconds is a number that
    ///depends on the machine, and this suite runs on a shared CI runner. The node count above is the assertion;
    ///this is the figure.
    ///
    test('pans a frame at a time, and says what a frame costs', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        const deltas = await page.evaluate(async (steps) => {
            const svg = document.getElementById('gdsSVG');
            const rect = svg.getBoundingClientRect();
            const x0 = rect.left + (rect.width / 2);
            const y0 = rect.top + (rect.height / 2);
            const options = (x, y, buttons) => ({ bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true, button: 0, buttons: buttons, clientX: x, clientY: y });
            const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
            const gaps = [];

            //The first move the page sees is swallowed - see isFirstClick - so it is spent here, before the press.
            svg.dispatchEvent(new PointerEvent('pointermove', options(x0, y0, 0)));
            svg.dispatchEvent(new PointerEvent('pointerdown', options(x0, y0, 1)));

            //The first frame is thrown away: it carries whatever the page was doing beforehand.
            let last = await frame();

            for (let step = 1; step <= steps; step++) {
                //Two pixels a step, so the whole picture stays on screen and every frame has the same amount to
                //show as the one before it.
                svg.dispatchEvent(new PointerEvent('pointermove', options(x0 + (step * 2), y0, 1)));

                const now = await frame();

                gaps.push(now - last);
                last = now;
            }

            svg.dispatchEvent(new PointerEvent('pointerup', options(x0 + (steps * 2), y0, 0)));

            return gaps;
        }, 60);

        const sorted = deltas.slice().sort((one, other) => one - other);
        const median = sorted[Math.floor(sorted.length / 2)];

        console.log(`frame: ${median.toFixed(1)} ms median, ${sorted[sorted.length - 1].toFixed(1)} ms worst (${(1000 / median).toFixed(0)} fps)`);

        expect(deltas.length).toBeGreaterThan(0);
    });

    ///
    ///**Panning is pure JS** - no Blazor round trip, and while the drag lasts not even a viewBox: the picture
    ///already drawn is moved, and the viewBox goes on once it stops. So what is measured here is Playwright's
    ///round trip and the move itself, through the page's real input.
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

        await saveImage(page, 'svg');

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

    ///
    ///**Opening in 3D**, which no instrument measured until the 2026-11-03 audit said so: from the press on the 3D
    ///button to the last layer's mesh put in the scene. A mesh is a layer - see Viewer3D - so the count is the layers.
    ///
    ///Timed by the scene itself: Object3D.add is wrapped before the press, and every mesh carrying outlines is a
    ///layer's slab arriving. Done once none has arrived for two seconds. Recorded rather than bounded, as the rest are.
    ///
    test('opens in 3D, and says how long it took', async ({ page }) => {
        await openAndTime(page, file, SHAPES);

        await page.evaluate(async () => {
            const THREE = await import('three');
            const original = THREE.Object3D.prototype.add;

            window.__slabsAt = [];

            THREE.Object3D.prototype.add = function (...objects) {
                for (const object of objects) {
                    if (object.userData != null && object.userData.outlines != null)
                        window.__slabsAt.push(performance.now());
                }

                return original.apply(this, objects);
            };
        });

        const pressed = await page.evaluate(() => performance.now());

        await selectView(page, 'View3D');

        await expect.poll(async () => page.evaluate(() => window.__slabsAt.length), { timeout: 240000 }).toBeGreaterThan(0);

        //Until no layer has arrived for two seconds.
        let seen = -1;

        while (true) {
            const now = await page.evaluate(() => window.__slabsAt.length);

            if (now === seen)
                break;

            seen = now;

            await page.waitForTimeout(2000);
        }

        const last = await page.evaluate(() => window.__slabsAt[window.__slabsAt.length - 1]);

        console.log(`3D open: ${Math.round(last - pressed)} ms to the last of ${seen} layer meshes`);

        expect(seen).toBeGreaterThan(0);
    });
});
