//
//The two switches on a layer row: the eye that draws it, and the lock that lets it be worked on.
//
//**Three states, not two.** Hiding takes a layer off the screen, which is the wrong answer when it is the
//thing being worked *against* - a via has to line up with the metal over it, and nothing lines up with what
//it cannot see. Locking leaves it drawn, fades it, and takes it out of everything that picks. What is left
//unlocked is the working set, and it can be as many layers as it needs to be.
//
//**Only a browser can answer this.** Whether a faded shape can still be picked is settled by HitTest in C#
//and reported back through the browser, and what a drag then does to it is pointer handling on top of that -
//so the question needs a real pointer against a real document. The first attempt at this feature faded the
//shapes in CSS and left them every bit as draggable, which looked exactly like it worked.
//
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeBox, allPoints, MOSFET } = require('./helpers');

///The row whose name contains what is asked for.
function rowNamed(page, name) {
    return page.locator('.layerRow').filter({ has: page.locator('.layerName', { hasText: name }) }).first();
}

///
///How every path in the drawing is currently painted and whether it can be pressed, in buckets.
///
///Bucketed rather than listed: what matters is how many are held back and how many are live, and a list of
///nineteen paths would pin which shapes the fixture happens to have.
///
async function paintBuckets(page) {
    return page.evaluate(() => {
        const buckets = {};

        for (const path of document.querySelectorAll('#gdsSVG path')) {
            const style = getComputedStyle(path);
            const key = style.opacity + '/' + style.pointerEvents;

            buckets[key] = (buckets[key] || 0) + 1;
        }

        return buckets;
    });
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    await expect(page.locator('#gdsSVG')).toBeVisible({ timeout: 60000 });
});

test('every layer row carries an eye and a lock', async ({ page }) => {
    const rows = await page.locator('.layerRow').count();

    expect(rows).toBeGreaterThan(0);

    await expect(page.locator('.layerEyeButton')).toHaveCount(rows);
    await expect(page.locator('.layerLockButton')).toHaveCount(rows);

    //The checkbox they replaced is gone rather than sitting beside them.
    await expect(page.locator('.layerVisible')).toHaveCount(0);
});

///
///The eye takes the layer off the screen entirely, which is what tells it from the lock.
///
test('the eye stops the layer being drawn', async ({ page }) => {
    const before = (await page.locator('#gdsSVG path').count());

    await rowNamed(page, 'poly (66/20)').locator('.layerEyeButton').click();

    await expect.poll(async () => page.locator('#gdsSVG path').count()).toBe(before - 1);

    await expect(rowNamed(page, 'poly (66/20)').locator('.layerEyeButton')).toHaveClass(/layerEyeOff/);

    //And back again, to exactly what was there.
    await rowNamed(page, 'poly (66/20)').locator('.layerEyeButton').click();

    await expect.poll(async () => page.locator('#gdsSVG path').count()).toBe(before);
});

///
///The lock leaves it drawn and fades it - the difference from the eye, stated.
///
test('the lock leaves the layer drawn and fades it', async ({ page }) => {
    const drawn = await page.locator('#gdsSVG path').count();

    await rowNamed(page, 'diff (65/20)').locator('.layerLockButton').click();

    await expect(rowNamed(page, 'diff (65/20)').locator('.layerLockButton')).toHaveClass(/layerLockOn/);

    //Still every path it had: locking is not hiding.
    await expect(page.locator('#gdsSVG path')).toHaveCount(drawn);

    const buckets = await paintBuckets(page);
    const faded = Object.entries(buckets).filter(([key]) => key.startsWith('0.12/'));

    //Exactly one layer held back, and the rest of the drawing untouched.
    expect(faded.length).toBe(1);
    expect(faded[0][1]).toBe(1);

    for (const [key] of faded)
        expect(key.endsWith('/none')).toBe(true);
});

///
///**Any number of them at once**, which is the whole reason this is a lock per row and not one isolated
///layer. A via and the two metals it joins are three layers in hand together; everything else can go.
///
test('several layers can be locked at once', async ({ page }) => {
    for (const name of ['diff (65/20)', 'poly (66/20)', 'li1 (67/20)'])
        await rowNamed(page, name).locator('.layerLockButton').click();

    await expect(page.locator('.layerLockOn')).toHaveCount(3);

    const buckets = await paintBuckets(page);
    const faded = Object.entries(buckets).filter(([key]) => key.startsWith('0.12/'));

    expect(faded.length).toBe(1);
    expect(faded[0][1]).toBe(3);

    //And the rest are still live.
    expect(Object.keys(buckets).some(key => key.endsWith('/auto'))).toBe(true);
});

///
///What the fade only looks like: a locked shape cannot be chosen.
///
///**Asserted on which layer answered, not that none did.** These layers overlap, so locking the one that
///answered and pressing the same place may legitimately find whatever is beneath it. What must never happen
///is the locked layer answering.
///
test.describe('what a click can land on', () => {
    ///The point of the first drawn shape, and the layer that answers a press there.
    async function pointAndLayer(page) {
        const box = await shapeBox(page, 0);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        await page.locator('#selectTool').click();
        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#selectionPanel')).toBeVisible();

        return { at, pair: (await page.locator('#chosenLayer').textContent()).match(/\d+\/\d+/)[0] };
    }

    ///Clears whatever is chosen by pressing where nothing is.
    async function letGo(page) {
        const view = await page.locator('#gdsSVG').boundingBox();

        await page.mouse.click(view.x + 4, view.y + 4);

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    }

    test('a locked layer cannot be chosen', async ({ page }) => {
        const { at, pair } = await pointAndLayer(page);

        await letGo(page);

        await rowNamed(page, pair).locator('.layerLockButton').click();

        await page.mouse.click(at.x, at.y);

        //Either nothing answered, or something under it did - never the locked layer.
        if (await page.locator('#selectionPanel').count() > 0)
            await expect(page.locator('#chosenLayer')).not.toContainText(pair);
    });

    test('unlocking puts it back within reach', async ({ page }) => {
        const { at, pair } = await pointAndLayer(page);

        await letGo(page);

        const lock = rowNamed(page, pair).locator('.layerLockButton');

        await lock.click();
        await expect(lock).toHaveClass(/layerLockOn/);

        await lock.click();
        await expect(lock).not.toHaveClass(/layerLockOn/);

        await letGo(page);
        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#selectionPanel')).toBeVisible();
        await expect(page.locator('#chosenLayer')).toContainText(pair);
    });

    ///
    ///A selection made before locking lets go of what has just gone out of reach.
    ///
    ///Otherwise the shapes stay chosen on a locked layer and the move tool goes on dragging them: they were
    ///picked while they were still pickable, and nothing asks again on the way into a drag.
    ///
    test('locking lets go of anything chosen on that layer', async ({ page }) => {
        const { pair } = await pointAndLayer(page);

        await expect(page.locator('#selectionPanel')).toBeVisible();

        await rowNamed(page, pair).locator('.layerLockButton').click();

        await expect(page.locator('#selectionPanel')).toHaveCount(0);
    });

    ///
    ///And it cannot be dragged, which is the half that changes the file.
    ///
    ///Locked from a point the *other* layers do not cover, so the drag has nothing legitimate to take hold
    ///of and the drawing must stand still. Asked of the drawing itself through isPointInFill rather than by
    ///naming a pair, so it does not pin the fixture's geometry.
    ///
    test('a locked layer cannot be dragged', async ({ page }) => {
        const box = await shapeBox(page, 0);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        //Every layer with something of its own under that point - all of which have to be locked for the
        //press to find nothing at all.
        const covering = await page.evaluate(([x, y]) => {
            const svg = document.querySelector('#gdsSVG');
            const where = new DOMPoint(x, y).matrixTransform(svg.getScreenCTM().inverse());
            const found = [];

            for (const path of svg.querySelectorAll('path')) {
                const layer = [...path.classList].find(name => /^l-?\d+_\d+$/.test(name));

                if (layer !== undefined && path.isPointInFill(where))
                    found.push(layer.slice(1).replace('_', '/'));
            }

            return found;
        }, [at.x, at.y]);

        expect(covering.length).toBeGreaterThan(0);

        for (const pair of covering)
            await rowNamed(page, pair).locator('.layerLockButton').click();

        await expect(page.locator('.layerLockOn')).toHaveCount(covering.length);

        const before = await allPoints(page);

        await page.locator('#moveTool').click();

        await page.mouse.move(at.x, at.y);
        await page.mouse.down();
        await page.mouse.move(at.x + 70, at.y + 60, { steps: 8 });
        await page.mouse.up();

        await page.waitForTimeout(800);

        //Every outline exactly where it was, which is the claim: a locked shape does not move.
        expect(await allPoints(page)).toEqual(before);

        //
        //**Something may well be chosen, and that is correct.**
        //
        //With nothing under the pointer to take hold of, the drag is a rubber band rather than a move - and
        //a band catches whatever unlocked shapes it crosses on the way. What it must not have caught is any
        //of the layers that were locked.
        //
        if (await page.locator('#selectionPanel').count() > 0) {
            const chosen = await page.locator('#chosenLayer').textContent();

            for (const pair of covering)
                expect(chosen).not.toContain(pair);
        }
    });
});
