//The guided tour: a card for each part of the app, the part showing through a hole in a shade over the rest.
//
//What takes a browser: which parts are on the page to be shown, where the hole and the card land, and the
//keyboard - every key is the tour's while it is open, so the arrow that moves to the next step does not also
//nudge whatever shape is chosen behind the shade.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, shapeCount, shapeBox, elementPoints, snapToGrid, pitchInUnits, MOSFET } = require('./helpers');

///Every step over the 2D view, which has every part the tour is about.
const ALL_STEPS = [
    'Welcome to GDS Viewer',
    'Examples',
    'Your own files',
    'History',
    'Three views',
    'The drawing',
    'Tools',
    'Layers',
    'The PDK',
    'Design rules',
    'The cell tree',
    'AI assistant',
    'Compare',
    'Download',
    'Share',
    'Settings',
    'That is the tour'
];

async function startFromSettings(page) {
    await page.locator('#settingsMenu').click();
    await page.locator('#tourItem').click();

    await expect(page.locator('#tourCard')).toBeVisible();
}

///Every title from the step on the card to the last, by the right arrow - which stops there rather than ending the tour.
async function walk(page) {
    const titles = [];

    for (;;) {
        titles.push(await page.locator('#tourTitle').textContent());

        const [at, of] = (await page.locator('#tourCount').textContent()).split(' of ').map(Number);

        if (at === of)
            return titles;

        await page.keyboard.press('ArrowRight');

        await expect(page.locator('#tourCount')).toHaveText(`${at + 1} of ${of}`);
    }
}

///The hole in the shade, read back off its clip-path: the window's five corners come first, then the hole's.
async function hole(page) {
    return page.evaluate(() => {
        const corners = document.getElementById('tourShade').style.clipPath
            .replace(/^polygon\(evenodd,\s*/, '')
            .replace(/\)$/, '')
            .split(',')
            .map(corner => corner.trim().split(/\s+/).map(parseFloat));

        return { left: corners[5][0], top: corners[5][1], right: corners[7][0], bottom: corners[7][1] };
    });
}

async function boxOf(page, selector) {
    return page.locator(selector).evaluate(part => {
        const box = part.getBoundingClientRect();

        return { left: box.left, top: box.top, right: box.right, bottom: box.bottom };
    });
}

///Steps into the cell and chooses one of its shapes on its own, as shortcuts.spec does. Returns it and its corners.
async function chooseAShape(page) {
    await page.locator('#selectTool').click();

    const cell = await shapeBox(page);

    await page.mouse.click(cell.x + (cell.width / 2), cell.y + (cell.height / 2));

    const count = await shapeCount(page, 'inContext');

    for (let nth = 0; nth < count; nth++) {
        const inside = await shapeBox(page, nth, 'inContext');

        await page.mouse.click(inside.x + (inside.width / 2), inside.y + (inside.height / 2));

        if (await page.locator('#gdsSVG .shapeSelected').count() !== 1)
            continue;

        const index = await page.locator('#gdsSVG .shapeSelected').getAttribute('data-element');
        const corners = await elementPoints(page, index);

        if (corners !== null)
            return { index: index, corners: corners };
    }

    throw new Error('no shape of this cell could be chosen on its own');
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///Never sprung on a visit - it would stand in the way of whatever somebody came to do - and from the start when asked.
test('it waits to be asked, and Settings starts it at the beginning', async ({ page }) => {
    await expect(page.locator('#tourCard')).toHaveCount(0);

    await startFromSettings(page);

    await expect(page.locator('#tourTitle')).toHaveText('Welcome to GDS Viewer');
    await expect(page.locator('#tourCount')).toHaveText('1 of 17');
    await expect(page.locator('#tourBack')).toBeDisabled();
    await expect(page.locator('#tourCard')).toBeFocused();
    await expect(page.locator('#settingsPicker')).toHaveCount(0);

    //About the whole app, so the hole has no size and all of the page is shaded.
    await expect.poll(async () => {
        const at = await hole(page);

        return [at.right - at.left, at.bottom - at.top];
    }).toEqual([0, 0]);
});

test('the command palette starts it too', async ({ page }) => {
    await page.locator('#gdsSVG').hover();
    await page.keyboard.press('Control+k');

    await expect(page.locator('#commandSearch')).toBeFocused();

    await page.keyboard.type('tour');
    await page.keyboard.press('Enter');

    await expect(page.locator('#commandPalette')).toHaveCount(0);
    await expect(page.locator('#tourTitle')).toHaveText('Welcome to GDS Viewer');
    await expect(page.locator('#tourCard')).toBeFocused();
});

///The hole is the part and a little round it, and the card is under it where there is room.
test('a step\'s part shows through the shade, with its card beside it', async ({ page }) => {
    await startFromSettings(page);

    await page.locator('#tourNext').click();

    await expect(page.locator('#tourTitle')).toHaveText('Examples');
    await expect(page.locator('#tourCount')).toHaveText('2 of 17');

    const part = await boxOf(page, '#examplesButton');

    await expect.poll(async () => {
        const at = await hole(page);

        return Math.max(
            Math.abs(at.left - (part.left - 4)),
            Math.abs(at.top - (part.top - 4)),
            Math.abs(at.right - (part.right + 4)),
            Math.abs(at.bottom - (part.bottom + 4)));
    }).toBeLessThan(1);

    await expect.poll(async () => (await boxOf(page, '#tourCard')).top).toBeGreaterThanOrEqual(part.bottom);

    await page.locator('#tourBack').click();

    await expect(page.locator('#tourTitle')).toHaveText('Welcome to GDS Viewer');
});

///Every step shows over the 2D view, which also proves each one's part is still where the tour looks for it.
test('the arrows walk every step, and the right one stops at the last', async ({ page }) => {
    await startFromSettings(page);

    expect(await walk(page)).toEqual(ALL_STEPS);

    await expect(page.locator('#tourNext')).toHaveAttribute('aria-label', 'Done');

    //Past the last step is not out of the tour: the left arrow after it is still the tour's.
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowLeft');

    await expect(page.locator('#tourCount')).toHaveText('16 of 17');
});

///
///Over the 3D view the drawing, its tools and the rules are not on the page, and the count says so. The AI assistant is: it is
///in the bar in every view since it left the 2D drawing. So is Compare, grayed rather than taken away, and its card says
///it is the 2D view's.
///
test('over the 3D view, only the parts that are there', async ({ page }) => {
    await selectView(page, 'View3D');

    await expect(page.locator('#gdsSVG')).toHaveCount(0);

    await startFromSettings(page);

    await expect(page.locator('#tourCount')).toHaveText('1 of 14');

    expect(await walk(page)).toEqual([
        'Welcome to GDS Viewer',
        'Examples',
        'Your own files',
        'History',
        'Three views',
        'Layers',
        'The PDK',
        'The cell tree',
        'AI assistant',
        'Compare',
        'Download',
        'Share',
        'Settings',
        'That is the tour'
    ]);
});

test('Escape, the cross and Done each end it, and it starts again from the beginning', async ({ page }) => {
    await startFromSettings(page);
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Escape');

    await expect(page.locator('#tourCard')).toHaveCount(0);
    await expect(page.locator('#tourShade')).toHaveCount(0);
    await expect(page.locator('#tourBlocker')).toHaveCount(0);

    await startFromSettings(page);

    await expect(page.locator('#tourCount')).toHaveText('1 of 17');

    await page.locator('#tourClose').click();

    await expect(page.locator('#tourCard')).toHaveCount(0);

    await startFromSettings(page);
    await walk(page);
    await page.locator('#tourNext').click();

    await expect(page.locator('#tourCard')).toHaveCount(0);
});

///
///**The keys are the tour's while it is open.** The editor listens on the window too, and an arrow that moved
///to the next step also nudged the shape chosen behind the shade - and Delete deleted it.
///
///Two arrows in the tour and one after it, so a shape that ends one pitch along heard only the last: had the
///tour let them through it would be three, and had it let Delete through it would be gone.
///
test('the keys are the tour\'s while it is open, so a shape chosen behind it stays put', async ({ page }) => {
    //Exactly a pitch a press, as shortcuts.spec measures it.
    await snapToGrid(page, false);

    const chosen = await chooseAShape(page);
    const step = await pitchInUnits(page);

    const leftOf = points => Math.min(...points.trim().split(/[\s,]+/)
        .map(Number)
        .filter((_, at) => at % 2 === 0));

    await page.keyboard.press('Control+k');
    await page.keyboard.type('tour');
    await page.keyboard.press('Enter');

    await expect(page.locator('#tourCard')).toBeFocused();

    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Delete');

    await expect(page.locator('#tourCount')).toHaveText('3 of 17');

    //Nor does the palette's key open it under the tour, where its box would take the keys out of sight.
    await page.keyboard.press('Control+k');
    await page.keyboard.press('Escape');

    await expect(page.locator('#tourCard')).toHaveCount(0);
    await expect(page.locator('#commandPalette')).toHaveCount(0);
    await expect(page.locator('#gdsSVG .shapeSelected')).toHaveCount(1);

    await page.keyboard.press('ArrowRight');

    await expect.poll(async () => leftOf(await elementPoints(page, chosen.index)), { timeout: 15000 })
        .toBe(leftOf(chosen.corners) + step);
});

///
///**The question as the page opens.** Never asked in a browser a test drives, so these ask for it before the page loads.
///Each open asks until "Don't show this again" is ticked; "Show me" starts the tour at its beginning.
///
async function reloadAsked(page) {
    await page.addInitScript(() => {
        window.gdsAskTourUnderTest = true;
    });

    await page.reload();

    await expect(page.locator('#tourQuestion')).toBeVisible({ timeout: 60000 });
}

test('the page asks whether a tour is wanted, and Show me starts it', async ({ page }) => {
    await expect(page.locator('#tourQuestion')).toHaveCount(0);

    await reloadAsked(page);

    await expect(page.locator('#tourQuestionYes')).toBeFocused();

    await page.locator('#tourQuestionYes').click();

    await expect(page.locator('#tourQuestion')).toHaveCount(0);
    await expect(page.locator('#tourTitle')).toHaveText('Welcome to GDS Viewer');
});

test("No thanks asks again next time, until Don't show this again is ticked", async ({ page }) => {
    await reloadAsked(page);

    await page.locator('#tourQuestionNo').click();

    await expect(page.locator('#tourQuestion')).toHaveCount(0);
    await expect(page.locator('#tourCard')).toHaveCount(0);

    await reloadAsked(page);

    await page.locator('#tourQuestionNever').check();
    await page.keyboard.press('Escape');

    await expect(page.locator('#tourQuestion')).toHaveCount(0);

    await page.reload();

    await expect(page.locator('#examplesButton')).toBeVisible({ timeout: 60000 });

    //Long enough for the question to have come, had it been going to.
    await page.waitForTimeout(2000);

    await expect(page.locator('#tourQuestion')).toHaveCount(0);
});

///Tab goes round the card's own buttons - Back is not one of them on the first step - and never out to the page.
test('Tab stays on the card', async ({ page }) => {
    await startFromSettings(page);

    const focused = [];

    for (const key of ['Tab', 'Tab', 'Tab', 'Shift+Tab']) {
        await page.keyboard.press(key);

        focused.push(await page.evaluate(() => document.activeElement.id));
    }

    expect(focused).toEqual(['tourClose', 'tourNext', 'tourClose', 'tourNext']);
});
