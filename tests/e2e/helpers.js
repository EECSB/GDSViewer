//Shared helpers for the Playwright e2e specs: boot the app, open one of the bundled examples, switch
//views, and read what was drawn.
//
//The app keeps its state in the query string (?file= and ?view=), so most specs can start exactly where
//they mean to rather than clicking their way there. That is also what makes them independent: nothing
//is stored between runs, so there is no shared workspace for parallel workers to tread on.
const { expect } = require('@playwright/test');

//The hand-made example. Small, and the one file whose contents are asserted here: 20 polygons on 6
//layers, 3 labels, and coordinates that go negative - which is what makes it worth using rather than a
//sky130 cell.
//
//It was 18 until the file gained two more licon1 contacts on 66/44, taking that layer from one shape to
//three. Every number below moved with it, and they are measured off the running app rather than counted
//out of the file - see the note on MOSFET_OUTLINES for why the two do not simply agree.
const MOSFET = 'Mosfet';
const MOSFET_POLYGONS = 20;
const MOSFET_LABELS = 3;

//
//And nineteen slabs in 3D, because that view merges each layer before extruding it.
//
//Two of the twenty boundaries are on 66/20 and meet along an edge, so they come out as one outline
//covering exactly the same ground. Held apart from the polygon count rather than derived from it: the two
//views deliberately draw the same layout differently now, and a single number would hide that.
//
const MOSFET_OUTLINES = 19;

//
//**Built as nine meshes**, one for each layer/datatype pair with something on it - which in this file is all
//of them. A mesh says how many outlines went into it, and that is where the nineteen are counted.
//
const MOSFET_MESHES = 9;
const MOSFET_LAYERS = [65, 66, 67, 68, 93, 95];

//The same file by layer/datatype pair, which is what the sidebar lists. Three of its six layers carry two
//purposes each, so keying on the number alone merged nine rows into six.
const MOSFET_LAYER_PAIRS = ['65/20', '66/20', '66/44', '67/20', '67/44', '68/5', '68/20', '93/44', '95/20'];

//
//The rung of the even stack - AdditionalGDSInformation.DefaultLayerSpacing.
//
//Where a layer rests when the file was told no height for it, which after bareNumbers is every layer.
//The spacing slider opens its gap on top of wherever a layer already rests, so a step between untold
//layers is this plus the slider, and at the slider's zero it is exactly this.
//
//It used to be neither: the slider was read as the step itself, so its minimum had to be this number to
//mean "add nothing" and a file could never open on its own process stack. Every assertion below that adds
//this to a slider value used to compare against the slider value alone.
//
const LAYER_RUNG = 50;

//A standard cell, for the cases that need a second file or the sky130-only PDK links.
const SKY130_CELL = 'sky130_fd_sc_hd__nand2_1';

///Opens the app and waits for the WASM runtime to have rendered.
///
///Waits on the Examples button rather than on the example picker, which is behind it now and not in the
///page until the popup is opened. The manifest race that used to be checked here - the picker appearing
///with only its placeholder while that fetch is still in flight - moved with it, into openExamples.
///
///Opens the app, with the cell tree shut unless the caller says otherwise - see gotoExample for why.
///
///`tree` takes three answers rather than two. false and true say so in the address, which beats the session;
///**null says nothing at all**, which leaves the session to decide and is what a spec about restoring one
///has to use. Anything that appends its own `tree=` is left alone.
///
async function gotoApp(page, query = '', tree = false) {
    let address = query;

    if (tree !== null && !address.includes('tree=')) {
        if (address.length === 0)
            address = `?tree=${tree}`;
        else
            address += `&tree=${tree}`;
    }

    await page.goto(`/${address}`);

    await expect(page.locator('#examplesButton')).toBeVisible({ timeout: 60000 });

    //
    //**And the file input wired, not merely drawn.**
    //
    //A change event that arrives before Blazor's InputFile has added its own listener carries no files at
    //all. onFileInputChanged gets FileCount 0 and returns - the same silent return a canceled file dialog
    //takes, and correct for that - so the upload leaves no mark anywhere: nothing drawn, nothing logged,
    //no dialog, and not even a history entry. What is on screen stays whatever the app opened by itself,
    //which is instance.spec.js finding the bundled Mosfet's 20 shapes where it had uploaded a file of 4.
    //
    //Rendered is not enough to wait on, and neither is visible: the element is in the page and already
    //carries its Blazor handler attribute while this is still open. `_blazorInputFileNextFileId` is what
    //that listener sets when it attaches, so it is the readiness itself rather than a proxy for it.
    //
    //Still only a spec can reach it. The input is hidden, so the two ways to a file are the dialog - which
    //cannot be opened and answered inside the window this closes - and a drop on the view, which reads this
    //same flag and declines rather than dispatching into a listener that is not there yet. See onDrop in
    //js/fileDrop.js.
    //
    await page.waitForFunction(() => {
        const input = document.getElementById('fileUpload');

        return input !== null && input._blazorInputFileNextFileId !== undefined;
    }, null, { timeout: 60000 });
}

///
///Opens a file the way the Open button does.
///
///`files` is whatever setInputFiles takes: a path, a list of them, or the {name, mimeType, buffer} form.
///
///It used to say yes to a question first: opening replaced what was on screen, and the app asked before it did.
///With tabs nothing is replaced - the file on screen keeps its tab - and the question went, along with the
///standing yes this armed and the helpers for a spec that wanted to answer it itself.
///
async function uploadFile(page, files) {
    //A relative path is resolved against tests/, not the runner's cwd - the specs were written when the
    //tooling lived at the repository root and 'e2e/fixtures/...' only still resolves when the runner
    //happens to start from tests/. An absolute path or a {name, buffer} object passes through untouched.
    if (typeof files === 'string')
        files = require('path').resolve(__dirname, '..', files);

    await page.locator('#fileUpload').setInputFiles(files);
}

///Opens the Examples popup and waits for the bundled list to have arrived.
async function openExamples(page) {
    await page.locator('#examplesButton').click();

    await expect(page.locator('#examplePicker')).toBeVisible({ timeout: 60000 });

    //Cleared first, because the box keeps whatever was typed in it last time the picker was open. A spec
    //that chooses two examples in turn found the second visit narrowed to one row by the first, and the
    //wait below - which is there for the manifest arriving - then never saw the full list at all.
    await page.locator('.examplePickerFilter').fill('');

    //
    //**Settled, not merely non-empty.**
    //
    //The list is virtualized and arrives in stages: the popup appears with nothing in it, the rows land a
    //moment later, and the headings - which are rows in the same sequence rather than markup wrapped around
    //groups - come after those. Waiting only for a row count over one returns inside that window, with the
    //rows present and the second group's heading not yet rendered. Traced: `rows 0, heads 0` at the open,
    //`rows 27, heads 1` about a second later, `heads 2` two hundred milliseconds after that. A spec that
    //reads `headings.nth(0)` on return is reading the middle of it.
    //
    //Two consecutive agreeing reads rather than a count of the groups the manifest happens to have - a
    //manifest with one group is legitimate and this should not start failing the day somebody ships one.
    //
    let was = null;

    await expect.poll(async () => {
        const now = await page.evaluate(() => ({
            rows: document.querySelectorAll('.examplePickerOption').length,
            heads: document.querySelectorAll('.examplePickerHeading').length
        }));

        const settled = was !== null && was.rows === now.rows && was.heads === now.heads && now.rows > 1;

        was = now;

        return settled;
    }, { timeout: 60000 }).toBe(true);
}

///
///The row for one example, found by narrowing the list to it first.
///
///Filtered rather than scrolled to, because the list is virtualized: a row nine hundred deep is not in the
///page at all until something brings it into view, and typing its name is what a person would do anyway.
///
function exampleRow(page, fileName) {
    return page.locator(`.examplePickerOption[data-file="${fileName}"]`);
}

///Narrows the list, and waits for the row wanted to be the one there.
async function filterExamples(page, text) {
    await page.locator('.examplePickerFilter').fill(text);

    //The list re-renders on the keystroke, so anything read before this is the unfiltered one.
    await expect.poll(async () => page.locator('.examplePickerOption').count(), { timeout: 60000 })
        .toBeGreaterThan(0);
}

///Opens the app straight onto a bundled example, and optionally a view.
///
///Opens an example, with the cell tree shut unless the caller asks otherwise.
///
///**A known page, rather than whatever the app happens to open with.** The tree is docked open for somebody
///using the app, which is right - and it takes 240 pixels out of the canvas and puts a cell row into the
///page for every cell and every layer. A spec about drawing a rectangle should not be at the mercy of a
///panel it never mentions, and one counting rows should not have to know which panel they came from.
///
///Passing tree = true puts it back, which is what cell-tree.spec does. The same parameter an embedder uses
///to open the viewer without it, rather than a hook that exists only for the tests.
///
///**full = false opens it with the page's margins**, for a spec measured to the pixel against them - which every
///spec was, until the app began full screen (2026-11-03). Left out, the app opens as anybody gets it. The same
///embedder's parameter again.
///
async function gotoExample(page, file, view, tree = false, full = null) {
    let query = `?file=${file}`;

    if (view)
        query += `&view=${view}`;

    query += `&tree=${tree}`;

    if (full !== null)
        query += `&full=${full}`;

    await gotoApp(page, query);

    await expectLoaded(page);

    //
    //**And drawn, not merely named** - see selectExample, which waits the same way for the same reason. A load is
    //paced now, so the page renders while the file is read and data-file names it a while before its picture is
    //in and framed: a spec that counted the shapes or read the viewBox straight after this read the page before
    //the file was on it. The text view never puts a note up, so there this passes at once.
    //
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 120000 });
}

///Waits for a file to be open.
///
///Read off the shell's data-file rather than the layer sidebar, because the text view does not render a
///sidebar - waiting for a checkbox there waits forever. It used to read the picker, which is behind the
///Examples button now and not in the page unless that is open.
async function expectLoaded(page) {
    await expect.poll(async () => openFile(page), { timeout: 60000 }).not.toBe('');
}

///
///Waits for a pan, a zoom or a pinch in the 2D view to settle: the picture's shift taken off and the viewBox put on.
///
///**A gesture moves the picture already drawn, and the viewBox follows once it has stopped** - see showViewBox
///in JavaScriptInterOp.js. So the attribute read straight after a gesture is where the view was before it, and
///a spec that reads the viewBox has to wait for this first. The point under the pointer needs no waiting: the
///SVG's own matrix includes the shift.
///
async function viewSettled(page) {
    await expect.poll(async () => page.evaluate(() => {
        const svg = document.getElementById('gdsSVG');

        return svg == null || svg.style.transform === '';
    }), { timeout: 15000 }).toBe(true);
}

///Whatever file the shell currently has open, or '' for none.
async function openFile(page) {
    return page.locator('#mainAppContainer').getAttribute('data-file');
}

///
///The layer/datatype pairs the sidebar lists, in the order it lists them, as "65/20" strings.
///
///A pair per row rather than a layer number, because the pair is what identifies a layer - so Mosfet.gds
///lists nine rows across six layer numbers. A named layer reads "diff.drawing (65/20)", which is why this
///matches the numbers at the end of the label rather than the whole of it.
///
async function layerPairs(page) {
    const labels = await page.locator('.layerRow .layerName').allTextContents();

    if (labels.length > 0)
        return labels.map(text => text.match(/(\d+\/\d+)\)?\s*$/)[1]);

    //Nothing is listed at all when no file is open, and one row is a text box rather than a name while it
    //is being renamed - so fall back to reading the panel.
    const text = await page.locator('body').innerText();

    return [...text.matchAll(/(\d+\/\d+)/g)].map(match => match[1]);
}

///The label a layer row is showing, whether or not it has a name.
async function layerLabel(page, pair) {
    const labels = await page.locator('.layerRow .layerName').allTextContents();

    return labels.find(text => text.includes(pair))?.trim();
}

///
///A layer row's eye - the switch that says whether the layer is drawn - by position in the list.
///
///Scoped to the row and to its own class rather than taken as "the first checkbox on the page": a row
///carries more than one control, and a test expecting fewer shapes after toggling the wrong one waits
///for something that never happens.
///
function layerCheckbox(page, index = 0) {
    return page.locator('.layerRow').nth(index).locator('.layerEyeButton');
}

///
///Hides a layer through the eye on its row, and leaves it hidden if it was already.
///
///**Idempotent, the way `uncheck` was.** The control is a button now rather than a checkbox, and a press on
///a button is a toggle - so a spec that meant "make sure this one is off" and called this twice would turn
///it back on. The state is read off `aria-pressed`, which the row sets from the same field the drawing does.
///
async function hideLayer(page, index = 0) {
    const eye = layerCheckbox(page, index);

    if (await eye.getAttribute('aria-pressed') === 'true')
        await eye.click();

    await expect(eye).toHaveClass(/layerEyeOff/);
}

///Shows a layer again, and leaves it shown if it already is. The pair of hideLayer.
async function showLayer(page, index = 0) {
    const eye = layerCheckbox(page, index);

    if (await eye.getAttribute('aria-pressed') === 'false')
        await eye.click();

    await expect(eye).not.toHaveClass(/layerEyeOff/);
}

///
///Locks or unlocks a layer through the padlock on its row, idempotently and for the same reason.
///
///A locked layer is still drawn - faded - and cannot be chosen, dragged or drawn on. See layer-lock.spec.
///
async function setLayerLocked(page, index, locked) {
    const lock = page.locator('.layerRow').nth(index).locator('.layerLockButton');

    if (await lock.getAttribute('aria-pressed') !== String(locked))
        await lock.click();

    if (locked)
        await expect(lock).toHaveClass(/layerLockOn/);
    else
        await expect(lock).not.toHaveClass(/layerLockOn/);
}

///Opens one layer's settings through the gear at the end of its row.
async function openLayerSettings(page, index = 0) {
    await page.locator('.layerRow').nth(index).locator('.layerSettingsButton').click();

    await expect(page.locator('.layerSettingsField')).toBeVisible();
}

///
///Chooses a color in the open settings: a hue on the slider, and a point in the field.
///
///Saturation and value are fractions of the field rather than pixels, and the exact color that comes out
///is deliberately not asserted anywhere - a picker you drag answers in whatever the pointer landed on, and
///a test that demanded #00ff00 to the digit would be testing the arithmetic that HsvColorTests already
///covers. What matters here is that dragging changes the drawing.
///
async function pickColor(page, { hue, saturation = 1, value = 1 }) {
    const field = page.locator('.layerSettingsField');

    await page.locator('.layerSettingsHue').evaluate((slider, degrees) => {
        slider.value = String(degrees);
        slider.dispatchEvent(new Event('input', { bubbles: true }));
    }, hue);

    //clientWidth rather than boundingBox().width: the box is the *border* box and a click position is
    //measured from the padding box, so on a bordered element the two differ and asking for width - 1
    //lands a pixel outside - on the popup behind, which then refuses the click as intercepted.
    const size = await field.evaluate(node => ({ width: node.clientWidth, height: node.clientHeight }));

    await field.click({
        position: {
            x: Math.min(size.width - 1, Math.max(1, size.width * saturation)),
            y: Math.min(size.height - 1, Math.max(1, size.height * (1 - value)))
        }
    });
}

///The name box at the top of the open settings, which is also its title.
function layerNameBox(page) {
    return page.locator('.layerSettingsName');
}

///The labels switch inside those settings. Belongs to whichever layer's are open.
function labelsToggle(page) {
    return page.locator('.layerLabelsToggle');
}

///
///The distinct layer numbers behind those pairs, in order. For the tests that care which layer is in the
///file rather than which purposes it is drawn for.
///
async function layerNumbers(page) {
    const pairs = await layerPairs(page);
    const numbers = pairs.map(pair => Number(pair.split('/')[0]));

    return [...new Set(numbers)];
}

///
///Opens the 3D view's settings, which hang from the cube of the view switch while that view is on - pressed, or the
///pointer arriving - and leaves them alone if they already are.
///
async function openViewSettings(page) {
    if (await page.locator('#viewSettingsMenu').count() === 0)
        await page.locator('#viewPick3D').click();

    await expect(page.locator('#viewSettingsMenu')).toBeVisible();
}

///
///Chooses a scene backdrop in the 3D view, by the file the shell names it with.
///
///It was a native select, then a menu of pictures behind a button of its own - the names are somebody's and no
///arrangement of words says what a photograph looks like - and it is those rows in the view's settings now, under the
///cube. So this is the one place that knows a backdrop is chosen by opening them and pressing a row. They stay open as
///one is chosen, so the pointer leaving puts them away after, as it would for somebody.
///
async function selectBackground(page, file) {
    await openViewSettings(page);

    const row = page.locator(`#viewBackground [data-background="${file}"]`);

    await row.click();

    await expect(row).toHaveClass(/shapePickOn/);

    await page.mouse.move(4, 4);

    await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);
}

///
///Saves the view as a picture, from the camera in its corner: the camera opens a menu of the two kinds, and the
///line chosen in it saves. kind is 'svg' or 'png'.
///
async function saveImage(page, kind) {
    let line = '#downloadSvg';

    if (kind === 'png')
        line = '#downloadPng';

    await page.locator('#downloadImage').click();
    await page.locator(`#imageMenu ${line}`).click();
}

///
///Switches view, by the value the shell uses.
///
///It was a native select, then a button that opened a menu, and it is three boxes with one of them lit
///now - so this is the one place that knows how a view is chosen, rather than seventeen places each
///knowing it and each needing changing again.
///
async function selectView(page, viewType) {
    await page.locator(`#viewPick [data-view="${viewType}"]`).click();

    await expect(page.locator(`#viewPick [data-view="${viewType}"]`)).toHaveAttribute('aria-pressed', 'true');
}

///Chooses a bundled example through the picker rather than the address, opening the popup it lives in.
///
///Closes it afterwards, because it does not close itself - it stays up so several cells can be looked at
///in turn - and it overlays the page while open, so anything clicking after this would click through it.
async function selectExample(page, fileName) {
    await openExamples(page);

    await filterExamples(page, fileName);

    await exampleRow(page, fileName).click();

    //
    //**That file, not merely a file.** A file is open the whole time - the one that was, until the chosen one
    //arrives - so waiting for any file returned with the old one still on screen, and the next step of a spec
    //acted on it. The question that used to come first gave the fetch a head start that hid it.
    //
    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(fileName);

    //
    //**And in, not merely named.** The shell writes the chosen file's name before it has read the file, and
    //data-file says it from that render - while the layout is swapped in only after the paint that puts the
    //loading note on screen, a frame later. A step taken in that frame acted on the file still open: a live
    //link call made the moment data-file changed was answered on Mosfet, about one run in five. The note goes
    //once the chosen file is drawn, which is after it is in; the text view never puts one up, so there this
    //passes at once.
    //
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });

    //
    //Gone on its own, rather than needing the pointer moved off it.
    //
    //This used to call closeExamples, which moves the mouse to a corner and waits for the popup to go. The
    //popup closes itself on a choice now - see closeOnChoice in Viewer.razor - so that wait was already
    //satisfied when it started, and the mouse move was a gesture with nothing left to do. Waited on rather
    //than assumed, since what follows a selectExample is usually a click somewhere the popup used to cover.
    //
    await expect(page.locator('#examplePicker')).toHaveCount(0);
}

///
///Dismisses the Examples popup, by taking the pointer off it.
///
///There is nothing to press. The list hangs off its button and closes when the pointer leaves the two of
///them, so putting it away is a move rather than a click - which is the whole point of it, and is why the
///cross it used to have is gone.
///
///mouse.move rather than hovering something, because everything worth naming is either under the popup or
///somewhere a spec would rather not send a click. A corner of the window is neither.
///
async function closeExamples(page) {
    await page.mouse.move(4, 4);

    await expect(page.locator('#examplePicker')).toHaveCount(0);
}

///Counts the shapes the 2D view drew.
///
///Every fill color the 2D view is drawing, as hex.
///
///**Computed rather than read off the element.** A shape's color is a rule keyed on its layer class now,
///not an attribute repeated on every one of them - see SvgWriter.appendStyle. Which is also the stronger
///question: it proves the rule reached the shape, where an attribute only proved it was written down.
///
///Put back into #rrggbb because that is what the layer settings, the palette and every layermap are
///written in, and a browser reports a computed color as rgb(). Converting here rather than in each
///assertion keeps the specs about colors instead of about notation.
///
///Only shapes the layout drew: JavaScript inserts the grid, the preview, the band and the snap mark into
///the same SVG, and none of those carry a data-element.
///
async function fillsDrawn(page) {
    return (await allFills(page)).map(computed => {
        const channels = computed.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/);

        if (channels === null)
            return computed;

        return '#' + channels.slice(1, 4)
            .map(channel => Number(channel).toString(16).padStart(2, '0'))
            .join('');
    });
}

///
///Every shape the layout drew, in the order the layout holds them.
///
///**One place that knows how the picture is written down.** A spec asking "how many shapes" or "where is
///the third one" is asking about the layout, not about the DOM - and the two stopped being the same thing
///when the picture became one path per layer with a subpath per shape. Nothing addressable by a CSS
///selector corresponds to a shape any more, so the question has to be answered by reading the markup here
///rather than by 230 hand-written locators.
///
///Reads either form. Per-shape polygons carry their own points; a merged path carries a subpath each, with
///`data-elements` listing which element drew which - so the order is the layout's either way, and not the
///drawing order, which groups by layer.
///
///Returned in layout coordinates. `shapeBox` is what converts to the screen, because that conversion needs
///the SVG's own transform and nothing else here does.
///
function shapesDrawn(page, withLabels = false, root = '#gdsSVG') {
    return page.evaluate(([withLabels, root]) => {
        const svg = document.querySelector(root);

        if (svg == null)
            return [];

        const found = [];

        //JavaScript puts the grid, the preview, the band, the snap mark and the selection overlay into the
        //same SVG. None of those carry an element number, which is what tells a drawn shape from a drawn
        //decoration.
        //Which of the three editing states the shape is in, so a spec can ask for the cell being edited
        //rather than for the whole layout. It is a class on the shape in one form and on the layer's path
        //in the other.
        const marks = (node) => [...node.classList].filter(name => !/^l-?\d+_\d+$/.test(name));

        //Direct children only. Blazor's markup goes straight into the SVG, so a layout shape is one; the
        //selection overlay is inside a group of its own and would otherwise be counted twice - once as the
        //shape and once as the highlight drawn over it.
        for (const shape of svg.querySelectorAll(':scope > polygon[data-element], :scope > polyline[data-element]')) {
            const numbers = shape.getAttribute('points').trim().split(/[\s,]+/).map(Number);
            const points = [];

            for (let i = 0; i + 1 < numbers.length; i += 2)
                points.push([numbers[i], numbers[i + 1]]);

            found.push({
                element: Number(shape.getAttribute('data-element')),
                marks: marks(shape),
                fill: getComputedStyle(shape).fill,
                opacity: parseFloat(getComputedStyle(shape).opacity),
                points
            });
        }

        //Labels only when they are asked for. They are elements of the layout like anything else, but a
        //count of "shapes" that quietly included them is exactly the confusion the aligning tests exist to
        //catch - a label counted as geometry once made a move look like it had worked.
        if (withLabels) {
            for (const label of svg.querySelectorAll(':scope > text[data-element]')) {
                found.push({
                    element: Number(label.getAttribute('data-element')),
                    marks: marks(label),
                    fill: getComputedStyle(label).fill,
                    opacity: parseFloat(getComputedStyle(label).opacity),
                    text: label.textContent,
                    points: [[Number(label.getAttribute('x')), Number(label.getAttribute('y'))]]
                });
            }
        }

        for (const path of svg.querySelectorAll(':scope > path[data-elements]')) {
            const elements = path.getAttribute('data-elements').trim().split(/\s+/).map(Number);
            const mark = marks(path);
            const style = getComputedStyle(path);
            const fill = style.fill;
            const opacity = parseFloat(style.opacity);

            //A subpath is one shape, which is the whole reason the picture can be one node per layer and
            //still say what it is made of. Read by the app's own reader, because the picture writes a shape
            //as steps between its corners rather than as the corners - see subpaths in viewGeometry.js.
            const runs = window.viewGeometry.subpaths(path.getAttribute('d'));

            for (let i = 0; i < runs.length; i++) {
                const numbers = runs[i].corners;
                const points = [];

                for (let n = 0; n + 1 < numbers.length; n += 2)
                    points.push([numbers[n], numbers[n + 1]]);

                found.push({ element: elements[i], marks: mark, fill, opacity, points });
            }
        }

        found.sort((one, other) => one.element - other.element);

        return found;
    }, [withLabels, root]);
}

///
///The picture in a preview frame - the Examples and History popups', or a cell list's - read back in the page: whose it
///says it is, how many pixels it is across and down, how many of them are drawn on, and the box round those.
///
///**An image since 2026-10-17**, not markup on the page - a PNG, or a small layout's SVG as a document of its own since
///2026-10-21 - see PreviewPictures in the app - so there are no shapes in it to count.
///What a spec can still ask is whether there is a picture, whose it is, and whether it is framed on what it draws; the
///drawing itself is pinned pixel by pixel in PngWriterTests. Null while the frame has no picture, or one that has not
///decoded yet, so a spec polls.
///
async function previewPicture(page, root = 'img.examplePreview') {
    return await page.evaluate(async selector => {
        const picture = document.querySelector(selector);

        if (picture == null || !picture.complete || picture.naturalWidth === 0)
            return null;

        const canvas = document.createElement('canvas');

        canvas.width = picture.naturalWidth;
        canvas.height = picture.naturalHeight;

        const context = canvas.getContext('2d');

        context.drawImage(picture, 0, 0);

        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        let drawn = 0;
        let left = canvas.width;
        let top = canvas.height;
        let right = -1;
        let bottom = -1;

        for (let y = 0; y < canvas.height; y++) {
            for (let x = 0; x < canvas.width; x++) {
                if (pixels[((y * canvas.width) + x) * 4 + 3] === 0)
                    continue;

                drawn++;
                left = Math.min(left, x);
                top = Math.min(top, y);
                right = Math.max(right, x);
                bottom = Math.max(bottom, y);
            }
        }

        return {
            alt: picture.getAttribute('alt'),
            width: canvas.width,
            height: canvas.height,
            drawn: drawn,
            box: { left: left, top: top, right: right, bottom: bottom },
            source: picture.getAttribute('src')
        };
    }, root);
}

///
///The same, with the labels in - what a cell actually holds, rather than only its geometry.
///
async function shapesAndLabels(page, marked = null) {
    const found = await shapesDrawn(page, true);

    if (marked === null)
        return found;

    return found.filter(shape => shape.marks.includes(marked));
}

///
///Just the ones marked a given way - `inContext` for the cell being edited, `alsoAffected` for its other
///instances, `outOfContext` for everything else. Null for the whole layout.
///
async function shapesMarked(page, marked = null) {
    const shapes = await shapesDrawn(page);

    if (marked === null)
        return shapes;

    return shapes.filter(shape => shape.marks.includes(marked));
}

///
///How many shapes the layout drew.
///
///**Counted in the page, not by reading them all back.** Every other helper here hands the shapes over to
///be looked at, which is right when a spec wants their corners and wrong when it only wants how many:
///polling that on a twenty-thousand-element layout parses the whole picture and marshals twenty thousand
///objects across the wire per poll, and it turned an open measured at 0.7 s into one measured at 3.2 s.
///The number was the harness, not the app.
///
async function shapeCount(page, marked = null) {
    return page.evaluate((marked) => {
        const svg = document.getElementById('gdsSVG');

        if (svg == null)
            return 0;

        const carries = (node) => marked === null || node.classList.contains(marked);

        let found = 0;

        for (const shape of svg.querySelectorAll(':scope > polygon[data-element], :scope > polyline[data-element]')) {
            if (carries(shape))
                found++;
        }

        //A subpath each, so the moves are the shapes.
        for (const path of svg.querySelectorAll(':scope > path[data-elements]')) {
            if (carries(path))
                found += path.getAttribute('data-elements').trim().split(/\s+/).length;
        }

        return found;
    }, marked);
}

///
///The nth drawn shape, counting from the end when nth is negative - so -1 is the last, the way
///`locator(...).last()` used to be reached.
///
function nthOf(shapes, nth) {
    if (nth < 0)
        return shapes[shapes.length + nth];

    return shapes[nth];
}

///Its corners, as the `points` string a polygon carried: "x,y x,y".
async function shapePoints(page, nth = 0, marked = null) {
    return asPoints(nthOf(await shapesMarked(page, marked), nth));
}

///What every one of them computes to, which is how a spec asks which layer a shape ended up on.
async function allFills(page, marked = null) {
    return (await shapesMarked(page, marked)).map(shape => shape.fill);
}

///
///Where it is on the screen, for clicking - the same rectangle `boundingBox()` gave.
///
///Worked out from the coordinates through the SVG's own screen transform rather than measured off a node,
///because with a merged picture there is no node whose box is one shape.
///
///
///A shape the selection panel is not sitting on top of.
///
///**Which matters for anything that clicks the same shape twice.** The panel opens over the top left of the
///canvas on the first click, and with the cell tree docked beside it the canvas is 240px narrower - so the
///panel covers a good half of what is left, and the second click lands on the panel rather than on the
///shape. Descending into a placement takes two clicks on one shape, and that is what it was failing at.
///
///Answered by where the panel *would* be rather than where it is, since it is not up yet when the first
///click is aimed: its left edge is the canvas's and it is at most 22em wide. A shape past that is a shape
///both clicks can reach.
///
///Falls back to the first shape when every one of them is under it, which is a layout too small to test
///this way rather than a shape worth hunting for.
///
async function shapeClearOfThePanel(page, marked = null) {
    const canvas = await page.locator('#gdsSVG').boundingBox();
    const clearOf = canvas.x + 320;

    const many = (await shapesMarked(page, marked)).length;

    for (let nth = 0; nth < many; nth++) {
        const box = await shapeBox(page, nth, marked);

        if (box !== null && box.x + (box.width / 2) > clearOf)
            return box;
    }

    return shapeBox(page, 0, marked);
}

async function shapeBox(page, nth = 0, marked = null) {
    const shape = nthOf(await shapesMarked(page, marked), nth);

    if (shape == null)
        return null;

    return page.evaluate((points) => {
        const svg = document.getElementById('gdsSVG');
        const toScreen = svg.getScreenCTM();
        const corner = svg.createSVGPoint();

        let left = Infinity;
        let top = Infinity;
        let right = -Infinity;
        let bottom = -Infinity;

        for (const [x, y] of points) {
            corner.x = x;
            corner.y = y;

            const on = corner.matrixTransform(toScreen);

            left = Math.min(left, on.x);
            top = Math.min(top, on.y);
            right = Math.max(right, on.x);
            bottom = Math.max(bottom, on.y);
        }

        return { x: left, y: top, width: right - left, height: bottom - top };
    }, shape.points);
}

///
///The shape a particular element drew, or null when that element is not on screen.
///
///The number is taken through `Number` on the way in, because half the callers have it as one and half
///read it out of an attribute as a string - and a strict comparison between the two quietly finds nothing
///rather than failing, which reads as "that element is not drawn".
///
async function elementPoints(page, element) {
    return asPoints((await shapesDrawn(page)).find(each => each.element === Number(element)));
}

///Whether it drew at all - one when it did, none when the layer is off or it was culled.
async function elementCount(page, element) {
    return (await shapesDrawn(page)).filter(each => each.element === Number(element)).length;
}

///What that element's shape computes to, which is the question a layer change asks.
async function elementFill(page, element) {
    return (await shapesDrawn(page)).find(each => each.element === Number(element))?.fill ?? null;
}

///
///Every drawn shape's corners, optionally only those marked a given way - `inContext` for the cell being
///edited, `outOfContext` for its surroundings.
///
///The shape of assertion this replaces read `points` off every polygon; with a merged picture there is no
///polygon per shape, and the mark is on the layer's path rather than on each of them.
///
async function allPoints(page, marked = null) {
    return (await shapesMarked(page, marked)).map(asPoints);
}

function asPoints(shape) {
    if (shape == null)
        return null;

    return shape.points.map(point => point.join(',')).join(' ');
}

///
///What the 2D view is showing, in one read.
///
///`polygons` is the count of *shapes*, which stopped being the count of nodes when the picture became one
///path per layer - so it goes through the same reader every other shape question does. The name is kept
///because the question has not changed and forty assertions ask it.
///
async function svgCounts(page) {
    const drawn = await shapesDrawn(page);

    const rest = await page.evaluate(() => {
        const svg = document.getElementById('gdsSVG');

        if (svg == null)
            return null;

        return {
            labels: svg.querySelectorAll('text').length,
            viewBox: svg.getAttribute('viewBox')
        };
    });

    if (rest === null)
        return null;

    //
    //**Computed, not read off the element.**
    //
    //The color and the opacity are a rule rather than an attribute per shape - a hundred bytes of the same
    //words repeated per element was most of the markup. Asking the browser what a shape actually ends up at
    //is the stronger question anyway: it proves the rule reached the shape, where an attribute only proved
    //it was written down.
    //
    let opacity = null;
    let fill = null;
    let points = null;

    if (drawn.length > 0) {
        opacity = String(drawn[0].opacity);
        fill = drawn[0].fill;
        points = asPoints(drawn[0]);
    }

    return {
        polygons: drawn.length,
        labels: rest.labels,
        opacity: opacity,
        fill: fill,
        points: points,
        viewBox: rest.viewBox
    };
}

///
///The heights the layers are actually stacked at, one per distinct height, low to high.
///
///**Redrawn by a layer switch rather than by the spacing slider**, which is the whole point of it. Every
///other way into the scene here nudges that slider to force a draw - and a nudge calls SetStackingOffsets,
///which is the one action that would repair a stack drawn at the wrong spacing. A layer switched off and
///straight back on asks for the same redraw and changes no height, so what comes back is the stack as the
///file was drawn rather than as the measurement left it.
///
///The offsets are database units and become the mesh's y directly; see ThreeInterop's `mesh.position.set`.
///
async function stackHeights(page) {
    return page.evaluate(async () => {
        const THREE = await import('three');
        const added = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects)
                added.push(object);

            return original.apply(this, objects);
        };

        const box = document.querySelector('.layerEyeButton');

        box.click();

        await new Promise(resolve => setTimeout(resolve, 400));

        box.click();

        await new Promise(resolve => setTimeout(resolve, 800));

        THREE.Object3D.prototype.add = original;

        //The slabs, which are the meshes carrying outlines: a layer's labels are a mesh too, lifted clear of it.
        const heights = added
            .filter(object => object.isMesh && object.userData.outlines != null)
            .map(object => Math.round(object.position.y));

        return [...new Set(heights)].sort((low, high) => low - high);
    });
}

///What the 3D scene holds, counted through three's own object graph.
///
///Hooks Object3D.add and then forces a redraw, because the scene is built inside a module the specs
///cannot reach into - this is the same route the manual checks took.
///
///`meshes` are the slabs, a mesh per layer carrying the outlines in it. The labels are meshes too, a layer's
///at a time, each carrying how many labels it draws and per label the point it hangs from - so `labels` is
///counted out of them and `labelMeshes` says how many draws that took.
async function threeCounts(page) {
    return page.evaluate(async () => {
        const THREE = await import('three');
        const added = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects)
                added.push(object);

            return original.apply(this, objects);
        };

        //The layer-spacing slider is the cheapest way to ask for a redraw from outside. By id since it moved
        //out of the view and into the layer sidebar, where it sits under the list the way opacity does in 2D.
        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 500));

        THREE.Object3D.prototype.add = original;

        const meshes = added.filter(object => object.isMesh && object.userData.outlines != null);
        const labeled = added.filter(object => object.isMesh && object.userData.labels != null);

        return {
            meshes: meshes.length,
            outlines: meshes.reduce((sum, mesh) => sum + mesh.userData.outlines, 0),
            labels: labeled.reduce((sum, mesh) => sum + mesh.userData.labels, 0),
            labelMeshes: labeled.length,
            labelHangs: labeled.flatMap(mesh => {
                const hang = mesh.geometry.getAttribute('hang');
                const points = [];

                for (let i = 0; i < hang.count; i++)
                    points.push([hang.getX(i), hang.getY(i)]);

                return points;
            })
        };
    });
}

///Waits for the text view to hold the file.
///
///The editor element appears before its content does: Monaco is fetched lazily through its own AMD
///loader and the dump is handed to it afterwards, so reading too early returns an empty buffer - which
///then looks like a file with no records rather than a test that did not wait.
async function expectEditorLoaded(page) {
    await expect(page.locator('.monaco-editor').first()).toBeVisible({ timeout: 60000 });

    await expect.poll(async () => editorText(page), { timeout: 60000 }).toContain('HEADER:');
}

///The text view's buffer, straight out of Monaco.
async function editorText(page) {
    return page.evaluate(() => {
        if (typeof window.GetMonacoContent !== 'function')
            return '';

        return window.GetMonacoContent();
    });
}

///
///Replaces the text view's buffer and presses save, returning whatever the app said about it.
///
///Read off the strip under the editor. It used to be read by replacing window.alert, which stopped being
///the truth when the message moved into the page - and a helper that watches the wrong place reports
///silence rather than failing, so anything asserting on the text would have passed with nothing said at
///all. A dialog appearing now is an error in itself: it means the message went back into a popup.
///
///An array of one, so the callers' messages.join(' ') still reads the same.
///
async function saveEditorText(page, text) {
    const dialogs = [];
    const onDialog = (dialog) => {
        dialogs.push(dialog.message());

        dialog.dismiss();
    };

    page.on('dialog', onDialog);

    await page.evaluate(async (contents) => window.SetMonacoContent(contents), text);

    await page.locator('#saveGdsText').click();

    //Polled: the save is a round trip through .NET, so the strip is not there on the next tick.
    await expect.poll(async () => page.locator('.editorMessage').count(), { timeout: 30000 }).toBeGreaterThan(0);

    const message = (await page.locator('.editorMessageText').innerText()).trim();

    page.off('dialog', onDialog);

    if (dialogs.length > 0)
        throw new Error(`Save reported through a dialog rather than the page: ${dialogs.join(' | ')}`);

    return [message];
}

///Whether the strip under the editor is the red one.
async function editorSaveFailed(page) {
    return page.locator('.editorMessage').evaluate(node => node.classList.contains('editorMessageError'));
}

///Stashes the three.js scene on the page so later steps can read the camera and background out of it.
///
///The scene lives inside a module the specs cannot reach into, so it is found the way everything else
///here finds it: hook Object3D.add, force a redraw, and walk up from whatever was added.
async function captureScene(page) {
    await page.evaluate(async () => {
        const THREE = await import('three');
        let captured = null;

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            if (captured === null && objects.length > 0)
                captured = objects[0];

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 500));

        THREE.Object3D.prototype.add = original;

        //Added to the chip group, which is added to the scene.
        window.__gdsScene = captured?.parent?.parent ?? null;
    });
}

///Where the camera is, out of the captured scene.
async function cameraPosition(page) {
    return page.evaluate(() => {
        const camera = window.__gdsScene?.children.find(child => child.isCamera);

        if (camera == null)
            return null;

        return { x: camera.position.x, y: camera.position.y, z: camera.position.z };
    });
}

///
///Where the stack lands on screen, through the camera the scene renders with - and the scene stashed, for
///cameraPosition after it.
///
///The corners the view frames on are projected - each layer's box, and a box around each layer's labels - so
///`left`, `right`, `bottom` and `top` inside -1..1 are inside the frame, and `nearest` and `farthest` inside
///-1..1 are between the near and far planes: past either, the renderer clips the stack away however well it is
///framed. Every vertex is inside one of those boxes. `pitch` is how far the camera looks down, in degrees, and
///`middle` and `radius` are the sphere around the stack.
///
///Found the way threeCounts finds the scene - a redraw asked for through the spacing slider - and counted until
///a second passes with nothing added, which a design on the dev server needs. Then two frames, because the
///planes are fitted to the stack on the frame after it changes.
///
async function stackOnScreen(page) {
    return page.evaluate(async () => {
        const THREE = await import('three');

        const added = [];
        let lastAdded = performance.now();

        const original = THREE.Object3D.prototype.add;

        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh) {
                    added.push(object);
                    lastAdded = performance.now();
                }
            }

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');

        slider.value = String(Number(slider.value) + 1);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        const started = performance.now();

        while (performance.now() - started < 60000) {
            await new Promise(resolve => setTimeout(resolve, 250));

            if (added.length > 0 && performance.now() - lastAdded > 1000)
                break;
        }

        THREE.Object3D.prototype.add = original;

        //Still in the scene, in case the redraw came twice and the first one's objects were let go. The slabs
        //are the meshes carrying outlines; the others are layers' labels.
        const drawn = added.filter(object => object.parent != null);
        const meshes = drawn.filter(object => object.userData.outlines != null);

        if (meshes.length === 0)
            return { meshes: 0 };

        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

        //Up to the scene, then the camera it holds - the same one the renderer draws with.
        let root = meshes[0];

        while (root.parent)
            root = root.parent;

        window.__gdsScene = root;

        const camera = root.children.find(child => child.isCamera);

        if (camera == null)
            return { meshes: meshes.length, camera: false };

        //A box per object, which is a box per layer and a box around each layer's labels.
        const boxes = [];

        for (const object of drawn) {
            object.updateMatrixWorld(true);

            boxes.push(new THREE.Box3().expandByObject(object));
        }

        camera.updateMatrixWorld(true);

        const whole = new THREE.Box3();
        const on = { left: 9, right: -9, bottom: 9, top: -9, nearest: 9, farthest: -9 };

        for (const box of boxes) {
            whole.union(box);

            for (const x of [box.min.x, box.max.x])
                for (const y of [box.min.y, box.max.y])
                    for (const z of [box.min.z, box.max.z]) {
                        const corner = new THREE.Vector3(x, y, z).project(camera);

                        on.left = Math.min(on.left, corner.x);
                        on.right = Math.max(on.right, corner.x);
                        on.bottom = Math.min(on.bottom, corner.y);
                        on.top = Math.max(on.top, corner.y);
                        on.nearest = Math.min(on.nearest, corner.z);
                        on.farthest = Math.max(on.farthest, corner.z);
                    }
        }

        const looking = camera.getWorldDirection(new THREE.Vector3());
        const sphere = whole.getBoundingSphere(new THREE.Sphere());

        return {
            meshes: meshes.length,
            camera: true,
            ...on,
            pitch: Math.asin(-looking.y) * (180 / Math.PI),
            middle: { x: sphere.center.x, y: sphere.center.y, z: sphere.center.z },
            radius: sphere.radius
        };
    });
}

///
///What stackOnScreen found, said as one line, for an assertion's message.
///
///Depth to five places, because that is where it changes: it crowds up towards 1 with distance, and a
///design past the far plane came out at 1.00003 - which at two places reads as exactly the limit.
///
function onScreenSaid(on) {
    const two = value => value.toFixed(2);
    const five = value => value.toFixed(5);

    return `x ${two(on.left)}..${two(on.right)}, y ${two(on.bottom)}..${two(on.top)}, depth ${five(on.nearest)}..${five(on.farthest)}`;
}

///Reads a download to a Buffer, so its bytes can be compared rather than only its name.
async function downloadBytes(download) {
    const stream = await download.createReadStream();
    const chunks = [];

    for await (const chunk of stream)
        chunks.push(chunk);

    return Buffer.concat(chunks);
}

///
///A GDSII file's bytes drawn on other layer numbers - a file from another process, for whether the PDK in use lays
///itself over one (it does over a file most of whose layers are its own; see the PDK manager). Each LAYER record's
///number is rewritten in place by `renumbered`, `{ 65: 22 }`, so the file is otherwise the one it was made from.
///
function onOtherLayers(bytes, renumbered) {
    const copy = Buffer.from(bytes);

    for (let at = 0; at + 4 <= copy.length; ) {
        const length = copy.readUInt16BE(at);

        if (length < 4)
            break;

        //LAYER is record 0x0D, holding one two-byte integer.
        if (copy[at + 2] === 0x0D && copy[at + 3] === 0x02) {
            const number = copy.readInt16BE(at + 4);

            if (renumbered[number] !== undefined)
                copy.writeInt16BE(renumbered[number], at + 4);
        }

        at += length;
    }

    return copy;
}

///Mosfet's layers moved onto gf180mcu's numbers - COMP, Poly2, Contact, Metal1, Nplus and SAB - none of them sky130's.
const MOSFET_ELSEWHERE = { 65: 22, 66: 30, 67: 33, 68: 34, 93: 32, 95: 49 };

///
///How far in from the left of the view a click has to be to miss the selection panel.
///
///The panel is solid - it takes its own clicks rather than letting them through to the layout behind it -
///and it sits over the top-left of the view whenever anything is chosen. So a spec that picks a *second*
///shape has to pick one the panel is not covering, or the click lands on the panel and nothing happens.
///
///Measured at 299 by 351 in an 823 by 428 view with two shapes chosen, which is when it is at its tallest.
///One number here rather than an offset guessed in each spec, so it stays right if the panel changes size.
///
const CLEAR_OF_PANEL = 360;

///
///The layer the selection panel says the chosen shapes are on, or '' when they are not all on one.
///
///Read off the picker's own `data-layer` rather than its text: the button reads the layer's *name* where it
///has one - `diff.drawing (65/20)` - and the pair is what identifies it. It is a built dropdown rather than
///a `select` because each row carries a swatch, which an `option` cannot; see layerPicker in the CSS.
///
async function chosenLayer(page) {
    return page.locator('#chosenLayer').getAttribute('data-layer');
}

///Opens the panel's layer picker and takes one, which moves whatever is chosen onto that layer.
async function chooseLayer(page, pair) {
    await page.locator('#chosenLayer').click();

    await expect(page.locator('#chosenLayerList')).toBeVisible();

    await page.locator(`.layerPickerOption[data-layer="${pair}"]`).click();
}

///The layers the panel's picker offers, as `65/20` pairs, in the order it lists them.
async function layersOffered(page) {
    await page.locator('#chosenLayer').click();

    await expect(page.locator('#chosenLayerList')).toBeVisible();

    const offered = await page.locator('.layerPickerOption').evaluateAll(nodes =>
        nodes.map(node => node.getAttribute('data-layer')));

    //Shut again, so the list is not left over the panel for whatever the spec does next.
    await page.locator('#chosenLayer').click();

    return offered;
}

///
///Clicks a bare patch of the view, which clears the selection and takes the panel away with it.
///
///For loops that try one shape after another. The panel opens on the first click and takes its own clicks,
///so without this every later attempt at a shape behind it lands on the panel and the selection never
///moves - the loop then reads the first shape's answer over and over.
///
///Bare means the SVG itself is the topmost thing at that point: no shape, no panel, no button.
///
async function dismissSelection(page) {
    const at = await page.evaluate(() => {
        const svg = document.getElementById('gdsSVG');

        if (svg == null)
            return null;

        const box = svg.getBoundingClientRect();

        for (let y = box.top + 6; y < box.bottom - 6; y += 7) {
            for (let x = box.left + 6; x < box.right - 6; x += 7) {
                if (document.elementFromPoint(x, y) === svg)
                    return { x, y };
            }
        }

        return null;
    });

    if (at === null)
        return false;

    await page.mouse.click(at.x, at.y);

    return true;
}

///
///The middle of a shape the selection panel is not covering, and which is not already chosen.
///
///For the second click of a multi-select. The panel is solid and opens over the top-left as soon as the
///first shape is picked, so a second click landing there hits the panel and the selection never grows.
///Returns null when every shape is behind it, which is a fixture worth failing on rather than working
///around.
///
async function otherShapeClearOfPanel(page, root) {
    const count = await shapeCount(page, root);

    for (let nth = 0; nth < count; nth++) {
        const box = await shapeBox(page, nth, root);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        const reachable = await page.evaluate(([x, y]) => {
            const panel = document.getElementById('selectionPanel');

            if (panel != null) {
                const over = panel.getBoundingClientRect();

                if (x >= over.left && x <= over.right && y >= over.top && y <= over.bottom)
                    return false;
            }

            //And not one already picked out, or the click would take it back off the selection.
            const under = document.elementsFromPoint(x, y);

            return !under.some(node => node.classList != null && node.classList.contains('shapeSelected'));
        }, [at.x, at.y]);

        if (reachable)
            return at;
    }

    return null;
}

///
///The middle of a shape that *is* chosen and that the selection panel is not covering.
///
///The mirror of otherShapeClearOfPanel, and for the gesture after it: that one finds a shape to *add*, so it
///skips whatever is already picked out, and this one finds a shape to take hold *of*. Asked after the whole
///selection is made rather than before, because the panel grows with what is in it - a point measured clear
///of it with one shape chosen can be behind it with two.
///
///Returns null when every chosen shape is behind the panel, which is a fixture worth failing on.
///
async function chosenShapeClearOfPanel(page, root) {
    const count = await shapeCount(page, root);

    for (let nth = 0; nth < count; nth++) {
        const box = await shapeBox(page, nth, root);
        const at = { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };

        const usable = await page.evaluate(([x, y]) => {
            const panel = document.getElementById('selectionPanel');

            if (panel != null) {
                const over = panel.getBoundingClientRect();

                if (x >= over.left && x <= over.right && y >= over.top && y <= over.bottom)
                    return false;
            }

            //And one that is picked out, which is what a drag of the group has to start on.
            const under = document.elementsFromPoint(x, y);

            return under.some(node => node.classList != null && node.classList.contains('shapeSelected'));
        }, [at.x, at.y]);

        if (usable)
            return at;
    }

    return null;
}

///
///Which layer a drawn shape would go on, as the `65/20` pair, or null when nothing says.
///
///Read off the sidebar's marked row. There used to be a dropdown in the toolbar to read instead; the row
///is the control now, and it is also the readout - see "Which layer a shape goes on" in DOCUMENTATION.
///
async function drawingLayer(page) {
    const marked = page.locator('.layerRowDrawing .layerName');

    if (await marked.count() === 0)
        return null;

    //The row's label carries the layer's name where it has one - `diff.drawing (65/20)` - and the pair is
    //the part that identifies it.
    return ((await marked.first().textContent()).match(/\d+\/\d+/) || [null])[0];
}

///
///Which layers the sidebar marks as holding a chosen shape, as `65/20` pairs in the order they are listed.
///
///The same mark as the row being drawn on - see drawingLayer - under a class of its own, so the two can be
///told apart here though they look alike on screen.
///
async function chosenLayers(page) {
    return page.locator('.layerRowChosen .layerName').evaluateAll(nodes =>
        nodes.map(node => ((node.textContent.match(/\d+\/\d+/) || [null])[0])).filter(Boolean));
}

///Takes the nth layer in the sidebar as the one to draw on, and hands back the pair it is.
async function useDrawingLayer(page, at) {
    await page.locator('.layerRow').nth(at).locator('.layerName').click();

    return drawingLayer(page);
}

///The layers the sidebar lists, as `65/20` pairs, in the order they are shown.
async function layersListed(page) {
    return page.locator('.layerRow .layerName').evaluateAll(nodes =>
        nodes.map(node => ((node.textContent.match(/\d+\/\d+/) || [null])[0])).filter(Boolean));
}

///
///Puts a toolbar toggle into the state a test needs, whatever state it is in now.
///
///`click()` asserts a default as much as it asks for anything: "click #gridToggle to turn the grid on" is
///only true while the grid starts off. It now starts on, and every one of those calls quietly became "turn
///the grid off" - the specs still ran, and a handful of them were testing the opposite of what they say.
///
///Asking for the state instead leaves the default free to move again without that happening. Tests that are
///*about* the switch still click it, since flipping it is the thing they are checking.
///
///
///The grid's three switches live behind its icon, so they are reached the way a person reaches them.
///
///Pointed at rather than pressed: pressing the icon toggles the menu, so a helper that pressed it would
///close the menu as often as it opened one.
///
async function openGridMenu(page) {
    if (await page.locator('#gridPicker').count() > 0)
        return;

    await page.locator('#gridMenu').hover();

    await expect(page.locator('#gridPicker')).toBeVisible();
}

async function setToggle(page, id, on) {
    await openGridMenu(page);

    const button = page.locator(id);
    const isOn = () => button.evaluate(node => node.classList.contains('shapePickOn'));

    if (await isOn() !== on) {
        await button.click();

        //Choosing from the menu leaves it up - these are switches rather than one answer out of several -
        //but the bar re-renders around them, so the line is found again before it is read.
        await openGridMenu(page);
    }

    await expect.poll(async () => {
        await openGridMenu(page);

        return isOn();
    }).toBe(on);
}

///
///The pitch in force, in database units, read off what the app says rather than assumed.
///
///It was a constant in several specs - a micron, a thousand units - and that was only ever the default. The
///pitch is worked out from the file now: its own grid raised until it is worth drawing, so Mosfet opens on
///fifty rather than a thousand. Every spec that multiplied by a thousand was pinning a default rather than
///the behavior it named.
///
///Taken from the readout because that is the app answering rather than the test deciding: it prints
///"0.05 um is 50 database units", and this reads the second number out of it.
///
async function pitchInUnits(page) {
    const said = await page.locator('#gridUnit').getAttribute('title');
    const found = said.match(/is ([\d,]+) database units/);

    if (found == null)
        throw new Error('the pitch readout did not name a number of database units: ' + said);

    return Number(found[1].replace(/,/g, ''));
}

///Sets the pitch, for a spec that is about one in particular rather than about whatever the file chose.
async function usePitch(page, microns) {
    await page.locator('#gridPitch').fill(String(microns));
    await page.locator('#gridPitch').blur();

    await expect.poll(async () => pitchInUnits(page)).toBe(Math.round(microns * 1000));
}

///Whether the grid is drawn, and whether the pointer lands on it. Both default to on.
async function showGrid(page, on = true) {
    await setToggle(page, '#gridToggle', on);
}

async function snapToGrid(page, on = true) {
    await setToggle(page, '#snapToggle', on);
}

///
///Chooses which shape the Draw tool draws.
///
///Through the pencil, because the choices are a menu hanging off it rather than a row in the bar: it opens
///on choosing Draw and on being pointed at, and choosing from it closes it again. So a second choice needs
///the pencil first, and pointing at it when it is already open costs nothing.
///
///One helper rather than a hover written out at each of the three dozen places a spec picks a shape - the
///menu's manners are the menu's business, and a test about paths should not have to know them.
///
async function chooseShape(page, which) {
    await page.locator('#drawTool').hover();

    await page.locator(which).click();
}

///
///Opens a shape's own settings, which hang off its row in the picker rather than sitting in the toolbar.
///
///Path has a width and an end style, Ellipse a side count; the rest have none. They were controls in the bar
///that appeared as the shape in hand changed, so a spec could reach them the moment the shape was chosen -
///now they are a panel held open by hovering the row, and reaching one means opening the picker and pointing
///at the shape it belongs to.
///
///Same reason chooseShape exists: the menu's manners are the menu's business, and a test about paths should
///not have to know them. Choosing the shape does *not* open this - that closes the picker - so a spec that
///wants both does them in that order, or calls setShapeSetting below, which handles it.
///
async function openShapeSettings(page, which) {
    await page.locator('#drawTool').hover();

    await page.locator(which).hover();

    //The panel is display:none until the row is hovered, so this is the wait that says it is reachable.
    await expect(page.locator(`${which} ~ .shapePickPanel`)).toBeVisible();
}

///
///Types a value into one of a shape's settings and lets it commit, the way leaving the field would.
///
///`which` is the shape's row, `field` the control inside its panel. Both are needed: the panel only exists
///while its own row is hovered, so the field cannot be found without saying which row to open first.
///
async function setShapeSetting(page, which, field, value) {
    await openShapeSettings(page, which);

    await page.locator(field).fill(String(value));
    await page.locator(field).blur();

    await closeShapeSettings(page);
}

///
///Puts the picker away by pointing somewhere else, and waits until it has actually gone.
///
///It closes when the pointer leaves the tools column, and the column hangs over the top of the view. A spec
///that went straight from typing a setting to a drag would press while the menu was still on screen, and the
///press would land on the menu instead of on the canvas. Moving the pointer *and waiting* is the difference -
///the move only starts the closing, which is a Blazor render away.
///
///The panel is held open by :focus-within as well as by :hover, so the field has to have been left first.
///
async function closeShapeSettings(page) {
    const view = await page.locator('#gdsSVG').boundingBox();

    await page.mouse.move(view.x + view.width - 20, view.y + view.height - 20);

    await expect(page.locator('#shapePicker')).toHaveCount(0);
}

///
///Answers the dialog an upload puts up when a file is already open, choosing to open the new file on its
///own - which is what an upload did before the dialog existed.
///
///Every spec here was written against that behavior and only one of them is about the dialog, so this keeps
///the rest saying what they always said. The app opens an example of its own at startup, so there is always
///a file open and the question is always asked: an upload that is not answered simply never happens, and the
///spec fails much later on a shape count that never moved.
///
///Quiet when there is no dialog - the question is only asked in the 2D view, and a spec in the text or 3D
///view uploads without one.
///
async function openedOnItsOwn(page, timeout = 6000) {
    const choice = page.locator('#importAsFile');

    //
    //**Waited for, not looked for once.**
    //
    //The dialog is not up the instant the file is set: the app reads the whole file and parses it before it
    //can say how many cells are in it, which on the twenty-thousand-shape layout is seconds. Asking whether
    //it is there yet and carrying on when it is not leaves the upload unanswered forever - and the spec then
    //fails much later reporting the default example's shape count, which is what this did on its first run.
    //
    try {
        await choice.waitFor({ state: 'visible', timeout });
    }
    catch {
        //
        //No dialog, which is now the common case rather than the odd one.
        //
        //The app skips the question when what is open is the example it chose for itself and nothing has
        //touched it, so a spec that lands on the app's own default and uploads is never asked. The wait is
        //bounded rather than long for that reason: every one of those sites would otherwise pay the whole
        //timeout for a dialog that is never coming. Uploading something big needs longer - the app reads and
        //parses the file before it can say what is in it - and those sites go through uploadOpenedOnItsOwn,
        //which stops waiting as soon as the file is opened without one.
        //
        return;
    }

    await choice.click();

    await expect(page.locator('#importDialog')).toHaveCount(0);
}

///
///Uploads a file and opens it on its own, as openedOnItsOwn does - without sitting out the wait for a question that
///is not coming.
///
///**Whichever comes first: the question, or the page taking the file's name.** The app asks only when the file on
///screen is somebody's - see mayOfferImport - and reads and parses the whole upload before it can, which on the
///twenty-thousand-shape layout is seconds, so the wait for it has to be long. Where it does not ask, it opens the
///file straight away, and the page has its name a render later. Sitting out the whole wait there was a minute of
///nothing: large-layout.spec reported every open of its layout as taking sixty seconds, and all of it was this.
///
///The open file's name is read before the upload rather than after it: without a question the page can have the
///new one by the time this looks. And a file uploaded over one of the same name says nothing by its name, so only
///the question is waited for then.
///
async function uploadOpenedOnItsOwn(page, files, timeout = 60000) {
    const before = await openFile(page);
    const name = uploadedName(files);

    await uploadFile(page, files);

    if (name === null || name === before) {
        await openedOnItsOwn(page, timeout);

        return;
    }

    let answer;

    try {
        answer = await page.waitForFunction((name) => {
            const choice = document.getElementById('importAsFile');

            if (choice !== null && choice.getClientRects().length > 0)
                return 'asked';

            const shell = document.getElementById('mainAppContainer');

            if (shell !== null && shell.getAttribute('data-file') === name)
                return 'opened';

            return null;
        }, name, { timeout, polling: 100 });
    }
    catch {
        throw new Error(`Uploading ${name} neither asked whether to bring it into ${before} nor opened it, in ${timeout} ms.`);
    }

    if (await answer.jsonValue() === 'asked')
        await openedOnItsOwn(page, timeout);
}

///The name of the file an upload carries - the first, for several - or null for one this cannot tell.
function uploadedName(files) {
    let first = files;

    if (Array.isArray(files))
        first = files[0];

    if (typeof first === 'string')
        return require('path').basename(first);

    if (first != null && typeof first.name === 'string')
        return first.name;

    return null;
}

///
///Steps out of whatever cell is open, back to the whole layout.
///
///**A file now opens inside its own top cell**, so "no cell is open" is a state a spec has to ask for
///rather than one it starts in. Reached the way a person reaches it: the first crumb in the context bar.
///
async function leaveCell(page) {
    await page.getByTitle('Stop editing and look at the whole layout again').click();

    await expect(page.locator('#contextBar')).toHaveCount(0);
}

///
///Opens the PDK manager from Settings, on the PDK in use, and closes it again.
///
///**A layermap or a deck reaches the open file through it** since 2026-10-19, when the layer and rules panels' own
///Import, Clear and Example went there - so the specs that put one in place come through here, by the helpers below.
///
async function openPdkManager(page) {
    await page.locator('#settingsMenu').click();
    await page.locator('#pdkManagerItem').click();

    await expect(page.locator('#pdkManagerWindow')).toBeVisible();
}

async function closePdkManager(page) {
    await page.locator('#pdkManagerWindow .closeButton').click();

    await expect(page.locator('#pdkManagerWindow')).toBeHidden();
}

///
///Puts a PDK with no layers in use, so the open file is drawn in none: bare numbers in the palette's colors, the
///automatic stack, and no rules. What Clear on the layer panel gave before it went (2026-10-19) - for every file opened
///afterwards as well as this one, since a file is drawn in a PDK or it is not.
///
///**The shipped mapping carries sky130's process stack**, so an example does not open on the evenly spaced synthetic
///heights. That is the point of it, and it is the wrong ground to stand on for a spec about the even spacing itself -
///those ask for a file with no process table, which is what this makes.
///
async function bareNumbers(page) {
    await openPdkManager(page);

    await page.locator('#pdkNew').click();
    await page.locator('#pdkUse').click();

    await expect(page.locator('#pdkUse')).toHaveCount(0);

    await closePdkManager(page);

    //Every row its pair alone, which is a file nothing names.
    await expect.poll(
        async () => (await page.locator('.layerRow .layerName').allTextContents()).every(text => /^\s*\d+\/\d+\s*$/.test(text)),
        { timeout: 20000 }).toBe(true);
}

///
///Imports a layermap into the PDK in use through the PDK manager's layers Import, and gives back what the manager said
///about how it read. What the layermap says about a pair replaces what the PDK said and the rest stays - and the open
///file, if it is drawn in the PDK, is named again by it. `files` is whatever setInputFiles takes.
///
///**Waited for on the manager's line, not on the file**: what the file shows is the caller's to poll for, since the
///layers are laid over it again after the line is said. A layermap that names nothing says so in a dialog instead,
///and is not one for this.
///
async function importLayermap(page, files) {
    await openPdkManager(page);
    await page.locator('#pdkLayersTab').click();

    await page.locator('#pdkLayersImport').setInputFiles(files);

    await expect(page.locator('#pdkLayersSays')).toContainText('Imported', { timeout: 60000 });

    const said = (await page.locator('#pdkLayersSays').textContent()).trim();

    await closePdkManager(page);

    return said;
}

///
///Imports a deck into the PDK in use through the PDK manager's rules Import, in place of its rules, and waits for the
///manager to say it applied - the way a deck reaches the rules panel since its Import went there. Whether the panel
///shows it is the caller's to wait for: it does only on a file drawn in the PDK.
///
async function importRules(page, contents, name = 'probe.drc') {
    await openPdkManager(page);
    await page.locator('#pdkRulesTab').click();

    await page.locator('#pdkRulesImport').setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from(contents, 'utf8') });

    await expect(page.locator('#pdkRulesSays')).toContainText('Applied', { timeout: 60000 });

    await closePdkManager(page);
}

///
///The session as it is stored, as text: IndexedDB's copy, or the one the exit handler leaves in localStorage.
///
async function storedSession(page, exitCopy = false) {
    return page.evaluate(async (exitCopy) => {
        let value;

        if (exitCopy)
            value = window.localStorage.getItem('gdsviewer.session');
        else
            value = await window.gdsStorage.get('gdsviewer.session');

        if (value === null)
            return '';

        //
        //**Decoded the way AppStorage encoded it.** A session under the threshold is stored as plain JSON
        //behind a marker and one over it is deflated and base64'd, and which side of that line a session
        //falls on depends on how many settings the app has - so a helper that read the raw string would
        //stop being able to see anything the day a field was added, having tested nothing in between.
        //
        if (value.startsWith('z')) {
            const bytes = Uint8Array.from(atob(value.slice(1)), letter => letter.charCodeAt(0));

            const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('deflate-raw'));

            return new Response(stream).text();
        }

        return value.slice(1);
    }, exitCopy);
}

module.exports = {
    storedSession,
    viewSettled,
    leaveCell,
    openPdkManager,
    closePdkManager,
    bareNumbers,
    importLayermap,
    importRules,
    openGridMenu,
    chooseShape,
    openShapeSettings,
    setShapeSetting,
    closeShapeSettings,
    pitchInUnits,
    usePitch,
    setToggle,
    showGrid,
    snapToGrid,
    MOSFET,
    CLEAR_OF_PANEL,
    otherShapeClearOfPanel,
    uploadFile,
    chosenShapeClearOfPanel,
    dismissSelection,
    chosenLayer,
    chooseLayer,
    layersOffered,
    drawingLayer,
    chosenLayers,
    useDrawingLayer,
    layersListed,
    MOSFET_POLYGONS,
    LAYER_RUNG,
    MOSFET_MESHES,
    MOSFET_OUTLINES,
    MOSFET_LABELS,
    MOSFET_LAYERS,
    MOSFET_LAYER_PAIRS,
    SKY130_CELL,
    gotoApp,
    gotoExample,
    openExamples,
    closeExamples,
    exampleRow,
    filterExamples,
    openFile,
    openedOnItsOwn,
    uploadOpenedOnItsOwn,
    expectLoaded,
    expectEditorLoaded,
    layerNumbers,
    layerPairs,
    layerCheckbox,
    hideLayer,
    showLayer,
    setLayerLocked,
    layerLabel,
    openLayerSettings,
    labelsToggle,
    pickColor,
    layerNameBox,
    openViewSettings,
    selectBackground,
    selectView,
    saveImage,
    selectExample,
    svgCounts,
    fillsDrawn,
    shapesDrawn,
    shapesMarked,
    shapesAndLabels,
    previewPicture,
    shapeCount,
    shapePoints,
    shapeBox,
    shapeClearOfThePanel,
    allPoints,
    allFills,
    elementPoints,
    elementCount,
    elementFill,
    threeCounts,
    stackHeights,
    editorText,
    saveEditorText,
    editorSaveFailed,
    captureScene,
    cameraPosition,
    stackOnScreen,
    onScreenSaid,
    downloadBytes,
    onOtherLayers,
    MOSFET_ELSEWHERE
};
