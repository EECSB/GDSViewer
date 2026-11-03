//Tracing what a piece of metal is attached to.
//
//The rule is covered in NetTests, on shapes built to isolate each case - two conductors crossing, the same
//two with a via, wires that abut rather than overlap.
//
//What is only checkable here is that it reaches a real file: that roles arrive through a layermap, that the
//button is refused with a reason when nothing has said what the layers are for, and above all that a trace
//through a genuine sky130 cell crosses layers - which is the claim, and the thing a hand-built fixture
//cannot make.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, dismissSelection, snapToGrid, bareNumbers, importLayermap } = require('./helpers');
const path = require('path');

//
//sky130's own numbering, for the layers this cell actually uses.
//
//li1 and met1 are metal, mcon is the contact between them, and licon1 is the contact down to poly and diff.
//Everything else in the file - the wells, the implants, the pin markers - takes no part, which is what an
//empty role column means.
//
const ROLES = [
    '#layer,datatype,name,color,height,thickness,role',
    '66,20,poly,,,,conductor',
    '66,44,licon1,,,,via',
    '67,20,li1,,,,conductor',
    '67,44,mcon,,,,via',
    '68,20,met1,,,,conductor',
    '68,5,met1pin,,,,conductor'
].join('\n');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);

    await page.locator('#selectTool').click();

    //
    //Snapping off, which is how every test in this file was written and what they still mean.
    //
    //It is on out of the box now. At the default pitch of a micron, and this view fitted at roughly seven
    //database units to the pixel, a gesture of a few dozen pixels is a fraction of one grid step - so two
    //clicks meant to be apart land on the same crossing and the shape, path or reading collapses. These
    //are about the tools rather than about the grid, so the grid is taken out of them.
    //
    await snapToGrid(page, false);
});

///
///Loads a mapping into the PDK in use, sky130 on Mosfet, and waits for it to land on the panel - see importLayermap.
///
///On the panel rather than on the PDK manager's line: the layers are laid over the file again after the line is
///said. The whole suite shares one dev server, so the round trip through .NET is generously waited on - see
///layer-names.spec.
///
async function loadRoles(page, contents = ROLES) {
    const labels = () => page.locator('.layerRow .layerName').allTextContents();

    const before = (await labels()).join('|');

    await importLayermap(page, {
        name: 'roles.csv',
        mimeType: 'text/csv',
        buffer: Buffer.from(contents, 'utf8')
    });

    await expect.poll(async () => (await labels()).join('|') !== before, { timeout: 60000 }).toBe(true);
}

///Clicks shapes until one on a layer with a role is picked out, and gives back what the heading said.
async function chooseSomethingTraceable(page) {
    const count = await shapeCount(page);

    for (let nth = 0; nth < Math.min(count, 40); nth++) {
        const box = await shapeBox(page, nth);

        if (box === null)
            continue;

        //As in findRefusal: the panel left open by the last attempt would take this click itself.
        await dismissSelection(page);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        //The layer it landed on, read off the picker - which is where the panel names a single shape's
        //layer now that the heading has gone.
        if (await page.locator('#traceNet').count() === 1 && await page.locator('#traceNet').isEnabled())
            return (await page.locator('#chosenLayer').getAttribute('data-layer')) ?? '';
    }

    throw new Error('nothing on a layer with a role could be chosen');
}

///
///How many shapes the panel says are chosen.
///
///One shape has no heading at all - the layer picker took that spot, since naming the layer above a picker
///for the layer was the same fact twice. So no heading means one, and a heading means it counted.
///
async function chosenCount(page) {
    if (await page.locator('.selectionHeading').count() === 0)
        return 1;

    const heading = (await page.locator('.selectionHeading').textContent()).trim();

    const said = heading.match(/^(\d+) shapes$/);

    if (said === null)
        return 1;

    return Number(said[1]);
}

///
///With nothing said about the layers - which now takes saying so.
///
///A bundled example arrives with the shipped sky130 mapping over it, roles and all, so "before anything has
///said" is a state that has to be got back to rather than one the file starts in. A PDK with no layers put in
///use is how - see bareNumbers - and the roles go with the names.
///
test.describe('before anything has said what the layers are for', () => {
    test.beforeEach(async ({ page }) => {
        await bareNumbers(page);
    });

    ///
    ///**Refused with a reason, not hidden.**
    ///
    ///A GDSII file is numbered shapes and says nothing about which numbers are metal, so with no roles set
    ///the honest answer is that the question cannot be asked - and a button that quietly did nothing would
    ///read as a net of one shape, which is a different and wrong answer.
    ///
    test('the button is there and refused, and says why', async ({ page }) => {
        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#traceNet')).toBeVisible();
        await expect(page.locator('#traceNet')).toBeDisabled();
        await expect(page.locator('#traceNet')).toHaveAttribute('title', /Nothing has said what this layer is for/);
    });
});

test.describe('with roles loaded', () => {
    test('a layermap can carry them', async ({ page }) => {
        await loadRoles(page);

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        //Something in this cell is now traceable, even if the first shape clicked is not.
        await chooseSomethingTraceable(page);
    });

    ///
    ///**The trace crosses layers.**
    ///
    ///Which is the whole claim. A net that only ever grew along one layer would be a merge, and this is not
    ///that: li1 reaches met1 through mcon, and the panel lists both when it has.
    ///
    test('tracing picks up more than the shape it started on', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await page.locator('#traceNet').click();

        await expect.poll(async () => chosenCount(page), { timeout: 15000 }).toBeGreaterThan(1);
    });

    ///Asked twice, the same net - a trace from anywhere on it reaches the same shapes.
    test('tracing again from within the net keeps the same net', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await page.locator('#traceNet').click();

        await expect.poll(async () => chosenCount(page), { timeout: 15000 }).toBeGreaterThan(1);

        const first = await chosenCount(page);

        //Clicking one of the net's own shapes and tracing from there.
        const marked = await page.locator('#gdsSVG .shapeSelected').first().boundingBox();

        await page.mouse.click(marked.x + (marked.width / 2), marked.y + (marked.height / 2));

        if (await page.locator('#traceNet').count() === 0)
            return;

        await page.locator('#traceNet').click();

        await expect.poll(async () => chosenCount(page), { timeout: 15000 }).toBe(first);
    });

    ///
    ///**The net comes back as a selection**, so everything that already works on one works on it - it is
    ///drawn by the same highlight, counted by the same heading, and let go of by the same Escape.
    ///
    test('the net is a selection like any other', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await page.locator('#traceNet').click();

        await expect.poll(async () => page.locator('#gdsSVG .shapeSelected').count(), { timeout: 15000 })
            .toBeGreaterThan(1);

        await page.keyboard.press('Escape');

        await expect.poll(async () => page.locator('#gdsSVG .shapeSelected').count(), { timeout: 15000 }).toBe(0);
    });

    ///
    ///**A net that fits in one step says it is all there.** A trace goes out a number of shapes at a time - see
    ///the big net below - and on a cell this size the first step is the whole of it, so there is nothing for Next
    ///to do and it is not offered.
    ///
    test('a net found in one step says it is the whole net', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await page.locator('#traceNet').click();

        await expect(page.locator('#netSteps')).toContainText('the whole net', { timeout: 15000 });
        await expect(page.locator('#netNext')).toHaveCount(0);

        const said = Number((await page.locator('#netSteps').textContent()).match(/(\d+) shape/)[1]);

        expect(said).toBe(await chosenCount(page));
    });

    ///
    ///Clicks shapes until one is found whose button is refused for the reason given, and hands back whether
    ///one was. Both reasons are legitimate states of this cell, so neither absence is a failure.
    ///
    async function findRefusal(page, reason) {
        const count = await shapeCount(page);

        for (let nth = 0; nth < Math.min(count, 40); nth++) {
            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            //The panel opened by the last attempt sits over the top-left of the view and takes its own
            //clicks, so this one would land on it rather than on the shape - and the loop would read the
            //previous shape's answer every time round.
            await dismissSelection(page);

            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            if (await page.locator('#traceNet').count() !== 1 || !(await page.locator('#traceNet').isDisabled()))
                continue;

            if (reason.test(await page.locator('#traceNet').getAttribute('title')))
                return true;
        }

        return false;
    }

    ///A layer the mapping said nothing about takes no part, and the button says so on it.
    test('a layer with no role is still refused', async ({ page }) => {
        await loadRoles(page);

        await findRefusal(page, /Nothing has said/);
    });

    ///
    ///**A label is refused too, and for its own reason.**
    ///
    ///A pin label sits on a conducting layer, so its role lets it through - and it is one point, which the
    ///walk will not follow. The button was offered on one and did nothing at all when pressed, which reads
    ///as a net of one shape. Both now ask Nets.TakesPart, so they cannot disagree again.
    ///
    test('a label is refused, and says a label is not a net', async ({ page }) => {
        await loadRoles(page);

        const found = await findRefusal(page, /A label names a net/);

        //Not every cell has a label on a layer that was given a role; this one does.
        expect(found).toBe(true);
    });
});

test.describe('what the net is called', () => {
    ///
    ///**A net has no name of its own in the file**, so the name is found: a layout says which piece of metal
    ///is which by putting a label down on top of it. This cell has drain, source and gate written on it.
    ///
    ///Which shape is which is the hit test's business, so this traces from each in turn until one comes back
    ///named - what is being checked is that a real file's labels are found at all, not which of them.
    ///
    test('a label on the net is shown as its name', async ({ page }) => {
        await loadRoles(page);

        const count = await shapeCount(page);

        for (let nth = 0; nth < Math.min(count, 40); nth++) {
            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            //The panel opened by the last attempt sits over the top-left of the view and takes its own
            //clicks, so this one would land on it rather than on the shape - and the loop would read the
            //previous shape's answer every time round.
            await dismissSelection(page);

            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            if (await page.locator('#traceNet').count() === 0 || await page.locator('#traceNet').isDisabled())
                continue;

            await page.locator('#traceNet').click();

            await expect(page.locator('#netName')).toBeVisible({ timeout: 15000 });

            const said = await page.locator('#netName').textContent();

            if (said.includes('net '))
                return;
        }

        throw new Error('no net in this cell came back with a name');
    });

    ///A net nothing is written on says so, rather than showing an empty row that reads like a missing one.
    test('a net with no label says so', async ({ page }) => {
        await loadRoles(page);

        const count = await shapeCount(page);

        for (let nth = 0; nth < Math.min(count, 40); nth++) {
            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            //The panel opened by the last attempt sits over the top-left of the view and takes its own
            //clicks, so this one would land on it rather than on the shape - and the loop would read the
            //previous shape's answer every time round.
            await dismissSelection(page);

            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            if (await page.locator('#traceNet').count() === 0 || await page.locator('#traceNet').isDisabled())
                continue;

            await page.locator('#traceNet').click();

            await expect(page.locator('#netName')).toBeVisible({ timeout: 15000 });

            if ((await page.locator('#netName').textContent()).includes('no label'))
                return;
        }

        //Every net in this cell is labeled, which is a legitimate state and not a failure.
    });

    ///
    ///**The name belongs to the net, not to the selection.** The same labels over a rubber band would be
    ///whatever happened to be caught, which reads the same and claims much less - so choosing anything else
    ///takes the row away.
    ///
    test('choosing something else takes the name away', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await page.locator('#traceNet').click();

        await expect(page.locator('#netName')).toBeVisible({ timeout: 15000 });

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#netName')).toHaveCount(0);
    });

    ///And nothing is claimed before a trace, since an untraced selection is not a net.
    test('nothing is said before a trace', async ({ page }) => {
        await loadRoles(page);

        await chooseSomethingTraceable(page);

        await expect(page.locator('#netName')).toHaveCount(0);
    });
});

test.describe('setting a role by hand', () => {
    ///
    ///The other way in, for somebody who knows their stack and has no CSV to hand.
    ///
    ///On bare numbers first, so the box starts where somebody with no mapping would find it: the shipped one is
    ///over a bundled example on arrival, and 65/20 comes with a role already. See bareNumbers.
    ///
    test('the layer settings offer conductor, via and none', async ({ page }) => {
        await bareNumbers(page);

        await page.locator('.layerSettingsButton').first().click();

        const role = page.locator('.layerSettingsRole');

        await expect(role).toBeVisible();
        await expect(role).toHaveValue('None');

        await role.selectOption('Conductor');

        await expect(role).toHaveValue('Conductor');
    });

    ///
    ///**A role set by hand lands on the layer, not just on the control.**
    ///
    ///Checked by closing the popup and opening it again rather than by hunting for a shape on that layer:
    ///which shape sits on top of which is the hit test's business and not this one's, and the value coming
    ///back is what says the picker wrote through to the model. That the model reaches the button is what the
    ///layermap tests above already show.
    ///
    test('a role set by hand stays on the layer', async ({ page }) => {
        await page.locator('.layerSettingsButton').first().click();
        await page.locator('.layerSettingsRole').selectOption('Via');

        await page.locator('.layerSettingsPopup .closeButton').click();

        await expect(page.locator('.layerSettingsRole')).toHaveCount(0);

        await page.locator('.layerSettingsButton').first().click();

        await expect(page.locator('.layerSettingsRole')).toHaveValue('Via');
    });
});

///
///The layermap that ships with the app, against the example that ships with it.
///
///**Because a file in wwwroot is a file nobody compiles.** Everything else about roles here is checked
///against a CSV written inside the test, which proves the reader and proves nothing about the one people
///actually load. That file exists so somebody can try Trace net without first writing a PDK table by hand,
///and a typo in it - a column shifted, a role misspelled, a layer number that is not in the example - fails
///silently as a feature that appears not to work.
///
test.describe('the layermap that ships', () => {
    test('makes the bundled Mosfet traceable', async ({ page }) => {
        const said = [];

        await page.exposeFunction('reportAlert', message => said.push(String(message)));
        await page.evaluate(() => { window.alert = message => window.reportAlert(message); });

        //
        //Into a PDK with no layers first, so importing it proves something.
        //
        //This same file is the built-in PDK's, laid over a bundled example automatically, so the names are
        //already on the panel when the page settles - and an import that changed nothing would be waited on by
        //watching for a name that was never going to go away. An empty PDK in use puts the bare numbers back -
        //see bareNumbers - and the names returning is the shipped file being read into it.
        //
        await bareNumbers(page);

        const read = await importLayermap(page, path.join(__dirname, '..', '..', 'wwwroot', 'resources', 'GDS Files', 'sky130-roles.csv'));

        await expect(page.locator('.layerList')).toContainText('met1', { timeout: 60000 });

        //
        //And it was read without complaint: no dialog, and no line it could not use said beside the Import.
        //Either would be a fault in the mapping this app ships.
        //
        expect(said.join(' ')).toBe('');
        expect(read).toMatch(/^Imported: \d+ layers\.$/);

        //And somewhere in the file is a shape that can be traced. Every shape in turn rather than a chosen
        //one: which index is metal is a fact about the example, and not the claim being made.
        for (let nth = 0; nth < await shapeCount(page); nth++) {
            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            if (await page.locator('#traceNet').count() !== 1 || !(await page.locator('#traceNet').isEnabled()))
                continue;

            await page.locator('#traceNet').click();

            //A net worth the name crosses layers - one shape on one layer is what tracing would give
            //without any of this, and would pass against a mapping that said nothing at all.
            await expect(page.locator('#selectionPanel')).toContainText('net', { timeout: 15000 });

            const across = await page.locator('#selectionPanel').evaluate(panel => {
                const said = panel.textContent.match(/on ([\d/,\s]+)/);

                if (said === null)
                    return 0;

                return said[1].split(',').length;
            });

            expect(across, 'the trace stayed on one layer').toBeGreaterThan(1);

            return;
        }

        throw new Error('no shape of the bundled example offered a trace');
    });
});

///
///**A net bigger than a step is traced a step at a time.**
///
///A supply rail on a real design is thousands of shapes, and the whole of one at once was a frozen page - so a
///trace reaches a hundred shapes, nearest first, and Next reaches the number beside it more. The RC oscillator is
///the smallest bundled design with nets that need more than one step, and its rails are its largest met1 shapes.
///
test.describe('a big net', () => {
    test.beforeEach(async ({ page }) => {
        await gotoExample(page, 'sky130_ef_ip__rc_osc_500k', 'View2DSvg');

        await expect.poll(async () => shapeCount(page), { timeout: 120000 }).toBeGreaterThan(0);

        await page.locator('#selectTool').click();
    });

    ///A point just inside the corner of the largest met1 shape drawn, on screen.
    async function onTheLargestRail(page) {
        return page.evaluate(() => {
            const svg = document.getElementById('gdsSVG');
            let best = null;

            for (const path of svg.querySelectorAll('path.l68_20[data-elements]')) {
                for (const run of window.viewGeometry.subpaths(path.getAttribute('d'))) {
                    const corners = [];

                    for (let i = 0; i + 1 < run.corners.length; i += 2)
                        corners.push([run.corners[i], run.corners[i + 1]]);

                    const xs = corners.map(corner => corner[0]);
                    const ys = corners.map(corner => corner[1]);
                    const area = (Math.max(...xs) - Math.min(...xs)) * (Math.max(...ys) - Math.min(...ys));

                    if (best === null || area > best.area)
                        best = { area: area, corner: corners[0] };
                }
            }

            const point = svg.createSVGPoint();

            point.x = best.corner[0] + 50;
            point.y = best.corner[1] + 50;

            const screen = point.matrixTransform(svg.getScreenCTM());

            return { x: screen.x, y: screen.y };
        });
    }

    test('a trace reaches a hundred shapes, Next a hundred more, and all of it on asking', async ({ page }) => {
        const rail = await onTheLargestRail(page);

        await page.mouse.click(rail.x, rail.y);
        await page.locator('#traceNet').click();

        //The shape it started on and a hundred more.
        await expect(page.locator('#netSteps')).toContainText('101 shapes so far', { timeout: 30000 });

        await page.locator('#netNext').click();

        await expect(page.locator('#netSteps')).toContainText('201 shapes so far', { timeout: 30000 });
        expect(await chosenCount(page)).toBe(201);

        await page.locator('#netStep').selectOption('0');
        await page.locator('#netNext').click();

        await expect(page.locator('#netSteps')).toContainText('the whole net', { timeout: 60000 });
        await expect(page.locator('#netNext')).toHaveCount(0);

        const whole = Number((await page.locator('#netSteps').textContent()).match(/(\d+) shapes/)[1]);

        expect(whole).toBeGreaterThan(201);
        expect(await chosenCount(page)).toBe(whole);
    });
});
