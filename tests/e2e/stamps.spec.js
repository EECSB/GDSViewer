//Writing and pictures as shapes: the Draw menu's Text and Image rows, words pasted onto the drawing and a picture
//dropped on it - each put down at once with a frame round it, whose corner sizes it and whose middle moves it.
//Asked for 2026-10-19, in place of the rows' boxes for a height, a size of square and a number across - and on
//2026-10-21 to read the right way round here, and to turn and mirror where it lies.
//
//**Geometry, not labels.** What lands is boundaries on the layer being drawn on - the words in a pixel font, a
//logo as its squares merged into outlines - so these count shapes and measure the frame, rather than look for text
//in the picture. The merge and the font are C#'s and covered in RasterTests; which side of a picture is the shape
//is viewGeometry's drawnSide, covered in the Node tests. What is here is the part only a browser has: the menu,
//the picture read by a canvas, the frame and its drags, the paste and the drop.
const { test, expect } = require('@playwright/test');
const { gotoExample, shapeCount, shapeBox, chooseShape, drawingLayer } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, 'Mosfet', 'View2DSvg');

    await expect.poll(async () => shapeCount(page), { timeout: 60000 }).toBeGreaterThan(0);
});

///Into a cell, since writing goes into the cell being edited like any other shape.
async function enterCell(page) {
    await page.locator('#selectTool').click();

    const shape = await shapeBox(page);

    await page.mouse.click(shape.x + (shape.width / 2), shape.y + (shape.height / 2));

    await expect(page.locator('#drawTool')).toBeEnabled({ timeout: 30000 });
}

///Into a cell and out with the pencil.
async function enterCellAndDraw(page) {
    await enterCell(page);

    await page.locator('#drawTool').click();
}

///A point of the view given as fractions of it, in the window's pixels.
async function at(page, across, down) {
    const view = await page.locator('#gdsSVG').boundingBox();

    return { x: view.x + (view.width * across), y: view.y + (view.height * down) };
}

///The frame round what was just put down, in the layout's units, or null when there is none.
async function frame(page) {
    return page.evaluate(() => {
        const outline = document.querySelector('#pieceFrame .pieceOutline');

        if (outline == null)
            return null;

        return {
            x: Number(outline.getAttribute('x')),
            y: Number(outline.getAttribute('y')),
            width: Number(outline.getAttribute('width')),
            height: Number(outline.getAttribute('height'))
        };
    });
}

///
///The frame once it has stopped changing: two reads a beat apart that agree. It follows a drag and is put on the grid as
///the drag lets go, so a read taken as soon as it had passed a mark found 1906.06 where the size let go at was 1955 (a
///run of the whole file on 2026-11-02, under load).
///
async function settledFrame(page) {
    let last = null;

    await expect.poll(async () => {
        const now = await frame(page);
        const same = now !== null && last !== null && JSON.stringify(now) === JSON.stringify(last);

        last = now;

        return same;
    }, { timeout: 15000, intervals: [150] }).toBe(true);

    return last;
}

///Drags from one point of the window to another, the way a hand does.
async function drag(page, from, to) {
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move((from.x + to.x) / 2, (from.y + to.y) / 2, { steps: 4 });
    await page.mouse.move(to.x, to.y, { steps: 4 });
    await page.mouse.up();
}

///
///The middle of an element on screen - once it is on screen. The frame is drawn a moment after the step that puts it up,
///so a box read at once was sometimes none at all: a full run on 2026-11-02 read null straight after Enter kept the
///writing, and the retry passed.
///
async function middleOf(page, selector) {
    const element = page.locator(selector);

    await expect(element).toBeVisible();

    const box = await element.boundingBox();

    return { x: box.x + (box.width / 2), y: box.y + (box.height / 2) };
}

///
///Writing put down with the Text row where the view's point is, its words left as they are - Enter in their box.
///
async function writeWithTheRow(page, across = 0.3, down = 0.6) {
    await chooseShape(page, '#textShape');

    const point = await at(page, across, down);

    await page.mouse.click(point.x, point.y);

    await expect(page.locator('#canvasWritingText')).toBeFocused();
}

///
///A picture as a PNG, drawn by the browser's own canvas from rows of # (black), . (white) and a space (see-through).
///
///Made in the page rather than checked in, so the picture a test is about is written next to the test - and a real
///PNG encoder writes it, whatever the machine running the specs has installed.
///
async function picture(page, rows, name = 'logo.png') {
    const encoded = await page.evaluate((rows) => {
        const canvas = document.createElement('canvas');

        canvas.width = rows[0].length;
        canvas.height = rows.length;

        const drawing = canvas.getContext('2d');

        for (let y = 0; y < rows.length; y++) {
            for (let x = 0; x < rows[y].length; x++) {
                if (rows[y][x] === ' ')
                    continue;

                if (rows[y][x] === '#')
                    drawing.fillStyle = '#000';
                else
                    drawing.fillStyle = '#fff';

                drawing.fillRect(x, y, 1, 1);
            }
        }

        return canvas.toDataURL('image/png').split(',')[1];
    }, rows);

    return { name: name, mimeType: 'image/png', buffer: Buffer.from(encoded, 'base64') };
}

///
///The outlines one layer's path draws, each as its box in layout units, in the order they were drawn - so what an
///edit added is what is past the count from before it.
///
async function outlinesOn(page, layerClass) {
    return page.evaluate((layerClass) => {
        const path = document.querySelector(`#gdsSVG > path.${layerClass}`);

        if (path == null)
            return [];

        return window.viewGeometry.subpaths(path.getAttribute('d')).map(run => {
            const numbers = run.corners;
            const xs = numbers.filter((_, i) => i % 2 === 0);
            const ys = numbers.filter((_, i) => i % 2 === 1);

            return { left: Math.min(...xs), right: Math.max(...xs), top: Math.min(...ys), bottom: Math.max(...ys) };
        });
    }, layerClass);
}

///The corners of the last outline one layer's path draws, as [x, y] in layout units.
async function lastCorners(page, layerClass) {
    return page.evaluate((layerClass) => {
        const path = document.querySelector(`#gdsSVG > path.${layerClass}`);

        //None yet, rather than a throw, so a poll can wait for the layer to be drawn.
        if (path === null)
            return [];

        const runs = window.viewGeometry.subpaths(path.getAttribute('d'));

        if (runs.length === 0)
            return [];

        const numbers = runs[runs.length - 1].corners;
        const corners = [];

        for (let i = 0; i < numbers.length; i += 2)
            corners.push([numbers[i], numbers[i + 1]]);

        return corners;
    }, layerClass);
}

///
///**The Text row is placed the way a label is**: a click puts writing down reading "Text", with a box under it for
///the words and a frame round it. Enter keeps what is typed, as the one step that put it down - one undo takes the
///whole of it back.
///
test('the Text row puts writing down where it is clicked, and its words are typed under it', async ({ page }) => {
    await enterCellAndDraw(page);

    const was = await shapeCount(page);

    await writeWithTheRow(page);

    await expect(page.locator('#canvasWritingText')).toHaveValue('Text');
    await expect(page.locator('#pieceFrame')).toHaveCount(1);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place text/);
    await expect.poll(async () => shapeCount(page)).toBeGreaterThan(was);

    const text = await shapeCount(page);

    //Typed over, since it went in selected, and kept by Enter.
    await page.keyboard.type('IIII');
    await page.keyboard.press('Enter');

    await expect(page.locator('#canvasWritingText')).toHaveCount(0);
    await expect(page.locator('#pieceHint')).toContainText('“IIII”');

    //Four I's are four separate outlines, where "Text" was some other number of them.
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 4);
    expect(text).not.toBe(was + 4);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was);
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///
///**The frame's corner sizes it and its middle moves it**, its proportions kept and each the same step as the one
///that put it down - so one undo after both still takes all of it back.
///
test('the frame\'s corner sizes the writing and its middle moves it, as the step that put it down', async ({ page }) => {
    await enterCellAndDraw(page);

    const was = await shapeCount(page);

    await writeWithTheRow(page, 0.25, 0.5);
    await page.keyboard.press('Enter');

    await expect(page.locator('#canvasWritingText')).toHaveCount(0);

    const first = await frame(page);
    const corner = await middleOf(page, '#pieceFrame .pieceHandle');

    await drag(page, corner, { x: corner.x + 120, y: corner.y + 40 });

    await expect.poll(async () => (await frame(page)).width, { timeout: 15000 }).toBeGreaterThan(first.width * 1.5);

    const sized = await settledFrame(page);

    expect(sized.x).toBe(first.x);
    expect(sized.y).toBe(first.y);
    expect(sized.height / sized.width).toBeCloseTo(first.height / first.width, 5);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place text/);

    const middle = await middleOf(page, '#pieceFrame .pieceOutline');

    await drag(page, middle, { x: middle.x - 60, y: middle.y + 50 });

    await expect.poll(async () => (await frame(page)).x, { timeout: 15000 }).toBeLessThan(sized.x);

    const moved = await settledFrame(page);

    expect(moved.y).toBeGreaterThan(sized.y);
    expect(moved.width).toBe(sized.width);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was);
});

///
///Enter, Escape, choosing another tool and a press anywhere else put it down for good - the frame goes and the shapes
///stay - and Delete takes it back. Another tool since 2026-10-21, asked for: the frame stayed up under Select. See
///emptyHand.
///
test('Enter, Escape, another tool and a press elsewhere put it down, and Delete takes it back', async ({ page }) => {
    await enterCellAndDraw(page);

    const was = await shapeCount(page);

    await writeWithTheRow(page, 0.3, 0.4);
    await page.keyboard.press('Enter');
    await page.keyboard.press('Escape');

    await expect(page.locator('#pieceFrame')).toHaveCount(0);
    await expect(page.locator('#pieceHint')).toHaveCount(0);

    const one = await shapeCount(page);

    expect(one).toBeGreaterThan(was);

    //Another tool.
    await writeWithTheRow(page, 0.3, 0.75);
    await page.keyboard.press('Enter');

    await expect(page.locator('#pieceFrame')).toHaveCount(1);

    await page.locator('#selectTool').click();

    await expect(page.locator('#pieceFrame')).toHaveCount(0);
    await expect(page.locator('#pieceHint')).toHaveCount(0);
    await expect.poll(async () => shapeCount(page)).toBeGreaterThan(one);

    const two = await shapeCount(page);

    //A press elsewhere - on words pasted with the select tool in hand, so the press puts nothing down itself.
    const pointer = await at(page, 0.6, 0.75);

    await page.mouse.move(pointer.x, pointer.y);

    await page.evaluate(() => {
        const data = new DataTransfer();

        data.setData('text/plain', 'AB');

        window.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true }));
    });

    await expect(page.locator('#pieceFrame')).toHaveCount(1);

    const away = await at(page, 0.85, 0.15);

    await page.mouse.click(away.x, away.y);

    await expect(page.locator('#pieceFrame')).toHaveCount(0);
    await expect.poll(async () => shapeCount(page)).toBeGreaterThan(two);

    const three = await shapeCount(page);

    //And Delete, with the frame up: taken back.
    await page.locator('#drawTool').click();
    await writeWithTheRow(page, 0.6, 0.4);
    await page.keyboard.press('Enter');

    await expect.poll(async () => shapeCount(page)).toBeGreaterThan(three);

    await page.keyboard.press('Delete');

    await expect(page.locator('#pieceFrame')).toHaveCount(0);
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(three);
});

///
///**Writing's words open again once kept** (2026-11-01, asked for): a double-click on its frame, or Edit text in the
///frame's menu, puts the box back under it - and what is typed there is the same step that put it down. The double-click
///did nothing: a press that went nowhere drew the frame again, and the browser fires no click on an element gone.
///
test('the words of writing open again by a double-click on its frame or Edit text in its menu, as the same step', async ({ page }) => {
    await enterCellAndDraw(page);

    const was = await shapeCount(page);

    await writeWithTheRow(page);
    await page.keyboard.type('IIII');
    await page.keyboard.press('Enter');

    await expect(page.locator('#canvasWritingText')).toHaveCount(0);

    const words = page.locator('#canvasWritingText');
    const middle = await middleOf(page, '#pieceFrame .pieceOutline');

    await page.mouse.dblclick(middle.x, middle.y);

    await expect(words).toBeFocused();
    await expect(words).toHaveValue('IIII');

    await words.fill('II');
    await page.keyboard.press('Enter');

    await expect(page.locator('#pieceHint')).toContainText('“II”');
    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 2);

    //And from the menu, Edit text at its top - on the frame as it is now, narrower for fewer letters.
    const now = await middleOf(page, '#pieceFrame .pieceOutline');

    await page.mouse.click(now.x, now.y, { button: 'right' });

    const menu = page.locator('#shapeMenu');

    await expect(menu.getByRole('menuitem').first()).toHaveText(/Edit text/);
    await menu.getByRole('menuitem', { name: 'Edit text' }).click();

    await expect(words).toBeFocused();
    await expect(words).toHaveValue('II');

    await words.fill('III');
    await page.keyboard.press('Enter');

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was + 3);

    //Still the one step: one undo takes the writing back whole.
    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was);
});

///
///**Writing reads the right way round here** (asked for 2026-10-21). This view draws Y downward, and words were put
///down upright in the file - so "Text" read as an upside-down mirror image of itself, a T with its bar at the foot.
///The bar is at the top of the screen now, the least Y, with the stem running down from the middle of it.
///
test('writing reads the right way round here: a T has its bar at the top of the screen', async ({ page }) => {
    await enterCellAndDraw(page);

    const layer = 'l' + (await drawingLayer(page)).replace('/', '_');

    await writeWithTheRow(page, 0.4, 0.5);
    await page.keyboard.type('T');
    await page.keyboard.press('Enter');

    await expect(page.locator('#pieceHint')).toContainText('“T”');
    await expect.poll(async () => frame(page)).not.toBeNull();

    const across = (corners, y) => {
        const xs = corners.filter(corner => corner[1] === y).map(corner => corner[0]);

        return Math.max(...xs) - Math.min(...xs);
    };

    //
    //Five squares across and seven down, one outline - the frame and the outline read together, and polled: the frame
    //goes up a moment before the view has drawn what it frames, and read once, a run of the whole file on 2026-11-02 found
    //another outline last on the layer, 250 across at the top where the T's bar is 1150. An upside-down T never comes
    //right, so the wait costs the test nothing.
    //
    await expect.poll(async () => {
        const box = await frame(page);

        if (box === null)
            return null;

        const corners = await lastCorners(page, layer);
        const square = box.width / 5;

        return {
            topAtFrame: Math.min(...corners.map(corner => corner[1])) === box.y,
            barAcrossTop: across(corners, box.y) === 5 * square,
            stemAtFoot: across(corners, box.y + box.height) === square
        };
    }, { timeout: 15000 }).toEqual({ topAtFrame: true, barAcrossTop: true, stemAtFoot: true });
});

///
///**R turns it a quarter about its middle, and Shift+R back** - asked for 2026-10-21, as part of putting it down rather
///than a step on the shapes after - and so do the two arrows over the frame. Each puts it down again in place of
///itself, so one undo after all of them still takes the whole of it back.
///
test('R turns writing a quarter about its middle, Shift+R and the arrows over it turn it back, all one step', async ({ page }) => {
    await enterCellAndDraw(page);

    const was = await shapeCount(page);

    await writeWithTheRow(page, 0.4, 0.5);
    await page.keyboard.press('Enter');

    await expect(page.locator('#canvasWritingText')).toHaveCount(0);
    await expect.poll(async () => frame(page)).not.toBeNull();

    //"Text": twenty-three squares across and seven down.
    const first = await frame(page);
    const square = first.height / 7;

    await page.keyboard.press('r');

    await expect.poll(async () => (await frame(page)).height, { timeout: 15000 }).toBe(first.width);

    const turned = await frame(page);

    expect(turned.width).toBe(first.height);
    expect(Math.abs((turned.x + (turned.width / 2)) - (first.x + (first.width / 2)))).toBeLessThanOrEqual(square);
    expect(Math.abs((turned.y + (turned.height / 2)) - (first.y + (first.height / 2)))).toBeLessThanOrEqual(square);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place text/);

    //Back by Shift+R, to the very corner it left.
    await page.keyboard.press('Shift+R');

    await expect.poll(async () => (await frame(page)).width, { timeout: 15000 }).toBe(first.width);
    expect(await frame(page)).toEqual(first);

    //The arrows: the right one a quarter, and the left one back.
    await page.locator('#pieceFrame .pieceTurn[data-turn="right"]').click();

    await expect.poll(async () => (await frame(page)).height, { timeout: 15000 }).toBe(first.width);

    await page.locator('#pieceFrame .pieceTurn[data-turn="left"]').click();

    await expect.poll(async () => (await frame(page)).width, { timeout: 15000 }).toBe(first.width);
    expect(await frame(page)).toEqual(first);

    await page.locator('#undoEdit').click();

    await expect.poll(async () => shapeCount(page), { timeout: 15000 }).toBe(was);
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///
///**A picture reads the way it was drawn**, its top row at the top of the screen, and **Ctrl+M and the frame's own menu
///mirror it** in place - the menu a right-click on the frame raises, rather than the menu of the square under the pointer.
///A block a square in from the picture's top left corner, which lands somewhere different every way round.
///
test('a picture reads the way it was drawn, and Ctrl+M and the frame\'s menu mirror it where it lies', async ({ page }) => {
    await enterCellAndDraw(page);

    const layer = 'l' + (await drawingLayer(page)).replace('/', '_');
    const before = await outlinesOn(page, layer);

    await page.locator('#stampPicture').setInputFiles(await picture(page, ['........', '.##.....', '.##.....', '........', '........', '........']));

    await expect.poll(async () => (await outlinesOn(page, layer)).length, { timeout: 15000 }).toBe(before.length + 1);
    await expect.poll(async () => frame(page)).not.toBeNull();

    const box = await frame(page);
    const square = box.width / 8;
    const block = async () => (await outlinesOn(page, layer))[before.length];

    expect((await block()).left).toBe(box.x + square);
    expect((await block()).top).toBe(box.y + square);

    //Top to bottom by Ctrl+M: its rows one and two are rows three and four of six, and the frame stays where it was.
    await page.keyboard.press('Control+M');

    await expect.poll(async () => (await block())?.top, { timeout: 15000 }).toBe(box.y + (3 * square));
    expect((await block()).left).toBe(box.x + square);
    expect(await frame(page)).toEqual(box);

    //Left to right from the frame's menu.
    const middle = await middleOf(page, '#pieceFrame .pieceOutline');

    await page.mouse.click(middle.x, middle.y, { button: 'right' });

    const menu = page.locator('#shapeMenu');

    await expect(menu.getByRole('menuitem', { name: 'Turn right' })).toBeVisible();
    await menu.getByRole('menuitem', { name: 'Mirror across' }).click();

    await expect.poll(async () => (await block())?.left, { timeout: 15000 }).toBe(box.x + (5 * square));
    expect((await block()).top).toBe(box.y + (3 * square));
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place image/);
});

///
///**Words pasted onto the drawing are written there**, where the pointer is, a line of writing for each of their
///lines - two rows of space between, all from the left. Through the browser's paste event, which is where a
///paste's words are.
///
test('words pasted onto the drawing are written where the pointer is, a line for each line', async ({ page }) => {
    await enterCell(page);

    const point = await at(page, 0.4, 0.5);

    await page.mouse.move(point.x, point.y);

    await page.evaluate(() => {
        const data = new DataTransfer();

        data.setData('text/plain', 'AB\nC');

        window.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true }));
    });

    await expect(page.locator('#pieceFrame')).toHaveCount(1);
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place text/);
    await expect(page.locator('#pieceHint')).toContainText('“AB C”');

    //Eleven squares across, for the longer line, and seven, two and seven down.
    const box = await frame(page);

    expect(box.height / box.width).toBeCloseTo(16 / 11, 5);

    //Held by its middle, which is where the pointer was - to within a square.
    const shown = await page.locator('#pieceFrame .pieceOutline').boundingBox();

    expect(Math.abs((shown.x + (shown.width / 2)) - point.x)).toBeLessThan(shown.width / 11 + 1);
    expect(Math.abs((shown.y + (shown.height / 2)) - point.y)).toBeLessThan(shown.height / 16 + 1);
});

///
///**Shapes copied here still paste as shapes**: copying puts a sentence on the clipboard that says so, and a paste
///finding it pastes the shapes rather than writing the sentence.
///
test('shapes copied in the app paste as shapes, not as their sentence written out', async ({ page }) => {
    await enterCell(page);

    await page.keyboard.press('Control+C');

    const was = await shapeCount(page);

    await page.evaluate(() => {
        const data = new DataTransfer();

        data.setData('text/plain', '1 shape copied in GDS Viewer');

        window.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true }));
    });

    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Paste/);
    await expect.poll(async () => shapeCount(page)).toBe(was + 1);
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///
///**The side drawn is the side the border is not**: a black square on white is drawn by its black, a white one on
///black by its white, and a white one cut out of a see-through ground by what is there at all. One outline each,
///a quarter of the picture in from its corner, where the frame is the whole picture.
///
test('a picture is drawn by the side its border is not: dark on light, light on dark, anything on nothing', async ({ page }) => {
    await enterCellAndDraw(page);

    //The layer the pencil is out on, as its path's class: 65/20 is l65_20.
    const layer = 'l' + (await drawingLayer(page)).replace('/', '_');

    //Each picture puts the one before it down for good, the frame going round the new one.
    for (const rows of [
        ['........', '.####...', '.####...', '........'],
        ['########', '#....###', '#....###', '########'],
        ['        ', ' ....   ', ' ....   ', '        ']
    ]) {
        const before = await outlinesOn(page, layer);

        await page.locator('#stampPicture').setInputFiles(await picture(page, rows));

        await expect.poll(async () => (await outlinesOn(page, layer)).length, { timeout: 15000 }).toBe(before.length + 1);

        //The frame is shown once what it goes round is drawn, a moment after.
        await expect.poll(async () => frame(page)).not.toBeNull();

        const box = await frame(page);
        const added = (await outlinesOn(page, layer))[before.length];
        const square = box.width / 8;

        expect(box.height).toBe(4 * square);
        expect(added.left).toBe(box.x + square);
        expect(added.right).toBe(box.x + (5 * square));
        expect(added.bottom - added.top).toBe(2 * square);
    }
});

///
///**A picture dropped on the drawing lands where it was let go**, rather than being opened as a layout - through the
///Image row's own input, which the menu's file dialog uses.
///
test('a picture dropped on the drawing lands where it was let go', async ({ page }) => {
    await enterCell(page);

    const file = await picture(page, ['....', '.##.', '.##.', '....'], 'dropped.png');
    const point = await at(page, 0.6, 0.35);

    const carrying = await page.evaluateHandle(({ content, name }) => {
        const bytes = Uint8Array.from(atob(content), character => character.charCodeAt(0));
        const transfer = new DataTransfer();

        transfer.items.add(new File([bytes], name, { type: 'image/png' }));

        return transfer;
    }, { content: file.buffer.toString('base64'), name: file.name });

    await page.locator('#gdsSVG').dispatchEvent('drop', { dataTransfer: carrying, clientX: point.x, clientY: point.y });

    await expect(page.locator('#pieceFrame')).toHaveCount(1);
    await expect(page.locator('#pieceHint')).toContainText('dropped.png');
    await expect(page.locator('#undoEdit')).toHaveAttribute('title', /Undo Place image/);

    //Its middle where it was let go, to within a square.
    const shown = await page.locator('#pieceFrame .pieceOutline').boundingBox();

    expect(Math.abs((shown.x + (shown.width / 2)) - point.x)).toBeLessThan(shown.width / 4 + 1);
    expect(Math.abs((shown.y + (shown.height / 2)) - point.y)).toBeLessThan(shown.height / 4 + 1);
});

///A file the browser cannot read as a picture is said, over the drawing, rather than nothing happening.
test('a file that is not a picture says so', async ({ page }) => {
    await enterCellAndDraw(page);

    await page.locator('#stampPicture').setInputFiles({
        name: 'notes.png',
        mimeType: 'image/png',
        buffer: Buffer.from('these are not the bytes of a picture')
    });

    await expect(page.locator('#drawRefusal')).toContainText('notes.png could not be read as a picture');
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///And one that is all ground - nothing on it its border is not - says that.
test('a picture with nothing on it but its ground says so', async ({ page }) => {
    await enterCellAndDraw(page);

    await page.locator('#stampPicture').setInputFiles(await picture(page, ['....', '....'], 'blank.png'));

    await expect(page.locator('#drawRefusal')).toContainText('Nothing in blank.png to draw');
    await expect(page.locator('#pieceFrame')).toHaveCount(0);
});

///
///**A photograph is read fewer squares across until it is few enough shapes**, rather than refused: there is no box
///to ask for fewer in any more. Noise this size is four thousand specks at a square a pixel, past the two thousand
///a picture may put down. The noise is seeded, so it is the same picture every run.
///
test('a picture that breaks into thousands of specks is read fewer squares across until it does not', async ({ page }) => {
    await enterCellAndDraw(page);

    const rows = [];
    let seed = 12345;

    for (let y = 0; y < 256; y++) {
        const row = [];

        for (let x = 0; x < 256; x++) {
            seed = ((seed * 1103515245) + 12345) & 0x7fffffff;

            if ((seed >> 16) % 2 === 0)
                row.push('#');
            else
                row.push('.');
        }

        rows.push(row.join(''));
    }

    const was = await shapeCount(page);

    await page.locator('#stampPicture').setInputFiles(await picture(page, rows, 'photo.png'));

    await expect(page.locator('#pieceFrame')).toHaveCount(1, { timeout: 60000 });
    await expect(page.locator('#drawRefusal')).toHaveCount(0);

    const added = (await shapeCount(page)) - was;

    expect(added).toBeGreaterThan(0);
    expect(added).toBeLessThanOrEqual(2000);
});
