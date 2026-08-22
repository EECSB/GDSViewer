//The actions on the right button, where the pointer already is.
//
//What each action does to the file is covered where that action is covered - turning in TurningTests and
//turning.spec.js, the booleans in combining.spec.js, and so on. Nothing here re-tests any of that. What is
//only checkable in a browser is the menu itself: that the right button raises it rather than selecting
//something, that it goes away every way it should, and that a line of it reaches the same method its button
//reaches.
const { test, expect } = require('@playwright/test');
const { gotoExample, MOSFET, shapeCount, shapeBox } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);

    await expect(page.locator('#gdsSVG')).toBeVisible();
});

///
///Picks a shape of the cell being edited, and hands back where it is on screen.
///
///By trying each in turn rather than by aiming at one: a click lands on whichever shape is drawn on top at
///that point, which in a layout of overlapping rectangles is often not the one whose middle was aimed at.
///What was actually chosen is read back off the panel.
///
async function chooseAShape(page) {
    await page.locator('#selectTool').click();

    for (let nth = 0; nth < await shapeCount(page); nth++) {
        const box = await shapeBox(page, nth);

        if (box === null)
            continue;

        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        await page.mouse.click(at.x, at.y);

        if (await page.locator('#turnLeft').count() > 0)
            return at;
    }

    throw new Error('no shape of the cell being edited could be chosen');
}

///Right-clicks a point.
async function rightClick(page, at) {
    await page.mouse.click(at.x, at.y, { button: 'right' });
}

///
///A point on the canvas with nothing on it, well clear of the panel.
///
///Taken from the view's own box rather than written down: the panel sits over the left of it, so a
///coordinate picked by hand lands on the panel about as often as on the layout - and a right-click on the
///panel never reaches the view at all, which makes a test of what the view does with one pass for nothing.
///
async function emptyCanvas(page) {
    const box = await page.locator('#gdsSVG').boundingBox();

    return { x: box.x + (box.width * 0.85), y: box.y + (box.height * 0.15) };
}

///
///A point on a chosen shape that the panel is not sitting over.
///
///Both halves are needed. A right-click has to land on a shape that is already chosen or it replaces the
///selection, and it has to land on the *view* or it never reaches it at all - and the panel grows with the
///selection, so the more shapes are chosen the more of them it covers.
///
async function insideSelection(page) {
    const panel = await page.locator('#selectionPanel').boundingBox();
    const boxes = await page.locator('#gdsSVG .shapeSelected').evaluateAll(all =>
        all.map(one => JSON.parse(JSON.stringify(one.getBoundingClientRect()))));

    for (const box of boxes) {
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        if (at.x > panel.x + panel.width)
            return at;
    }

    throw new Error('every chosen shape is behind the panel');
}

///Adds a shape to what is chosen. mouse.click takes no modifiers, so the key is held around it.
async function addShape(page, at) {
    await page.keyboard.down('Control');

    await page.mouse.click(at.x, at.y);

    await page.keyboard.up('Control');
}

///
///What the menu is offering at its top level, as the words on its lines.
///
///Only the top level: the lines inside a submenu are descendants of the menu too, and counting those would
///make "the menu offers Union" true whether Union is on the face of it or three hovers away.
///
async function menuSays(page) {
    const said = await page.locator('#shapeMenu > .shapeMenuItem, #shapeMenu > .shapeMenuOpens > .shapeMenuParent').allTextContents();

    //The arrow a line carries when it opens onto more is decoration, not part of the word.
    return said.map(one => one.replace('›', '').trim());
}

///
///Opens the submenu of the line named, and hands back the panel.
///
///Hovered rather than clicked, which is what the line does: it opens on hover and has no action of its own,
///so a click on it would do nothing at all and a test that clicked would pass or fail for the wrong reason.
///
async function openSubmenu(page, named) {
    const opens = page.locator('#shapeMenu .shapeMenuOpens')
        .filter({ has: page.locator('.shapeMenuParent', { hasText: new RegExp(`^\\s*${named}`) }) });

    await opens.hover();

    await expect(opens.locator('.shapeSubmenu')).toBeVisible();

    return opens.locator('.shapeSubmenu');
}

test.describe('raising it', () => {
    test('the right button over a chosen shape opens the menu', async ({ page }) => {
        const at = await chooseAShape(page);

        await expect(page.locator('#shapeMenu')).toHaveCount(0);

        await rightClick(page, at);

        await expect(page.locator('#shapeMenu')).toBeVisible();
    });

    ///Bare canvas with nothing chosen has nothing to offer, so the press does nothing at all.
    test('with nothing chosen and nothing under it there is no menu', async ({ page }) => {
        await page.locator('#selectTool').click();

        await rightClick(page, await emptyCanvas(page));

        await expect(page.locator('#shapeMenu')).toHaveCount(0);
    });

    ///
    ///The right button picks what it lands on.
    ///
    ///It did not, and that is what made the menu feel broken rather than merely limited: right-clicking a
    ///shape nothing had selected opened nothing, because the menu was written to need a selection that only
    ///a left-click could make. Measured before the fix - no menu, and nothing chosen afterwards either.
    ///
    test('the right button chooses the shape it lands on', async ({ page }) => {
        const at = await chooseAShape(page);

        //Put it down again, so nothing is chosen when the right button is pressed.
        await page.keyboard.press('Escape');
        await page.mouse.click((await emptyCanvas(page)).x, (await emptyCanvas(page)).y);

        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(0);

        await rightClick(page, at);

        await expect(page.locator('#shapeMenu')).toBeVisible();
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);
    });

    ///
    ///And leaves a selection of several alone.
    ///
    ///The other half of the same rule: picking what is under the pointer must not mean picking *only* it,
    ///or asking for the menu over one of four shapes would throw the other three away before offering to
    ///line them up.
    ///
    test('the right button inside a selection of several keeps all of them', async ({ page }) => {
        await chooseAShape(page);

        //
        //Where the last shape was added, which is the one point known to be inside the selection.
        //
        //Not where the first was: Control toggles, and these shapes overlap, so a second click aimed at a
        //neighbor can land on the first one again and take it back out. The right-click then falls on a
        //shape that is genuinely not chosen, which correctly replaces the selection - and the test reads
        //that as the rule being broken. It did, first time.
        //
        let inside = null;

        for (let nth = 0; nth < await shapeCount(page); nth++) {
            if (await page.locator('#combineUnion').count() > 0)
                break;

            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            inside = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

            await addShape(page, inside);
        }

        const several = await page.locator('#gdsSVG .shapeSelected').count();

        expect(several).toBeGreaterThan(1);

        await rightClick(page, inside);

        await expect(page.locator('#shapeMenu')).toBeVisible();
        await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(several);
    });

    ///
    ///The press must not reach the tool.
    ///
    ///It did: the view's pointerdown handler knew about the middle button and not the right one, so a
    ///right-click with Select in hand ran the hit test first - which changed what was chosen before the
    ///menu about that selection had opened, and cleared it outright on the background.
    ///
    test('opening it does not change what is chosen', async ({ page }) => {
        await chooseAShape(page);

        const before = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        //On bare canvas, which is where a stray hit test would do the most damage: it would clear the
        //selection the menu is about to offer actions for.
        await rightClick(page, await emptyCanvas(page));

        await expect(page.locator('#shapeMenu')).toBeVisible();

        expect(await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element')).toBe(before);
    });
});

test.describe('putting it away', () => {
    test('Escape closes it', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        await expect(page.locator('#shapeMenu')).toBeVisible();

        await page.keyboard.press('Escape');

        await expect(page.locator('#shapeMenu')).toHaveCount(0);
    });

    test('a press outside closes it, and does nothing else', async ({ page }) => {
        const at = await chooseAShape(page);

        const before = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');

        await rightClick(page, at);

        await expect(page.locator('#shapeMenu')).toBeVisible();

        //
        //On bare canvas, clear of the menu.
        //
        //Both halves of that matter, and each was got wrong once. Near the click is inside the menu, which
        //opens at the pointer and runs down and right of it - that ran a line of the menu instead of
        //dismissing it. And a point still on the chosen shape proves nothing, because a press that leaked
        //through would choose the same shape again and leave the reading identical.
        //
        //What this does not pin down is whether the sheet closes on the press or on the click: the view
        //selects on the press, which the sheet takes either way. The click is still the right one - see the
        //markup - but that is an argument about which half of a gesture reaches a handler, and nothing
        //visible from out here tells the two apart.
        //
        const away = await emptyCanvas(page);
        const menu = await page.locator('#shapeMenu').boundingBox();

        expect(away.x < menu.x || away.x > menu.x + menu.width || away.y < menu.y || away.y > menu.y + menu.height).toBe(true);

        await page.mouse.click(away.x, away.y);

        await expect(page.locator('#shapeMenu')).toHaveCount(0);

        //The sheet took that press, so it did not also land on the layout and choose something else.
        expect(await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element')).toBe(before);
    });

    ///
    ///Opened low in the window, it stays inside it.
    ///
    ///The menu is as tall as the selection makes it, so this is not a size a stylesheet can know: three
    ///shapes offer twenty-two lines, and a right-click near the bottom of a short window put the last of
    ///them past the edge of the screen.
    ///
    test('it stays on screen when raised near the bottom', async ({ page }) => {
        await chooseAShape(page);

        const view = await page.locator('#gdsSVG').boundingBox();
        const window = page.viewportSize();

        //As low in the view as there is view to press on.
        await rightClick(page, { x: view.x + (view.width * 0.8), y: Math.min(view.y + view.height - 6, window.height - 6) });

        const menu = await page.locator('#shapeMenu').boundingBox();

        expect(menu.y + menu.height).toBeLessThanOrEqual(window.height);
    });

    test('choosing a line closes it', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        await (await openSubmenu(page, 'Turn')).locator('.shapeMenuItem', { hasText: 'Right' }).click();

        await expect(page.locator('#shapeMenu')).toHaveCount(0);
    });
});

test.describe('what it offers', () => {
    test('the same actions the panel offers', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        const says = await menuSays(page);

        //One shape of a cell: copy it, repeat it, take it away, turn it, name it.
        expect(says).toContain('Copy');
        expect(says).toContain('Cut');
        expect(says).toContain('Delete');
        expect(says).toContain('Turn');
        expect(says).toContain('Array…');
        expect(says).toContain('New cell');

        //And nothing that needs a number typed into it, which is what the panel's boxes are for.
        expect(says).not.toContain('Grow by');
    });

    ///
    ///The four ways of turning are behind the one line that says Turn.
    ///
    ///Twenty-two lines on the face of a menu is a list to read rather than a thing to point at, and most of
    ///them are one of four alternatives to something already there. What each one *does* is unchanged: the
    ///step it leaves behind is still named with the constant its button uses, which is why the words inside
    ///are short - the line that opened them has already said the verb.
    ///
    test('turning is four lines behind one', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        //Not on the face of it.
        expect(await menuSays(page)).not.toContain('Mirror down');

        const turns = await openSubmenu(page, 'Turn');

        expect((await turns.locator('.shapeMenuItem').allTextContents()).map(one => one.trim()))
            .toEqual(['Left', 'Right', 'Mirror across', 'Mirror down']);
    });

    ///
    ///The lines that need more than one shape are not there for one.
    ///
    ///The same condition the buttons carry, which is the whole point of building both from one list: a menu
    ///offering Union on a single shape would be a second opinion about what may be done to a selection.
    ///
    test('lining up is offered for several and not for one', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        expect(await menuSays(page)).not.toContain('Combine');
        expect(await menuSays(page)).not.toContain('Line up');

        await page.keyboard.press('Escape');

        //A second shape, added with Control, until the panel says there is more than one.
        for (let nth = 0; nth < await shapeCount(page); nth++) {
            if (await page.locator('#combineUnion').count() > 0)
                break;

            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            await addShape(page, { x: box.x + (box.width / 2), y: box.y + (box.height / 2) });
        }

        await expect(page.locator('#combineUnion')).toBeVisible();

        await rightClick(page, at);

        const says = await menuSays(page);

        expect(says).toContain('Combine');
        expect(says).toContain('Line up');

        //And what those two open onto is there behind them.
        await expect((await openSubmenu(page, 'Combine')).locator('.shapeMenuItem', { hasText: 'Union' })).toBeVisible();
        await expect((await openSubmenu(page, 'Line up')).locator('.shapeMenuItem', { hasText: 'Left' }).first()).toBeVisible();
    });

    ///
    ///Spacing out joins lining up rather than taking a submenu of its own.
    ///
    ///Two lines behind a line is a menu that costs more to open than it holds. It needs three shapes where
    ///lining up needs two, so the rule above it goes when it does - a rule with nothing under it is a stray
    ///mark at the foot of a submenu.
    ///
    test('spacing out is in with lining up, and only for three', async ({ page }) => {
        await chooseAShape(page);

        //The last point added, which is the one known to be inside the selection - Control toggles, and the
        //right button picks what it lands on, so aiming at the first shape can end up choosing it alone.
        let inside = null;

        //Two shapes: lining up, no spacing.
        for (let nth = 0; nth < await shapeCount(page); nth++) {
            if (await page.locator('#combineUnion').count() > 0)
                break;

            const box = await shapeBox(page, nth);

            if (box === null)
                continue;

            inside = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

            await addShape(page, inside);
        }

        await rightClick(page, inside);

        const two = await (await openSubmenu(page, 'Line up')).locator('.shapeMenuItem').allTextContents();

        expect(two.map(one => one.trim())).toEqual(['Left', 'Center', 'Right', 'Top', 'Middle', 'Bottom']);
    });
});

test.describe('acting on it', () => {
    ///
    ///A line reaches the same method its button reaches.
    ///
    ///Checked through the undo stack rather than through the geometry, because what a turn does to the
    ///coordinates is covered in C# - what is worth knowing here is that the menu ran the editor's action and
    ///not a copy of it, and the step it left behind is named by the same constant the button names it with.
    ///
    test('a line does the edit, and leaves one step behind it', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        await (await openSubmenu(page, 'Turn')).locator('.shapeMenuItem', { hasText: 'Right' }).click();

        await expect(page.locator('#undoEdit')).toBeEnabled();
        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Turn right/);
    });

    test('deleting from the menu takes the shape away', async ({ page }) => {
        const at = await chooseAShape(page);

        const before = await shapeCount(page);

        await rightClick(page, at);

        await page.locator('#shapeMenu .shapeMenuItem', { hasText: 'Delete' }).click();

        await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeLessThan(before);
    });
});

///
///How long the menu is on its face, which is the whole reason for the submenus.
///
///Flat, a selection of three or more offered twenty-two lines: four turns, four booleans, six alignments,
///two spacings and the rest. Twenty-two is a list to read rather than a thing to point at, and most of it
///is one of four alternatives to something already there.
///
test.describe('how long it is', () => {
    test('the face of it stays short for the widest selection', async ({ page }) => {
        await chooseAShape(page);

        //
        //Everything the band crosses, which is as wide as a selection gets.
        //
        //Not Control-clicking each shape in turn: Control *toggles*, and these shapes overlap, so a click
        //aimed at one lands on a neighbor already in the selection and takes it back out. How many were
        //chosen at the end of that loop depended on the order they happened to be drawn in, which is a test
        //that passes and fails on its own.
        //
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.move(view.x + 5, view.y + view.height - 5);
        await page.mouse.down();
        await page.mouse.move(view.x + view.width - 5, view.y + 5, { steps: 10 });
        await page.mouse.up();

        //Three or more is what puts Space out in the list as well.
        await expect(page.locator('#spaceAcross')).toBeVisible();

        await rightClick(page, await insideSelection(page));

        const says = await menuSays(page);

        expect(says.length).toBeLessThanOrEqual(10);

        //And the whole of it still fits the window without scrolling.
        const menu = await page.locator('#shapeMenu').boundingBox();

        expect(menu.y + menu.height).toBeLessThanOrEqual(page.viewportSize().height);
    });

    ///
    ///Near the right-hand edge the submenus open on the other side.
    ///
    ///Which side that is cannot be settled in a stylesheet - it is a comparison against the window's width -
    ///so the component asks once when the menu opens. Without it a submenu raised near the edge is a panel
    ///half off the screen.
    ///
    test('a submenu raised at the right edge stays inside the window', async ({ page }) => {
        await chooseAShape(page);

        //The whole window for the view, which is what puts a right-click close enough to the edge for the
        //side a submenu opens on to matter at all.
        await page.locator('#fullScreen').click();

        const window = page.viewportSize();
        const view = await page.locator('#gdsSVG').boundingBox();

        await rightClick(page, { x: Math.min(view.x + view.width - 4, window.width - 4), y: view.y + (view.height / 2) });

        const box = await (await openSubmenu(page, 'Turn')).boundingBox();

        //
        //Inside the window, whichever side it chose.
        //
        //The rule rather than the side: which side there is room on is a comparison the component makes
        //against the window's width, and a test naming the answer would be asserting the arithmetic back at
        //itself. What matters is that the panel can be read.
        //
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(window.width);
    });
});

///
///The menu is not a scroll box, and that is what lets a submenu exist at all.
///
///Capping its height and letting it scroll seemed like the way to keep it on screen. It is not: overflow on
///one axis computes to auto on the other, a scroll box clips what is positioned inside it, and every
///submenu is positioned beside the menu rather than within it. The submenu was never drawn - its width
///turned into a horizontal scrollbar instead.
///
test.describe('not a scroll box', () => {
    test('nothing in it scrolls', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        const menu = await page.locator('#shapeMenu').evaluate(one => {
            const style = getComputedStyle(one);

            return {
                overflowX: style.overflowX,
                overflowY: style.overflowY,
                scrollsX: one.scrollWidth > one.clientWidth,
                scrollsY: one.scrollHeight > one.clientHeight
            };
        });

        expect(menu.overflowX).toBe('visible');
        expect(menu.overflowY).toBe('visible');
        expect(menu.scrollsX).toBe(false);
        expect(menu.scrollsY).toBe(false);
    });

    test('and a submenu is drawn wholly outside it', async ({ page }) => {
        const at = await chooseAShape(page);

        await rightClick(page, at);

        const opened = await (await openSubmenu(page, 'Turn')).boundingBox();
        const menu = await page.locator('#shapeMenu').boundingBox();

        //
        //Clear of the menu on one side or the other, rather than somewhere inside its box.
        //
        //To the pixel, not exactly: the submenu is placed at 100% of the line that opens it, which is the
        //menu's *content* box, so it starts one pixel inside the border. Anything more than that is a panel
        //drawn over the menu rather than beside it.
        //
        const overlap = Math.min(opened.x + opened.width, menu.x + menu.width) - Math.max(opened.x, menu.x);

        expect(overlap).toBeLessThanOrEqual(2);

        //And big enough to be a menu rather than a sliver of one that survived a clip.
        expect(opened.width).toBeGreaterThan(80);
        expect(opened.height).toBeGreaterThan(60);

        //
        //Reached from where it is drawn, which is the part a box cannot tell you.
        //
        //A clipped element still reports its layout position, and Playwright's own visibility check does not
        //look at what an ancestor's overflow does to it - so both of those pass on a submenu nobody can see
        //or press. Asking the page what is actually at the point is what tells the two apart.
        //
        const reachable = await page.evaluate((at) => {
            const under = document.elementFromPoint(at.x, at.y);

            return under !== null && under.closest('.shapeSubmenu') !== null;
        }, { x: opened.x + (opened.width / 2), y: opened.y + 10 });

        expect(reachable).toBe(true);
    });
});
