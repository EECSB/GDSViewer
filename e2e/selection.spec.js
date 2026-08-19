//Picking a shape out of the 2D view and asking what it is.
//
//This is the first thing in the app that goes JS-to-C#: the browser does the hit test, names the node it
//landed on, and calls back into Blazor with which element of the layout that was. Nothing about that path
//is visible to a unit test - the tagging is covered in SvgWriterTests, and the way back from an element
//to its cell in ProvenanceTests. What is only checkable here is that the two are wired to each other.
const { test, expect } = require('@playwright/test');
const { gotoExample, MOSFET, SKY130_CELL, shapeCount, shapesDrawn, shapeBox, openedOnItsOwn } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect(page.locator('#gdsSVG')).toBeVisible();
});

///Clicks the middle of the nth drawn shape.
async function clickShape(page, nth = 0) {
    const box = await shapeBox(page, nth);

    await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));
}

test('every drawn shape is tagged so a click can be traced back', async ({ page }) => {
    const drawn = await shapesDrawn(page);

    //Every shape says which element of the layout drew it, and no two claim the same one - which is what
    //lets a click be traced back to something that can be edited.
    const named = new Set(drawn.map(shape => shape.element));

    expect(drawn.length).toBeGreaterThan(0);
    expect(named.size).toBe(drawn.length);
    expect([...named].every(element => Number.isInteger(element) && element >= 0)).toBe(true);
});

test('nothing is picked out until the tool is used', async ({ page }) => {
    await expect(page.locator('#selectionPanel')).toHaveCount(0);

    //Clicking in pan mode pans; it does not select.
    await clickShape(page);

    await expect(page.locator('#selectionPanel')).toHaveCount(0);
});

test('clicking a shape says which layer it is on and how big it is', async ({ page }) => {
    await page.locator('#selectTool').click();

    await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);

    await clickShape(page);

    const panel = page.locator('#selectionPanel');

    await expect(panel).toBeVisible();

    //A layer/datatype pair, a corner count and an area in both units.
    await expect(panel).toContainText(/\d+\/\d+/);
    await expect(panel).toContainText(/\d+ corners/);

    //
    //Once, not twice, and on the picker rather than above it.
    //
    //DisplayName is already the pair for an unnamed layer, so printing the key beside it read "67/20 67/20"
    //- which the tests could not see and a glance at the running app could. The heading that carried it has
    //since gone: a title naming the layer over a picker for the layer was the same fact twice, and the one
    //you could act on was the one further down.
    //
    const naming = await panel.locator('#chosenLayer').textContent();

    expect(naming).toMatch(/\d+\/\d+/);
    expect(naming.match(/\d+\/\d+/g)).toHaveLength(1);

    //
    //And "On layer" in front of it, saying what the control is for.
    //
    //Beside the picker, not inside it - the button holds the layer and nothing else, which is what the two
    //lines above are checking. Without the word the panel opens on a colored square and a pair of numbers,
    //which reads as a heading naming the layer rather than as something you can change.
    //
    await expect(panel.locator('.layerPickerRow')).toContainText('On layer');
    expect(naming).not.toContain('On layer');

    await expect(panel).toContainText(/sq units/);
    await expect(panel).toContainText(/sq µm/);

    //And a bounding box, which is what says where it is.
    await expect(panel).toContainText(/\(-?\d+, -?\d+\) to \(-?\d+, -?\d+\)/);

    //Something is outlined - but not necessarily the polygon whose middle was aimed at. Shapes overlap,
    //and the browser gives the click to whichever is drawn on top there, which is the whole reason the
    //hit test is left to it. Which one it was is checked below, by the reading changing with the click.
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
});

test('only one shape is picked out at a time', async ({ page }) => {
    await page.locator('#selectTool').click();

    await clickShape(page, 0);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

    await clickShape(page, 3);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
});

///
///The panel has to name the element that was actually clicked, not merely some element. Checked by
///clicking two shapes on different layers and requiring the reading to change with them.
///
test('the reading follows the shape that was clicked', async ({ page }) => {
    await page.locator('#selectTool').click();

    const readings = new Set();

    for (let i = 0; i < 6; i++) {
        await clickShape(page, i);

        readings.add(await page.locator('#selectionPanel').textContent());
    }

    //Mosfet.gds draws on nine layer/datatype pairs, so six shapes cannot all read the same.
    expect(readings.size).toBeGreaterThan(1);
});

test('clicking the background clears the selection', async ({ page }) => {
    await page.locator('#selectTool').click();

    await clickShape(page);
    await expect(page.locator('#selectionPanel')).toBeVisible();

    //A corner of the view, which the layout does not reach.
    const box = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.click(box.x + 4, box.y + 4);

    await expect(page.locator('#selectionPanel')).toHaveCount(0);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(0);
});

///Selecting leaves the view still, or a click that started on one shape could finish on another.
test('selecting does not pan the view', async ({ page }) => {
    await page.locator('#selectTool').click();

    const before = await page.locator('#gdsSVG').getAttribute('viewBox');

    const box = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(box.x + (box.width / 2) - 80, box.y + (box.height / 2));
    await page.mouse.down();
    await page.mouse.move(box.x + (box.width / 2) + 80, box.y + (box.height / 2), { steps: 8 });
    await page.mouse.up();

    expect(await page.locator('#gdsSVG').getAttribute('viewBox')).toBe(before);
});

test('switching tools puts the selection down', async ({ page }) => {
    await page.locator('#selectTool').click();
    await clickShape(page);

    await expect(page.locator('#selectionPanel')).toBeVisible();

    await page.locator('#measureTool').click();

    await expect(page.locator('#selectionPanel')).toHaveCount(0);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(0);
});

///Changing what is drawn replaces every shape, so the one that was picked out is no longer there.
test('a redraw puts the selection down rather than leaving it pointing at nothing', async ({ page }) => {
    await page.locator('#selectTool').click();
    await clickShape(page);

    await expect(page.locator('#selectionPanel')).toBeVisible();

    //The opacity slider rebuilds every shape.
    await page.locator("#layerOpacity").fill('0.9');

    await expect(page.locator('#selectionPanel')).toHaveCount(0);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(0);
});

///
///The whole point of provenance: a shape drawn from a placed cell names the cell, and warns that editing
///it would change every instance.
///
///**Needs a fixture, because the corpus cannot do this.** Not one of the 897 bundled files has a
///placement that resolves - the one file with SREFs in it references four cells that are not in it, so
///they draw nothing. Checked with `gds info`, which reports them as unresolved. So there is no bundled
///layout in which any shape is more than one level deep, and this path would go untested against all of
///them. e2e/fixtures/placed.gds is a top-level square plus three placements of a leaf.
///
test('a shape inside a placed cell names the cell and says so', async ({ page }) => {
    await page.locator('#fileUpload').setInputFiles('e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    //Four shapes: the top's own, and one from each of the three placements.
    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    await page.locator('#selectTool').click();

    const panel = page.locator('#selectionPanel');
    const readings = [];

    for (let i = 0; i < 4; i++) {
        await clickShape(page, i);

        await expect(panel).toBeVisible();

        readings.push(await panel.textContent());
    }

    //Three of the four came through a placement and say so; one is the top's own and does not.
    const placed = readings.filter(reading => reading.includes('TOP > LEAF'));
    const direct = readings.filter(reading => !reading.includes(' > '));

    expect(placed).toHaveLength(3);
    expect(direct).toHaveLength(1);

    for (const reading of placed)
        expect(reading).toContain('editing it changes every instance');

    //And the one that belongs to the top carries no such warning, because it does not.
    expect(direct[0]).not.toContain('editing it changes every instance');

    //
    //The warning is the sentence and a mark, not a sentence introduced by one.
    //
    //It read "in a placed cell - editing it changes every instance", which said twice what the line above
    //it had already said once: the line above names the placement. What is left is the consequence, with
    //an icon in front of it doing the work the lead-in was doing.
    //
    //Back onto one that carries it, since the loop above ends wherever it ends.
    await clickShape(page, readings.findIndex(reading => reading.includes('TOP > LEAF')));

    await expect(panel).toBeVisible();

    const note = await page.evaluate(() => {
        const found = document.querySelector('#selectionPanel .selectionNote.noteWithIcon');

        if (found === null)
            return null;

        return {
            words: found.textContent.trim(),
            icons: found.querySelectorAll('svg.noteIcon').length,
            first: found.firstElementChild.tagName.toLowerCase()
        };
    });

    expect(note.words).toBe('editing it changes every instance');
    expect(note.icons).toBe(1);

    //In front of the words rather than after them.
    expect(note.first).toBe('svg');
});

///
///Where the panel puts things, which is only answerable in a browser.
///
///Every one of these is a claim about *rendered position* - what shares a line with what, what sits above
///which rule - and none of it is in the markup to be read off. Blazor writes the elements in an order; CSS
///decides whether seven of them fit across a panel or fall onto two lines, and a rule that loses on
///specificity changes the answer without changing a line of the component. That has happened more than
///once here, which is why these ask the browser for coordinates rather than for classes.
///
test.describe('the shape of the panel', () => {
    ///Clicks about until a shape of the cell being edited is chosen, which is what the turns need.
    async function chooseEditableShape(page) {
        await page.locator('#selectTool').click();

        //The first click goes into the cell, so the shapes to aim at are the ones it holds - and there is
        //no telling in advance which of them is drawn on top at any point.
        for (let nth = 0; nth < await shapeCount(page); nth++) {
            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            if (await page.locator('#turnLeft').count() > 0)
                return;
        }

        throw new Error('no shape of the cell being edited could be chosen');
    }

    ///
    ///Where each of the panel's rules is, top to bottom.
    ///
    ///Only the ones that are drawn: a rule against the top of the panel, the bottom, or another rule is
    ///hidden rather than rendered - see .panelDivider - and counting a hidden one would put a section
    ///boundary where the eye sees none.
    ///
    async function ruleTops(page) {
        return page.locator('.panelDivider').evaluateAll(all =>
            all.filter(one => getComputedStyle(one).display !== 'none')
               .map(one => Math.round(one.getBoundingClientRect().y)));
    }

    ///The top of an element on screen, to the pixel.
    async function topOf(page, selector) {
        const box = await page.locator(selector).boundingBox();

        expect(box, `${selector} is not on screen`).not.toBeNull();

        return Math.round(box.y);
    }

    ///The eight squares, in the order the row puts them.
    const ACROSS = ['#copyShapes', '#arrayOpen', '#cutShapes', '#deleteShape',
                    '#turnLeft', '#turnRight', '#mirrorAcross', '#mirrorDown'];

    test('the eight squares share one line, in order', async ({ page }) => {
        await chooseEditableShape(page);

        const tops = [];
        const lefts = [];

        for (const one of ACROSS) {
            const box = await page.locator(one).boundingBox();

            expect(box, `${one} is not on screen`).not.toBeNull();

            tops.push(Math.round(box.y));
            lefts.push(Math.round(box.x));
        }

        //One line: every one of the eight starts at the same height.
        expect(new Set(tops).size).toBe(1);

        //And in the order they are written, left to right, rather than merely on the same line.
        expect(lefts).toEqual([...lefts].sort((left, right) => left - right));

        //Array beside Copy, because it is copying - and next to it rather than merely somewhere in a row
        //that has eight things in it.
        const copy = await page.locator('#copyShapes').boundingBox();
        const array = await page.locator('#arrayOpen').boundingBox();

        expect(array.x - (copy.x + copy.width)).toBeLessThan(10);
    });

    ///
    ///The row fits, with room, in the panel at its narrowest.
    ///
    ///Eight squares is one more than the panel was ever asked to carry, and the width they have is not
    ///fixed: the panel takes a scrollbar as soon as its content is taller than the window, which costs
    ///the row about fifteen pixels. Measured wide, eight of the old 28px squares fit by a single pixel
    ///and wrapped the moment anything scrolled.
    ///
    test('the eight squares fit the row with room to spare', async ({ page }) => {
        await chooseEditableShape(page);

        const spread = await page.evaluate((across) => {
            const boxes = across.map(one => document.querySelector(one).getBoundingClientRect());
            const row = document.querySelector('#copyShapes').parentElement.getBoundingClientRect();

            return {
                content: Math.max(...boxes.map(b => b.right)) - Math.min(...boxes.map(b => b.left)),
                row: row.width
            };
        }, ACROSS);

        //Fifteen for a scrollbar that is not there in this window but is in a shorter one.
        expect(spread.content).toBeLessThan(spread.row - 15);
    });

    test('a rule stands between what removes a shape and what turns one', async ({ page }) => {
        await chooseEditableShape(page);

        const rule = await page.locator('.actionDivider').boundingBox();
        const deletes = await page.locator('#deleteShape').boundingBox();
        const turns = await page.locator('#turnLeft').boundingBox();

        expect(rule).not.toBeNull();

        //Between them, not merely present: a rule at either end of the row would pass a count.
        expect(rule.x).toBeGreaterThan(deletes.x + deletes.width);
        expect(rule.x + rule.width).toBeLessThan(turns.x);

        //And drawn rather than collapsed - a divider of no height is one nobody can see.
        expect(rule.height).toBeGreaterThan(20);
    });

    ///
    ///Growing is a number, so it sits with the numbers - and the numbers come after the squares.
    ///
    ///The squares moved above them: the panel answers what is chosen, then what can be done to it, then
    ///what it measures. Growing is the one control that could plausibly go either way - it is a button, and
    ///it changes the shapes - and it stays with the numbers, because using it means typing one.
    ///
    test('growing sits with the numbers, under the rule the squares end at', async ({ page }) => {
        await chooseEditableShape(page);

        //
        //A verb on the button, and the preposition between it and the number.
        //
        //The row is a sentence with a blank in it - grow, by, this much - the same shape as the At and By
        //rows below it. A reader takes the button on its own, though, and "Grow" alone does not say by
        //what, so the full name stays on both halves as an aria-label.
        //
        await expect(page.locator('#growApply')).toHaveText('Grow');
        await expect(page.locator('#growApply')).toHaveAttribute('aria-label', 'Grow by');
        await expect(page.locator('#growBy')).toHaveAttribute('aria-label', 'Grow by');

        const written = await page.locator('#growApply').evaluate(button => {
            const between = button.nextElementSibling;

            return {
                says: between.textContent.trim(),
                andThenTheBox: between.nextElementSibling.id
            };
        });

        expect(written.says).toBe('by');
        expect(written.andThenTheBox).toBe('growBy');

        const squares = await topOf(page, '#copyShapes');
        const grow = await topOf(page, '#growBy');

        //The squares come first, and every number follows them.
        expect(squares).toBeLessThan(grow);
        expect(squares).toBeLessThan(await topOf(page, '#sizeX'));

        //Growing is the last of the numbers rather than the first of what comes after them.
        expect(grow).toBeGreaterThan(await topOf(page, '#sizeX'));

        //Inside the numbers' own section, both ways: the nearest rule above it is the one that ends the
        //squares, and the nearest below it is the one that ends the numbers.
        const rules = await ruleTops(page);
        const above = Math.max(...rules.filter(y => y < grow));
        const below = Math.min(...rules.filter(y => y > grow));

        expect(above).toBeGreaterThan(squares);
        expect(below).toBeLessThan(await topOf(page, '#traceNet'));
    });

    ///
    ///The button is as wide as the box it acts on.
    ///
    ///Worth a test of its own because the width is set in ems on an element that also sets its own font
    ///size, and the rule that sets that size has to out-specify another one to do it. Asked at one class
    ///too few the button rendered half again as wide, with the number in the width and nothing wrong in
    ///the markup to see.
    ///
    test('the grow button leads its row, at the size of the box', async ({ page }) => {
        await chooseEditableShape(page);

        const box = await page.locator('#growBy').boundingBox();
        const button = await page.locator('#growApply').boundingBox();

        //In front of the box, where the label used to be.
        expect(button.x + button.width).toBeLessThanOrEqual(box.x);

        //And the size of it, both ways - the height is the half that does not come for free, since a
        //button with padding above and below is close to twice the height of one of these boxes.
        expect(Math.abs(button.width - box.width)).toBeLessThan(4);
        expect(Math.abs(button.height - box.height)).toBeLessThan(2);

        //The word fits the button it is written on rather than spilling out of it.
        const spills = await page.locator('#growApply').evaluate(one => one.scrollWidth > Math.ceil(one.getBoundingClientRect().width));

        expect(spills).toBe(false);
    });

    test('tracing a net is the last of the actions', async ({ page }) => {
        await chooseEditableShape(page);

        //It went from the head of the section to its foot, so what it is under matters as much as what it
        //is over: at the head this sat above Make cell and passed nothing below.
        expect(await topOf(page, '#traceNet')).toBeGreaterThan(await topOf(page, '#copyShapes'));
        expect(await topOf(page, '#traceNet')).toBeGreaterThan(await topOf(page, '#makeCell'));
    });

    ///
    ///Every word button in the panel is one height and one size of type.
    ///
    ///Trace net was neither. Alone in its group, it kept the panel's own 13.6px where New cell above it is
    ///11.15, and a 23.8px line inside a 26px box left the word sitting against the top of it rather than in
    ///the middle. Nothing next to it meant nothing to see it against.
    ///
    test('trace net is the height and the type size of the other word buttons', async ({ page }) => {
        await chooseEditableShape(page);

        const set = await page.evaluate(() => {
            const box = one => document.querySelector(one).getBoundingClientRect();
            const type = one => parseFloat(getComputedStyle(document.querySelector(one)).fontSize);
            const trace = document.querySelector('#traceNet');
            const inside = trace.getBoundingClientRect();

            return {
                heights: [box('#traceNet').height, box('#copyShapes').height, box('#makeCell').height],
                sizes: [type('#traceNet'), type('#makeCell')],

                //Where the word actually sits in the box, measured off the two clearances rather than
                //trusting that a line-height and a box height agree.
                over: parseFloat(getComputedStyle(trace).lineHeight),
                tall: inside.height,
                spills: trace.scrollWidth > Math.ceil(inside.width)
            };
        });

        //One height across the squares, the pair and this.
        expect(set.heights[0]).toBeCloseTo(set.heights[1], 1);
        expect(set.heights[0]).toBeCloseTo(set.heights[2], 1);

        //And the same type as New cell, rather than the panel's.
        expect(set.sizes[0]).toBeCloseTo(set.sizes[1], 1);

        //A line shorter than the box it is in, which is what leaves room to center it.
        expect(set.over).toBeLessThan(set.tall - 4);

        expect(set.spills).toBe(false);
    });

    ///
    ///The eight squares stand between two rules, as a section of their own.
    ///
    ///They are the one row that acts on the shapes as they are - copy them, cut them, turn them, repeat
    ///them - where everything under them is a number that describes the shapes instead of changing them.
    ///
    test('the squares have a rule above and below them', async ({ page }) => {
        await chooseEditableShape(page);

        const squares = await topOf(page, '#copyShapes');
        const rules = await ruleTops(page);

        expect(rules.some(y => y < squares)).toBe(true);
        expect(rules.some(y => y > squares)).toBe(true);

        //And the rule below is above the numbers, rather than at the foot of the panel.
        const under = Math.min(...rules.filter(y => y > squares));

        expect(under).toBeLessThan(await topOf(page, '#atX'));
    });

    ///
    ///The word buttons are set smaller than the panel they sit in.
    ///
    ///`.lineUpButton` says so, and for a long time it did not happen: the rule was written at one class
    ///where `.buttonStyle2.selectionAction` sets a size and a padding at two, so eleven buttons rendered
    ///at the panel's own size with the padding meant for a button with a word in it. Nothing about the
    ///markup looked wrong, and the only sign was that two rows wrapped which should not have.
    ///
    ///Asked of the computed style rather than of the class list, because carrying the class is exactly
    ///what those eleven were doing while none of it applied.
    ///
    ///
    ///What the Array square opens stays inside the square's own section.
    ///
    ///A disclosure belongs with the control that opened it. Past the rule the boxes read as the start of
    ///the next group instead of as the inside of this one, and the rule fell between a button and the
    ///boxes it had just put on screen.
    ///
    test('the array boxes open inside the squares section', async ({ page }) => {
        await chooseEditableShape(page);

        await page.locator('#arrayOpen').click();

        await expect(page.locator('#arrayColumns')).toBeVisible();

        const squares = await topOf(page, '#copyShapes');
        const boxes = await topOf(page, '#arrayColumns');

        //The rule that ends the section is the first one below the squares.
        const under = Math.min(...(await ruleTops(page)).filter(y => y > squares));

        expect(boxes).toBeGreaterThan(squares);
        expect(boxes).toBeLessThan(under);
    });

    ///
    ///And they stand on a ground of their own while they are open.
    ///
    ///They are the only rows in the panel that are there because a button is held down, and nothing else
    ///said so: the square lit at the top, the boxes somewhere below it, and no telling that one is the
    ///other's inside.
    ///
    test('the array boxes stand on a darker ground', async ({ page }) => {
        await chooseEditableShape(page);

        //Nothing there while it is shut.
        await expect(page.locator('#arrayOpened')).toHaveCount(0);

        await page.locator('#arrayOpen').click();

        await expect(page.locator('#arrayOpened')).toBeVisible();

        //Darker than the panel it sits in, and holding the boxes rather than merely near them.
        const ground = await page.evaluate(() => {
            const patch = document.querySelector('#arrayOpened');
            const panel = document.querySelector('#selectionPanel');

            const lightness = (color) => {
                const parts = color.match(/[\d.]+/g).map(Number);

                //Over whatever is behind it, which for a translucent patch is the panel's own white.
                let alpha = 1;

                if (parts.length > 3)
                    alpha = parts[3];

                return ((parts[0] + parts[1] + parts[2]) / 3 * alpha) + (255 * (1 - alpha));
            };

            return {
                patch: lightness(getComputedStyle(patch).backgroundColor),
                panel: lightness(getComputedStyle(panel).backgroundColor),
                holdsTheBoxes: patch.contains(document.querySelector('#arrayColumns'))
            };
        });

        expect(ground.holdsTheBoxes).toBe(true);
        expect(ground.patch).toBeLessThan(ground.panel);
    });

    ///
    ///**The squares stand above the numbers.**
    ///
    ///Which is the arrangement itself rather than a consequence of it, and the reason the two tests above
    ///had to change. Copying, cutting, deleting and turning are what somebody reaches the panel for; the
    ///bounds, the corner, the size, the path width and the grow are numbers to read when a number is
    ///wanted. Under the old order every one of those stood between the selection and the scissors.
    ///
    test('the squares stand above every number the panel gives', async ({ page }) => {
        await chooseEditableShape(page);

        const squares = await topOf(page, '#copyShapes');

        //The corners and the area, which read as identity and are a measurement.
        expect(squares).toBeLessThan(await topOf(page, '.selectionPanel .selectionRow >> text=corners'));

        //Then position, then size, then the last of them.
        expect(squares).toBeLessThan(await topOf(page, '#atX'));
        expect(squares).toBeLessThan(await topOf(page, '#sizeX'));
        expect(squares).toBeLessThan(await topOf(page, '#growBy'));

        //And still below what is chosen, which is the one thing that has to come first.
        expect(squares).toBeGreaterThan(await topOf(page, '#selectionPanel'));
    });

    ///
    ///Making a cell is the last line of what is chosen, not the first of what is done to it.
    ///
    ///It reads as the end of the answer to "what am I looking at" - a shape, in this cell, reached through
    ///this chain, and if you like in a cell of its own called this. Everything under the first rule acts on
    ///the shapes where they are; this is the one control that changes what "where they are" means.
    ///
    test('naming a new cell is the last of what is chosen', async ({ page }) => {
        await chooseEditableShape(page);

        const naming = await topOf(page, '#cellName');

        //Above the squares, and above the first rule with them.
        expect(naming).toBeLessThan(await topOf(page, '#copyShapes'));

        const rules = await ruleTops(page);

        expect(Math.min(...rules)).toBeGreaterThan(naming);

        //Under the cell chain, which is what it is the last line of.
        const chain = await page.locator('.selectionPanel .selectionRow').filter({ hasText: 'Cell:' }).first().boundingBox();

        expect(naming).toBeGreaterThan(chain.y);
    });

    ///
    ///The name and the button that uses it, as one control.
    ///
    ///Three things at once, and all three are the same claim: they are the same height as the squares they
    ///sit under, that height is the squares' own rather than a number that happens to match, and there is
    ///no space between them.
    ///
    test('the new cell name and its button are one control', async ({ page }) => {
        await chooseEditableShape(page);

        const sizes = await page.evaluate(() => {
            const box = one => document.querySelector(one).getBoundingClientRect();

            return {
                square: box('#copyShapes').height,
                button: box('#makeCell').height,
                name: box('#cellName').height,
                gap: box('#makeCell').left - box('#cellName').right,
                label: document.querySelector('#makeCell').textContent.trim()
            };
        });

        //The squares' height, to the pixel, on both halves.
        expect(sizes.button).toBeCloseTo(sizes.square, 1);
        expect(sizes.name).toBeCloseTo(sizes.square, 1);

        //Touching. At most the hairline they share, and never a space.
        expect(sizes.gap).toBeLessThanOrEqual(0);
        expect(sizes.gap).toBeGreaterThan(-2);

        expect(sizes.label).toBe('New cell');
    });

    ///
    ///And the pair is as wide as the strip of squares beneath it, rather than as wide as the panel.
    ///
    ///It read as a stray control before: the strip stopped at 220px and the pair ran on to 275px, so the
    ///one row that was not part of a run was also the only row that reached the panel's edge.
    ///
    test('the new cell pair spans the strip of actions below it', async ({ page }) => {
        await chooseEditableShape(page);

        const spans = await page.evaluate(() => {
            const box = one => document.querySelector(one).getBoundingClientRect();

            //
            //How far the squares reach, not how wide the box around them is.
            //
            //Two traps here, and both were fallen into. `.selectionActions` is a flex container that
            //stretches the width of the panel, so its own rectangle is 274.8 where the eight squares in it
            //span 221.2 - measuring the container has this row chasing a width nothing is drawn at. And the
            //panel has more than one group carrying that class, so naming it picks whichever comes first,
            //which is a different row depending on what is selected: 65.1 wide in one state and 221.2 in
            //another.
            //
            //Anchored on the copy button instead, which is only ever in the row this is about.
            //
            const squares = [...document.querySelector('#copyShapes').parentElement.children];
            const strip = {
                left: squares[0].getBoundingClientRect().left,
                right: squares[squares.length - 1].getBoundingClientRect().right
            };

            return {
                strip: strip.right - strip.left,
                pair: box('#makeCell').right - box('#cellName').left,
                lefts: [strip.left, box('#cellName').left]
            };
        });

        //Within a pixel of the strip, and never wider - the row is what set the panel's width before.
        expect(spans.pair).toBeLessThanOrEqual(spans.strip + 1);
        expect(spans.pair).toBeGreaterThan(spans.strip - 4);

        //Starting from the same edge, so the two rows read as one column.
        expect(spans.lefts[1]).toBeCloseTo(spans.lefts[0], 0);
    });

    ///
    ///Nothing in the panel has square corners, except where two things are butted into one.
    ///
    ///It was the one place in the app that did: `.buttonStyle2` carries no radius of its own, and the
    ///rounding everywhere else comes from rules the panel had no equivalent of.
    ///
    ///The exceptions are the joins, and they are the rule rather than a hole in it - a run of buttons is
    ///round at its two ends and square where its members meet, which is what makes it read as one control
    ///instead of four. The name box and its button are the same thing with two members.
    ///
    test('every control in the panel has the rounded corners the app uses', async ({ page }) => {
        await chooseEditableShape(page);

        const square = await page.evaluate(() => {
            const panel = document.querySelector('#selectionPanel');
            const joined = ['cellName', 'makeCell'];

            return [...panel.querySelectorAll('button, input, select')]
                .filter(one => !joined.includes(one.id))

                //A member of a run is square on purpose; the run's own ends are checked below.
                .filter(one => one.closest('.selectionActions') === null)

                .filter(one => {
                    const corners = getComputedStyle(one);

                    return [corners.borderTopLeftRadius, corners.borderTopRightRadius,
                            corners.borderBottomLeftRadius, corners.borderBottomRightRadius]
                        .some(corner => parseFloat(corner) === 0);
                })
                .map(one => one.id || one.className);
        });

        expect(square).toEqual([]);

        //
        //And a run of actions: round at its ends, square inside, with no space between its members.
        //
        //Which is the shape the tools in the bar make, and what says "these are parts of one thing" rather
        //than "these are four decisions that happen to be near each other".
        //
        const run = await page.evaluate(() => {
            //One run. The panel has several groups of actions, and a button at the end of one is not meant
            //to touch the button at the start of the next.
            const group = document.querySelector('#selectionPanel .selectionActions');
            const buttons = [...group.querySelectorAll(':scope > .selectionAction')];
            const corners = one => getComputedStyle(one);

            return {
                many: buttons.length,
                firstRoundedLeft: parseFloat(corners(buttons[0]).borderTopLeftRadius),
                insideSquare: buttons.slice(1, -1).every(one => parseFloat(corners(one).borderTopLeftRadius) === 0),
                touching: buttons.slice(1).every((one, at) =>
                    Math.abs(one.getBoundingClientRect().left - buttons[at].getBoundingClientRect().right) < 1),
                seamed: buttons.slice(1).every(one => parseFloat(corners(one).borderLeftWidth) > 0)
            };
        });

        expect(run.many).toBeGreaterThan(2);
        expect(run.firstRoundedLeft).toBeGreaterThan(0);
        expect(run.insideSquare).toBe(true);
        expect(run.touching).toBe(true);

        //A hairline between neighbours, so a strip does not read as one wide button.
        expect(run.seamed).toBe(true);

        //And the pair: rounded on the outside of the pair, square on the seam.
        const seam = await page.evaluate(() => {
            const name = getComputedStyle(document.querySelector('#cellName'));
            const button = getComputedStyle(document.querySelector('#makeCell'));

            return {
                outsideLeft: parseFloat(name.borderTopLeftRadius),
                seamLeft: parseFloat(name.borderTopRightRadius),
                seamRight: parseFloat(button.borderTopLeftRadius),
                outsideRight: parseFloat(button.borderTopRightRadius)
            };
        });

        expect(seam.outsideLeft).toBeGreaterThan(0);
        expect(seam.outsideRight).toBeGreaterThan(0);
        expect(seam.seamLeft).toBe(0);
        expect(seam.seamRight).toBe(0);
    });

    test('the panel word buttons are set smaller than the panel', async ({ page }) => {
        await chooseEditableShape(page);

        const sizes = await page.evaluate(() => {
            const panel = getComputedStyle(document.querySelector('#selectionPanel'));
            const button = getComputedStyle(document.querySelector('#makeCell'));

            return {
                panel: parseFloat(panel.fontSize),
                button: parseFloat(button.fontSize),
                padding: parseFloat(button.paddingLeft)
            };
        });

        expect(sizes.button).toBeLessThan(sizes.panel);

        //And the padding with it - both come from the rule, and both lost to the same one.
        expect(sizes.padding).toBeLessThan(9);
    });

    test('the panel no longer says how to move a shape', async ({ page }) => {
        await chooseEditableShape(page);

        //A line of instructions on every selection, for the one gesture nobody needs telling twice.
        await expect(page.locator('#selectionPanel')).not.toContainText('nudge with the arrow keys');
    });
});
