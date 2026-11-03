//Cancel, on the note over the view while something loads: a load given up, and the page back as it was.
//
//Opening a real chip is seconds of reading, flattening and drawing on the page's only thread, and it is paced - the
//long walks let the page in every so often, see Pacer - so the note's Cancel can be pressed while it runs. What only
//a browser can show is that the press is heard mid-load, and what is left after it: the file that was open, on its
//own tab, with nothing of the one given up - not its name, not a tab of its own, not its picture.
//
//The big file is the RGB mixer, the largest bundled design that opens in seconds on the dev server. The note says
//how far a load has got (data-stage): reading until its file goes in, drawing after - which is what decides what a
//cancel has to put back, and so which test is which.
const { test, expect } = require('@playwright/test');
const { gotoExample, openExamples, filterExamples, exampleRow, selectExample, selectView, openFile, svgCounts, MOSFET, MOSFET_POLYGONS, SKY130_CELL } = require('./helpers');

const MOSFET_FILE = `${MOSFET}.gds`;
const SKY130_FILE = `${SKY130_CELL}.gds`;
const BIG = 'rgb_mixer.gds';

//The largest bundled design, for a load that is still being read whatever is done while it is.
const BIGGEST = 'sky130_fd_io__top_gpiov2.gds';

//A big file on the dev server, while the rest of the suite runs beside it.
test.describe.configure({ timeout: 300000 });

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect.poll(async () => openFile(page)).toBe(MOSFET_FILE);
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });
});

///Chooses an example and returns straight away, with it still loading - where selectExample waits for it to be in.
async function startOpening(page, fileName) {
    await openExamples(page);
    await filterExamples(page, fileName);
    await exampleRow(page, fileName).click();
}

///The note's Cancel, pressed once the load has got as far as a stage: reading, or drawing.
async function cancelWhile(page, stage) {
    await expect(page.locator('#viewLoading')).toHaveAttribute('data-stage', stage, { timeout: 120000 });
    await page.locator('#cancelLoad').click();
}

///The tabs as their names, the one on screen marked with a star - or an empty list when there is no strip.
async function tabsShown(page) {
    return page.evaluate(() => [...document.querySelectorAll('#fileTabs .fileTab')]
        .map(tab => {
            let mark = '';

            if (tab.classList.contains('fileTabOn'))
                mark = '*';

            return mark + tab.querySelector('.fileTabName').textContent;
        }));
}

///Mosfet, drawn and alone, which is what every cancel below should come back to.
async function expectMosfetAsItWas(page) {
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect.poll(async () => (await svgCounts(page)).polygons, { timeout: 60000 }).toBe(MOSFET_POLYGONS);
    await expect(page.locator('#fileTabs')).toHaveCount(0);
}

///
///Given up before the file went in, nothing on screen has changed but the page's name - which goes back - and the
///file that was open never left. No tab for the one given up, since it never arrived.
///
test('Cancel while a big file is read leaves the file that was open, as it was', async ({ page }) => {
    await startOpening(page, BIG);
    await cancelWhile(page, 'reading');

    await expectMosfetAsItWas(page);
});

///
///Given up once the file is in and only its picture is still to come: the tab it was given goes, and the file that
///was open comes back onto its own - the road a tab switch takes.
///
test('Cancel while it is drawn puts back the file that was open, and closes the tab it was given', async ({ page }) => {
    await startOpening(page, BIG);
    await cancelWhile(page, 'drawing');

    await expectMosfetAsItWas(page);
});

///The point of the button: something else, straight after.
test('after a cancel the next file opens as usual', async ({ page }) => {
    await startOpening(page, BIG);
    await cancelWhile(page, 'reading');

    await expectMosfetAsItWas(page);

    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);
});

///
///Another file chosen while a big one loads is a change of mind as good as a cancel - the page answers through the
///load, so the list can be opened at all - and the one chosen last is the one that opens. The first never arrives,
///not even late: it is overtaken, and stops at its next pause.
///
test('another file chosen while a big one loads opens in its place', async ({ page }) => {
    await startOpening(page, BIG);

    await expect(page.locator('#viewLoading')).toHaveAttribute('data-stage', 'reading', { timeout: 120000 });

    await selectExample(page, SKY130_FILE);

    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);

    //And stays that way: a load overtaken stops at its next pause, a fraction of a second, so this is long enough
    //for one that went on to have landed.
    await page.waitForTimeout(3000);

    expect(await openFile(page)).toBe(SKY130_FILE);
    expect(await tabsShown(page)).toEqual([MOSFET_FILE, `*${SKY130_FILE}`]);
});

///
///**A load overtaken before its file went in has its renaming put back as it is overtaken**, so the load that takes its
///place notes the page as it really is. Canceling that second one then comes back to the file that was open, under
///its own name - where it used to come back to the first one's name, which was all the second had found to note.
///
test('Cancel of a file chosen over another still loading comes back to the file that was open', async ({ page }) => {
    await startOpening(page, BIGGEST);

    await expect(page.locator('#viewLoading [role="status"]')).toContainText(BIGGEST, { timeout: 120000 });

    await startOpening(page, BIG);

    await expect(page.locator('#viewLoading [role="status"]')).toContainText(BIG, { timeout: 120000 });
    await cancelWhile(page, 'reading');

    await expectMosfetAsItWas(page);

    //And neither of them arrives late.
    await page.waitForTimeout(3000);

    expect(await openFile(page)).toBe(MOSFET_FILE);
    expect(await tabsShown(page)).toEqual([]);
});

///
///**New, pressed while a big file loads**, overtakes it like any other choice - and puts its empty layout in without
///the page rendering in between. So the view has to ask the shell which load the file arrived with rather than go by
///the one it was last handed, which is the big file's, canceled by New a moment before: built at that pace, the empty
///layout's scene would be given up before it began, and the note would never come down.
///
test('New pressed while a big file loads in 3D opens the empty layout, and the note goes', async ({ page }) => {
    await selectView(page, 'View3D');

    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });

    await startOpening(page, BIG);

    await expect(page.locator('#viewLoading')).toHaveAttribute('data-stage', 'reading', { timeout: 120000 });

    await page.locator('#newLayout').click();

    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });
    await expect.poll(async () => openFile(page)).toBe('Untitled.gds');
    await expect.poll(async () => tabsShown(page)).toEqual([MOSFET_FILE, '*Untitled.gds']);

    //And the big one never arrives behind it - see the test above.
    await page.waitForTimeout(3000);

    expect(await openFile(page)).toBe('Untitled.gds');
    await expect(page.locator('#container canvas')).toHaveCount(1);
});

///
///A switch of view is a load too - the view arriving builds its whole picture, the 3D view merging every layer of the
///chip - and a cancel while it does goes back to the view it left, drawn again.
///
test('Cancel while the 3D view is built goes back to the 2D view', async ({ page }) => {
    await selectExample(page, BIG);

    await selectView(page, 'View3D');
    await cancelWhile(page, 'drawing');

    await expect(page.locator('#viewPick [data-view="View2DSvg"]')).toHaveAttribute('aria-pressed', 'true', { timeout: 60000 });
    await expect(page.locator('#container canvas')).toHaveCount(0);
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });

    expect(await openFile(page)).toBe(BIG);
    expect((await svgCounts(page)).polygons).toBeGreaterThan(0);
});

///
///Pressing a tab is a load of the file put away in it, and a cancel on the way there stays on the tab it was pressed
///from - whose file never left the screen.
///
test('Cancel on the way to a tab holding a big file stays on the tab it was pressed from', async ({ page }) => {
    await selectExample(page, BIG);

    await page.locator('#fileTabs .fileTab').filter({ has: page.locator('.fileTabName', { hasText: MOSFET_FILE }) }).click();

    await expect.poll(async () => openFile(page), { timeout: 120000 }).toBe(MOSFET_FILE);
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });

    await page.locator('#fileTabs .fileTab').filter({ has: page.locator('.fileTabName', { hasText: BIG }) }).click();
    await cancelWhile(page, 'reading');

    await expect(page.locator('#viewLoading')).toHaveCount(0);
    await expect.poll(async () => openFile(page)).toBe(MOSFET_FILE);
    await expect.poll(async () => tabsShown(page)).toEqual([`*${MOSFET_FILE}`, BIG]);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

///
///The same, once the tab's file is in and its 3D scene is being merged - a tab comes back in the view it was left in.
///
///**The case where the way back overlaps the load it undoes.** The 3D view's merge is part of the draw the restore is
///waiting on, so the restore is still unwinding when the way back has begun its own - and it must leave that one's
///state alone. A save let through in the middle of putting a file back writes half of it.
///
test('Cancel while a tab\'s file is merged in 3D goes back to the tab it was pressed from, and saves it whole', async ({ page }) => {
    await selectExample(page, BIG);
    await selectView(page, 'View3D');

    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 180000 });

    //Back to Mosfet, which comes back in the 2D view it was left in - and the mixer's tab keeps the 3D view.
    await page.locator('#fileTabs .fileTab').filter({ has: page.locator('.fileTabName', { hasText: MOSFET_FILE }) }).click();

    await expect.poll(async () => openFile(page), { timeout: 120000 }).toBe(MOSFET_FILE);
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });

    await page.locator('#fileTabs .fileTab').filter({ has: page.locator('.fileTabName', { hasText: BIG }) }).click();
    await cancelWhile(page, 'drawing');

    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(MOSFET_FILE);
    await expect.poll(async () => tabsShown(page)).toEqual([`*${MOSFET_FILE}`, BIG]);
    await expect(page.locator('#viewPick [data-view="View2DSvg"]')).toHaveAttribute('aria-pressed', 'true');
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    //And what the session holds is Mosfet - the file on screen, written once it was all back - rather than the mixer
    //that was given up. A moment for the saves that follow a restore, which are not something the page shows.
    await page.waitForTimeout(2000);

    await page.reload();

    await expect.poll(async () => openFile(page), { timeout: 120000 }).toBe(MOSFET_FILE);
});
