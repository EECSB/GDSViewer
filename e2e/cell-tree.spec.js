//The file's cells as a tree, docked down the side of the view.
//
//The shape of the tree is covered in HierarchyTests - what a shared cell, a loop and an unreachable cell come
//out as, and in what order. Nothing here re-tests that.
//
//What is only checkable in a browser is the panel itself: that it is open on arrival and stays open with the
//pointer somewhere else, that it takes the left of the view without landing on the panel that already starts
//there, that a row reaches the cell it names, and that it comes back a session later.
//
//It replaced a popup under a book in the toolbar, whose specs were cell-list.spec - three of them are here,
//marked, because they covered cases nothing else does.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, shapeCount, shapeBox } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg', true);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///
///Makes sure the tree is open, which it is by default - so this presses the button only if it is not.
///
///It used to open shut and every test began by pressing. Pressing an open one closes it.
///
async function openTree(page) {
    if (await page.locator('#cellTree').count() === 0)
        await page.locator('#cellTreeButton').click();

    await expect(page.locator('#cellTree')).toBeVisible();
}

///
///The cell rows of the docked tree.
///
///Scoped to the kind, because the tree grew a layer level and a row of it is as likely to be a layer as a
///cell - and to the panel, since `.cellRow` is a class rather than an id.
///
function rows(page) {
    return page.locator('#cellTree .cellRowPair[data-kind="cell"]');
}

///Enters a cell the ordinary way, and groups what is in it - which is how a file gets a second level.
async function makeALevel(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    //The first click enters the cell; the second chooses a shape in it.
    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#makeCell')).toBeEnabled();

    await page.locator('#makeCell').click();

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Make cell/, { timeout: 15000 });
}

test.describe('the button', () => {
    ///
    ///Open on arrival, like the layer list opposite it.
    ///
    ///It started shut, which for a panel that is the file explorer of this app meant most people never found
    ///it. Both sidebars are up when the app opens now, and the buttons are how either is put away.
    ///
    test('it is open when the app opens, with its button lit', async ({ page }) => {
        await expect(page.locator('#cellTreeButton')).toBeVisible();
        await expect(page.locator('#cellTree')).toBeVisible();
        await expect(page.locator('#cellTreeButton')).toHaveClass(/toolButtonOn/);
    });

    ///
    ///One of each, which is not as obvious as it sounds.
    ///
    ///The layer switch was added twice - once beside full screen and once in this pair - and the second did
    ///not replace the first, so the bar carried two buttons with one id and the page had a duplicate id in
    ///it. Nothing caught it because nothing had ever clicked that button by name.
    ///
    test('there is one of each switch, not two', async ({ page }) => {
        await expect(page.locator('#cellTreeButton')).toHaveCount(1);
        await expect(page.locator('#layersToggle')).toHaveCount(1);

        //Side by side, in one group.
        const together = await page.evaluate(() => {
            const cells = document.querySelector('#cellTreeButton');
            const layers = document.querySelector('#layersToggle');

            return {
                sameGroup: cells.parentElement === layers.parentElement,
                touching: Math.abs(layers.getBoundingClientRect().left - cells.getBoundingClientRect().right) < 1
            };
        });

        expect(together.sameGroup).toBe(true);
        expect(together.touching).toBe(true);
    });

    ///
    ///Off in the 3D view, because the tree is the 2D view's to draw.
    ///
    ///Greyed rather than taken away: a button that vanishes leaves somebody wondering whether the app can do
    ///the thing at all, and it would leave a single button where the pair beside it was.
    ///
    test('it is greyed out in the 3D view, and the layer switch is not', async ({ page }) => {
        await page.locator('[data-view="View3D"]').click();

        await expect(page.locator('#cellTreeButton')).toBeDisabled();

        //Still there, and still saying why.
        await expect(page.locator('#cellTreeButton')).toBeVisible();
        await expect(page.locator('#cellTreeButton')).toHaveAttribute('title', /2D view/);

        //The layer list stands beside the 3D view, so its switch keeps working.
        await expect(page.locator('#layersToggle')).toBeEnabled();
    });

    ///
    ///**It stays.** Which is the whole reason for a second way to the same list.
    ///
    ///The popup this replaced closed the moment the pointer left the tools column - right for glancing at a
    ///name, useless for reading a hierarchy while working. This one is closed by a press and by nothing else,
    ///so the pointer going to the layout leaves it where it is.
    ///
    test('it stays open with the pointer somewhere else', async ({ page }) => {
        await openTree(page);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width * 0.8), view.y + (view.height * 0.8));

        await expect(page.locator('#cellTree')).toBeVisible();
    });

    test('a second press puts it away', async ({ page }) => {
        await openTree(page);

        await page.locator('#cellTreeButton').click();

        await expect(page.locator('#cellTree')).toHaveCount(0);
        await expect(page.locator('#cellTreeButton')).not.toHaveClass(/toolButtonOn/);
    });

    test('and so does the cross on the panel', async ({ page }) => {
        await openTree(page);

        await page.locator('#cellTreeClose').click();

        await expect(page.locator('#cellTree')).toHaveCount(0);
    });
});

test.describe('where it sits', () => {
    ///
    ///Down the left of the *view*, not of the window.
    ///
    ///The layer sidebar has the right and the page has its own margins; a panel placed against the window
    ///would be over one or the other. Measured against #svgWrapper for that reason.
    ///
    test('it takes the left of the view, top to bottom', async ({ page }) => {
        await openTree(page);

        const where = await page.evaluate(() => {
            const tree = document.querySelector('#cellTree').getBoundingClientRect();
            const view = document.querySelector('#svgWrapper').getBoundingClientRect();

            return {
                fromLeft: tree.left - view.left,
                fromRight: view.right - tree.right,
                ofTheHeight: tree.height / view.height
            };
        });

        //Against the left edge, and nowhere near the right one.
        expect(where.fromLeft).toBeLessThan(20);
        expect(where.fromRight).toBeGreaterThan(100);

        //Most of the view's height, which is what makes a long library readable in it.
        expect(where.ofTheHeight).toBeGreaterThan(0.85);
    });

    ///
    ///The selection panel starts in the same corner, and moves rather than being covered.
    ///
    ///Both are the view's own furniture and both open at top left. The tree is the one that stays, so the
    ///panel is what steps aside - and it has to still be inside the view afterwards, which is the half that
    ///a fixed offset gets wrong on a narrow window.
    ///
    test('the selection panel steps aside rather than under it', async ({ page }) => {
        await openTree(page);

        await page.locator('#selectTool').click();

        const shape = await shapeBox(page);

        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
        await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();

        const clear = await page.evaluate(() => {
            const tree = document.querySelector('#cellTree').getBoundingClientRect();
            const panel = document.querySelector('#selectionPanel').getBoundingClientRect();
            const view = document.querySelector('#svgWrapper').getBoundingClientRect();

            return {
                past: panel.left - tree.right,
                insideTheView: panel.right <= view.right + 1
            };
        });

        expect(clear.past).toBeGreaterThanOrEqual(0);
        expect(clear.insideTheView).toBe(true);
    });
});

test.describe('what it shows', () => {
    test('the file\'s cells, one row each', async ({ page }) => {
        await openTree(page);

        await expect(rows(page)).toHaveCount(1);

        //The cell row's own name. Every layer row carries one too.
        await expect(rows(page).locator('.cellRowName')).toHaveText('mosfet');
    });

    ///
    ///Indented under what places it, which is the point of drawing it as a tree at all.
    ///
    ///Mosfet is one flat cell, so a second level has to be made here rather than found - grouping a shape
    ///into a cell writes the placement that puts one under the other.
    ///
    test('what a cell places is indented under it', async ({ page }) => {
        await openTree(page);

        await makeALevel(page);

        await expect(rows(page)).toHaveCount(2);

        const shape = await page.evaluate(() => [...document.querySelectorAll('#cellTree .cellRowPair[data-kind="cell"]')].map(row => ({
            depth: row.dataset.depth,
            indent: parseFloat(getComputedStyle(row).paddingLeft),
            name: row.querySelector('.cellRowName').textContent,
            folds: row.querySelector('.cellRowFold') !== null,
            guide: getComputedStyle(row, '::before').width
        })));

        //The top at the margin, and what it places a level in.
        expect(shape[0].depth).toBe('0');
        expect(shape[0].indent).toBe(0);

        expect(shape[1].depth).toBe('1');
        expect(shape[1].indent).toBeGreaterThan(0);

        //Drawn as an indent, not only recorded as an attribute.
        expect(shape[1].indent).toBeGreaterThan(shape[0].indent);

        //And both say what is under them: the top places a cell, the cell below draws on a layer. A
        //chevron that turns rather than a character, so what is checked is that the control is there.
        expect(shape[0].folds).toBe(true);
        expect(shape[1].folds).toBe(true);

        //The guide lines that make an indent read as a tree: one level of them under the root, none at it.
        expect(parseFloat(shape[0].guide)).toBe(0);
        expect(parseFloat(shape[1].guide)).toBeGreaterThan(0);
    });

    ///
    ///The cell under the pointer, drawn, the way the library draws it.
    ///
    ///Under the rows rather than beside them: the library's picture hangs off the side because that panel is
    ///pinned to the right of the view with the whole canvas to grow into, where this one is the left edge and
    ///a picture growing right from it would cross the layout being looked at.
    ///
    test('pointing at a row draws the cell', async ({ page }) => {
        await openTree(page);

        //The frame is there before anything is pointed at, holding its place and saying what it is for.
        await expect(page.locator('#cellTreePreview')).toBeVisible();
        await expect(page.locator('#cellTreePreview')).toHaveText(/Point at a cell/);

        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellTreePreview svg.cellPreview')).toBeVisible({ timeout: 15000 });

        //A drawing of the cell, not an empty frame.
        await expect.poll(async () => page.locator('#cellTreePreview svg.cellPreview path, #cellTreePreview svg.cellPreview polygon').count(),
            { timeout: 15000 }).toBeGreaterThan(0);

        const fits = await page.evaluate(() => {
            const frame = document.querySelector('#cellTreePreview').getBoundingClientRect();
            const tree = document.querySelector('#cellTree').getBoundingClientRect();

            return {
                inside: frame.left >= tree.left - 1 && frame.right <= tree.right + 1 && frame.bottom <= tree.bottom + 1,
                square: Math.abs(frame.width - frame.height) < frame.width * 0.2
            };
        });

        expect(fits.inside).toBe(true);
        expect(fits.square).toBe(true);
    });

    ///
    ///**The lens, which borrows room rather than taking it.**
    ///
    ///The panel is a column and the drawing in it is as big as a thumbnail gets - enough to tell two cells
    ///apart and not enough to read one. The lens opens a bigger square beside the panel, over the layout,
    ///and it is gone the moment the pointer leaves - so nothing is covered for longer than it is looked at.
    ///
    test('the lens opens a bigger picture beside the panel', async ({ page }) => {
        await openTree(page);

        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellPreviewLens')).toBeVisible({ timeout: 15000 });

        //Nothing until it is pointed at.
        await expect(page.locator('#cellPreviewLarge')).toHaveCount(0);

        await page.locator('#cellPreviewLens').hover();

        await expect(page.locator('#cellPreviewLarge')).toBeVisible();

        const bigger = await page.evaluate(() => {
            const big = document.querySelector('#cellPreviewLarge').getBoundingClientRect();
            const small = document.querySelector('#cellTreePreview').getBoundingClientRect();
            const tree = document.querySelector('#cellTree').getBoundingClientRect();

            return {
                times: (big.width * big.height) / (small.width * small.height),
                pastThePanel: big.left - tree.right,
                drawn: document.querySelectorAll('#cellPreviewLarge path, #cellPreviewLarge polygon').length,

                //Whether it is still on screen, which is the whole reason its size is a min() of three
                //things rather than a number.
                spare: [window.innerWidth - big.right, window.innerHeight - big.bottom, big.top]
            };
        });

        //
        //Worth opening: several times the thumbnail it came out of, rather than merely bigger than it.
        //
        //Three and a bit by area, which is a little under twice as wide. The number is between what the
        //lens gave at 340px across - 2.3 - and what it gives now, so it is the growth that is pinned rather
        //than the measurement of the day.
        //
        expect(bigger.times).toBeGreaterThan(3.2);

        //Beside the panel rather than inside it, which is where the room is. To the pixel, since the
        //offset is measured off a border and comes out a fraction either side of it.
        expect(bigger.pastThePanel).toBeGreaterThan(-2);

        //And it is the cell, not an empty frame.
        expect(bigger.drawn).toBeGreaterThan(0);

        //Inside the window on all three sides it could leave by. It is sized off the viewport for exactly
        //this, and a fixed number would have run off the bottom of a short one.
        for (const edge of bigger.spare)
            expect(edge).toBeGreaterThan(-1);
    });

    test('and the bigger picture goes when the pointer leaves the lens', async ({ page }) => {
        await openTree(page);

        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellPreviewLens')).toBeVisible({ timeout: 15000 });

        await page.locator('#cellPreviewLens').hover();

        await expect(page.locator('#cellPreviewLarge')).toBeVisible();

        //Back onto the row, which is off the lens.
        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellPreviewLarge')).toHaveCount(0);

        //The thumbnail it came out of is still there.
        await expect(page.locator('#cellTreePreview svg.cellPreview')).toBeVisible();
    });

    ///
    ///The lit row does not sit on the rule under the heading.
    ///
    ///The highlight is a filled band the width of the panel, and butted against the line it covered it - the
    ///heading lost its underline exactly when a cell at the top of the file was the one being edited.
    ///
    test('the first row clears the rule under the heading', async ({ page }) => {
        await openTree(page);

        const clear = await page.evaluate(() => {
            const rule = document.querySelector('#cellTree .sidebarTitle').getBoundingClientRect();
            const rows = document.querySelector('.cellTreeRows').getBoundingClientRect();

            return rows.top - rule.bottom;
        });

        expect(clear).toBeGreaterThan(2);
    });

    ///
    ///And the panel is one height whether or not anything is under the pointer.
    ///
    ///A picture that pushed the rows down would be the pointer moving the thing it is pointing at - point at
    ///a row, the row moves, and you are now pointing at a different one.
    ///
    test('the picture takes the rows\' space rather than adding to it', async ({ page }) => {
        await openTree(page);

        const before = {
            panel: await page.locator('#cellTree').boundingBox(),
            rows: await page.locator('.cellTreeRows').boundingBox(),
            frame: await page.locator('#cellTreePreview').boundingBox()
        };

        await page.locator('#cellTree .cellRow').first().hover();

        await expect(page.locator('#cellTreePreview svg.cellPreview')).toBeVisible({ timeout: 15000 });

        const after = {
            panel: await page.locator('#cellTree').boundingBox(),
            rows: await page.locator('.cellTreeRows').boundingBox(),
            frame: await page.locator('#cellTreePreview').boundingBox()
        };

        //Nothing moves. The frame was already holding its place, so what changed is what is drawn in it.
        expect(Math.abs(after.panel.height - before.panel.height)).toBeLessThan(2);
        expect(Math.abs(after.rows.height - before.rows.height)).toBeLessThan(2);
        expect(Math.abs(after.frame.y - before.frame.y)).toBeLessThan(2);
    });

    ///Carried over from cell-list.spec, which went with the popup it tested.
    test('a row says what is in the cell and what places it', async ({ page }) => {
        await openTree(page);

        const row = rows(page).locator('.cellRow').first();

        await expect(row.locator('.cellRowCounts')).toContainText('top');
        await expect(row).toHaveAttribute('title', /Nothing places it/);
    });

    ///
    ///Two cells are drawn as themselves, not as one picture reused.
    ///
    ///Which is the whole claim of a preview: the frame is a fixed square and the drawing fits itself to it,
    ///so what says the two are different is the viewBox each one brings.
    ///
    test('two cells are drawn differently', async ({ page }) => {
        await openTree(page);

        await makeALevel(page);

        const frames = [];

        for (const name of ['mosfet', 'CELL']) {
            await rows(page).filter({ hasText: name }).first().locator('.cellRow').hover();

            await expect(page.locator('#cellTreePreview svg.cellPreview')).toHaveAttribute('aria-label', `Drawing of ${name}`,
                { timeout: 15000 });

            frames.push(await page.locator('#cellTreePreview svg.cellPreview').getAttribute('viewBox'));
        }

        expect(frames[0]).not.toBe(frames[1]);
    });

    ///
    ///**A cell with nothing in it can still be opened.**
    ///
    ///The case every other route misses, and the reason a list of cells exists at all: every other way into
    ///a cell starts from clicking a shape, and an empty cell has none. Carried over from cell-list.spec.
    ///
    test('an empty cell can be opened and drawn into', async ({ page }) => {
        await openTree(page);

        await makeALevel(page);

        //Into CELL, and take everything out of it.
        await rows(page).filter({ hasText: 'CELL' }).first().locator('.cellRow').click();

        await expect.poll(async () => (await page.locator('.contextCrumbOn').first().textContent()).trim(),
            { timeout: 15000 }).toBe('CELL');

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + view.height - 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + 5, { steps: 10 });
        await page.mouse.up();

        await expect(page.locator('#deleteShape')).toBeVisible();

        await page.locator('#deleteShape').click();

        //In this cell. The rest of the file still draws whatever it drew.
        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(0);

        //Nothing left to click anywhere, and the tree is still the way in.
        const empty = rows(page).filter({ hasText: 'CELL' }).first();

        await expect(empty.locator('.cellRowCounts')).toContainText('0');

        await empty.locator('.cellRow').click();

        await expect.poll(async () => (await page.locator('.contextCrumbOn').first().textContent()).trim(),
            { timeout: 15000 }).toBe('CELL');

        //And it can be drawn into, which is the whole reason to be able to open it.
        await page.locator('#drawTool').click();

        const into = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(into.x + 120, into.y + 120);
        await page.mouse.down();
        await page.mouse.move(into.x + 260, into.y + 240, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page, 'inContext'), { timeout: 15000 }).toBe(1);
    });

    test('a row opens that cell, and the tree marks the open one', async ({ page }) => {
        await openTree(page);

        await makeALevel(page);

        await page.locator('#cellTree .cellRow').filter({ hasText: 'CELL' }).click();

        await expect.poll(async () => (await page.locator('.contextCrumbOn').first().textContent()).trim(),
            { timeout: 15000 }).toBe('CELL');

        const lit = page.locator('#cellTree .cellRowOn .cellRowName');

        await expect(lit).toHaveCount(1);
        await expect(lit).toHaveText('CELL');
    });
});

///
///The two levels under a cell: what it draws on, and what it draws there.
///
///The shape of all three is covered in HierarchyTests - which layers a cell owns, what a shared cell and a
///loop come out as, and where the cap on shapes falls. What is only checkable here is that the twisty is a
///control rather than a mark, and that a shape row reaches the shape it names.
///
test.describe('layers and shapes', () => {
    ///Rows of one kind, in order.
    function ofKind(page, kind) {
        return page.locator(`#cellTree .cellRowPair[data-kind="${kind}"]`);
    }

    test('a cell lists the layers it draws on', async ({ page }) => {
        await openTree(page);

        //Mosfet draws on several, and each says how many shapes are on it.
        await expect(ofKind(page, 'layer').first()).toBeVisible();

        const layers = await ofKind(page, 'layer').evaluateAll(rows => rows.map(row => ({
            depth: row.dataset.depth,
            says: row.querySelector('.cellRowName').textContent.trim(),
            count: Number(row.querySelector('.cellRowCounts').textContent.trim()),
            swatch: getComputedStyle(row.querySelector('.layerSwatch')).backgroundColor
        })));

        expect(layers.length).toBeGreaterThan(1);

        //A level in from the cell above them.
        expect(layers.every(layer => layer.depth === '1')).toBe(true);

        //Named the way the rest of the app names a layer, counted, and drawn in its own color.
        expect(layers[0].says).toMatch(/\d+\/\d+/);
        expect(layers[0].count).toBeGreaterThan(0);
        expect(layers[0].swatch).not.toBe('rgba(0, 0, 0, 0)');
    });

    ///
    ///Shut until asked, unlike the cells.
    ///
    ///The level that can be a hundred thousand rows: a cell of forty thousand boundaries is a real file, and
    ///a tree that drew them all on sight would take the page down. So the cells are the tree you get and the
    ///shapes are the one you ask for.
    ///
    test('the shapes on a layer are shut until its twisty is pressed', async ({ page }) => {
        await openTree(page);

        await expect(ofKind(page, 'shape')).toHaveCount(0);

        const layer = ofKind(page, 'layer').first();
        const on = Number(await layer.locator('.cellRowCounts').textContent());

        await layer.locator('.cellRowFold').click();

        await expect(ofKind(page, 'shape')).toHaveCount(on);

        //A level in again, under the layer they are on.
        const depths = await ofKind(page, 'shape').evaluateAll(rows => rows.map(row => row.dataset.depth));

        expect(depths.every(depth => depth === '2')).toBe(true);

        //And pressing it again puts them away.
        await layer.locator('.cellRowFold').click();

        await expect(ofKind(page, 'shape')).toHaveCount(0);
    });

    ///A shape says what it is and where it starts, which is something to look for on screen.
    test('a shape row says what kind it is and where', async ({ page }) => {
        await openTree(page);

        await ofKind(page, 'layer').first().locator('.cellRowFold').click();

        await expect(ofKind(page, 'shape').first()).toBeVisible();

        const says = await ofKind(page, 'shape').first().locator('.cellRowName').textContent();

        expect(says).toMatch(/^(boundary|path|label|box|node) /);
    });

    ///
    ///**Pressing a shape finds it in the layout and chooses it.**
    ///
    ///Which is what the level is for. The tree carries the file's own element and the flattener hangs one on
    ///every shape it draws, so the two meet at the object itself rather than at a count the two would have
    ///to agree about.
    ///
    test('pressing a shape chooses it in the layout', async ({ page }) => {
        await openTree(page);

        await expect(page.locator('#selectionPanel')).toHaveCount(0);

        //A layer with geometry on it rather than labels, so what is chosen has an area to report.
        const layers = ofKind(page, 'layer');

        for (let nth = 0; nth < await layers.count(); nth++)
            await layers.nth(nth).locator('.cellRowFold').click();

        const shape = ofKind(page, 'shape').filter({ hasText: 'boundary' }).first();

        await expect(shape).toBeVisible();

        await shape.locator('.cellRow').click();

        //The panel opens on it, and one shape is marked in the view.
        await expect(page.locator('#selectionPanel')).toBeVisible({ timeout: 15000 });

        await expect.poll(async () => page.locator('#gdsSVG .shapeSelected, #gdsSVG #chosenShapes *').count(),
            { timeout: 15000 }).toBeGreaterThan(0);
    });

    ///
    ///A cell folds away everything under it - its layers and what it places alike.
    ///
    ///Which is the twisty becoming a control. In the library popup it is a mark and nothing folds; here a
    ///press on it is how a big library is made readable.
    ///
    test('folding a cell takes its layers away with it', async ({ page }) => {
        await openTree(page);

        const before = await page.locator('#cellTree .cellRowPair').count();

        expect(before).toBeGreaterThan(1);

        const cell = ofKind(page, 'cell').first();

        await cell.locator('.cellRowFold').click();

        await expect(page.locator('#cellTree .cellRowPair')).toHaveCount(1);

        //The row itself stays, and says it is shut.
        await expect(cell.locator('.cellRowFold')).toHaveAttribute('aria-expanded', 'false');

        await cell.locator('.cellRowFold').click();

        await expect(page.locator('#cellTree .cellRowPair')).toHaveCount(before);
        await expect(cell.locator('.cellRowFold')).toHaveAttribute('aria-expanded', 'true');
    });
});

///
///Both sidebars can be dragged wider or narrower, and neither remembers it.
///
///The drag is JavaScript rather than Blazor - a pointer event every few milliseconds through C# would be a
///render of a nine-hundred-row panel to move a border one pixel - so what is checkable here is the whole of
///it: the grabber is there, dragging moves the edge by what the pointer moved, and shutting the panel throws
///the width away.
///
test.describe('dragging a sidebar', () => {
    ///
    ///Drags a grabber by a number of pixels and hands back the panel's width before and after.
    ///
    ///page.mouse rather than dispatched events, so this goes through the same capture and the same listeners
    ///a hand does.
    ///
    async function dragBy(page, panel, pixels) {
        const grabber = page.locator(`${panel} .sidebarGrabber`);

        await expect(grabber).toBeAttached();

        const was = (await page.locator(panel).boundingBox()).width;
        const at = await grabber.boundingBox();

        await page.mouse.move(at.x + (at.width / 2), at.y + (at.height / 2));
        await page.mouse.down();
        await page.mouse.move(at.x + (at.width / 2) + pixels, at.y + (at.height / 2), { steps: 5 });
        await page.mouse.up();

        return { was, now: (await page.locator(panel).boundingBox()).width };
    }

    test('the cell tree widens as the pointer goes right', async ({ page }) => {
        await openTree(page);

        const sized = await dragBy(page, '#cellTree', 90);

        expect(sized.now - sized.was).toBeGreaterThan(70);
        expect(sized.now - sized.was).toBeLessThan(110);
    });

    ///The other way round, because it is the other edge: a right-hand panel grows as the pointer goes left.
    test('the layer list widens as the pointer goes left', async ({ page }) => {
        const sized = await dragBy(page, '#layerSidebar', -80);

        expect(sized.now - sized.was).toBeGreaterThan(60);
        expect(sized.now - sized.was).toBeLessThan(100);
    });

    ///
    ///**Shut it and open it, and it is the width it started at.**
    ///
    ///Which is the whole of what is remembered: a drag is a decision about the layout in front of you now,
    ///where a session is about the next one. The cell tree is the one that needs saying so - its width is
    ///declared on the wrapper around it, and the wrapper is still there when the tree is not.
    ///
    test('a dragged width is forgotten when the panel is shut', async ({ page }) => {
        await openTree(page);

        const sized = await dragBy(page, '#cellTree', 100);

        expect(sized.now).toBeGreaterThan(sized.was);

        await page.locator('#cellTreeButton').click();

        await expect(page.locator('#cellTree')).toHaveCount(0);

        await openTree(page);

        const again = (await page.locator('#cellTree').boundingBox()).width;

        expect(Math.abs(again - sized.was)).toBeLessThan(2);
    });

    ///It cannot be dragged away to nothing, nor out over the drawing.
    test('it stops at a width that can still be read', async ({ page }) => {
        await openTree(page);

        const narrow = await dragBy(page, '#cellTree', -600);

        expect(narrow.now).toBeGreaterThan(100);

        const wide = await dragBy(page, '#cellTree', 3000);

        expect(wide.now).toBeLessThan(800);
    });
});

///
///Kept, unlike every other panel over the view.
///
///It is opened by a press and closed by one, with nothing timed and no position to put back - the two things
///that make the rest of them unrestorable. See SavedSession.CellTree.
///
test('it is still open a session later', async ({ page }) => {
    await openTree(page);

    //The bare address with nothing said about the tree, so the session is what answers - see gotoApp.
    await gotoApp(page, '', null);

    await expectLoaded(page);

    await expect(page.locator('#cellTree')).toBeVisible();
    await expect(page.locator('#cellTreeButton')).toHaveClass(/toolButtonOn/);
});
