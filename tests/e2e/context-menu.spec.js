//
//The browser's own menu stays out of the way of the app's, on every tool and in both views.
//
//**This was broken for three of the five tools.** `@oncontextmenu:preventDefault` on the drawing was
//conditional on `selecting`, which only pickShapes sets - so Select and Move suppressed the browser's menu
//and Pan, Measure and Draw did not. openShapeMenu runs whichever tool is in hand, and opens on anything
//already chosen or on a full clipboard, so with the pen a right-click raised the app's menu and the
//browser's on top of it.
//
//Asserted by dispatching the event and reading what dispatchEvent returns, which is false when something
//called preventDefault. Playwright cannot see a native context menu - it is drawn by the operating system,
//outside the page - so "was the default prevented" is the only question that can actually be asked here,
//and it is the one that decides whether the menu appears.
//
const { test, expect } = require('@playwright/test');
const { gotoExample, expectLoaded, MOSFET } = require('./helpers');

///Whether a right-click on `selector` is taken by the app rather than left to the browser.
async function rightClickPrevented(page, selector) {
    return page.evaluate(one => {
        const node = document.querySelector(one);

        if (node == null)
            return null;

        const box = node.getBoundingClientRect();

        const event = new MouseEvent('contextmenu', {
            bubbles: true,
            cancelable: true,
            clientX: box.x + (box.width / 2),
            clientY: box.y + (box.height / 2),
            button: 2
        });

        //False when a handler called preventDefault, which is the whole question.
        return !node.dispatchEvent(event);
    }, selector);
}

///
///Every tool, because the hole was per-tool and a fix that covers four of five is the same bug.
///
///Drawing is not in this list: it is only offered inside a cell, so reaching it needs a cell entered first,
///and it gets its own test below rather than a special case in this one.
///
for (const tool of ['panTool', 'measureTool', 'selectTool', 'moveTool']) {
    test(`the browser menu stays shut over the drawing with ${tool} in hand`, async ({ page }) => {
        await gotoExample(page, MOSFET);

        await expectLoaded(page);

        await page.locator(`#${tool}`).click();

        expect(await rightClickPrevented(page, '#gdsSVG')).toBe(true);
    });
}

///
///The pen, which is the one that was reported.
///
///Only offered inside a cell - there is nowhere to put a shape at the top of a library - so this enters one
///the way the editing specs do before asking.
///
test('the browser menu stays shut over the drawing with the pen in hand', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d', true);

    await expectLoaded(page);

    //Into the first cell the tree offers, which is what makes Draw available at all - the same way in
    //cell-tree.spec.js uses, and the crumb is how it says it worked.
    await page.locator('.cellRow').first().click();

    await expect(page.locator('.contextCrumbOn').first()).toBeVisible({ timeout: 20000 });

    await page.locator('#drawTool').click();

    expect(await rightClickPrevented(page, '#gdsSVG')).toBe(true);
});

///
///And the 3D canvas, which was never broken - held here so it stays that way.
///
///OrbitControls maps the right button to PAN, and Chrome raises contextmenu on the press rather than the
///release, so a right-drag to move the stack sideways would begin by opening a menu over it. It does not,
///because OrbitControls registers its own onContextMenu and calls preventDefault there.
///
///**Written first as a fix, and the fix was wrong.** A listener was added here on the reasoning above
///before checking the vendored source, and the mutation check found it: taking the listener away left this
///test passing, because the thing it was guarding was already guarded. What survives is the assertion.
///
///Worth keeping even so. OrbitControls only prevents it while `enabled` is true, so anything that disables
///the controls - or replaces them - takes this with it, and the failure would look like the 2D one did.
///
test('the browser menu stays shut over the 3D canvas, where a right drag pans', async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 30000 });

    expect(await rightClickPrevented(page, '#container canvas')).toBe(true);
});
