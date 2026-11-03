//The command palette: Ctrl+K, a box to type into, and the things the app can do that match.
//
//What takes a browser: the key reaching the page from anywhere but a box, the list narrowing as letters are
//typed, the arrows and Enter, and a command doing what its button does - which is the whole of a command, since
//each one is a button on the page pressed for you.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectExample, selectView, openFile, MOSFET, SKY130_CELL } = require('./helpers');

///The commands listed, by name, the one picked out marked with a star.
async function commandsShown(page) {
    return page.evaluate(() => [...document.querySelectorAll('#commandList .commandItem')]
        .map(item => {
            let mark = '';

            if (item.classList.contains('commandItemPicked'))
                mark = '*';

            return mark + item.querySelector('.commandName').textContent;
        }));
}

async function openPalette(page) {
    await page.keyboard.press('Control+k');

    await expect(page.locator('#commandPalette')).toBeVisible();
    await expect(page.locator('#commandSearch')).toBeFocused();
}

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    //The key belongs to the page only when nothing is being typed into, so somewhere that is not a box.
    await page.locator('#gdsSVG').hover();
});

test('Ctrl+K opens the palette over the drawing, and Escape closes it', async ({ page }) => {
    await openPalette(page);

    expect((await commandsShown(page)).length).toBeGreaterThan(20);

    await page.keyboard.press('Escape');

    await expect(page.locator('#commandPalette')).toHaveCount(0);
});

test('the key closes it again, and so does a press outside it', async ({ page }) => {
    await openPalette(page);

    await page.keyboard.press('Control+k');

    await expect(page.locator('#commandPalette')).toHaveCount(0);

    await openPalette(page);

    await page.mouse.click(10, 10);

    await expect(page.locator('#commandPalette')).toHaveCount(0);
});

///Every word typed has to be found, so "zoom in" is Zoom in and not Zoom out - and other words a command answers to count.
test('typing narrows the list to what matches', async ({ page }) => {
    await openPalette(page);

    await page.keyboard.type('zoom');

    await expect.poll(async () => commandsShown(page)).toEqual(['*Zoom in', 'Zoom out']);

    await page.keyboard.type(' in');

    await expect.poll(async () => commandsShown(page)).toEqual(['*Zoom in']);

    await page.locator('#commandSearch').fill('save');

    await expect.poll(async () => commandsShown(page)).toEqual(['*Download']);

    await page.locator('#commandSearch').fill('nothing like this');

    await expect(page.locator('.commandNone')).toBeVisible();
});

///Enter runs the one picked out, closing the palette first - here a view, so what it did is plain to see.
test('Enter runs the command picked out', async ({ page }) => {
    await openPalette(page);

    await page.keyboard.type('3d');
    await page.keyboard.press('Enter');

    await expect(page.locator('#commandPalette')).toHaveCount(0);
    await expect(page.locator('#viewPick [data-view="View3D"]')).toHaveAttribute('aria-pressed', 'true');
});

///The arrows move the pick, round from the last to the first, and Enter runs whatever is picked.
test('the arrows move the pick', async ({ page }) => {
    await openPalette(page);

    await page.keyboard.type('zoom');
    await page.keyboard.press('ArrowDown');

    await expect.poll(async () => commandsShown(page)).toEqual(['Zoom in', '*Zoom out']);

    await page.keyboard.press('ArrowDown');

    await expect.poll(async () => commandsShown(page)).toEqual(['*Zoom in', 'Zoom out']);

    await page.keyboard.press('ArrowUp');

    await expect.poll(async () => commandsShown(page)).toEqual(['Zoom in', '*Zoom out']);
});

///A command for a tool presses the tool's own button, so it is in hand exactly as a press would put it there.
test('a tool command puts the tool in hand', async ({ page }) => {
    await openPalette(page);

    await page.keyboard.type('measure');
    await page.keyboard.press('Enter');

    await expect(page.locator('#measureTool')).toHaveClass(/toolButtonOn/);
});

///In a box the key is the box's - the rule line here - and the palette does not open over what is being typed.
test('the key typed into a box is the box\'s', async ({ page }) => {
    await page.locator('#sidebarPanelRules').click();
    await page.locator('#addRule').click();

    await page.locator('#newRuleLine').focus();
    await page.keyboard.press('Control+k');

    await expect(page.locator('#commandPalette')).toHaveCount(0);
});

///Only what can be done from here: the 2D view's tools are not offered over the 3D view, and that view is not offered over itself.
test('it offers only what can be done from here', async ({ page }) => {
    await selectView(page, 'View3D');
    await page.locator('#viewPane').hover();

    await openPalette(page);

    const names = await commandsShown(page);

    expect(names.map(name => name.replace('*', ''))).toContain('2D view');
    expect(names.map(name => name.replace('*', ''))).not.toContain('3D view');
    expect(names.map(name => name.replace('*', ''))).not.toContain('Draw');
});

///With a second file open, each other tab is a command away.
test('a tab is a command away', async ({ page }) => {
    await selectExample(page, `${SKY130_CELL}.gds`);
    await page.locator('#gdsSVG').hover();

    await openPalette(page);

    await page.keyboard.type('go to');

    await expect.poll(async () => commandsShown(page)).toEqual([`*Go to ${MOSFET}.gds`]);

    await page.keyboard.press('Enter');

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);
});
