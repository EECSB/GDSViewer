//The app boots and its shell is there: the WASM runtime started, the example manifest was fetched, and
//the router resolved the viewer at "/". Everything else assumes all of that, so it is worth failing here
//rather than somewhere confusing.
const { test, expect } = require('@playwright/test');
const { gotoApp, gotoExample, openExamples, closeExamples, exampleRow, filterExamples, openFile, openLayerSettings, selectView, MOSFET, MOSFET_POLYGONS, previewShapeCount } = require('./helpers');

test('the WASM app boots and renders its shell', async ({ page }) => {
    await gotoApp(page);

    await expect(page.locator('#gdsSVG')).toBeVisible();
    await expect(page).toHaveTitle(/GDS/i);
});

///
///The manifest reaches the picker. Counted through the filter rather than off the rendered rows, since
///the list is virtualized and only the visible handful are in the page at any moment.
///
test('the example picker is filled from the generated manifest', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    //Written by the build's GenerateExampleGdsManifest target, so an empty list means that did not run.
    await expect(exampleRow(page, `${MOSFET}.gds`)).toHaveCount(1);

    //The placeholder counts what the picker has: everything, plus the hand-made one.
    const offered = await page.locator('.examplePickerFilter').getAttribute('placeholder');

    expect(Number(offered.match(/\d+/)[0])).toBeGreaterThan(800);

    //And a name deep in the list is reachable by narrowing to it.
    await filterExamples(page, 'nand2_1');

    await expect(exampleRow(page, 'sky130_fd_sc_hd__nand2_1.gds')).toHaveCount(1);
});

///
///The groups are named, and a heading only appears when something is left under it.
///
///They are rows in the same virtualized sequence as the files rather than markup wrapped around groups,
///which is the only way a heading can land in the right place in a list that renders a window at a time.
///
test('the example list names its groups', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    const headings = page.locator('.examplePickerHeading');

    await expect(headings.nth(0)).toHaveText('Test GDS Files');

    //The hand-made one sits under the first heading, ahead of the library.
    await expect(exampleRow(page, `${MOSFET}.gds`)).toBeVisible();

    //Filtering to something only the library has leaves that group's heading and drops the other.
    await filterExamples(page, 'nand2_1');

    await expect(headings).toHaveCount(1);
    await expect(headings.nth(0)).toHaveText('Sky130 GDS Examples');

    //And the other way round.
    await filterExamples(page, 'Mosfet');

    await expect(headings).toHaveCount(1);
    await expect(headings.nth(0)).toHaveText('Test GDS Files');
});

///The examples and the PDK links moved behind a button, so the bar above the view carries only what acts
///on the file. Worth pinning, because a popup that will not open looks exactly like one nobody opened.
test('the Examples popup opens and closes', async ({ page }) => {
    await gotoApp(page);

    await expect(page.locator('#examplePicker')).toHaveCount(0);

    await openExamples(page);
    await expect(page.locator('#examplePicker')).toBeVisible();

    await closeExamples(page);
});

///
///The two file lists open by being pointed at, and go by not being.
///
///They were dialogs in the middle of the window with a cross in the corner, which is a lot of ceremony
///for "what else could I open" - a question usually answered by looking. Pointing is the whole gesture
///now, which is why there is nothing left to press.
///
test.describe('the file lists hang off their buttons', () => {
    test('pointing at one opens it, under the button', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await expect(page.locator('#examplePicker')).toHaveCount(0);

        await page.locator('#examplesButton').hover();

        await expect(page.locator('#examplePicker')).toBeVisible();

        //
        //Directly below: the box touching the button, the white starting on the canvas.
        //
        //Those are two different edges and both matter. A list that closes when the pointer leaves cannot
        //have a strip of bar between it and its button, because crossing that strip is leaving - so the
        //*box* has to reach the button. But a panel whose white begins on the bar looks stuck to it, so
        //what is painted starts lower, and the distance between the two is a transparent border.
        //
        const gap = await page.evaluate(() => {
            const round = (value) => Math.round(value);
            const button = document.querySelector('#examplesButton').getBoundingClientRect();
            const list = document.querySelector('.popupDiv.popupUnder');
            const box = list.getBoundingClientRect();
            const drop = parseFloat(getComputedStyle(list).borderTopWidth);
            const view = document.querySelector('.viewWrapper').getBoundingClientRect();

            return {
                below: round(box.top - button.bottom),
                across: round(box.left - button.left),
                whiteBelowCanvasTop: round(box.top + drop - view.top)
            };
        });

        //No strip of bar to cross.
        expect(gap.below).toBeLessThanOrEqual(8);
        expect(gap.below).toBeGreaterThanOrEqual(-8);

        //And the white on the canvas rather than on the bar, with a little air above it.
        expect(gap.whiteBelowCanvasTop).toBeGreaterThan(0);
        expect(gap.whiteBelowCanvasTop).toBeLessThan(20);

        //
        //From this end of the bar rather than centered on the window, as it used to be - but it is allowed
        //to slide *left* of its button to take room the view has and it does not.
        //
        //That slide is the point of measurePopupRoom: the popup's left edge is pinned under the button, so
        //without it the room it can use is only whatever happens to be to the right of wherever the toolbar
        //put that button - which left a third of the view empty beside it on a wide window. It never slides
        //right, and never past the left of the view, both of which the next test measures.
        //
        expect(gap.across).toBeLessThanOrEqual(0);
    });

    ///
    ///And once open it takes most of the view, with the room going to the picture.
    ///
    ///It was a 220px list beside a 260px square in a popup about a third the size of the canvas: ten names
    ///showing out of nine hundred, and a thumbnail of the one being pointed at. The room was there.
    ///
    ///Proportions rather than pixel counts, since every number here moves with the window: the popup is
    ///most of the height of the view, the list keeps a list's width while the picture takes what is left,
    ///and the scrolling happens *in the list* rather than in the popup - a popup that scrolls would put the
    ///picture below the fold, which is the arrangement this replaced.
    ///
    ///Sideways it is asked to stay in the **window**, not in the view. It hangs off a button a quarter of
    ///the way along the bar, so on a narrow one there is not room between there and the edge of the canvas
    ///for a list and a picture worth looking at; below about 1280 it covers some of the layer sidebar,
    ///which is a thing a panel under the pointer may do. Running off the screen is not.
    ///
    test('it fills most of the view, and the picture gets the room', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#examplesButton').hover();

        await expect(page.locator('#examplePicker')).toBeVisible();

        const laid = await page.evaluate(() => {
            const box = (selector) => document.querySelector(selector).getBoundingClientRect();
            const view = box('.viewWrapper');
            const popup = box('.popupDiv.popupUnder');
            const list = document.querySelector('.examplePickerOptions');

            return {
                coversHeight: 100 * popup.height / view.height,
                pastWindow: popup.right - window.innerWidth,
                pastBottom: popup.bottom - view.bottom,
                listWidth: box('.examplePickerList').width,
                listHeight: list.getBoundingClientRect().height,
                pictureWidth: box('.examplePreviewFrame').width,
                listScrolls: list.scrollHeight > list.clientHeight,
                popupScrolls: (() => {
                    const it = document.querySelector('.popupDiv.popupUnder');

                    return it.scrollHeight > it.clientHeight;
                })()
            };
        });

        //Most of the height of the canvas, and stopping short of the bottom of it.
        expect(laid.coversHeight).toBeGreaterThan(70);
        expect(laid.pastBottom).toBeLessThanOrEqual(0);

        //On the screen at any width, which is the one thing that is not allowed to give.
        expect(laid.pastWindow).toBeLessThanOrEqual(0);

        //A list stays a list's width; everything the popup gained sideways went to the picture beside it.
        expect(laid.listWidth).toBeLessThanOrEqual(340);
        expect(laid.pictureWidth).toBeGreaterThan(laid.listWidth);

        //
        //Taller than the 220 it was fixed at, and it is the list that scrolls rather than the popup.
        //
        //Not much taller here, and that is the point of asking it this way: the height follows the window,
        //and the one these run at is 720, where the whole popup has about 490 to live in. On a 900-tall
        //window the same rule gives the list 450.
        //
        expect(laid.listHeight).toBeGreaterThan(225);
        expect(laid.listScrolls).toBe(true);
        expect(laid.popupScrolls).toBe(false);
    });

    ///
    ///Two ways out, and moving away is still one of them.
    ///
    ///The cross was taken off these when they learned to close on mouse-out, on the grounds that a popup
    ///which needs no pressing needs no button. That is true of the pointer and false of everything else:
    ///a touch has no mouse-out to give, and neither does the keyboard.
    ///
    ///So it is back, and this asks for both - because the failure worth catching is the cross arriving and
    ///the mouse-out quietly going, which would leave a popup that only closes if you find the button.
    ///
    test('moving away closes it, and so does the cross', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#examplesButton').hover();

        await expect(page.locator('#examplePicker')).toBeVisible();
        await expect(page.locator('#closeExamples')).toBeVisible();

        await page.mouse.move(4, 4);

        await expect(page.locator('#examplePicker')).toHaveCount(0);

        //And again, out by the button this time.
        await page.locator('#examplesButton').hover();

        await expect(page.locator('#examplePicker')).toBeVisible();

        await page.locator('#closeExamples').click();

        await expect(page.locator('#examplePicker')).toHaveCount(0);
    });

    ///
    ///And the pointer can get from the button down onto the list without it shutting on the way.
    ///
    ///Which is the whole reason both lists are rendered *inside* the column their buttons are in: an
    ///absolutely positioned child is still a descendant, so the leave that closes them never fires while
    ///the pointer is over either one.
    ///
    ///**With steps, which is the whole test.** A mouse.move with no steps is one event at the destination
    ///and the browser never samples what is in between - so this passed for as long as the two were flush
    ///and would have gone on passing if a gap opened up, because it jumped straight over it. Twenty steps
    ///puts a real move in the twelve pixels between the button and the white.
    ///
    test('the pointer can travel from the button onto the list', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        const button = await page.locator('#examplesButton').boundingBox();

        await page.locator('#examplesButton').hover();

        const list = await page.locator('.popupDiv.popupUnder').boundingBox();

        await page.mouse.move(button.x + (button.width / 2), list.y + 40, { steps: 20 });
        await page.mouse.move(list.x + 40, list.y + list.height / 2, { steps: 10 });

        await expect(page.locator('#examplePicker')).toBeVisible();

        //And the filter is usable once you are there, which is what the list is for.
        await page.locator('.examplePickerFilter').fill('nand2_1');

        await expect(exampleRow(page, 'sky130_fd_sc_hd__nand2_1.gds')).toHaveCount(1);
    });

    ///Crossing between the two buttons swaps the list rather than closing it, since they share a host.
    test('crossing from one button to the other swaps the list', async ({ page }) => {
        await gotoExample(page, MOSFET, '2d');

        await page.locator('#historyButton').hover();

        await expect(page.locator('#historyPicker')).toBeVisible();

        await page.locator('#examplesButton').hover();

        await expect(page.locator('#examplePicker')).toBeVisible();
        await expect(page.locator('#historyPicker')).toHaveCount(0);
    });
});

///
///A picture of the chosen cell, beside the list that chose it.
///
///The popup stays open while cells are looked through, which is what makes this worth having: it is the
///difference between reading a list of names and seeing what they are. Framed to the cell's own bounds,
///so a small cell fills the thumbnail rather than sitting in the corner of a fixed window.
///
test('the preview is framed to the cell it is of, and carries no labels', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    const preview = page.locator('svg.examplePreview');

    //
    //Nothing until a row is pointed at.
    //
    //This used to be the open file's own thumbnail, and the popup opened showing a shrunken copy of the
    //drawing behind it - which reads as though a row is selected when none is. The frame says what to do
    //instead, in the cell tree's words for the same empty state.
    //
    await expect(preview).toHaveCount(0);
    await expect(page.locator('.examplePreviewEmpty')).toHaveText('Point at a file');

    await filterExamples(page, `${MOSFET}.gds`);
    await exampleRow(page, `${MOSFET}.gds`).hover();

    await expect(preview).toBeVisible();
    await expect.poll(async () => previewShapeCount(page), { timeout: 15000 }).toBe(MOSFET_POLYGONS);

    const framedOnMosfet = await preview.getAttribute('viewBox');

    //
    //Off the list before pointing at the next row, which is not fussiness.
    //
    //Narrowing the list is a fill() and moves no pointer, so the row that arrives under a stationary
    //pointer gets no mouseenter and the frame keeps showing the cell before it. Leaving and coming back is
    //what a person does anyway, and it makes the second hover a hover rather than a hope.
    //
    await page.locator('.examplePickerFilter').hover();

    await expect(preview).toHaveCount(0);

    //A different cell, with different geometry and a different size.
    await filterExamples(page, 'a211oi_1');
    await exampleRow(page, 'sky130_fd_sc_hd__a211oi_1.gds').hover();

    await expect.poll(async () => previewShapeCount(page), { timeout: 60000 }).toBe(74);

    //Reframed to the new cell rather than kept on the old one's bounds.
    expect(await preview.getAttribute('viewBox')).not.toBe(framedOnMosfet);

    //No labels: at a couple of hundred pixels they are a smudge, and this answers "what is this cell".
    await expect(preview.locator('text')).toHaveCount(0);
});

///
///Pointing at a row shows that cell, without opening it.
///
///The whole reason the picker is a list of divs: an <option> is drawn by the operating system and reports
///no hover, so a native one could only ever preview what had already been chosen. Leaving the list empties
///the frame, rather than stranding whichever row the pointer last crossed.
///
test('pointing at an example previews it without opening it', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    const preview = page.locator('svg.examplePreview');

    //Nothing is pointed at yet.
    await expect(preview).toHaveCount(0);

    await filterExamples(page, 'a211oi_1');
    await exampleRow(page, 'sky130_fd_sc_hd__a211oi_1.gds').hover();

    await expect.poll(async () => previewShapeCount(page), { timeout: 60000 }).toBe(74);

    //Looked at, not opened: the file behind the popup is untouched.
    expect(await openFile(page)).toBe(`${MOSFET}.gds`);

    //
    //And the pointer leaving empties it again.
    //
    //It used to put the open file's picture up instead. Pinned the other way now, because the picture the
    //popup was showing most of the time was of the file you already had - see clearPreview in Viewer.razor.
    //
    await page.locator('.examplePickerFilter').hover();

    await expect(preview).toHaveCount(0);
    await expect(page.locator('.examplePreviewEmpty')).toHaveText('Point at a file');
});

///
///
///**It closes once a cell is chosen**, and this test used to pin the opposite.
///
///It stayed up so several cells could be looked at without reopening it, which was right while clicking a
///row was how you looked at one. Pointing at a row previews it now, without opening anything - that is the
///looking - and a row that is chosen asks first, so clicking through the list is a dialog per cell rather
///than a glance. What was left after a choice was a list nobody was reading, over the file it had opened.
///
///The warning the old version of this carried still stands, pointing the other way: whichever of the two it
///is, it is a decision rather than an accident, so it is pinned. See closeOnChoice in Viewer.razor.
///
test('the Examples popup closes after picking one', async ({ page }) => {
    await gotoApp(page);
    await openExamples(page);

    await filterExamples(page, `${MOSFET}.gds`);

    //Choosing a row closes what is open, and the app asks before it does.
    page.once('dialog', (dialog) => dialog.accept());

    await exampleRow(page, `${MOSFET}.gds`).click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    await expect(page.locator('#examplePicker')).toHaveCount(0);

    //And it opens again for the next one, rather than being spent.
    await openExamples(page);
    await filterExamples(page, 'nand2_1');

    page.once('dialog', (dialog) => dialog.accept());

    await exampleRow(page, 'sky130_fd_sc_hd__nand2_1.gds').click();

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe('sky130_fd_sc_hd__nand2_1.gds');
    await expect(page.locator('#examplePicker')).toHaveCount(0);
});

test('the router resolves the viewer at the root, and says so for anything else', async ({ page }) => {
    await gotoApp(page);
    await expect(page.locator('#gdsSVG')).toBeVisible();

    await page.goto('/no-such-page');

    await expect(page.getByRole('alert')).toContainText('nothing at this address');
    await expect(page.locator('#gdsSVG')).toHaveCount(0);
});

///
///Every button in the bar above the view is the same size.
///
///They arrive from different places - the model download from the 3D view, the tools from the 2D one - and each used to
///carry the full button style, which made them the loudest thing in a row of dropdowns and icons. They
///share a class now, and this is what says the next one added did not quietly miss it.
///
///Download Image was one of these and is not any more: it moved into the corner of the view as an icon,
///because it takes what is on screen rather than changing it. See the Layers sidebar and viewAction.
///
///Examples is the exception, and is the only one: it is the last button in the bar that is a word rather
///than a glyph, so it is the only place a label has to fit - and at the bar's own type size it was the
///widest control in the row by half again. The *box* is still one of the set, which is what this checks
///about it; only the type inside it is smaller.
///
test('the buttons in the view bar are all one size', async ({ page }) => {
    //
    //By its word or by the name a reader hears, because some of these are pictures now.
    //
    //The size is still the question - a bar of buttons that are not one height reads as a bar somebody
    //stopped tidying - but "the button that says Select" stopped finding anything the moment Select became
    //a pointer with its word in an aria-label.
    //
    const measure = (name) => page.evaluate((text) => {
        const button = [...document.querySelectorAll('button')]
            .find(b => b.textContent.trim() === text || b.getAttribute('aria-label') === text);

        if (button == null)
            return null;

        const style = getComputedStyle(button);

        return { fontSize: style.fontSize, padding: style.padding, height: Math.round(button.getBoundingClientRect().height) };
    }, name);

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#container canvas')).toBeVisible();

    //
    //The 3D view's own contribution, as the yardstick.
    //
    //Admire used to be it, and Admire is not in the bar any more - it is a control about the view rather
    //than about the file, and it sits on the canvas now with the other two of those. The download this
    //view adds is the one left that arrives from somewhere else, which is what made Admire worth measuring.
    //
    const yardstick = await measure('Download 3D model');

    expect(yardstick).not.toBeNull();

    //History is an icon like the rest, so it matches outright.
    expect(await measure('History')).toEqual(yardstick);

    //
    //Examples is the same box with smaller type in it.
    //
    //The height is the part that matters and is what a bar of mismatched buttons gets wrong; the type is
    //a deliberate size down, because it is the one label left in the bar and at the others' size it was
    //the widest thing in the row.
    //
    const examples = await measure('Examples');

    expect(examples.height).toBe(yardstick.height);
    expect(examples.padding).toBe(yardstick.padding);
    expect(parseFloat(examples.fontSize)).toBeLessThan(parseFloat(yardstick.fontSize));

    await selectView(page, 'View2DSvg');
    await expect(page.locator('#gdsSVG')).toBeVisible();

    //The 2D view's own contribution to the bar, which is a tool rather than a worded button now.
    expect(await measure('Select')).toEqual(yardstick);
});

///
///Saving the picture sits in the picture, not in the bar above it.
///
///It takes what is on screen rather than changing it, so it belongs with the view. In the corner nothing
///else uses: the readout of where the pointer is holds the other bottom one.
///
test('the download button is an icon in the corner of the view', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect(page.locator('#gdsSVG')).toBeVisible();

    const button = page.locator('#downloadImage');

    await expect(button).toBeVisible();

    //An icon, with the words in the tooltip rather than on it.
    await expect(button).toHaveText('');
    await expect(button).toHaveAttribute('title', /Save this view as an SVG/);
    await expect(button.locator('svg')).toHaveCount(1);

    const view = await page.locator('#svgWrapper').boundingBox();
    const at = await button.boundingBox();

    //Bottom right of the view, and clear of the readout in the other corner.
    expect(view.x + view.width - (at.x + at.width)).toBeLessThan(30);
    expect(view.y + view.height - (at.y + at.height)).toBeLessThan(30);
    expect(at.x).toBeGreaterThan(view.x + (view.width / 2));
});

///
///Every close button is square and carries the same mark.
///
///There are four, in four different files - About, the QR popup, Examples, and the color picker - so
///"they all match" is a claim nothing but a browser can settle. Square is asserted from the rendered box
///rather than from the stylesheet, because symmetric padding around a glyph still gives an oblong: the
///width and the height have to be set together, and this is what says they still are.
///
test('the close buttons are square and all carry the same mark', async ({ page }) => {
    await gotoExample(page, MOSFET, '2d');

    //
    //A dialog that is still a dialog, since the file lists stopped being ones.
    //
    //Examples, History and the library hang off their buttons and go when the pointer does, so they have
    //no cross to check - what is left is the layer settings, which is a panel somebody works in and puts
    //away deliberately.
    //
    await openLayerSettings(page);

    const closes = await page.evaluate(() =>
        [...document.querySelectorAll('.closeButton')].map(button => {
            const box = button.getBoundingClientRect();

            return {
                mark: button.textContent.trim(),
                width: Math.round(box.width),
                height: Math.round(box.height)
            };
        }));

    expect(closes.length).toBeGreaterThan(0);

    for (const close of closes) {
        //The multiplication sign, not the letter.
        expect(close.mark).toBe('×');

        expect(close.width).toBe(close.height);
    }
});

///
///Opening the app with nothing asked for lands on the bundled example rather than an empty canvas.
///
///This used to assert the opposite - that nothing was open until something was chosen. The empty state
///was not telling anyone anything: a picker behind a button and a blank page, with no indication that the
///next move was to go and find a file.
///
test('the bundled example is open when nothing else was asked for', async ({ page }) => {
    await gotoApp(page);

    await expect.poll(async () => openFile(page), { timeout: 60000 }).toBe(`${MOSFET}.gds`);

    //Its nine layer/datatype pairs are listed, which is the file having been parsed rather than named.
    await expect(page.locator('.layerRow')).toHaveCount(9);
});

///
///Every control on the 2D view's own bar is one height.
///
///Not only the buttons. The pitch box and its unit sit in the same row as Show, Snap and Shapes, and the
///row is a flex container - so a single control taller than the rest stretches everything beside it, and
///the whole column stands above the tools next to it. That is how it happened: nothing set a size on the
///pitch box, so it took the browser's own for a number input, 16px of type on a 24px line against the
///0.9em on 1.5 the bar uses everywhere else. Thirty pixels against twenty-five point six.
///
test('the 2D bar controls are all one height', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect(page.locator('#gdsSVG')).toBeVisible();

    const heights = await page.evaluate(() => {
        const of = (selector) => {
            const one = document.querySelector(selector);

            if (one === null)
                return null;

            return Math.round(one.getBoundingClientRect().height * 10) / 10;
        };

        return {
            pan: of('#panTool'),
            select: of('#selectTool'),
            draw: of('#drawTool'),
            gridMenu: of('#gridMenu'),
            gridPitch: of('#gridPitch'),
            gridUnit: of('#gridUnit')
        };
    });

    const measured = Object.values(heights).filter(one => one !== null);

    //Enough of them to be worth calling an invariant, and all the same.
    expect(measured.length).toBeGreaterThan(5);
    expect(new Set(measured).size, JSON.stringify(heights)).toBe(1);
});

///
///The view picker is an icon that opens a menu, and it says which view you are in.
///
///It was a native select with "View" written over it - the one control in the bar drawn by the operating
///system rather than by the page, and the only one that could not be made to sit in a row of square
///buttons. The button now carries the view's own shorthand, which is a heading and a value in the room a
///value took.
///
test.describe('the view switch', () => {
    ///
    ///Three boxes, one of them lit - the same control the tools are, doing the same job.
    ///
    ///It was a native select, then a button that opened a menu of three. There are exactly three, they
    ///never change, and all three fit in the room one word takes, so the press that opened the menu was
    ///charging a click for something already affordable.
    ///
    test('it is three boxes with the current view lit', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        const boxes = page.locator('#viewPick button');

        await expect(boxes).toHaveCount(3);

        //
        //A picture each, and the words kept where a reader can still get at them.
        //
        //They were the tokens Txt, 2D and 3D, which is what a row of words beside a row of glyphs looks
        //like - two kinds of control where there is one. What a screen reader hears did not change.
        //
        await expect(page.locator('#viewPick button svg')).toHaveCount(3);

        expect(await boxes.evaluateAll(all => all.map(one => one.getAttribute('aria-label'))))
            .toEqual(['Text Editor', '2D Editor', '3D Viewer']);

        //One of them pressed, and it is the view that is on.
        const pressed = await boxes.evaluateAll(all =>
            all.filter(one => one.getAttribute('aria-pressed') === 'true').map(one => one.getAttribute('data-view')));

        expect(pressed).toEqual(['View2DSvg']);

        //And no word over it, nor a select anywhere in the bar.
        const labels = await page.locator('.toolbarLabel').allTextContents();

        expect(labels.map(one => one.trim())).not.toContain('View');
        await expect(page.locator('#viewSelect')).toHaveCount(0);
    });

    ///<summary>The lit one is lit the way a chosen tool is, rather than by something invented for it.</summary>
    test('the lit box is marked the way a chosen tool is', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        const on = page.locator('#viewPick [data-view="View2DSvg"]');
        const off = page.locator('#viewPick [data-view="View3D"]');

        await expect(on).toHaveClass(/toolButtonOn/);
        await expect(off).not.toHaveClass(/toolButtonOn/);

        //Which is a real difference on screen, not only a class name.
        const opacities = await page.locator('#viewPick button').evaluateAll(all =>
            all.map(one => getComputedStyle(one).opacity));

        expect(new Set(opacities).size).toBeGreaterThan(1);
    });

    ///One press switches, with no menu in between.
    test('pressing a box switches to that view', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await page.locator('#viewPick [data-view="ViewText"]').click();

        await expect(page.locator('#gdsTextEditor')).toBeVisible({ timeout: 60000 });

        //And the lit box follows, which is the only thing it is there to say.
        await expect(page.locator('#viewPick [data-view="ViewText"]')).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('#viewPick [data-view="View2DSvg"]')).toHaveAttribute('aria-pressed', 'false');
    });

    ///<summary>And pressing the one already on is not a change, rather than a redraw of the same view.</summary>
    test('pressing the lit one changes nothing', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        await expect(page.locator('#gdsSVG')).toBeVisible();

        await page.locator('#viewPick [data-view="View2DSvg"]').click();

        await expect(page.locator('#gdsSVG')).toBeVisible();
        await expect(page.locator('#viewPick [data-view="View2DSvg"]')).toHaveAttribute('aria-pressed', 'true');
    });

    ///<summary>Each box says what it is for, since two or three characters cannot.</summary>
    test('each box names the view it opens', async ({ page }) => {
        await gotoExample(page, MOSFET, 'View2DSvg');

        const titles = await page.locator('#viewPick button').evaluateAll(all =>
            all.map(one => one.getAttribute('title')));

        expect(titles).toEqual(['Text Editor', '2D Editor', '3D Viewer']);
    });
});

///
///The Open dialog offers layouts rather than everything in the folder.
///
///A hint to the picker and nothing more - the reader decides what a file is by what is inside it, so a file
///that arrives another way is read exactly as before. DXF is on the list because the app opens those: a
///format that works and cannot be reached from the button that opens files is worse than an unfiltered
///dialog.
///
test('the file picker is filtered to the formats the app opens', async ({ page }) => {
    await gotoApp(page);

    const accept = await page.locator('#fileUpload').getAttribute('accept');

    expect(accept).toBeTruthy();

    const offered = accept.split(',').map(one => one.trim().toLowerCase());

    for (const extension of ['.gds', '.oas', '.dxf'])
        expect(offered).toContain(extension);

    //And it is a filter, not a free-for-all: something the app cannot open is not on it.
    expect(offered).not.toContain('.png');
    expect(offered).not.toContain('*');
});

///
///And the fence never costs a second row.
///
///The bar is a flex row that wraps, so asking it for more space than the window has does not clip anything
///- it quietly becomes two rows, which is worse than a narrow gap and is the exact thing four fences at
///51px each nearly bought: at 1280 the row went over by three pixels and the full-screen button dropped
///onto a line of its own. It used to be behind a media query for that reason; the fence is 1em at every
///width now, which is less than the narrow setting used to be, so the widths below are what says so.
///
///Measured in the bar's widest state - a drawing tool on, so the merge switch is in the row too.
///
test.describe('the toolbar fits on one line', () => {
    for (const width of [1280, 1366, 1399, 1400, 1440, 1920])
    {
        test(`at ${width}`, async ({ page }) => {
            await page.setViewportSize({ width, height: 900 });

            await gotoExample(page, MOSFET, 'View2DSvg');

            await expect(page.locator('#toolGroup button').first()).toBeVisible();

            //Select brings the merge switch in, which is the widest the bar ever gets.
            await page.locator('#selectTool').click();

            await expect(page.locator('#joinToggle')).toBeVisible();

            const rows = await page.evaluate(() => {
                const tops = new Set();

                for (const column of document.querySelector('.viewToolbar').children)
                    tops.add(Math.round(column.getBoundingClientRect().top));

                return tops.size;
            });

            expect(rows).toBe(1);
        });
    }
});

///
///About opens in the middle of the window.
///
///It carried an inline left: 10% on a class that had already pulled it back half its own width, so a tenth
///of the way in came out as a tenth in *minus half a popup* and it sat off to one side. The QR code had the
///same two lines on it and the same problem; both are gone.
///
test('the About popup is centered on the window', async ({ page }) => {
    await gotoApp(page);

    await page.getByText('About', { exact: true }).click();

    await expect(page.locator('.popupDiv')).toBeVisible();

    const placed = await page.evaluate(() => {
        const at = document.querySelector('.popupDiv').getBoundingClientRect();

        return { left: at.left, right: window.innerWidth - at.right };
    });

    //A pixel of slack, since the two halves of an odd width do not round the same way.
    expect(Math.abs(placed.left - placed.right)).toBeLessThan(2);
    expect(placed.left).toBeGreaterThan(0);
});

///
///And it stays on the canvas whatever shape the window is.
///
///This is the one that broke. The popup's width was capped at 100vw less a constant, which is right only if
///the view is the whole window - it is about four fifths of it, the rest being page margins and the layer
///sidebar. At 1024 that allowed 644px into a space of 451 and the popup hung 192px over the layout. The
///height had the same shape of error in it: measured from a window whose bar fits on one line, where a
///narrow one wraps to two rows and is 32px taller.
///
///Six shapes rather than one, because every one of these faults appears at some sizes and not at others -
///and the two that were wrong were both wrong at sizes nobody had opened the popup at.
///
test.describe('the file lists stay on the canvas', () => {
    for (const { name, width, height } of [
        { name: 'narrow and tall', width: 900, height: 1400 },
        { name: 'narrow', width: 1024, height: 800 },
        { name: 'short', width: 1600, height: 620 },
        { name: 'tall', width: 1600, height: 1400 },
        { name: 'wide', width: 2560, height: 1400 }
    ])
    {
        test(`at ${name}`, async ({ page }) => {
            await page.setViewportSize({ width, height });

            await gotoExample(page, MOSFET, 'View2DSvg');

            await page.locator('#examplesButton').hover();

            await expect(page.locator('#examplePicker')).toBeVisible();

            const fits = await page.evaluate(() => {
                const round = (value) => Math.round(value);
                const box = (selector) => document.querySelector(selector).getBoundingClientRect();
                const view = box('.viewWrapper');
                const popup = document.querySelector('.popupDiv.popupUnder');
                const at = popup.getBoundingClientRect();
                const picture = document.querySelector('.examplePreviewFrame');
                let pictureWidth = 0;

                if (picture !== null)
                    pictureWidth = round(picture.getBoundingClientRect().width);

                return {
                    pastRight: round(at.right - view.right),
                    pastBottom: round(at.bottom - view.bottom),
                    offScreen: round(at.right - window.innerWidth),
                    scrolls: popup.scrollHeight > popup.clientHeight,
                    pictureWidth: pictureWidth,
                    listWidth: round(box('.examplePickerList').width)
                };
            });

            //Inside the canvas on both edges, and on the screen.
            expect(fits.pastRight).toBeLessThanOrEqual(0);
            expect(fits.pastBottom).toBeLessThanOrEqual(0);
            expect(fits.offScreen).toBeLessThanOrEqual(0);

            //The list scrolls; the popup does not. A popup that scrolls puts the picture below the fold.
            expect(fits.scrolls).toBe(false);

            //
            //And the picture is either worth looking at or not there.
            //
            //It was being squeezed to 136px on a narrow window - a frame with a smudge in it. The answer to
            //that was to drop it below 1200px, which was far too blunt: a window a hair under the breakpoint
            //lost the whole point of the panel to save twenty pixels. Both give way instead, each to a floor,
            //and the picture only goes at a width where the app itself is barely usable.
            //
            //So the claim is a floor rather than "bigger than the list" - on a narrow window the list's own
            //floor is the larger of the two, and that is the right way round: names are what you came for.
            //
            if (fits.pictureWidth > 0)
                expect(fits.pictureWidth).toBeGreaterThanOrEqual(160);
        });
    }
});

///
///Merge stands on its own: an ordinary gutter from the tools, and the fence from the grid.
///
///It is not one of the drawing tools - it is what the drawing tools do when they land on something - so it
///is a column of its own between them and the grid.
///
///**The two sides are not equal, and that is the second answer here.** They were made equal first, at a
///fence's worth each, and it reads as too much: the gap after Merge has a hairline down the middle of it
///and the gap before it has nothing, so the same distance looks deliberate on one side and like a mistake
///on the other. What is asserted is the shape of that - a column's own gutter before, more than that after,
///and a line in the wider one.
///
test('Merge sits a gutter from the tools and a fence from the grid', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    //Only in the bar with a drawing tool chosen, which is what it applies to.
    await page.locator('#selectTool').click();

    await expect(page.locator('#joinToggle')).toBeVisible();

    const around = await page.evaluate(() => {
        const box = (selector) => document.querySelector(selector).getBoundingClientRect();
        const merge = box('#joinToggle');
        //
        //Two that really are next to each other, to say what "inside a group" means: pan and measure.
        //
        //It comes out as nothing at all, which is the point - the buttons of a group are butted together
        //with no gap, and that is what makes them read as one control with five states.
        //
        const gutter = box('#measureTool').left - box('#panTool').right;

        return {
            //The tool group's last button, which is what Merge follows now that the library is gone.
            before: Math.round(merge.left - box('#drawTool').right),
            after: Math.round(box('#gridMenu').left - merge.right),
            insideAGroup: Math.round(gutter)
        };
    });

    //Buttons in a group touch; Merge does not, which is what makes it its own column.
    expect(around.insideAGroup).toBe(0);
    expect(around.before).toBeGreaterThan(0);

    //And the fence past it is wider than the gap before it, because that is the one with a line in it.
    expect(around.after).toBeGreaterThan(around.before);
});

///
///And when it does not fit, the rows are six apart - the same six that runs round the outside of the bar.
///
///The bar wraps rather than scrolls, so on a narrow window becoming two rows is the designed outcome and
///not a failure. What was wrong is that the two rows sat directly on each other: six pixels of air all the
///way round the outside, and none at all between the controls of one row and the controls of the next.
///
///Six is not chosen here, it is copied - whatever the bar's own padding is, the space between its rows is
///the same. Read from the computed padding rather than written down twice.
///
test('a wrapped toolbar keeps the bar\'s own spacing between its rows', async ({ page }) => {
    //Narrow enough that the bar cannot hold its groups on one line.
    await page.setViewportSize({ width: 1000, height: 900 });

    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect(page.locator('#toolGroup button').first()).toBeVisible();

    const bar = await page.evaluate(() => {
        const round = (value) => Math.round(value);
        const toolbar = document.querySelector('.viewToolbar');
        const box = toolbar.getBoundingClientRect();
        const columns = [...toolbar.children].map((column) => column.getBoundingClientRect());

        //A row is everything sharing a top; the gap is one row's lowest edge to the next row's top.
        const tops = [...new Set(columns.map((column) => round(column.top)))].sort((first, second) => first - second);

        const rows = tops.map((top) => {
            const on = columns.filter((column) => round(column.top) === top);

            return { top, bottom: round(Math.max(...on.map((column) => column.bottom))) };
        });

        const between = [];

        for (let i = 0; i < rows.length - 1; i++)
            between.push(rows[i + 1].top - rows[i].bottom);

        return {
            rows: rows.length,
            between,
            above: rows[0].top - round(box.top),
            below: round(box.bottom) - rows[rows.length - 1].bottom,
            padding: parseFloat(getComputedStyle(toolbar).paddingTop)
        };
    });

    //The case this is about: nothing to say if the bar did not wrap.
    expect(bar.rows).toBeGreaterThan(1);

    for (const gap of bar.between)
        expect(gap).toBe(bar.padding);

    //And the same above the first row and below the last, which is what it is being matched to.
    expect(bar.above).toBe(bar.padding);
    expect(bar.below).toBe(bar.padding);
});

///
///The download and the format it writes are one control, not two that happen to be adjacent.
///
///A format nothing writes and a save with no say in what it writes are each meaningless alone, so they are
///butted together the way the grid's icon is butted to its pitch: no seam, one height, and the rounding
///only on the outside corners.
///
test('the download button and the format picker are joined', async ({ page }) => {
    await gotoExample(page, MOSFET);

    const joined = await page.evaluate(() => {
        const button = document.getElementById('downloadGds');
        const picker = document.getElementById('downloadFormat');

        const left = button.getBoundingClientRect();
        const right = picker.getBoundingClientRect();

        const corners = (node) => getComputedStyle(node).borderRadius;

        return {
            seam: Math.round(right.left - left.right),
            heights: [Math.round(left.height), Math.round(right.height)],
            tops: [Math.round(left.top), Math.round(right.top)],
            buttonCorners: corners(button),
            pickerCorners: corners(picker)
        };
    });

    expect(joined.seam).toBe(0);
    expect(joined.heights[0]).toBe(joined.heights[1]);
    expect(joined.tops[0]).toBe(joined.tops[1]);

    //Square where they meet, round where they do not - which is what makes the pair read as one box.
    expect(joined.buttonCorners).toMatch(/^3px 0px 0px 3px/);
    expect(joined.pickerCorners).toMatch(/^0px 3px 3px 0px/);
});

///
///The full-screen button is on the line with everything else, at the far end of it.
///
///Apart by distance rather than by height. It was top-aligned, which read as deliberate while the bar was a
///mix of heights and read as misaligned once the rest became one row of squares of one size.
///
test('the full-screen button lines up with the rest of the bar', async ({ page }) => {
    await gotoExample(page, MOSFET, 'View2DSvg');

    await expect(page.locator('#toolGroup button').first()).toBeVisible();

    const lined = await page.evaluate(() => {
        const box = (selector) => {
            const found = document.querySelector(selector).getBoundingClientRect();

            return { w: Math.round(found.width), h: Math.round(found.height), bottom: Math.round(found.bottom) };
        };

        const grid = document.querySelector('.toolbarGrid');
        const button = document.querySelector('#fullScreen').getBoundingClientRect();

        return {
            full: box('#fullScreen'),
            peer: box('#historyButton'),

            //
            //How the column is aligned, not only where it landed.
            //
            //Every column in the bar is one control tall, so top and bottom alignment put the button in
            //exactly the same place today - a geometric check passes either way and says nothing. This is
            //what actually differs, and it is what starts to matter the moment any column is taller than
            //the rest.
            //
            alignment: [
                getComputedStyle(document.querySelector('.toolbarEnd')).alignSelf,
                getComputedStyle(grid).alignSelf
            ],

            clear: Math.round(button.left - grid.getBoundingClientRect().right),

            //The space either end of the bar: its edge to the first control, and the last control to its
            //edge. Two numbers that have to be one number.
            //
            //**The first control, not Open.** New was added ahead of it, and a selector naming Open measured
            //the air before the bar's first control plus that whole button - a failure about a bar nothing
            //had moved.
            ends: [
                Math.round(document.querySelector('#newLayout').getBoundingClientRect().left
                    - document.querySelector('.viewToolbar').getBoundingClientRect().left),
                Math.round(document.querySelector('.viewToolbar').getBoundingClientRect().right - button.right)
            ]
        };
    });

    //The same box as the bar's other icon buttons, to the pixel.
    expect(lined.full).toEqual(lined.peer);

    //And aligned the way they are, rather than pinned to the top of the bar as it was.
    expect(lined.alignment[0]).toBe(lined.alignment[1]);

    //Held well clear of the last group, which is what says it is about the window rather than the file.
    expect(lined.clear).toBeGreaterThan(40);

    //
    //And inset from its end of the bar by what the first control is inset from the other.
    //
    //This column had its gutter taken off, on the reading that the bar's own padding was enough for the
    //last thing in a row. What that produced was 22px of air at the left end and 12 at the right - a
    //difference small enough to pass for a rendering artefact and large enough to look wrong.
    //
    expect(lined.ends[0]).toBe(lined.ends[1]);
    expect(lined.ends[0]).toBeGreaterThan(0);
});
