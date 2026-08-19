//
//The viewer dropped into somebody else's page: what the address can set, and how much of the app is offered.
//
//Only a browser can show this. The parsing is covered in C# - see EmbeddingTests - and what is left is the
//part that only exists once it is rendered: whether the bar is in the page at all, whether a control is
//actually unusable rather than merely faded, and whether a setting the address named beat the session.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, expectLoaded, openFile, showGrid, MOSFET } = require('./helpers');

///Opens the app on a query of its own, and waits for a file to be on screen.
async function embed(page, query) {
    await page.goto(`/${query}`, { waitUntil: 'domcontentloaded' });

    await expectLoaded(page);

    //The embed's settings land on the render after the file, so the wait is for them and not for the file.
    await page.waitForTimeout(1200);
}

test.describe('what the address can set', () => {
    ///
    ///The plain case, so the rest of this is a difference from something.
    ///
    test('an ordinary visit is the whole app, as it always was', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect(page.locator('.top-row')).toHaveCount(1);
        await expect(page.locator('.viewToolbar')).toHaveCount(1);
        await expect(page.locator('#layerSidebar')).toHaveCount(1);
        await expect(page.locator('#drawTool')).toBeVisible();
    });

    ///
    ///Each setting, named at once, because they are applied in one pass and a break in that pass would show
    ///as some of them landing.
    ///
    test('the settings an embed names are the ones it gets', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&full=true&banner=false&grid=false&tool=select&pitch=50&unit=nm');

        //The page's margins, given to the view.
        await expect(page.locator('.viewerPageFull')).toHaveCount(1);

        //No band of somebody else's branding across the top of the host's page.
        await expect(page.locator('.top-row')).toHaveCount(0);

        //Off is a value: this has to beat a session that says the grid is on.
        await expect(page.locator('#gridOverlay')).toHaveCount(0);

        await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);

        //Written in the unit it was named in rather than converted to the default.
        await expect(page.locator('#gridPitch')).toHaveValue('50');
        await expect(page.locator('#gridUnit')).toHaveValue('Nanometer');
    });

    ///
    ///**A named parameter beats the session; an unnamed one does not touch it.**
    ///
    ///Which is the whole precedence rule, and the one thing about this that could not be worked out by
    ///looking at the screen: the embedder pins what they care about, and everything else is still the
    ///visitor's from last time.
    ///
    ///
    ///**Without ?file=**, and that is not incidental.
    ///
    ///A link that names a file is treated as authoritative and opens it directly, without going near the
    ///session - which is right for a shared link and means an embed naming a file starts from the app's
    ///defaults rather than from what the visitor left. So the precedence rule is stated here on an embed
    ///that does not name one. See the note in the commit: an embed that pins a file currently pins
    ///everything else it does not name to the defaults too.
    ///
    test('what the address names wins, and what it does not name is left alone', async ({ page }) => {
        //A visit that leaves a session behind: grid off, and a file open.
        await gotoExample(page, MOSFET, 'View2DSvg');

        await showGrid(page, false);

        await expect(page.locator('#gridOverlay')).toHaveCount(0);

        //Now an embed that says nothing about the grid. The session's answer survives.
        await embed(page, '?view=2d&tool=measure');

        await expect(page.locator('#gridOverlay')).toHaveCount(0);
        await expect(page.locator('#measureTool')).toHaveClass(/toolButtonOn/);

        //And one that does name it. The address wins over the session that said otherwise.
        await embed(page, '?view=2d&grid=true');

        await expect(page.locator('#gridOverlay')).toHaveCount(1);
    });

    ///A parameter this build cannot read costs that setting and nothing else - see Embedding.
    test('a setting that will not read leaves the rest working', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&grid=true&pitch=fifty&tool=teleport');

        //The two that could be read.
        await expect(page.locator('#gridOverlay')).toHaveCount(1);

        //The one that could not leaves the view in the tool it opens in.
        await expect(page.locator('#panTool')).toHaveClass(/toolButtonOn/);

        //And the app is on screen rather than refusing to draw over a typo.
        expect(await openFile(page)).toContain('Mosfet');
    });
});

///
///The embedder's own files, offered in the picker beside the ones that ship with the app.
///
///What is parsed out of the address is covered in C# - see EmbeddingTests, which holds the awkward halves,
///the bar in a URL, and every kind of address that is refused. What only exists in a browser is the rest of
///it: whether an injected name reaches the list, whether choosing it fetches the address it was given
///rather than the served folder, and whether it beats a bundled file of the same name.
///
///**Served from this app's own origin**, which is a real absolute URL and takes the same path through the
///code as somebody else's - without needing a second host to make the point. The one thing it cannot show
///is CORS, which is the browser's and not this app's: a cross-origin file has to be served with a header
///allowing this page, and fetchExample says so when it fails.
///
///
///The layermap an address names.
///
///The one setting that is not a preference: what a layer is called and what it is for are the two things a
///GDSII file does not carry, so without this a page showing one layout has no way to say "and these numbers
///are metal" - and Trace net is a button that cannot work. The parsing and the URL guard are covered in C#;
///what only a browser shows is that the fetch happens, lands on the open file, and arms the feature.
///
///Served from this app's own origin, like the injected examples above and for the same reason: it is a real
///absolute URL taking the same path through the code, without needing a second host.
///
///Built from the run's own baseURL rather than spelled out, because the parameter has to be an absolute
///http address whatever port the server took. Written literally, four of these tests fail on an isolated
///port with the port as the whole story.
///
test.describe('the layermap an address names', () => {
    const MAP = (baseURL) => `${baseURL}/resources/GDS Files/sky130-roles.csv`;

    test('the layers arrive named, with no import', async ({ page, baseURL }) => {
        await embed(page, `?file=Mosfet&view=2d&tree=false&layermap=${encodeURIComponent(MAP(baseURL))}`);

        //The sidebar stops reading as bare numbers, which is the visible half of it.
        await expect.poll(async () => (await page.locator('.layerRow .layerName').allTextContents()).join(' '),
            { timeout: 20000 }).toContain('met1');

        //And nothing was reported, because nothing went wrong.
        await expect(page.locator('#layerMapNotice')).toHaveCount(0);
    });

    ///
    ///And the roles land, which is the half that matters: a name is decoration, a role is what the walk
    ///follows. Measured through Trace net rather than through the settings popup, because the button being
    ///enabled is the whole point of putting a layermap in the address.
    ///
    test('Trace net works straight away, without importing anything', async ({ page, baseURL }) => {
        await embed(page, `?file=Mosfet&view=2d&tree=false&layermap=${encodeURIComponent(MAP(baseURL))}`);

        await page.locator('#selectTool').click();

        //A met1 shape, reached in the layout's own coordinates so no label on top of it takes the click.
        const at = await page.evaluate(() => {
            const view = document.getElementById('gdsSVG');
            const point = view.createSVGPoint();

            point.x = 1380;
            point.y = 960;

            const on = point.matrixTransform(view.getScreenCTM());

            return { x: on.x, y: on.y };
        });

        await page.mouse.click(at.x, at.y);

        await expect(page.locator('#traceNet')).toBeEnabled({ timeout: 20000 });

        await page.locator('#traceNet').click();

        //Across the layers it climbed, which is what a role that landed buys.
        await expect(page.locator('#selectionPanel')).toContainText('67/44', { timeout: 20000 });
    });

    ///
    ///An address naming one that is not there says so, rather than leaving the layers as numbers and the
    ///feature quietly refusing - which is exactly what a page author cannot debug.
    ///
    test('a layermap that cannot be fetched is reported', async ({ page, baseURL }) => {
        await embed(page, `?file=Mosfet&view=2d&layermap=${encodeURIComponent(`${baseURL}/no-such-layermap.csv`)}`);

        await expect(page.locator('#layerMapNotice')).toBeVisible({ timeout: 20000 });
        await expect(page.locator('#layerMapNotice')).toContainText('no-such-layermap.csv');

        //Dismissable, because the visitor can do nothing about it.
        await page.locator('#layerMapNotice .closeButton').click();

        await expect(page.locator('#layerMapNotice')).toHaveCount(0);
    });

    ///
    ///And an address that is not the web is refused without a word, the way an example is.
    ///
    ///**Asked as "was anything requested", not "are the layers unnamed".** The layers *are* named on a
    ///bundled example, by the mapping the app ships - so the state this used to check cannot tell a refused
    ///`layermap=` from a default that landed. What the test is named for is whether the app went and fetched
    ///it, so that is what is watched.
    ///
    test('a layermap that is not http is not fetched', async ({ page }) => {
        const asked = [];

        page.on('request', one => asked.push(one.url()));

        await embed(page, `?file=Mosfet&view=2d&layermap=${encodeURIComponent('file:///C:/layers.csv')}`);

        await expect(page.locator('#layerMapNotice')).toHaveCount(0);

        //Nothing went out for it. The bundled mapping is fetched by name, so this looks for the refused one.
        expect(asked.filter(url => url.includes('C:/layers.csv') || url.startsWith('file:'))).toHaveLength(0);
    });
});

test.describe('the files an embedder brings', () => {
    const OWN = (baseURL) => encodeURIComponent(`Chip A|${baseURL}/resources/GDS Files/Sky130 GDS/Mosfet.gds`);
    const NAND = (baseURL) => `${baseURL}/resources/GDS Files/Sky130 GDS/sky130_fd_sc_hd__nand2_1.gds`;

    ///
    ///Opens an embed that names no file, and waits for the app rather than for a layout.
    ///
    ///Not the `embed` above, which waits for something to be on screen: these addresses inject a list
    ///without opening anything from it, so there is nothing to draw until a row is chosen.
    ///
    async function openEmbed(page, query) {
        await page.goto(`/${query}`, { waitUntil: 'domcontentloaded' });

        await expect(page.locator('#examplesButton')).toBeVisible({ timeout: 60000 });
    }

    ///Opens the picker without waiting on the bundled list, since an injected row is there before it.
    async function picker(page) {
        await page.locator('#examplesButton').click();

        await expect(page.locator('#examplePicker')).toBeVisible({ timeout: 60000 });

        await page.locator('.examplePickerFilter').fill('');
    }

    test('an injected file is listed, under a heading that says whose it is', async ({ page, baseURL }) => {
        await openEmbed(page, `?view=2d&example=${OWN(baseURL)}`);

        await picker(page);

        await expect(page.locator('.examplePickerOption[data-file="Chip A"]')).toBeVisible();

        //First, over the files the app ships with - the page that named it means it to be the one offered.
        const headings = await page.locator('.examplePickerHeading').allTextContents();

        expect(headings[0]).toBe('From this page');

        const first = await page.locator('.examplePickerOption').first().getAttribute('data-file');

        expect(first).toBe('Chip A');
    });

    test('choosing it fetches the address it was given', async ({ page, baseURL }) => {
        await openEmbed(page, `?view=2d&example=${OWN(baseURL)}`);

        await picker(page);

        await page.locator('.examplePickerOption[data-file="Chip A"]').click();

        //Under the name the page gave it, and with the kind taken off the address - the name it was given
        //carries no extension, and the download button needs one.
        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Chip A.gds');

        await expect(page.locator('#gdsSVG')).toBeVisible();

        await expect.poll(async () => page.locator('#gdsSVG path').count(), { timeout: 30000 })
            .toBeGreaterThan(0);
    });

    ///An embedder who calls their file "Mosfet" gets theirs, not the one in the folder.
    test('an injected name beats a bundled one', async ({ page, baseURL }) => {
        await openEmbed(page, `?view=2d&example=${encodeURIComponent(`Mosfet|${NAND(baseURL)}`)}`);

        await picker(page);

        //Listed twice, since the bundled one is still there under its own heading - and the injected row
        //is the first, which is the one this opens.
        await page.locator('.examplePickerOption[data-file="Mosfet"]').first().click();

        await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('Mosfet.gds');

        //The cell the address pointed at, which is the whole claim: the bundled Mosfet holds no cell of
        //this name.
        await expect.poll(async () => page.locator('.cellRowName').allTextContents(), { timeout: 30000 })
            .toEqual(expect.arrayContaining([expect.stringContaining('nand2')]));
    });

    ///More than one, in the order they were written - which is what repeating the parameter is for.
    test('several can be injected at once', async ({ page, baseURL }) => {
        const second = encodeURIComponent(`Chip B|${NAND(baseURL)}`);

        await openEmbed(page, `?view=2d&example=${OWN(baseURL)}&example=${second}`);

        await picker(page);

        const named = await page.locator('.examplePickerOption').evaluateAll(rows =>
            rows.slice(0, 2).map(row => row.getAttribute('data-file')));

        expect(named).toEqual(['Chip A', 'Chip B']);
    });

    ///
    ///An entry this build will not take costs that file and nothing else.
    ///
    ///Which of them are refused is EmbeddingTests' business; that a refused one leaves no row, and leaves
    ///the good one beside it working, is this one's.
    ///
    test('an entry that will not read leaves the rest of the list working', async ({ page, baseURL }) => {
        const noBar = encodeURIComponent('JustAName');
        const notWeb = encodeURIComponent('Local|file:///c:/chip.gds');

        await openEmbed(page, `?view=2d&example=${noBar}&example=${notWeb}&example=${OWN(baseURL)}`);

        await picker(page);

        await expect(page.locator('.examplePickerOption[data-file="JustAName"]')).toHaveCount(0);
        await expect(page.locator('.examplePickerOption[data-file="Local"]')).toHaveCount(0);

        await expect(page.locator('.examplePickerOption[data-file="Chip A"]')).toBeVisible();
    });

    ///Nothing injected leaves the picker exactly as it was, heading and all.
    test('an ordinary visit has no heading for files nobody brought', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await page.locator('#examplesButton').click();

        await expect(page.locator('#examplePicker')).toBeVisible({ timeout: 60000 });

        await page.locator('.examplePickerFilter').fill('');

        const headings = await page.locator('.examplePickerHeading').allTextContents();

        expect(headings).not.toContain('From this page');
    });
});

test.describe('how much of the app is offered', () => {
    ///
    ///Read-only: the whole app, with everything that would change the file turned off.
    ///
    ///**Turned off rather than taken away.** A missing button leaves somebody wondering whether the app can
    ///do the thing at all; one that is visibly off says the page has decided.
    ///
    test('mode=noedit keeps the app and disables what would change the file', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&mode=noedit');

        //Still the whole app.
        await expect(page.locator('.viewToolbar')).toHaveCount(1);
        await expect(page.locator('#layerSidebar')).toHaveCount(1);
        await expect(page.locator('#gdsSVG')).toBeVisible();

        //And these cannot be used.
        await expect(page.locator('#moveTool')).toBeDisabled();
        await expect(page.locator('#drawTool')).toBeDisabled();
        await expect(page.locator('#historyButton')).toBeDisabled();

        //The file button is a label, which cannot be disabled - it loses its `for` and its pointer events.
        const upload = await page.evaluate(() => {
            const label = document.querySelector('label.fileButton');

            return {
                stillThere: label !== null,
                opensNothing: label.getAttribute('for') === '',
                unclickable: getComputedStyle(label).pointerEvents === 'none'
            };
        });

        expect(upload.stillThere).toBe(true);
        expect(upload.opensNothing).toBe(true);
        expect(upload.unclickable).toBe(true);
    });

    ///Reading the layout is the point of a read-only viewer, so the tools that only read stay usable.
    test('mode=noedit leaves the tools that only look at the layout', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&mode=noedit');

        await expect(page.locator('#panTool')).toBeEnabled();
        await expect(page.locator('#measureTool')).toBeEnabled();
        await expect(page.locator('#selectTool')).toBeEnabled();

        //And it can actually be used, rather than merely looking enabled.
        await page.locator('#measureTool').click();

        await expect(page.locator('#measureTool')).toHaveClass(/toolButtonOn/);
    });

    ///
    ///Viewer only: the canvas, and nothing else.
    ///
    ///Left out of the render rather than hidden, so nothing in the bar is focusable or read out - an
    ///invisible toolbar somebody can still tab into is worse than one that is there.
    ///
    test('mode=viewer is the canvas on its own', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&mode=viewer');

        await expect(page.locator('#gdsSVG')).toBeVisible();

        await expect(page.locator('.viewToolbar')).toHaveCount(0);
        await expect(page.locator('#layerSidebar')).toHaveCount(0);
        await expect(page.locator('.top-row')).toHaveCount(0);

        //Nothing left behind to tab into.
        await expect(page.locator('#drawTool')).toHaveCount(0);
        await expect(page.locator('#historyButton')).toHaveCount(0);
        await expect(page.locator('#examplesButton')).toHaveCount(0);
    });

    ///The banner is separable from the mode, for a page that wants the header and nothing else.
    test('viewer only can still be asked for the banner', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&mode=viewer&banner=true');

        await expect(page.locator('.top-row')).toHaveCount(1);
        await expect(page.locator('.viewToolbar')).toHaveCount(0);
    });

    ///
    ///A mode nobody recognizes is the whole app.
    ///
    ///The safe way to be wrong in one direction and not the other: a misspelled "noedit" that fell back to
    ///the viewer would take the toolbar away from somebody who only wanted it read-only, with nothing on
    ///screen to say why.
    ///
    test('a mode this build does not know is the whole app', async ({ page }) => {
        await embed(page, '?file=Mosfet&view=2d&mode=kiosk');

        await expect(page.locator('.viewToolbar')).toHaveCount(1);
        await expect(page.locator('#drawTool')).toBeVisible();
    });
});
