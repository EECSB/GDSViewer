//Drawing a new shape into a cell, and dragging one of a shape's corners.
//
//The edits are covered in LayoutEditTests, including that an open outline is closed and that a ring's
//repeated corner moves with its twin. What is only checkable here is the wiring: that a rectangle dragged
//on screen lands in the cell being edited, that a handle appears on each corner and only where an edit is
//allowed, and that both go through the same undo as everything else.
const { test, expect } = require('@playwright/test');
const { gotoApp, shapeCount, shapeBox, shapePoints, layersListed, snapToGrid, chooseShape, openGridMenu, openedOnItsOwn, leaveCell, uploadFile } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);

    await uploadFile(page, 'e2e/fixtures/placed.gds');

    await openedOnItsOwn(page);

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

    await page.locator('#selectTool').click();
});

///Clicks shapes until one from the placed cell is picked out, then enters that cell.
async function enterLeaf(page) {
    for (let i = 0; i < 4; i++) {
        const box = await shapeBox(page, i);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        if ((await page.locator('#selectionPanel').textContent()).includes('TOP > LEAF')) {
            //Again, on the same shape: the first click took hold of the placement, the second goes inside
            //it. See descendsOnClick in Viewer2DSvg.
            await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

            await expect(page.locator('#contextBar')).toContainText('LEAF');

            return;
        }
    }

    throw new Error('no shape from the placed cell was found');
}

async function chooseInContext(page) {
    const box = await shapeBox(page, 0, 'inContext');

    await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

    await expect(page.locator('#selectionPanel')).toBeVisible();

    return box;
}

test.describe('drawing', () => {
    ///
    ///The Draw tool is there before a cell is, and says so when it is pressed.
    ///
    ///It was left out of the bar entirely first, which is a worse puzzle than a tool that does nothing:
    ///five icons became four, the row changed width, and the one that went was the one somebody was
    ///looking for - which reads as the button having been taken away. It was disabled next, with the
    ///reason in its tooltip. Better, but a tooltip is read by somebody who already suspects there is
    ///something to read, and pressing a disabled button produces nothing at all - the same silence the
    ///missing button had, and the way this was reported: "the draw tool just doesn't put down anything".
    ///
    ///So it is live, and the press is what answers. The `D` shortcut answers the same way: it never went
    ///near the button, so a disabled one could not have stopped it either.
    ///
    test('the Draw tool says why it will not draw rather than going quiet', async ({ page }) => {
        await leaveCell(page);

        await expect(page.locator('#drawTool')).toBeVisible();
        await expect(page.locator('#drawTool')).toBeEnabled();
        await expect(page.locator('#drawTool')).toHaveAttribute('title', /Open a cell first/);

        await page.locator('#drawTool').click();

        //Named, and the way out named with it.
        await expect(page.locator('#drawRefusal')).toBeVisible();
        await expect(page.locator('#drawRefusal')).toContainText('a new shape goes in a cell');
        await expect(page.locator('#drawRefusal')).toContainText('cell tree');

        //And nothing was taken up: the pencil is not lit and Pan still is.
        await expect(page.locator('#drawTool')).not.toHaveClass(/toolButtonOn/);

        await enterLeaf(page);

        //Entering a cell answers it, so the line goes without being dismissed.
        await expect(page.locator('#drawRefusal')).toHaveCount(0);

        await expect(page.locator('#drawTool')).toBeEnabled();
        await expect(page.locator('#drawTool')).toHaveAttribute('title', /Drag out a rectangle/);
    });

    ///The same refusal from the keyboard, which never touched the button and so was never gated by it.
    test('the Draw shortcut says the same thing', async ({ page }) => {
        await leaveCell(page);

        await page.locator('#gdsSVG').click({ position: { x: 5, y: 5 } });
        await page.keyboard.press('d');

        await expect(page.locator('#drawRefusal')).toBeVisible();

        //And it can be put away, like the rule check's message one place over.
        await page.locator('#drawRefusalClose').click();

        await expect(page.locator('#drawRefusal')).toHaveCount(0);
    });

    ///
    ///**Exactly one tool is on, always.**
    ///
    ///The tools are a group of alternatives and the highlight is what says which you are in. Adding Draw
    ///left Pan lit alongside it, because Pan decides it is on by listing the others and the new one was
    ///not on the list - a shape of bug that comes back every time a tool is added, so this asks the
    ///question about all of them rather than about the pair that happened to be wrong.
    ///
    test('exactly one tool is highlighted whichever is chosen', async ({ page }) => {
        await enterLeaf(page);

        //Scoped to the tools. The same highlight marks which shape is being drawn and whether the grid
        //is on, and neither of those is a tool - counting them made "exactly one" mean nothing.
        const lit = async () => page.locator('#toolGroup .toolButton.toolButtonOn').count();

        for (const tool of ['Pan', 'Measure', 'Select', 'Draw']) {
            await page.locator('#toolGroup').getByRole('button', { name: tool, exact: true }).click();

            expect(await lit(), `after choosing ${tool}`).toBe(1);

            await expect(page.locator('#toolGroup').getByRole('button', { name: tool, exact: true }))
                .toHaveClass(/toolButtonOn/);
        }
    });

    ///The sidebar is the layer picker now; the toolbar's dropdown has gone. See draw-layer.spec.
    test('the layers the file has are the ones offered to draw on', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await expect(page.locator('.layerRowPickable')).not.toHaveCount(0);

        //placed.gds draws on 65/20 and 67/20.
        const offered = await layersListed(page);

        expect(offered).toContain('65/20');
        expect(offered).toContain('67/20');
    });

    ///
    ///Drawn into the cell being edited, so it appears once per placement of that cell - three times here,
    ///from one drag.
    ///
    test('a dragged rectangle is added to the cell and appears in every instance', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 120, view.y + 120);
        await page.mouse.down();
        await page.mouse.move(view.x + 220, view.y + 200, { steps: 6 });
        await page.mouse.up();

        //Four shapes became seven: one new square in each of the cell's three placements.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
    });

    test('a drawn shape can be undone and redone', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 120, view.y + 120);
        await page.mouse.down();
        await page.mouse.move(view.x + 200, view.y + 190, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);

        await page.locator('#redoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
    });

    ///
    ///A stray click has no area, and should not put an empty shape into the file.
    ///
    ///**Without snapping.** With the grid snapped to, a click fills the square it landed in - that is the
    ///shortcut for the commonest thing there is to draw, and grid.spec covers it. There is no square to
    ///mean when nothing is snapping, so a click there is still what it always was: nothing.
    ///
    test('a click without a drag draws nothing while nothing is snapping', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        //Snapping is on out of the box now, and this is the case where nothing snaps at all.
        await snapToGrid(page, false);
        await openGridMenu(page);
        await expect(page.locator('#snapToggle')).not.toHaveClass(/shapePickOn/);

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 150, view.y + 150);

        expect(await shapeCount(page)).toBe(4);
        await expect(page.locator('#undoEdit')).toHaveCount(0);
    });

    test('a drawn shape is in the file that is downloaded', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 120, view.y + 120);
        await page.mouse.down();
        await page.mouse.move(view.x + 210, view.y + 195, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        const started = page.waitForEvent('download');

        await page.locator('#downloadGds').click();

        const path = await (await started).path();

        await uploadFile(page, path);

        await openedOnItsOwn(page);

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(7);
    });
});

test.describe('vertex handles', () => {
    test('a shape in the cell being edited gets a handle on each corner', async ({ page }) => {
        await enterLeaf(page);
        await chooseInContext(page);

        //A closed square: five corners, the last repeating the first.
        await expect(page.locator('#vertexHandles circle')).toHaveCount(5);
    });

    ///
    ///A shape outside the cell gets handles, because clicking it moved the work to *its* cell.
    ///
    ///It used to get none: a shape you were not editing could be chosen but not changed, and pressing Edit
    ///was what let you at it. That button is gone and a click means the cell of the shape under it - so
    ///what this now checks is that the handles follow the context rather than lag behind it. Handles on a
    ///shape that cannot be changed would be the real bug, and it is the one the old test was guarding.
    ///
    test('a shape outside the cell gets them once the click moves there', async ({ page }) => {
        await enterLeaf(page);

        const box = await shapeBox(page, 0, 'outOfContext');

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#contextBar')).toContainText('TOP');

        //Its own cell now, so it can be reshaped - and the handles say so.
        await expect.poll(async () => page.locator('#vertexHandles circle').count(), { timeout: 15000 })
            .toBeGreaterThan(0);
    });

    ///
    ///Nothing has handles until something is chosen, which is now the same click that enters the cell.
    ///
    ///The old version of this asserted that a first click gave no handles, because entering was a separate
    ///press. It is not any more - a shape the top structure owns outright descends at once - so what is
    ///left to check is the state before any click at all: no selection, nothing to reshape, no handles.
    ///
    test('nothing has handles before anything is chosen', async ({ page }) => {
        await expect(page.locator('#selectionPanel')).toHaveCount(0);
        await expect(page.locator('#vertexHandles')).toHaveCount(0);

        //And one click is enough to get them, which is the change that made the button unnecessary.
        const box = await shapeBox(page);

        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect.poll(async () => page.locator('#vertexHandles circle').count(), { timeout: 15000 })
            .toBeGreaterThan(0);
    });

    ///
    ///Dragging one handle moves one corner, and leaves the rest of the outline alone - which is what makes
    ///it vertex editing rather than another way to move the shape.
    ///
    test('dragging a handle moves one corner', async ({ page }) => {
        await enterLeaf(page);
        await chooseInContext(page);

        const before = await shapePoints(page, 0, 'inContext');

        const handle = await page.locator('#vertexHandles circle').nth(1).boundingBox();

        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2));
        await page.mouse.down();
        await page.mouse.move(handle.x + (handle.width / 2) + 50, handle.y + (handle.height / 2) + 40, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapePoints(page, 0, 'inContext'), { timeout: 15000 })
            .not.toBe(before);

        const after = await shapePoints(page, 0, 'inContext');

        const was = before.split(' ');
        const now = after.split(' ');

        expect(now).toHaveLength(was.length);

        //Exactly one corner of the five moved.
        const moved = now.filter((corner, i) => corner !== was[i]);

        expect(moved).toHaveLength(1);
    });

    ///
    ///**The corner that is written twice.**
    ///
    ///A GDSII boundary repeats its opening corner at the end to close the ring, so two of the five handles
    ///sit on the same point. Dragging whichever is on top has to move both copies - leave one behind and
    ///the outline opens into a hook, which draws as a filled shape with a slit in it and reads back as a
    ///perfectly valid file.
    ///
    test('dragging the corner a ring closes on moves both copies of it', async ({ page }) => {
        await enterLeaf(page);
        await chooseInContext(page);

        const before = (await shapePoints(page, 0, 'inContext')).split(' ');

        //The first and last are the same corner, which is what makes this the case worth testing.
        expect(before[0]).toBe(before[before.length - 1]);

        const handle = await page.locator('#vertexHandles circle').first().boundingBox();

        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2));
        await page.mouse.down();
        await page.mouse.move(handle.x + (handle.width / 2) - 40, handle.y + (handle.height / 2) - 30, { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapePoints(page, 0, 'inContext'), { timeout: 15000 })
            .not.toBe(before.join(' '));

        const after = (await shapePoints(page, 0, 'inContext')).split(' ');

        //Still closed, and it is the corner that moved rather than some other one.
        expect(after[0]).toBe(after[after.length - 1]);
        expect(after[0]).not.toBe(before[0]);
    });

    test('a corner drag can be undone', async ({ page }) => {
        await enterLeaf(page);
        await chooseInContext(page);

        const before = await shapePoints(page, 0, 'inContext');

        const handle = await page.locator('#vertexHandles circle').nth(2).boundingBox();

        await page.mouse.move(handle.x + (handle.width / 2), handle.y + (handle.height / 2));
        await page.mouse.down();
        await page.mouse.move(handle.x + (handle.width / 2) - 45, handle.y + (handle.height / 2), { steps: 6 });
        await page.mouse.up();

        await expect.poll(async () => shapePoints(page, 0, 'inContext'), { timeout: 15000 })
            .not.toBe(before);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapePoints(page, 0, 'inContext'), { timeout: 15000 })
            .toBe(before);
    });
});

///
///Drawing onto a layer and becoming part of what is already there.
///
///The union itself is BooleansTests' problem and the Union button's; what is only checkable here is the
///switch - that it changes what a drag does rather than what a later button press does, that it costs one
///press of undo and not two, and that a shape drawn clear of everything is still simply a shape.
///
///Counted in threes, because LEAF is placed three times: one shape drawn into it appears three times, and
///two that become one appear three times rather than six.
///
test.describe('joining as you draw', () => {
    //Clear of the cell's own geometry, so what merges is what was drawn rather than what was there.
    const CLEAR = { x: 320, y: 300 };

    ///Drags out a rectangle of a fixed size, offset from the clear corner.
    async function drawAt(page, dx, dy) {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + CLEAR.x + dx, view.y + CLEAR.y + dy);
        await page.mouse.down();
        await page.mouse.move(view.x + CLEAR.x + dx + 90, view.y + CLEAR.y + dy + 90, { steps: 6 });
        await page.mouse.up();
    }

    test('the switch is there for the shapes that enclose an area, and not the others', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        for (const shape of ['rectangleShape', 'polygonShape', 'ellipseShape']) {
            await chooseShape(page, '#' + shape);

            await expect(page.locator('#joinToggle'), `for #${shape}`).toBeVisible();
        }

        //
        //A path is a centerline and a width, and a label is a string. Neither is an area to add to.
        //
        //Hidden rather than gone: the switch keeps its place in the bar whichever shape is in hand, because
        //taking it out and putting it back moved every control to its right by 92 pixels each time.
        //
        for (const shape of ['pathShape', 'labelShape']) {
            await chooseShape(page, '#' + shape);

            await expect(page.locator('#joinToggle'), `for #${shape}`).toBeHidden();
        }
    });

    ///
    ///Drawing and dragging are two ways of putting a shape somewhere, so the switch is offered for both -
    ///and not for the tools that move the view rather than anything in it.
    ///
    test('the switch is there for the tools that can bring shapes together', async ({ page }) => {
        await enterLeaf(page);

        for (const tool of ['#selectTool', '#moveTool', '#drawTool']) {
            await page.locator(tool).click();

            await expect(page.locator('#joinToggle'), `for ${tool}`).toBeVisible();
        }

        //Pan and Measure move what you are looking at, not what is in it - so the switch is hidden, and its
        //place in the bar is kept so that nothing shifts when it comes back.
        for (const tool of ['Pan', 'Measure']) {
            await page.locator('#toolGroup').getByRole('button', { name: tool, exact: true }).click();

            await expect(page.locator('#joinToggle'), `for ${tool}`).toBeHidden();
        }

        //And the controls past it do not move when it does, which is the whole reason it is hidden rather
        //than removed.
        const parked = await page.locator('#gridPitch').boundingBox();

        await page.locator('#selectTool').click();
        await expect(page.locator('#joinToggle')).toBeVisible();

        expect((await page.locator('#gridPitch').boundingBox()).x).toBe(parked.x);
    });

    ///One switch, so turning it on while drawing leaves it on when the drag is with Move.
    test('the switch is the same one whichever tool is in hand', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();
        await page.locator('#joinToggle').click();

        await expect(page.locator('#joinToggle')).toHaveClass(/toolButtonOn/);

        await page.locator('#moveTool').click();

        await expect(page.locator('#joinToggle')).toHaveClass(/toolButtonOn/);
    });

    test('is off until it is asked for', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await expect(page.locator('#joinToggle')).not.toHaveClass(/toolButtonOn/);
    });

    ///Two overlapping rectangles, one shape: the second is absorbed rather than laid over the first.
    test('a shape drawn over another on the same layer becomes one with it', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();
        await page.locator('#joinToggle').click();

        await drawAt(page, 0, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        //Overlapping the first by half.
        await drawAt(page, 45, 45);

        //Still seven. Had it not joined there would be ten.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        //And what is there is bigger than either rectangle was, so it is the union rather than one of them
        //having been thrown away.
        const box = await shapeBox(page, -1);

        expect(box.width).toBeGreaterThan(100);
        expect(box.height).toBeGreaterThan(100);
    });

    test('and stays a second shape while the switch is off', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await drawAt(page, 0, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        await drawAt(page, 45, 45);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    ///
    ///**The whole run, not the one shape touched.**
    ///
    ///Two shapes can already be touching without being one - drawn before the switch was on, or brought
    ///together by a drag. A third drawn onto one of them makes all three one thing, so what comes out has to
    ///be one shape and not a pair. That is the case a single pass over the layer gets wrong and the reason
    ///joinedTo follows the chain.
    ///
    test('what is already touching comes along, not just what was drawn on', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        //Two overlapping rectangles that are still two shapes, because the switch was off for both.
        await drawAt(page, 0, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        await drawAt(page, 70, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);

        await page.locator('#joinToggle').click();

        //Onto the second only - clear of the first, which is reached through it.
        await drawAt(page, 140, 0);

        //One shape out of three. Following only what was drawn on would leave two.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
    });

    ///Nothing to join to is not a reason to draw nothing.
    test('a shape drawn clear of everything is simply added', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();
        await page.locator('#joinToggle').click();

        await drawAt(page, 0, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        //Well clear of the first, and of everything else.
        await drawAt(page, 200, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    ///
    ///**One press, not two.**
    ///
    ///The shape that was absorbed was never separately there, so an add followed by a merge would make
    ///somebody press undo twice to get back to a state they had drawn once.
    ///
    test('the join costs one press of undo', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();
        await page.locator('#joinToggle').click();

        await drawAt(page, 0, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        const alone = await shapePoints(page, -1);

        await drawAt(page, 45, 45);

        //Joined rather than laid over: still seven, and the shape that is there is not the one that was.
        await expect.poll(async () => shapePoints(page, -1), { timeout: 15000 }).not.toBe(alone);
        expect(await shapeCount(page)).toBe(7);

        await page.locator('#undoEdit').click();

        //The first rectangle, whole and by itself again.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
        expect(await shapePoints(page, -1)).toBe(alone);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(4);
    });

    ///
    ///Two rectangles well apart, so a drag can bring one onto the other.
    ///
    ///Drawn with the switch off and joined by the drag afterwards, which is the case this exists for: a
    ///shape that was already its own thing being made part of another by being moved.
    ///
    async function twoApart(page) {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await drawAt(page, 0, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        await drawAt(page, 200, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    }

    ///Takes hold of the shape at an offset from the clear corner and drags it by the distance given.
    async function dragFrom(page, dx, dy, byX, byY) {
        const view = await page.locator('#gdsSVG').boundingBox();
        const at = { x: view.x + CLEAR.x + dx + 45, y: view.y + CLEAR.y + dy + 45 };

        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#gdsSVG .shapeSelected')).not.toHaveCount(0);

        await page.mouse.move(at.x, at.y);
        await page.mouse.down();
        await page.mouse.move(at.x + byX, at.y + byY, { steps: 6 });
        await page.mouse.up();
    }

    ///A shape dragged onto another one on its layer becomes one with it, exactly as a drawn one does.
    test('a shape dragged onto another on the same layer becomes one with it', async ({ page }) => {
        await twoApart(page);

        await page.locator('#moveTool').click();
        await page.locator('#joinToggle').click();

        //The far rectangle back onto the near one, overlapping it by half.
        await dragFrom(page, 200, 0, -155, 0);

        //Ten shapes became seven: the pair is one shape in each of the cell's three placements.
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
    });

    test('and a dragged one stays a second shape while the switch is off', async ({ page }) => {
        await twoApart(page);

        await page.locator('#moveTool').click();

        await dragFrom(page, 200, 0, -155, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    ///
    ///**A short drag, where the shape ends up overlapping where it started.**
    ///
    ///What the search must not find is the shape being moved. Its own record still holds the coordinates it
    ///had before the drag, so a nudge onto a neighbor leaves the old footprint sitting under the new one -
    ///and a shape that finds itself goes into the union twice and is deleted twice. A long drag never
    ///notices, which is why this one is short.
    ///
    test('a shape nudged onto its neighbour does not find itself on the way', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await drawAt(page, 0, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        //Close enough that a small drag reaches it.
        await drawAt(page, 110, 0);
        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);

        await page.locator('#moveTool').click();
        await page.locator('#joinToggle').click();

        //Thirty pixels: far enough to touch the first rectangle, short enough to still cover most of where
        //this one was.
        await dragFrom(page, 110, 0, -30, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);

        //And one press puts both back, rather than one of them having gone twice.
        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    ///Nothing to land on is not a reason to refuse the move.
    test('a shape dragged into open space is simply moved', async ({ page }) => {
        await twoApart(page);

        await page.locator('#moveTool').click();
        await page.locator('#joinToggle').click();

        //Downwards, away from everything.
        await dragFrom(page, 200, 0, 0, 160);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move/);
    });

    ///
    ///**One press, not two.**
    ///
    ///The union is folded into the move rather than written after it, so undoing puts back two shapes with
    ///the dragged one where it started - rather than leaving it landed on its neighbor and un-joined.
    ///
    test('the joined move costs one press of undo', async ({ page }) => {
        await twoApart(page);

        await page.locator('#moveTool').click();
        await page.locator('#joinToggle').click();

        await dragFrom(page, 200, 0, -155, 0);

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(7);
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Move joined/);

        await page.locator('#undoEdit').click();

        await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(10);
    });

    ///Whether you are painting or placing is a decision about the work, like the grid pitch beside it.
    test('the switch survives a reload', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();
        await page.locator('#joinToggle').click();

        await expect(page.locator('#joinToggle')).toHaveClass(/toolButtonOn/);

        await page.goto('/');

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBe(4);

        //The cell being edited is deliberately not saved, so the way back in is the way in was.
        await page.locator('#selectTool').click();
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        await expect(page.locator('#joinToggle')).toHaveClass(/toolButtonOn/);
    });
});

///
///The five tools are pictures, and each still answers to its name.
///
///Both halves matter. The words came off the bar because five of them was the widest group on the one row
///the app has to share - and the specs above choose a tool by its accessible name, so an icon without an
///aria-label would not break the markup, it would break every one of them at once.
///
test.describe('the tools as icons', () => {
    test('each is a picture with a name a reader can say', async ({ page }) => {
        //Draw among them: it is in the bar whether or not a cell is open, which is the point of it being
        //disabled rather than absent.
        for (const tool of ['Pan', 'Measure', 'Select', 'Move', 'Draw']) {
            const button = page.locator('#toolGroup').getByRole('button', { name: tool, exact: true });

            await expect(button).toBeVisible();

            //A drawing, and no word beside it.
            await expect(button.locator('svg')).toHaveCount(1);
            expect((await button.textContent()).trim(), `${tool} still has its word`).toBe('');
        }
    });

    ///
    ///The library sits with them without being one of them.
    ///
    ///It is reached for in the middle of using the tools - a cell is picked up from it and carried onto the
    ///canvas - so it belongs beside them rather than alone at the far end of the bar. But exactly one tool
    ///is lit at a time and that is the only thing the group says, so a sixth button lit all the time would
    ///make the highlight mean nothing. It is in the group and out of the count.
    ///
    test('everything in the tool group is a tool, and one of them is lit', async ({ page }) => {
        const inTheGroup = await page.locator('#toolGroup > button').count();
        const tools = await page.locator('#toolGroup > button.toolButton').count();

        expect(inTheGroup).toBeGreaterThan(1);
        expect(tools).toBe(inTheGroup);

        await page.locator('#toolGroup').getByRole('button', { name: 'Select', exact: true }).click();

        expect(await page.locator('#toolGroup .toolButton.toolButtonOn').count()).toBe(1);
    });
});

///
///Which shape the pencil draws, behind the pencil.
///
///It was five words in the bar, present for as long as the tool was, whether or not anybody was about to
///change the answer. A question asked once belongs behind the thing that asks it.
///
test.describe('the shape menu', () => {
    test('it hangs under the pencil when Draw is chosen', async ({ page }) => {
        await enterLeaf(page);

        await expect(page.locator('#shapePicker')).toHaveCount(0);

        await page.locator('#drawTool').click();

        const menu = await page.locator('#shapePicker').boundingBox();
        const pencil = await page.locator('#drawTool').boundingBox();

        //Under it, not beside it.
        expect(menu.y).toBeGreaterThanOrEqual(pencil.y + pencil.height);

        //And no word left over it in the bar, which is what it replaced.
        await expect(page.locator('.toolbarLabel', { hasText: 'Shape' })).toHaveCount(0);
    });

    ///
    ///A press outside puts it away, and pointing at the pencil brings it back.
    ///
    ///**Dismissed by a press that does nothing else.** Pressing on the layout also draws, and an edit
    ///re-renders the bar for its own reasons - so a menu that closed for that reason instead would pass a
    ///test written against the canvas, which is exactly what happened: the dismissal was doing nothing at
    ///all, and the draw was hiding it. The layer sidebar is outside the menu and changes nothing.
    ///
    ///**And the press is dispatched rather than performed**, because the menu now also closes when the
    ///pointer leaves the column. Clicking the sidebar means moving there, which shuts it on the way - so
    ///a click would have gone back to proving nothing, this time with the leave doing the hiding. Sending
    ///the pointerdown without moving the mouse is the only way to ask about the press on its own, and it
    ///is also the case that matters: a touchscreen has no pointer to move off anything.
    ///
    test('a press outside puts it away, and the pencil brings it back', async ({ page }) => {
        await enterLeaf(page);

        await page.locator('#drawTool').click();

        await expect(page.locator('#shapePicker')).toBeVisible();

        const before = await shapeCount(page);

        await page.evaluate(() => {
            document.querySelector('#layerSidebar')
                .dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        });

        await expect(page.locator('#shapePicker')).toHaveCount(0);

        //Nothing was drawn by that press, so the menu closed on its own account.
        expect(await shapeCount(page)).toBe(before);

        //
        //Pointing at the pencil is enough to have it back - no second press needed.
        //
        //Away first, because the pointer never moved: it is still sitting on the pencil from the click that
        //opened the menu, and Playwright's hover on an element already under the pointer sends nothing at
        //all. Without this the reopening is asked for with an event that was never dispatched.
        //
        await page.mouse.move(4, 4);
        await page.locator('#drawTool').hover();

        await expect(page.locator('#shapePicker')).toBeVisible();
    });

    ///
    ///And the pointer can get from the pencil down onto it, which is what makes closing-on-leave usable.
    ///
    ///**The drop is a transparent border rather than a gap**, so the menu's own box touches the column and
    ///the pointer never leaves it. That distinction is the whole of this test: the first attempt hung the
    ///menu 12px lower and covered the gap with a ::before, which looks identical, is hit by
    ///elementFromPoint at every pixel of the gap - and still let the menu close halfway down. A pseudo-
    ///element is not an event target, so hovering one does not count as being inside the column.
    ///
    ///Stepped, or the move is one event at the destination and jumps clean over the thing being tested.
    ///
    test('the pointer can travel from the pencil onto the menu', async ({ page }) => {
        await enterLeaf(page);

        const pencil = await page.locator('#drawTool').boundingBox();

        await page.locator('#drawTool').click();

        const menu = await page.locator('#shapePicker').boundingBox();

        await page.mouse.move(pencil.x + (pencil.width / 2), menu.y + 30, { steps: 20 });

        await expect(page.locator('#shapePicker')).toBeVisible();

        //And the lines are live once you get there.
        await expect(page.locator('#ellipseShape')).toBeVisible();
    });

    ///
    ///Each line is drawn as well as named.
    ///
    ///Five words in a column is a list you have to read; the shapes they name are the one thing about them
    ///that can be shown instead.
    ///
    test('every line carries a drawing of its shape', async ({ page }) => {
        await enterLeaf(page);

        await page.locator('#drawTool').click();

        await expect(page.locator('#shapePicker')).toBeVisible();

        for (const line of ['#rectangleShape', '#polygonShape', '#ellipseShape', '#pathShape', '#labelShape'])
            await expect(page.locator(`${line} svg`)).toHaveCount(1);

        //The word stays: a drawing of a path and a drawing of a polygon are not that different at 14px.
        //Minus the chevron, which the rows that open onto their own settings carry to say they do.
        const says = (await page.locator('#ellipseShape').textContent()).replace('›', '').trim();

        expect(says).toBe('Ellipse');

        //And the chevron is on exactly those two, because it promises something the other three have not got.
        await expect(page.locator('#ellipseShape .shapePickArrow')).toHaveCount(1);
        await expect(page.locator('#pathShape .shapePickArrow')).toHaveCount(1);

        await expect(page.locator('#rectangleShape .shapePickArrow')).toHaveCount(0);
        await expect(page.locator('#polygonShape .shapePickArrow')).toHaveCount(0);
        await expect(page.locator('#labelShape .shapePickArrow')).toHaveCount(0);
    });

    ///
    ///And moving off it is enough, which is what the press used to be needed for.
    ///
    ///The menu is a question about which shape, asked when the pencil is chosen and answered by pointing at
    ///a line. Leaving the column without answering is an answer too - it means the one already marked - and
    ///a menu that sat over the layout until it was dismissed made you say so twice.
    ///
    test('moving onto the layout puts it away', async ({ page }) => {
        await enterLeaf(page);

        await page.locator('#drawTool').click();

        await expect(page.locator('#shapePicker')).toBeVisible();

        const view = await page.locator('#gdsSVG').boundingBox();

        //Moved, not pressed: the point is that nothing has to be pressed.
        await page.mouse.move(view.x + (view.width * 0.8), view.y + (view.height * 0.8), { steps: 10 });

        await expect(page.locator('#shapePicker')).toHaveCount(0);
    });

    ///
    ///Choosing one answers the question, so the menu goes.
    ///
    ///It opened to be chosen from. One that stayed up afterwards would sit over the layout waiting for a
    ///press it had already been given - and the pencil brings it straight back if the answer was wrong.
    ///
    test('choosing a shape marks it and closes the menu', async ({ page }) => {
        await enterLeaf(page);

        await page.locator('#drawTool').click();

        await chooseShape(page, '#ellipseShape');

        await expect(page.locator('#shapePicker')).toHaveCount(0);

        //And the choice took: the mark is on the ellipse when the menu is asked for again.
        await page.locator('#drawTool').hover();

        await expect(page.locator('#ellipseShape')).toHaveClass(/shapePickOn/);
        await expect(page.locator('#rectangleShape')).not.toHaveClass(/shapePickOn/);
    });

    ///The tools are pictures, and a heading is the only text a column of pictures would have.
    test('the tools have no word over them', async ({ page }) => {
        await expect(page.locator('#toolGroup')).toBeVisible();

        const labels = await page.locator('.toolbarLabel').allTextContents();

        expect(labels.map(one => one.trim())).not.toContain('Tool');
    });
});

///
///Merge is one switch, and one switch does not need a column heading over it.
///
///A column called Join holding a single button called Merge said the same thing twice and took two rows to
///do it. The picture says it once.
///
test.describe('the merge switch', () => {
    test('it is an icon with no word over it', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        const button = page.locator('#joinToggle');

        await expect(button).toBeVisible();
        await expect(button).toHaveAttribute('aria-label', 'Merge');

        //A drawing, and neither the word on it nor a heading above it.
        await expect(button.locator('svg')).toHaveCount(1);
        expect((await button.textContent()).trim()).toBe('');

        const labels = await page.locator('.toolbarLabel').allTextContents();

        expect(labels.map(one => one.trim())).not.toContain('Join');
    });

    ///Still a switch: the highlight is what says whether it is on, which is all it has to say.
    test('it still turns on and off', async ({ page }) => {
        await enterLeaf(page);
        await page.locator('#drawTool').click();

        const button = page.locator('#joinToggle');

        await expect(button).not.toHaveClass(/toolButtonOn/);

        await button.click();

        await expect(button).toHaveClass(/toolButtonOn/);

        await button.click();

        await expect(button).not.toHaveClass(/toolButtonOn/);
    });
});
