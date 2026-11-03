//The note over the view while something loads: a file opening, or a view being built.
//
//A load holds the page - reading, flattening and drawing a file is C# on the page's only thread - so the view says
//what it is doing for as long as that takes, and the view takes the note down once its first picture is on the page.
//The notes are read through a watcher put on the page before anything runs, since on Mosfet a whole load is over
//faster than a poll from here could catch it.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, selectExample, MOSFET, SKY130_CELL } = require('./helpers');

//SKY130_CELL is the cell's name; the picker lists files, so the extension goes back on. Same as slider-carry.
const SKY130_FILE = `${SKY130_CELL}.gds`;

test.beforeEach(async ({ page }) => {
    //Every note the page puts up, in order.
    await page.addInitScript(() => {
        window.notesSeen = [];

        //What the note says, which is its status - not the Cancel button beside it.
        new MutationObserver(() => {
            const note = document.querySelector('#viewLoading [role="status"]');

            if (note == null)
                return;

            const said = note.textContent.trim();

            if (window.notesSeen[window.notesSeen.length - 1] !== said)
                window.notesSeen.push(said);
        }).observe(document, { childList: true, subtree: true, characterData: true });
    });
});

async function notesSeen(page) {
    return page.evaluate(() => window.notesSeen);
}

///Opened, drawn, and the note gone - the state every test below starts its own step from.
async function openedAndDrawn(page, view) {
    await gotoExample(page, MOSFET, view);

    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 30000 });
}

test('opening a file says which file is loading, and the note goes once it is drawn', async ({ page }) => {
    await openedAndDrawn(page, '2d');

    expect(await notesSeen(page)).toContain('Loading Mosfet.gds…');

    await expect(page.locator('#gdsSVG path').first()).toBeAttached();
});

test('a file chosen from the examples says its own name while it loads', async ({ page }) => {
    await openedAndDrawn(page, '2d');

    await selectExample(page, SKY130_FILE);

    await expect.poll(() => notesSeen(page)).toContain(`Loading ${SKY130_FILE}…`);
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 30000 });
});

///The 3D view merges and builds the whole scene as it mounts, which is the wait on a big design.
test('switching to 3D says the view is loading, and the note goes once the scene is built', async ({ page }) => {
    await openedAndDrawn(page, '2d');

    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();
    await expect.poll(() => notesSeen(page)).toContain('Loading the 3D view…');
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 30000 });

    //And back, which draws the whole picture again.
    await selectView(page, 'View2DSvg');

    await expect.poll(() => notesSeen(page)).toContain('Loading the 2D view…');
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 30000 });
});

///The text view draws no picture to say it is done with, so it never puts a note up - and takes down one that is.
test('the text view puts no note up', async ({ page }) => {
    await openedAndDrawn(page, '2d');

    const before = (await notesSeen(page)).length;

    await selectView(page, 'ViewText');
    await page.waitForTimeout(1500);

    expect((await notesSeen(page)).length).toBe(before);

    await expect(page.locator('#viewLoading')).toHaveCount(0);
});

///A note that outstayed its load must never come between the pointer and the drawing.
test('the note takes no presses', async ({ page }) => {
    await openedAndDrawn(page, '2d');

    const passes = await page.evaluate(() => {
        const note = document.createElement('div');

        note.className = 'viewLoading';
        document.getElementById('viewPane').appendChild(note);

        const said = getComputedStyle(note).pointerEvents;

        note.remove();

        return said;
    });

    expect(passes).toBe('none');
});
