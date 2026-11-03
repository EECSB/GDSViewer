//
//Takes the pictures FEATURES-DEMO.md and the readme are built from.
//
//**Regenerated rather than collected.** Screenshots are the part of documentation that rots without anything
//failing: the app moves, the pictures do not, and nobody notices until a reader is looking at a toolbar that
//has not existed for six months. This runs the app and takes them again, so bringing them up to date is a
//command rather than an afternoon.
//
//It is not part of the test suite - see playwright.screenshots.config.js for why - and it asserts only enough
//to know the app was ready when the shutter opened. A picture taken mid-draw is the one failure a screenshot
//cannot report on its own, so every shot waits for something specific to be on screen first.
//
//    npm run screenshots
//
const { test, expect } = require('@playwright/test');
const path = require('path');

const IMAGES = path.join(__dirname, '..', '..', 'docs', 'images');

//Mosfet.gds is the hand-made one: nine layers, a few dozen shapes, and small enough that the whole thing is
//legible at 1400 pixels. A sky130 standard cell is the real thing and is used where density is the point.
const MOSFET = 'Mosfet';
const CELL = 'sky130_fd_sc_hd__nand2_1';

//The bundled mapping, served from the app's own origin - so the layers arrive named, colored and roled
//without a second host, which is what makes Trace net possible in a picture.
const LAYERMAP = 'resources/GDS%20Files/sky130-roles.csv';

///
///Opens the app, waits for the file to be in the shell, and sees the editor full screen.
///
///data-file rather than the layer sidebar, because the text view does not draw one - the same reason the
///suite's own helper reads it there.
///
///**Full screen for every picture.** The page's own margins and the header take about a third of the frame
///otherwise, so a screenshot of the editor is mostly a screenshot of the page around it. The toolbar button
///is a CSS class rather than the browser's Fullscreen API - see Viewer.razor's viewerPageFull - which is why
///it works here at all. Where the app starts since 2026-11-03, so nothing is pressed for it.
///
async function open(page, query) {
    await page.goto(`/${query}`);

    await expect
        .poll(async () => page.locator('#mainAppContainer').getAttribute('data-file'), { timeout: 120000 })
        .not.toBe('');

    await expect(page.locator('.viewerPageFull')).toBeVisible();

    //The layout is refitted to the larger box, which is a render away.
    await page.waitForTimeout(500);
}

///Waits until the 2D view has actually drawn something, rather than until it exists.
async function drawn(page) {
    await expect
        .poll(async () => page.locator('#gdsSVG path[class^="l"]').count(), { timeout: 60000 })
        .toBeGreaterThan(0);

    //One more frame, so a picture is never taken between the markup landing and the browser painting it.
    await page.waitForTimeout(600);
}

async function shoot(page, name, options = {}) {
    await page.screenshot({ path: path.join(IMAGES, `${name}.png`), ...options });
}

test('the 2D editor', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await shoot(page, '2d-view');
});

test('a real standard cell, dense', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await shoot(page, '2d-standard-cell');
});

test('the 3D view', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=3d&layermap=${LAYERMAP}`);

    await expect(page.locator('#container canvas')).toBeVisible({ timeout: 60000 });

    //WebGL needs longer than the SVG does: the scene is built, extruded and then orbited into place.
    await page.waitForTimeout(4000);

    //
    //**Closed right up, because the process stack is the thing worth showing.**
    //
    //This used to pull the slider open to 60, on the reasoning that a screenshot of "layers extruded and
    //stacked in space" should not show a flat lump. What that produced was a diagram of a stack rather than
    //a stack: even spacing between films whose real separations are the whole point, and a contact drawn
    //floating in a gap it exists to bridge.
    //
    //At zero, and with the bundled layermap's real heights under it, this is the wafer - li1 at 940 nm,
    //met1 at 1370, mcon spanning exactly the 330 between them, touching at both ends. That is what the app
    //claims to draw and what the surrounding prose in the readme describes, so it is what the picture
    //should be.
    //
    //Set rather than left alone: zero is the default today, and a picture that depends on a default is
    //one that changes when the default does.
    //
    await page.locator('#layerSpacing').evaluate(slider => {
        slider.value = '0';
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    });

    await page.waitForTimeout(1000);

    //Framed by a real control rather than by a number tuned to the frame, which is what the spacing used to
    //be. See Viewer3D's frameAfterDrawing: opening a file frames it, and a spacing change deliberately does
    //not - so whatever the stack has just become, this is the button that fits it.
    await page.locator('#centerView').click();

    await page.waitForTimeout(1500);

    await shoot(page, '3d-view');
});

test('the text editor', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=text`);

    //Monaco arrives through its own AMD loader, so the element exists before the records are in it.
    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 60000 });
    await expect(page.locator('.view-lines')).toContainText('HEADER', { timeout: 60000 });

    await page.waitForTimeout(800);

    await shoot(page, 'text-view');
});

test('the layer sidebar, named from a layermap', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await expect(page.locator('#layerSidebar')).toBeVisible();

    await shoot(page, 'layer-sidebar', { clip: await box(page, '#layerSidebar') });
});

///
///The tree is open in every 2D picture here, because it opens by default - so this one has to show what the
///others do not, or it is the same screenshot under a second name. The first version of it was exactly that,
///byte for byte identical to the standard cell's.
///
///What it has that they do not is depth: a cell holds layers, and a layer holds shapes. Two rows expanded is
///what says so.
///
test('the cell tree, opened down to the shapes', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&tree=true&layermap=${LAYERMAP}`);
    await drawn(page);

    await expect(page.locator('#cellTree')).toBeVisible();

    //licon1 has fifteen, which is enough to read as a list rather than as one more row.
    const folds = page.locator('#cellTree .cellRowFold');

    await folds.nth(6).click();
    await page.waitForTimeout(300);

    await folds.nth(5).click();
    await page.waitForTimeout(600);

    await shoot(page, 'cell-tree');
});

test('the examples picker', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#examplesButton').click();

    await expect(page.locator('#examplePicker')).toBeVisible();

    //
    //Pointed at a row, because the preview only draws for the row under the pointer.
    //
    //The frame used to hold the open file's thumbnail whenever nothing was hovered, so this shot got a
    //picture without asking for one. That idle state is empty now - the picture is what pointing at a name
    //gets you, and a documentation shot of the picker with an empty frame documents the wrong half of it.
    //
    await page.locator('.examplePickerOption').first().hover();

    await expect(page.locator('img.examplePreview')).toBeVisible();
    await page.waitForTimeout(600);

    await shoot(page, 'examples');
});

test('the shape picker and a shape\'s own settings', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await enterCell(page);

    await page.locator('#drawTool').click();

    await expect(page.locator('#shapePicker')).toBeVisible();

    //Hovering the row opens its panel, which is the thing worth a picture.
    await page.locator('#pathShape').hover();

    await expect(page.locator('#pathShape ~ .shapePickPanel')).toBeVisible();

    await shoot(page, 'shape-picker');
});

test('a shape chosen, and what can be done to it', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    //The Select tool first: the panel is what a *selection* offers, and a click with Pan in hand pans.
    await page.locator('#selectTool').click();

    await expect(page.locator('#selectTool')).toHaveClass(/toolButtonOn/);

    await clickAShape(page);

    await expect(page.locator('#selectionPanel')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(400);

    await shoot(page, 'selection');
});

test('the layer settings popup', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('.layerRow .layerSettingsButton').first().click();

    await expect(page.locator('.layerSettingsField').first()).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(400);

    await shoot(page, 'layer-settings');
});

test('the grid, in real units', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&grid=true&snap=true&pitch=0.5&unit=um&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.waitForTimeout(600);

    await shoot(page, 'grid');
});

///
///A net traced across the layout, which is the feature the layermap exists for.
///
///Needs the role column: nothing in a GDSII file says which of its numbers carry a net, so without the
///mapping the button is disabled and there is no picture to take.
///
test('a net traced through its vias', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#selectTool').click();

    //A li1 wire, which climbs through mcon into met1 - three layers, so the highlight crosses the stack
    //rather than lighting one shape.
    await clickShapeOnLayer(page, 'l67_20');

    await expect(page.locator('#traceNet')).toBeEnabled({ timeout: 30000 });

    await page.locator('#traceNet').click();

    //**The net comes back as a selection**, drawn by the same highlight everything else uses - so more than
    //one shape marked is how this knows the trace reached past where it started.
    await expect
        .poll(async () => page.locator('#gdsSVG .shapeSelected').count(), { timeout: 30000 })
        .toBeGreaterThan(1);

    await page.waitForTimeout(600);

    await shoot(page, 'trace-net');
});

///The ruler, dragged across the layout.
test('measuring across the layout', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#measureTool').click();

    //Across the transistor, corner to corner of its implant, so the reading is a diagonal rather than a width. From the
    //shape rather than from numbers tuned to the frame, which put the far end off the drawing once the frame narrowed.
    const across = await page.locator('#gdsSVG path.l93_44').first().boundingBox();

    await page.mouse.move(across.x, across.y);
    await page.mouse.down();
    await page.mouse.move(across.x + across.width, across.y + across.height, { steps: 12 });

    await page.waitForTimeout(500);

    await shoot(page, 'measure');

    await page.mouse.up();
});

///A layer given a hatch, which is what tells two similar shades apart.
test('fill patterns over the colors', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    //diff, then a diagonal hatch from the pattern row of its settings.
    await page.locator('.layerRow .layerSettingsButton').first().click();

    await expect(page.locator('.layerSettingsFills').first()).toBeVisible({ timeout: 30000 });

    await page.locator('.layerSettingsFills button').nth(5).click();

    await page.waitForTimeout(800);

    await shoot(page, 'patterns');
});

///
///The rules, in the panel the layers usually have.
///
///The bundled deck rather than a fixture, because it is the one a sky130 cell arrives with - the PDK in use's -
///and a picture of a hand-fed deck would be a picture of something nobody does.
///
test('the rules panel, with the bundled deck loaded', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#sidebarPanelRules').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

    await shoot(page, 'rules-sidebar', { clip: await box(page, '#layerSidebar') });
});

///
///A run that finds something, which is the picture the feature is for.
///
///The bundled cells are signed-off layout and the real deck finds nothing in them - the right answer and a
///useless picture. The demonstration deck is the one whose limits the layout cannot meet, so the markers,
///the counts and the message all have something to show.
///
test('violations marked on the drawing', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#sidebarPanelRules').click();
    await page.locator('#sidebarPdkManager').click();
    await page.locator('#pdkRulesTab').click();
    await page.locator('#pdkHelp').click();
    await page.locator('#loadStrictDeck').click();

    await expect(page.locator('#drcRun')).toBeVisible({ timeout: 60000 });

    await page.locator('#drcRun').click();

    await expect(page.locator('#drcNotice')).toBeVisible({ timeout: 60000 });

    //The marks are drawn into the SVG, so wait for them rather than for the message that counts them.
    await expect
        .poll(async () => page.locator('#gdsSVG .drcMarker, #gdsSVG .drcMarkerPoint').count(), { timeout: 60000 })
        .toBeGreaterThan(0);

    await page.waitForTimeout(600);

    await shoot(page, 'rules-violations');
});

///
///**The PDK manager**, from the PDK row at the top of the layer panel: the PDKs down its left, and the one in use saying
///what it is - sky130's own README, with its metal stack drawn, where the lists of its layers and rules are picked under it.
///
test('the PDK manager, on sky130\'s README', async ({ page }) => {
    await open(page, `?file=${CELL}&view=2d&layermap=${LAYERMAP}`);
    await drawn(page);

    await page.locator('#sidebarPdkManager').click();

    await expect(page.locator('#pdkReadme h1')).toHaveText('SkyWater SKY130', { timeout: 30000 });

    //Its pictures are files in the PDK, read in after the words.
    await expect
        .poll(async () => page.locator('#pdkReadme img').evaluateAll(pictures => pictures.every(each => each.complete && each.naturalWidth > 0)))
        .toBe(true);

    await page.waitForTimeout(600);

    await shoot(page, 'pdk-manager');
});

///
///**Structures made from numbers, on more than one layer**: a spiral inductor on sky130's met5, its underpass on met4 and
///its vias on via4, each put down at the same point of a new layout - the three share their numbers, so they meet - and
///the Generator's panel open on the spiral, saying what its numbers come to.
///
test('a spiral inductor from the Generator, its three layers lined up', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#newLayout').click();
    await page.locator('#layersSetPdk').click();
    await page.locator('#pdkUse').click();

    await expect(page.locator('.layerRow', { hasText: 'met5 (72/20)' })).toBeVisible({ timeout: 30000 });

    const view = await page.locator('#gdsSVG').boundingBox();
    const middle = { x: view.x + (view.width * 0.5), y: view.y + (view.height * 0.5) };

    for (const [layer, kind] of [['met5 (72/20)', 'spiral'], ['met4 (71/20)', 'underpass'], ['via4 (71/44)', 'inductorvias']]) {
        await page.locator('#drawTool').click();
        await page.locator('.layerRow', { hasText: layer }).first().locator('.layerName').click();

        await openGenerator(page);
        await page.locator('#markKind').selectOption(kind);
        await page.locator('#pickUpMark').click();

        await expect(page.locator('#carryingStamp')).toBeVisible();

        await page.mouse.move(middle.x, middle.y);
        await page.mouse.down();
        await page.mouse.up();

        await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Place/);

        await page.keyboard.press('Escape');
    }

    await page.locator('#centerView').click();
    await page.waitForTimeout(1000);

    //Smaller, and left of and under where the menu hangs: the wheel zooms about the pointer, so out about a point low on
    //the view's left edge.
    await page.mouse.move(view.x + 30, view.y + (view.height * 0.8));

    for (let notch = 0; notch < 5; notch++) {
        await page.mouse.wheel(0, 100);
        await page.waitForTimeout(150);
    }

    await page.waitForTimeout(1200);

    await openGenerator(page);
    await page.locator('#markKind').selectOption('spiral');

    await expect(page.locator('#markNote')).toContainText('nH');
    await page.waitForTimeout(400);

    await shoot(page, 'generator');
});

///The Draw menu's Generator row, pointed at so its panel hangs open beside it.
async function openGenerator(page) {
    await page.locator('#drawTool').hover();
    await page.locator('#markShape').hover();

    await expect(page.locator('#markPanel')).toBeVisible();
}

///
///The assistant, having looked at the cell and drawn what it was asked for.
///
///**The model's side is scripted** - Anthropic's API is intercepted and answers from the lines below, so a picture
///costs nothing and comes out the same every time - and everything else is the app: the cards, the picture `look`
///drew and sent, the rectangle in the view, picked out the way a paste leaves what it pasted. The scripted
///answer names #1 because that is the number the rectangle really gets: nothing was numbered before it.
///
test('the assistant, having drawn what it was asked for', async ({ page }) => {
    const replies = [
        { content: [{ type: 'text', text: 'Let me look at the cell first.' }, { type: 'tool_use', id: 'toolu_1', name: 'look', input: {} }], stop_reason: 'tool_use' },
        { content: [{ type: 'tool_use', id: 'toolu_2', name: 'draw_rectangle', input: { layer: 'met1', from: [1.6, 1.3], to: [2.4, 1.8] } }], stop_reason: 'tool_use' },
        { content: [{ type: 'text', text: 'Done: a 0.8 by 0.5 µm met1 pad from x 1.6 to 2.4 and y 1.3 to 1.8 µm. It is #1, and one press of Undo takes it back.' }], stop_reason: 'end_turn' }
    ];

    let call = 0;

    await page.route(url => (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== 'localhost', route => route.abort());

    await page.route('https://api.anthropic.com/**', async route => {
        const cors = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };

        if (route.request().method() === 'OPTIONS') {
            await route.fulfill({ status: 204, headers: cors });

            return;
        }

        const reply = replies[Math.min(call, replies.length - 1)];

        call++;

        await route.fulfill({ status: 200, headers: { ...cors, 'Content-Type': 'application/json' }, body: JSON.stringify(reply) });
    });

    await page.addInitScript(() => {
        const saved = { Claude: { Name: 'Claude', ProviderType: 'Anthropic', ApiKey: 'not-a-real-key', MaxTokens: 8192 } };

        window.localStorage.setItem('gdsviewer.llmConnections', 'r' + JSON.stringify(saved));
    });

    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#askAiButton').click();
    await page.locator('#aiPrompt').fill('Put a 0.8 by 0.5 µm met1 pad with its corner at x 1.6, y 1.3.');
    await page.locator('#aiSend').click();

    await page.locator('.aiCard[data-tool="draw_rectangle"] .aiApprove').click();

    await expect(page.locator('.aiBubbleAssistant').last()).toContainText('Undo takes it back', { timeout: 30000 });
    await expect(page.locator('.aiCard[data-tool="look"] .aiPicture')).toBeVisible();

    //The change's diff is drawn after its card, which leaves the answer under it out of sight: down to the answer.
    await page.waitForTimeout(800);
    await page.locator('#aiTranscript').evaluate(transcript => transcript.scrollTop = transcript.scrollHeight);
    await page.waitForTimeout(400);

    await shoot(page, 'ask-ai');
});

///
///**Share, writing an embed** - the readme's Embedding section starts here rather than at a block of code to copy
///(2026-11-03, asked for). Taller than the other shots, so the popup's code box is in the frame with its switches.
///
test('the Share popup, writing the code to embed the viewer', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });

    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#shareButton').click();
    await page.locator('#shareEmbedTab').click();
    await page.locator('#shareMode').selectOption({ label: 'The drawing on its own' });

    await expect(page.locator('#shareCode')).toHaveValue(/<iframe/);

    //The address the published app writes, rather than the test server's - what a reader of the picture would get.
    await page.locator('#shareCode').evaluate((code, origin) => {
        code.value = code.value.split(origin + '/').join('https://eecs.blog/BlazorApps/GDSViewer/');
    }, new URL(page.url()).origin);

    await page.waitForTimeout(400);

    const popup = await box(page, '#sharePopup');

    await shoot(page, 'share-embed', { clip: { x: popup.x - 12, y: popup.y - 12, width: popup.width + 24, height: popup.height + 24 } });
});

///
///**And what that code does**: the viewer in somebody else's page, as a reader of it sees it. The page is made up on
///the spot - a heading, a paragraph, the frame - and the frame is the code the Share popup writes, pointed here.
///
test("the viewer embedded in a page of somebody else's", async ({ page, baseURL }) => {
    const src = `${baseURL}/?file=${MOSFET}&view=2d&mode=viewer&banner=false&tree=false`;

    await page.setContent(`
        <body style="margin: 0; padding: 32px 48px; font-family: Georgia, serif; background: #fafafa; color: #222;">
            <h1 style="margin: 0 0 8px; font-size: 28px;">A first look at a MOSFET's layout</h1>
            <p style="margin: 0 0 20px; font-size: 16px; color: #555;">Scroll to zoom, drag to pan - the layout below is live.</p>
            <iframe src="${src}" width="100%" height="520" style="border: 1px solid #ddd; border-radius: 6px;"
                    title="GDS layout viewer"></iframe>
        </body>`);

    const frame = page.frameLocator('iframe');

    await expect
        .poll(async () => frame.locator('#gdsSVG path[class^="l"]').count(), { timeout: 120000 })
        .toBeGreaterThan(0);

    await page.waitForTimeout(1200);

    await shoot(page, 'embedded');
});

///
///**A tour card**, over the layers, with the picture it carries - the eye shutting one layer away, caught mid-loop.
///
test('the guided tour, at the layers', async ({ page }) => {
    await open(page, `?file=${MOSFET}&view=2d`);
    await drawn(page);

    await page.locator('#settingsMenu').click();
    await page.locator('#tourItem').click();

    while (await page.locator('#tourTitle').textContent() !== 'Layers')
        await page.keyboard.press('ArrowRight');

    //Into the loop, where the poly row's eye is shut and its shape gone.
    await page.waitForTimeout(2200);

    await shoot(page, 'tour');
});

///Clicks the middle of the first shape drawn on a given layer's path, so a test can choose *which* shape
///rather than whichever happens to be first.
async function clickShapeOnLayer(page, className) {
    const shape = await page.locator(`#gdsSVG path.${className}`).first().boundingBox();

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///Into a cell, so the drawing tools are live - they are disabled until something says which cell a new
///shape would go in.
async function enterCell(page) {
    await page.locator('#selectTool').click();

    await clickAShape(page);

    await expect
        .poll(async () => page.locator('#drawTool').isDisabled(), { timeout: 30000 })
        .toBe(false);
}

///Clicks the middle of the first drawn shape, which is how everything in this view is chosen.
async function clickAShape(page) {
    const shape = await page.locator('#gdsSVG path[class^="l"]').first().boundingBox();

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));
}

///The rectangle an element occupies, for a screenshot of one panel rather than the page.
async function box(page, selector) {
    const found = await page.locator(selector).boundingBox();

    return {
        x: Math.max(0, Math.floor(found.x)),
        y: Math.max(0, Math.floor(found.y)),
        width: Math.ceil(found.width),
        height: Math.ceil(found.height)
    };
}
