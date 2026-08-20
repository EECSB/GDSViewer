//Editing a placement rather than what it places.
//
//The composition is covered in HierarchyTests, where a transform can be read directly - including the case
//that decides the approach, an instance turned inside a cell that is itself placed mirrored.
//
//What is only checkable here is which thing an action lands on, and that is the whole feature: the same drag
//on the same pixels moves *one* instance when the cell above is being edited and *every* instance when the
//cell below is. Nothing in C# can see which of those happened, because both are the same edit on a different
//element.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, allPoints, openedOnItsOwn, leaveCell } = require('./helpers');

test.beforeEach(async ({ page }) => {
    //With the cell tree open, since one of these reads the library - see gotoApp.
    await gotoApp(page, '', true);

    await page.locator('#fileUpload').setInputFiles('e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    await page.locator('#selectTool').click();
});

///Where every drawn shape is, as a stable string, for comparing before and after.
async function corners(page) {
    return allPoints(page).then(points => points.sort().join(' | '));
}

///Clicks shapes until one is found whose chain reads exactly `chain`, and gives back its box on screen.
async function clickChain(page, chain) {
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const said = await page.locator('#selectionPanel').textContent();

        if (said.includes(chain))
            return box;
    }

    throw new Error(`no shape was reached through ${chain}`);
}

///
///Opens TOP for editing, by way of the one shape TOP holds itself.
///
///That shape's chain is TOP alone, where the other three read TOP > LEAF - so it is the one that puts the
///editor in the cell that *holds* the placements rather than in the cell being placed.
///
async function editTop(page) {
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        const said = await page.locator('#selectionPanel').textContent();

        if (!said.includes('LEAF')) {

            await expect.poll(async () => (await page.locator('.contextCrumbOn').textContent()).trim(),
                { timeout: 15000 }).toBe('TOP');

            return;
        }
    }

    throw new Error('no shape of TOP\'s own was found');
}

///Editing TOP, with one instance of LEAF chosen. Gives back that shape's box on screen.
async function chooseAnInstance(page) {
    await editTop(page);

    const box = await clickChain(page, 'TOP > LEAF');

    await expect(page.locator('#instanceNote')).toBeVisible();

    return box;
}

///How many of the drawn shapes are in a different place than they were.
function moved(before, after) {
    const was = before.split(' | ');
    const now = after.split(' | ');

    return now.filter(shape => !was.includes(shape)).length;
}

test.describe('knowing what will be acted on', () => {
    test('the panel says it is an instance, and names the cell', async ({ page }) => {
        await chooseAnInstance(page);

        await expect(page.locator('#instanceNote')).toContainText('one instance of LEAF');
    });

    ///
    ///**Inside LEAF, the same shape is a shape again.**
    ///
    ///Which is the whole distinction: a placement belongs to the cell above it, so it is only editable from
    ///there. Descending makes the shapes editable and the placement not.
    ///
    test('descending into the cell makes it a shape again', async ({ page }) => {
        const box = await chooseAnInstance(page);


        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        await expect(page.locator('#deleteShape')).toBeVisible();
        await expect(page.locator('#instanceNote')).toHaveCount(0);
    });

    ///A shape TOP holds itself is not an instance of anything, whatever else is on screen.
    test('a shape of the cell\'s own is not treated as one', async ({ page }) => {
        await editTop(page);

        await clickChain(page, 'TOP');

        await expect(page.locator('#instanceNote')).toHaveCount(0);
        await expect(page.locator('#deleteShape')).toBeVisible();
    });
});

test.describe('moving one', () => {
    ///
    ///**The headline: one instance moves, and the other two stay.**
    ///
    ///The same drag from inside LEAF moves all three, which editing.spec covers. Both are correct and they
    ///are opposites, so which cell is being edited is the only thing that decides between them.
    ///
    test('dragging moves that instance alone', async ({ page }) => {
        const before = await corners(page);

        const box = await chooseAnInstance(page);

        await page.mouse.move(box.x + (box.width / 2), box.y + (box.height / 2));
        await page.mouse.down();
        await page.mouse.move(box.x + (box.width / 2) + 70, box.y + (box.height / 2) + 40, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => corners(page), { timeout: 15000 }).not.toBe(before);

        expect(moved(before, await corners(page))).toBe(1);
    });

    test('and it undoes as a move', async ({ page }) => {
        const before = await corners(page);

        const box = await chooseAnInstance(page);

        await page.mouse.move(box.x + (box.width / 2), box.y + (box.height / 2));
        await page.mouse.down();
        await page.mouse.move(box.x + (box.width / 2) + 70, box.y, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => corners(page), { timeout: 15000 }).not.toBe(before);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);
    });
});

test.describe('turning one', () => {
    ///
    ///A square turned about its own middle looks the same, so the fixture's square would prove nothing about
    ///a quarter turn. What it does prove is that the *placement* changed and can be taken back - and the
    ///mirror-and-move case below is the one that shows on screen.
    ///
    test('a turn is one step and undoes exactly', async ({ page }) => {
        const before = await corners(page);

        await chooseAnInstance(page);

        await page.locator('#turnRight').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Turn right/, { timeout: 15000 });

        await page.locator('#undoEdit').click();

        await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);
    });

    ///
    ///**It turns about the middle of what is chosen, so a square placed off-center moves.**
    ///
    ///Turning a placement is its STRANS and its reference point together: the cell turns and its origin
    ///travels round the pivot. Only the one instance is affected, which is what says the edit landed on the
    ///placement rather than on the cell.
    ///
    test('mirroring moves only that instance', async ({ page }) => {
        const before = await corners(page);

        await chooseAnInstance(page);

        await page.locator('#mirrorDown').click();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Mirror down/, { timeout: 15000 });

        //A square mirrored about its own middle lands on itself, so nothing need have moved - but nothing
        //else may have, and the file has to still be readable, which the redraw is the proof of.
        expect(moved(before, await corners(page))).toBeLessThanOrEqual(1);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///Four quarters is where it started - the composition closing, seen from outside.
    test('four quarter turns come back to where it was', async ({ page }) => {
        const before = await corners(page);

        await chooseAnInstance(page);

        for (let i = 0; i < 4; i++) {
            await page.locator('#turnRight').click();

            await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Turn right/, { timeout: 15000 });
        }

        await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);
    });
});

test.describe('deleting one', () => {
    ///
    ///**The instance goes and the cell stays.**
    ///
    ///Which is the difference between this and deleting a cell: one placement fewer, and everything else that
    ///places LEAF is untouched. Two of the three squares remain, plus TOP's own.
    ///
    test('takes out one placement and leaves the cell', async ({ page }) => {
        await chooseAnInstance(page);

        await page.locator('#deleteInstance').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(3);

        //LEAF is still in the library, still placed twice. Counted as cells: every layer the tree lists
        //under one carries .cellRow as well.
        const cells = page.locator('#cellTree .cellRowPair[data-kind="cell"]');

        await expect(cells).toHaveCount(2);
        await expect(cells.filter({ hasText: 'LEAF' })).toContainText('placed 2');
    });

    test('and undoing puts the instance back', async ({ page }) => {
        const before = await corners(page);

        await chooseAnInstance(page);

        await page.locator('#deleteInstance').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(3);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);
    });
});

///
///Picking a cell up out of the tree and putting it down where the pointer is.
///
///This whole flow was taken out once and is back by request. What is checked here is what only a browser
///can see: that a picture follows the cursor at all, that the keys reach the thing in hand rather than
///whatever is chosen underneath it, and that a click leaves one more instance in the file. The record it
///writes is HierarchyTests' business, where an angle can be read rather than guessed at from an outline.
///
test.describe('carrying one in', () => {
    ///
    ///Enters the top cell, which is what makes placing mean anything.
    ///
    ///An SREF lives *inside* a structure, so there is nothing to place into until one is being edited -
    ///which is why the square is not offered before then, and is the first thing asserted below.
    ///
    async function intoTheTop(page) {
        await editTop(page);

        await expect(page.locator('.cellRowPlace[data-place="LEAF"]')).toHaveCount(1);
    }

    ///Picks up the leaf and puts it down on the right of the view.
    async function carryLeafIn(page, keys = []) {
        await intoTheTop(page);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        await expect(page.locator('#carryingCell')).toBeVisible();

        for (const key of keys)
            await page.keyboard.press(key);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width * 0.65), view.y + (view.height * 0.35));
        await page.mouse.down();
        await page.mouse.up();
    }

    ///
    ///Nothing offers to be picked up until a cell is open to put it in.
    ///
    ///And the cell being edited never offers itself: a structure placed inside itself is a library with no
    ///bottom, and the flattener would walk it to its depth limit rather than refuse it.
    ///
    test('a cell is offered only where it could actually go', async ({ page }) => {
        //Outside every cell there is nowhere to put one, which a file no longer opens in - see leaveCell.
        await leaveCell(page);

        await expect(page.locator('.cellRowPlace')).toHaveCount(0);

        await editTop(page);

        const offered = await page.locator('.cellRowPlace').evaluateAll(all => all.map(one => one.dataset.place));

        expect(offered).toEqual(['LEAF']);
    });

    ///The bar says what is in hand, which is all it has to say now that the four controls have gone.
    test('the bar says which cell is being carried', async ({ page }) => {
        await intoTheTop(page);

        await expect(page.locator('#carryingCell')).toHaveCount(0);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        await expect(page.locator('#carryingCell')).toContainText('LEAF');
    });

    ///
    ///It follows the pointer, drawn, which is the whole of what replaced the picker.
    ///
    ///Read off the transform rather than off a screenshot: the cell's markup is built once and moved by the
    ///browser - a cell is however many hundred shapes, and rebuilding it through the component on every
    ///pointer move would be a render per pixel. What changes as the pointer moves is that one attribute.
    ///
    test('the cell follows the pointer while it is carried', async ({ page }) => {
        await intoTheTop(page);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        //
        //Wait for the carry to have started before moving, which carryLeafIn above already does.
        //
        //Without it this reads #carriedCell in a race with the click that creates it, and the race is won
        //or lost on how long the layout took to settle - so it passed for a year and then started failing
        //when an unrelated panel changed width. A null transform there says nothing about whether the
        //cell follows the pointer, which is what this test is for.
        //
        await expect(page.locator('#carryingCell')).toBeVisible();

        const view = await page.locator('#gdsSVG').boundingBox();

        //
        //Two points clear of whatever is floating over the view, worked out rather than guessed.
        //
        //The tree has the left of the window and the selection panel floats over the top-left of the
        //drawing, and a pointer move landing on either never reaches the layout at all - which reads as
        //the cell refusing to follow rather than as a test aiming at the wrong place.
        //
        //It used to aim at 60% and 85% of the width, which cleared the panel by a margin that depended on
        //how wide the view happened to be. The layer sidebar going to a fixed width took fifty pixels off
        //the view, the panel's share of it grew, and 60% landed on the panel. Measured from the panel's
        //own right edge there is no fraction to get wrong.
        //
        const clear = await page.evaluate(() => {
            const panel = document.getElementById('selectionPanel');

            if (panel === null)
                return 0;

            return panel.getBoundingClientRect().right;
        });

        const from = Math.max(view.x + (view.width * 0.6), clear + 30);
        const to = Math.max(view.x + (view.width * 0.85), clear + 90);

        await page.mouse.move(from, view.y + (view.height * 0.3));

        const first = await page.locator('#carriedCell').getAttribute('transform');

        await page.mouse.move(to, view.y + (view.height * 0.6));

        const second = await page.locator('#carriedCell').getAttribute('transform');

        expect(first).not.toBeNull();
        expect(second).not.toBe(first);

        //And it is the cell that is being carried, rather than an empty group following the cursor.
        expect(await page.locator('#carriedCell *').count()).toBeGreaterThan(0);

        //
        //**And the thing in hand does not repaint the layout it is held over.**
        //
        //The carried picture is SvgWriter markup like any other, which means it carries a <style> block -
        //and a <style> inside an inline SVG is hoisted into the document, so an unscoped one would set every
        //shape on those layers to the carried cell's own opacity of 1 for as long as something was in hand.
        //The picture token is what stops it; see SvgWriter.PictureToken.
        //
        const painted = await page.evaluate(() => {
            const layer = document.querySelector('#gdsSVG path[class*="l"]');
            const ghost = document.querySelector('#carriedCell path');

            let ghostOpacity = null;

            if (ghost !== null)
                ghostOpacity = getComputedStyle(ghost).opacity;

            return {
                layout: getComputedStyle(layer).opacity,
                ghost: ghostOpacity
            };
        });

        expect(painted.ghost).toBe('1');
        expect(painted.layout).not.toBe('1');
    });

    ///Ctrl+R turns the picture as well as the record, so what lands is what was being carried.
    test('Ctrl+R turns what is being carried, and Ctrl+M mirrors it', async ({ page }) => {
        await intoTheTop(page);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        //Clear of the panels, for the reason above.
        await page.mouse.move(view.x + (view.width * 0.75), view.y + (view.height * 0.5));

        expect(await page.locator('#carriedCell').getAttribute('transform')).not.toContain('rotate');

        await page.keyboard.press('Control+r');

        expect(await page.locator('#carriedCell').getAttribute('transform')).toContain('rotate(90)');

        await page.keyboard.press('Control+m');

        //Both at once, and the mirror written after the turn because SVG applies them right to left - see
        //placeCarried, where the order is what keeps the cell spinning on the spot rather than about the
        //cursor.
        expect(await page.locator('#carriedCell').getAttribute('transform')).toContain('scale(1 -1)');
    });

    ///
    ///Escape puts it back, and takes the picture with it.
    ///
    ///Answered in the interop rather than through the shortcut handler, so it cannot also clear the
    ///selection on the way past: putting down what you are carrying and throwing away what was chosen are
    ///two things, and one press should not be both.
    ///
    test('escape puts a carried cell back', async ({ page }) => {
        await intoTheTop(page);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width * 0.75), view.y + (view.height * 0.5));

        await expect(page.locator('#carriedCell')).toHaveCount(1);

        await page.keyboard.press('Escape');

        await expect(page.locator('#carryingCell')).toHaveCount(0);
        await expect(page.locator('#carriedCell')).toHaveCount(0);

        //And nothing was placed by the press that put it back.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///
    ///**A carried cell lands as one more instance.**
    ///
    ///The square in this fixture is symmetric, so what is checked here is that a fifth shape arrives at all
    ///and that the file survives it - the angle itself is checked in HierarchyTests, where the record can be
    ///read rather than guessed at from an outline.
    ///
    test('putting one down adds an instance', async ({ page }) => {
        await carryLeafIn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(5);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place cell/);
    });

    ///Ctrl+R turns what is in hand. It replaced a dropdown of four angles, and turns the picture with it.
    test('turning it before it lands adds one too', async ({ page }) => {
        await carryLeafIn(page, ['Control+r']);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(5);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place cell/);
    });

    test('mirroring it before it lands adds one, and undoes', async ({ page }) => {
        const before = await corners(page);

        await carryLeafIn(page, ['Control+m']);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(5);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => corners(page), { timeout: 15000 }).toBe(before);
    });

    ///
    ///It stays in hand afterwards, so a row of the same cell is a row of clicks.
    ///
    ///Which is what the old picker's kept-orientation was for, done by not letting go rather than by
    ///remembering a choice across placements.
    ///
    test('it is still in hand after one lands', async ({ page }) => {
        await carryLeafIn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(5);

        await expect(page.locator('#carryingCell')).toBeVisible();

        //
        //And the *picture* is still in hand, not only the state behind it.
        //
        //Placing rebuilds the drawing, and what follows the cursor has to come through that: the failure
        //this rules out is a flow that goes on placing cells with nothing on screen to aim with. It does
        //not prove restoreCarried is what keeps it there - measured, the group survives every render this
        //view has without it, because it is appended beside the region Blazor owns rather than inside it.
        //
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + (view.width * 0.8), view.y + (view.height * 0.6));

        await expect(page.locator('#carriedCell')).toHaveCount(1);
        expect(await page.locator('#carriedCell *').count()).toBeGreaterThan(0);

        //A second click, a second instance.
        await page.mouse.down();
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(6);
    });

    ///
    ///It is held by the middle of its shapes rather than by its origin.
    ///
    ///A cell's origin is wherever the file says it is, and grouping shapes into one keeps the coordinates
    ///they already had - so a cell made from something drawn two thousand units out has an origin two
    ///thousand units from anything in it. Held by that, the picture hangs off the side of the screen while
    ///the cursor holds an empty patch of nothing.
    ///
    test('a carried cell sits under the pointer, not at its own origin', async ({ page }) => {
        await intoTheTop(page);

        await page.locator('.cellRowPlace[data-place="LEAF"]').click();

        const view = await page.locator('#gdsSVG').boundingBox();
        const at = { x: view.x + (view.width * 0.7), y: view.y + (view.height * 0.45) };

        await page.mouse.move(at.x, at.y);

        const drawn = await page.locator('#carriedCell').boundingBox();

        expect(drawn).not.toBeNull();

        //The pointer is inside what it is carrying, which is the whole claim.
        expect(at.x).toBeGreaterThan(drawn.x);
        expect(at.x).toBeLessThan(drawn.x + drawn.width);
        expect(at.y).toBeGreaterThan(drawn.y);
        expect(at.y).toBeLessThan(drawn.y + drawn.height);
    });
});

///
///Deleting more than one placement at once.
///
///**A band across a layout of placed cells offered nothing at all.** None of what it catches is the current
///cell's own, so the shape Delete never appeared - and the instance one asked whether the selection was
///*exactly one* placement, which it is not, so that never appeared either. Copy was the only thing on the
///row, and the Delete key did nothing and said nothing.
///
///Turning and moving still ask the stricter question, and should: two placements turned about their own
///middles are two different pictures and there is no single answer. Taking records out has no such problem.
///
test.describe('deleting several instances', () => {
    ///
    ///A band over the placements alone, clear of the shape the top cell owns.
    ///
    ///Which matters: a selection holding any of the cell's own shapes is a selection of *shapes*, and the
    ///shape Delete wins - correctly. In this fixture the top's own square is on the left and the three
    ///placements are to the right of it.
    ///
    async function bandTheInstances(page) {
        await editTop(page);

        await page.keyboard.press('Escape');

        //
        //Aimed at the geometry rather than at a fraction of the view.
        //
        //A band from "42% across" is a band at the mercy of the panels: these open with the cell tree
        //docked, which takes 240 pixels off the left, and the same fraction lands somewhere else with it
        //open than without. It caught the top's own square, so the shape Delete won - correctly, and the
        //test then failed looking for the instance one.
        //
        //Once TOP is being edited its own shapes are marked inContext and everything reached through a
        //placement is outOfContext, which is what tells the two apart without knowing where either is.
        //
        const drawn = await page.evaluate(() => {
            const box = one => {
                const found = document.querySelector(one);

                if (found === null)
                    return null;

                const rect = found.getBoundingClientRect();

                return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
            };

            return {
                own: box('#gdsSVG path.inContext'),
                placed: box('#gdsSVG path.outOfContext'),
                canvas: box('#gdsSVG')
            };
        });

        expect(drawn.own, 'the top cell draws nothing of its own').not.toBeNull();
        expect(drawn.placed, 'nothing is drawn through a placement').not.toBeNull();

        //The fixture puts the top's square to the left of its placements. If that ever stops being true
        //this band cannot separate them, and saying so beats failing three tests further down.
        expect(drawn.placed.left, 'the fixture no longer separates the two along x')
            .toBeGreaterThan(drawn.own.right);

        //
        //**Dragged from the right, because the left is under the selection panel.**
        //
        //The panel is an overlay on the left of the canvas and it is wide: with a shape already chosen and
        //the cell tree docked it reaches x 686, past the left edge of the placements at 646. A press that
        //lands on it never reaches the SVG at all, so the band never starts - which reads as the selection
        //refusing to change rather than as a test aiming at furniture. Measured with elementFromPoint after
        //it did exactly that.
        //
        //Starting clear of the shapes on the right and dragging back over them keeps the press on the
        //drawing; the pointer is captured from there, so passing over the panel on the way costs nothing.
        //
        const from = Math.min(drawn.placed.right + 25, drawn.canvas.right - 6);
        const to = (drawn.own.right + drawn.placed.left) / 2;

        await page.mouse.move(from, drawn.placed.top - 25);
        await page.mouse.down();
        await page.mouse.move(from - 20, drawn.placed.top - 10, { steps: 3 });
        await page.mouse.move(to, drawn.placed.bottom + 25, { steps: 12 });
        await page.mouse.up();

        await expect(page.locator('#deleteInstance')).toHaveCount(1);
    }

    ///<summary>The button appears, and says how many it is about to take out.</summary>
    test('the panel offers to delete every instance the band caught', async ({ page }) => {
        await bandTheInstances(page);

        await expect(page.locator('#deleteInstance')).toHaveText(/Delete 3 instances/);

        //And the shape Delete is not also offered: none of this is the cell's own.
        await expect(page.locator('#deleteShape')).toHaveCount(0);
    });

    ///
    ///The Delete key does it too, which is the half that was silent.
    ///
    ///It goes through the same method the button does, so the failure was one thing rather than two - but a
    ///key that does nothing is worse than a button that is missing, because nothing on screen says why.
    ///
    test('the Delete key takes them all out', async ({ page }) => {
        await bandTheInstances(page);

        await page.keyboard.press('Delete');

        //
        //Two shapes left, not one.
        //
        //The top's own square, and LEAF - which nothing places any more, so the flattener walks it as a
        //top-level cell in its own right and draws it at its own coordinates. That is the flattener's rule
        //rather than a placement that survived, and it is checked in HierarchyTests where the library can
        //be read directly. It is also exactly what sends somebody looking for a bug that is not there.
        //
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(2);

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Delete/);
    });

    ///<summary>And one press of undo brings all three back, because it went on the stack as one step.</summary>
    test('undoing puts all of them back', async ({ page }) => {
        await bandTheInstances(page);

        await page.locator('#deleteInstance').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(2);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///
    ///A selection holding one of the cell's own shapes is a selection of shapes.
    ///
    ///Shapes win everywhere else here, and this is the case that says so: a band over the whole view
    ///catches the top's square as well, and what it offers is the shape Delete rather than the instance one.
    ///
    test('a band that also catches the cell own shapes deletes shapes', async ({ page }) => {
        await editTop(page);

        await page.keyboard.press('Escape');

        //Everything drawn, both kinds, so the precedence has something to decide between.
        const all = await page.evaluate(() => {
            const rect = document.querySelector('#gdsSVG').getBoundingClientRect();

            return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
        });

        await page.mouse.move(all.left + 6, all.top + 6);
        await page.mouse.down();
        await page.mouse.move(all.right - 6, all.bottom - 6, { steps: 12 });
        await page.mouse.up();

        await expect(page.locator('#deleteShape')).toHaveCount(1);
        await expect(page.locator('#deleteInstance')).toHaveCount(0);
    });
});

///
///A band over a cell the current one does not hold.
///
///**Reported from a file whose library is two top cells rather than one placed in the other.** Nine shapes
///caught, none of them the cell being edited owns, no placement to take hold of - so the panel offered Copy
///and nothing else, and the Delete key was silent. A click on one of those shapes already entered its cell;
///a band did not, which is the whole of the difference.
///
///Reached here by taking the placements out first, which leaves the placed cell referenced by nothing and
///so a top-level cell in its own right - the same shape of library, arrived at through the app rather than
///through a fixture written for it.
///
test.describe('a band over another top cell', () => {
    ///Leaves the library as two tops: TOP with its own square, and LEAF with nobody placing it.
    async function untilTwoTops(page) {
        await editTop(page);

        await page.keyboard.press('Escape');

        const drawn = await page.evaluate(() => {
            const box = one => {
                const found = document.querySelector(one);

                if (found === null)
                    return null;

                const rect = found.getBoundingClientRect();

                return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
            };

            return { own: box('#gdsSVG path.inContext'), placed: box('#gdsSVG path.outOfContext'), canvas: box('#gdsSVG') };
        });

        //From the right, clear of the selection panel; see the note on bandTheInstances above.
        const from = Math.min(drawn.placed.right + 25, drawn.canvas.right - 6);

        await page.mouse.move(from, drawn.placed.top - 25);
        await page.mouse.down();
        await page.mouse.move(from - 20, drawn.placed.top - 10, { steps: 3 });
        await page.mouse.move((drawn.own.right + drawn.placed.left) / 2, drawn.placed.bottom + 25, { steps: 12 });
        await page.mouse.up();

        await expect(page.locator('#deleteInstance')).toHaveCount(1);

        await page.locator('#deleteInstance').click();

        //Two shapes: the top's own, and LEAF drawn in its own right now that nothing places it.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(2);

        await page.keyboard.press('Escape');
    }

    ///
    ///The band lands in the cell it caught, and what it caught is then editable.
    ///
    ///Which is what a click on one of those shapes has always done. The two agreeing is the claim.
    ///
    test('a band over it enters it, and the shapes become editable', async ({ page }) => {
        await untilTwoTops(page);

        const orphan = await page.evaluate(() => {
            const found = [...document.querySelectorAll('#gdsSVG path')]
                .filter(one => /(^|\s)l-?\d+_\d+(\s|$)/.test(one.getAttribute('class') || ''))
                .filter(one => one.classList.contains('outOfContext'));

            if (found.length === 0)
                return null;

            const rect = found[0].getBoundingClientRect();

            return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
        });

        expect(orphan, 'nothing is drawn outside the cell being edited').not.toBeNull();

        const canvas = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(Math.min(orphan.right + 30, canvas.x + canvas.width - 6), orphan.top - 30);
        await page.mouse.down();
        await page.mouse.move(orphan.left - 10, orphan.bottom + 30, { steps: 10 });
        await page.mouse.up();

        //Into the cell it caught, which the breadcrumb says out loud.
        await expect.poll(async () => (await page.locator('.contextCrumbOn').textContent()).trim(),
            { timeout: 15000 }).toBe('LEAF');

        //And the shapes are the cell's own now, so the whole row of actions is offered rather than Copy.
        await expect(page.locator('#deleteShape')).toHaveCount(1);
        await expect(page.locator('#cutShapes')).toHaveCount(1);
    });

    ///<summary>And the Delete key works there, which is what the report was actually about.</summary>
    test('the Delete key then takes them out', async ({ page }) => {
        await untilTwoTops(page);

        const orphan = await page.evaluate(() => {
            const found = document.querySelector('#gdsSVG path.outOfContext');

            if (found === null)
                return null;

            const rect = found.getBoundingClientRect();

            return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
        });

        const canvas = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(Math.min(orphan.right + 30, canvas.x + canvas.width - 6), orphan.top - 30);
        await page.mouse.down();
        await page.mouse.move(orphan.left - 10, orphan.bottom + 30, { steps: 10 });
        await page.mouse.up();

        await expect(page.locator('#deleteShape')).toHaveCount(1);

        await page.keyboard.press('Delete');

        //The leaf's square is gone; the top's own is what is left.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(1);
    });

    ///
    ///A band that catches the current cell's own shapes stays where it is.
    ///
    ///Shapes of the cell being edited win everywhere else here, and leaving on a band that holds any of them
    ///would throw away the selection somebody just made.
    ///
    test('a band holding the cell own shapes does not leave it', async ({ page }) => {
        await untilTwoTops(page);

        const canvas = await page.locator('#gdsSVG').boundingBox();

        //Everything, both cells.
        await page.mouse.move(canvas.x + 6, canvas.y + 6);
        await page.mouse.down();
        await page.mouse.move(canvas.x + canvas.width - 6, canvas.y + canvas.height - 6, { steps: 12 });
        await page.mouse.up();

        await expect(page.locator('#selectionPanel')).toBeVisible();

        expect((await page.locator('.contextCrumbOn').textContent()).trim()).toBe('TOP');
    });
});
