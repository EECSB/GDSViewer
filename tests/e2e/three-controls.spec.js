//The 3D view's own controls: the QR popup, the scene backgrounds, and the cinematic camera orbit. None of
//these draw geometry, so nothing else in the suite touches them - and all three run entirely in JS the
//build cannot check.
const { test, expect } = require('@playwright/test');
const { gotoExample, captureScene, cameraPosition, selectBackground, MOSFET } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
});

test('the QR popup encodes the address, so it carries the file and view', async ({ page }) => {
    await page.locator('#openOnPhone').click();

    //
    //Rendered as an SVG of modules by the QR generator; a handful of shapes would mean it drew nothing.
    //
    //Named by the box the popup puts it in, not as "the first svg that is not the layout". That found the
    //right node for as long as the QR code was the only other SVG in the page, and stopped the day a
    //toolbar button was drawn as an inline icon - it picked up a one-path glyph in the bar and reported
    //that the QR code had failed to draw.
    //
    const qr = page.locator('.svgContainer svg');

    await expect(qr).toBeVisible();

    const modules = await qr.locator('path, rect').count();

    expect(modules).toBeGreaterThan(50);
});

///
///And it opens in the middle of the view rather than half off the side of the window.
///
///The popup class it uses is fixed and centered on the *window*, which is already the wrong box - the
///window includes a sidebar the 3D view has nothing to do with. On top of that it carried an inline
///left: 10%, measured from a box that class had already pulled back half its own width, so a tenth of the
///way in came out as a tenth in minus half a popup and most of the code hung off the left edge.
///
///Centered on the .viewWrapper it is written inside now. Asserted as a *pair* of margins rather than as a
///position, because equal margins either side is the whole claim and it holds at any window size.
///
test('the QR code is centered on the view it belongs to', async ({ page }) => {
    await page.locator('#openOnPhone').click();

    await expect(page.locator('.svgContainer svg')).toBeVisible();

    const placed = await page.evaluate(() => {
        const box = (selector) => document.querySelector(selector).getBoundingClientRect();
        const view = box('.viewWrapper');
        const popup = box('.popupDiv.popupInView');

        return {
            left: popup.left - view.left,
            right: view.right - popup.right,
            top: popup.top - view.top,
            bottom: view.bottom - popup.bottom,
            code: box('.svgContainer svg').width,
            coversHeight: 100 * popup.height / view.height
        };
    });

    //Sub-pixel, since the two halves of an odd width do not round the same way.
    expect(Math.abs(placed.left - placed.right)).toBeLessThan(2);
    expect(Math.abs(placed.top - placed.bottom)).toBeLessThan(2);

    //Inside the view on every side, which is the thing that was actually broken.
    for (const margin of [placed.left, placed.right, placed.top, placed.bottom])
        expect(margin).toBeGreaterThan(0);

    //
    //And big enough to point a phone at - which is a share of the view rather than a number.
    //
    //It collapsed to 125px the moment the popup stopped being 80% of the window, because the code was
    //sized at 100% of a parent that was sized to its contents. It was then enlarged twice and did not move
    //either time, because the popup kept a max-width of 460px from the first of those changes and that cap
    //bound before any of the arithmetic did. A test for "bigger than 240" passed through all of it.
    //
    //So what is asked here is that the popup is most of the height of the view. A cap in pixels cannot
    //satisfy that at more than one window size, which is the property the two fixed numbers lacked.
    //
    expect(placed.code).toBeGreaterThan(240);
    expect(placed.coversHeight).toBeGreaterThan(80);
});

///
///Open On Phone sits with Enter VR and Enter AR, and is built to look like them.
///
///Worth asserting rather than eyeballing, because the two beside it are not ours: three.js creates them
///and styles them inline, and it goes on restyling them after the fact - isSessionSupported resolves a
///frame or two later and the button relabels and resizes itself then. What keeps the row a row is that
///the interop takes the positioning off them on the way in, and nothing in a build would notice if a
///version bump changed how that works.
///
test('the three ways onto a headset or a phone are one row of matching buttons', async ({ page }) => {
    //Drawn after the view mounts, so polled rather than read once.
    await expect.poll(async () => page.locator('#xrButtons button').count(), { timeout: 30000 })
        .toBe(3);

    const buttons = await page.locator('#xrButtons button').evaluateAll(elements => elements.map(element => {
        const rect = element.getBoundingClientRect();

        return {
            text: (element.textContent || '').replace(/\s+/g, ' ').trim(),
            x: Math.round(rect.x),
            y: Math.round(rect.y),
            width: Math.round(rect.width),
            height: Math.round(rect.height)
        };
    }));

    //Ours last, after the two the library builds.
    expect(buttons.map(button => button.text)).toEqual([
        'VR NOT SUPPORTED',
        'AR NOT SUPPORTED',
        'OPEN ON PHONE'
    ]);

    //One row: same top, same size, and each one to the right of the last.
    for (const button of buttons) {
        expect(button.y).toBe(buttons[0].y);
        expect(button.height).toBe(buttons[0].height);
        expect(button.width).toBe(buttons[0].width);
    }

    expect(buttons[1].x).toBeGreaterThan(buttons[0].x);
    expect(buttons[2].x).toBeGreaterThan(buttons[1].x);

    //
    //And inside the view rather than hanging off the bottom of it.
    //
    //This used to check the row cleared the layer-distance slider beneath it. That slider is a row under
    //the layer list now, so there is nothing under the buttons to clear - what is still worth pinning is
    //that the row sits within the scene it belongs to, which is what its bottom offset is for.
    //
    const view = await page.locator('.viewWrapper').boundingBox();

    expect(buttons[0].y).toBeGreaterThanOrEqual(view.y);
    expect(buttons[0].y + buttons[0].height).toBeLessThanOrEqual(view.y + view.height);
});

///
///History and Examples are a pair, History first, and the pair sits a gutter from the file controls.
///
///Three spacings, and each one says something different. The two lists are all but touching, because they
///are the same kind of thing. They are one ordinary column gutter from Open and Save, because those open
///files too - wider than the gap inside the pair, and nothing like the fence past them. And the fence goes
///after Examples, where the bar stops being about getting a file on screen.
///
test('History and Examples are a pair, a gutter from the file controls', async ({ page }) => {
    const boxes = await page.evaluate(() => {
        const box = (selector) => {
            const found = document.querySelector(selector).getBoundingClientRect();

            return { left: found.left, right: found.right, bottom: Math.round(found.bottom) };
        };

        return {
            open: box('label[for="fileUpload"]'),

            //The download is two halves joined into one control, so its edges are two different elements.
            downloadStarts: box('#downloadGds'),
            downloadEnds: box('#downloadFormat'),

            history: box('#historyButton'),
            examples: box('#examplesButton'),
            view: box('#viewPick')
        };
    });

    //In that order along the bar.
    expect(boxes.history.left).toBeGreaterThanOrEqual(boxes.downloadEnds.right);
    expect(boxes.examples.left).toBeGreaterThanOrEqual(boxes.history.right);

    const insideTheFileControls = boxes.downloadStarts.left - boxes.open.right;
    const insideThePair = boxes.examples.left - boxes.history.right;

    const betweenThem = boxes.history.left - boxes.downloadEnds.right;
    const theFence = boxes.view.left - boxes.examples.right;

    //The pair is as close together as the file controls are to each other, which is what makes it a pair.
    expect(Math.round(insideThePair)).toBe(Math.round(insideTheFileControls));

    //Plainly further from the file controls than that, and further still past Examples.
    expect(betweenThem).toBeGreaterThan(insideThePair * 2);
    expect(theFence).toBeGreaterThan(betweenThem);

    //
    //And the fence is a drawn line, which is what lets the space around it be as small as it now is.
    //
    //This used to ask for the fence to be half again a gutter, back when space was the only thing saying
    //where one group ended. It is 26 against 20 now - the difference somebody sees is the hairline down the
    //middle of it, so that is what gets asserted rather than a multiple that was really a note of what the
    //fence measured on the day it was written.
    //
    const line = await page.evaluate(() => {
        const drawn = getComputedStyle(document.querySelector('.toolbarGroupEnd'), '::after');

        return { content: drawn.content, width: drawn.borderLeftWidth, color: drawn.borderLeftColor };
    });

    expect(line.content).not.toBe('none');
    expect(parseFloat(line.width)).toBeGreaterThan(0);

    //And all of it on the one baseline the whole bar sits on.
    expect(boxes.history.bottom).toBe(boxes.downloadEnds.bottom);
    expect(boxes.examples.bottom).toBe(boxes.downloadEnds.bottom);
});

test('choosing a background fetches it and puts it on the scene', async ({ page }) => {
    await captureScene(page);

    //Nothing behind the layout to begin with.
    expect(await page.evaluate(() => window.__gdsScene?.background)).toBeNull();

    const request = page.waitForRequest(url => url.url().includes('/resources/Images/Background/'));

    await selectBackground(page, 'background1.jpg');

    //The image is fetched, then turned into a cube render target once it has decoded.
    await request;
    await expect.poll(async () => page.evaluate(() => window.__gdsScene?.background != null), { timeout: 30000 })
        .toBe(true);
});

test('choosing no background clears it again', async ({ page }) => {
    await captureScene(page);

    await selectBackground(page, 'background1.jpg');
    await expect.poll(async () => page.evaluate(() => window.__gdsScene?.background != null), { timeout: 30000 })
        .toBe(true);

    await selectBackground(page, 'none');
    await expect.poll(async () => page.evaluate(() => window.__gdsScene?.background == null)).toBe(true);
});

test('Admire moves the camera, and pressing it again stops', async ({ page }) => {
    await captureScene(page);

    const before = await cameraPosition(page);

    expect(before).not.toBeNull();

    const admire = page.getByRole('button', { name: 'Admire' });

    await admire.click();

    //Driven from the render loop, so it takes frames rather than a call to move.
    await expect.poll(async () => {
        const now = await cameraPosition(page);

        return now.x !== before.x || now.y !== before.y || now.z !== before.z;
    }, { timeout: 15000 }).toBe(true);

    await admire.click();

    //Settled once toggled off: two readings a moment apart match.
    await page.waitForTimeout(500);

    const stopped = await cameraPosition(page);

    await page.waitForTimeout(500);

    expect(await cameraPosition(page)).toEqual(stopped);
});

test('the layer-spacing slider spreads the stack', async ({ page }) => {
    await captureScene(page);

    //Read off the meshes' own offsets, which is what the slider actually changes.
    const spreadOf = () => page.evaluate(() => {
        const meshes = window.__gdsScene?.children
            .flatMap(child => child.children ?? [])
            .filter(child => child.isMesh) ?? [];

        if (meshes.length === 0)
            return 0;

        const offsets = meshes.map(mesh => mesh.position.y);

        return Math.max(...offsets) - Math.min(...offsets);
    });

    const before = await spreadOf();

    await page.locator('#layerSpacing').fill('600');

    await expect.poll(spreadOf).toBeGreaterThan(before);
});

///
///Spreading the stack keeps it in front of the camera rather than growing off the top of it.
///
///The offsets run from zero upward, so at the widest spacing the bundled transistor stood 5,600 units tall
///while the camera, 2,000 back at a 100 degree field of view, saw about 2,384 either side of what it was
///aimed at - five of its nine layers above the top edge. The spacing was even the whole time. What looked
///like layers scattered at random was the stack leaving the frame, and the test above passes either way
///because a spread is a spread wherever it sits.
///
///Asserted in world space, which is the only place it shows: the offsets were never the thing that was
///wrong, and it is the group that moves rather than any object in it.
///
test('spreading the stack keeps it in front of the camera, not above it', async ({ page }) => {
    await captureScene(page);

    const stackInWorld = () => page.evaluate(() => {
        const scene = window.__gdsScene;

        if (scene == null)
            return null;

        //The group's own position is part of the answer, so the whole tree has to be current before reading.
        scene.updateMatrixWorld(true);

        const meshes = scene.children
            .flatMap(child => child.children ?? [])
            .filter(child => child.isMesh);

        if (meshes.length === 0)
            return null;

        //Element 13 of a matrix4 is its y translation, which saves needing THREE itself in here.
        const heights = meshes.map(mesh => mesh.matrixWorld.elements[13]);

        return { low: Math.min(...heights), high: Math.max(...heights) };
    });

    await page.locator('#layerSpacing').fill('700');

    //Wait for the spread before judging where it sits, or this reads the stack it had before the slider.
    await expect.poll(async () => {
        const stack = await stackInWorld();

        if (stack == null)
            return 0;

        return stack.high - stack.low;
    }).toBeGreaterThan(1000);

    const stack = await stackInWorld();
    const middle = Math.abs((stack.low + stack.high) / 2);
    const spread = stack.high - stack.low;

    //
    //A tenth of its own height, which is the difference between centered and standing on the camera's axis.
    //
    //Growing upward from zero puts the middle at half the spread, so that failure is five times this bound
    //rather than a near miss - and it is stated as a fraction because the spread depends on the slider and
    //on the file, where being centered does not.
    //
    expect(middle).toBeLessThan(spread / 10);
});

///
///The distance slider lives with the layers, the way the 2D view's opacity does.
///
///It was an .overlay pinned to the bottom of the scene - a control laid over the very thing it changes, in
///a view whose whole surface is a drag target for the orbit controls. Both views now answer "let me see
///past the layer on top" in the same place: the row under the layer list.
///
test('the layer-distance slider sits under the layer list, in the layer sidebar', async ({ page }) => {
    await expect(page.locator('#layerSpacing')).toBeVisible();

    //In the sidebar, and the thing directly after the list rather than merely somewhere near it.
    await expect(page.locator('#layerSidebar #layerSpacing')).toHaveCount(1);

    const follows = await page.evaluate(() => {
        const control = document.querySelector('.layerSpacing');

        return control.previousElementSibling === document.querySelector('.layerList');
    });

    expect(follows).toBe(true);

    //And nothing left floating over the scene.
    await expect(page.locator('.viewWrapper .overlay')).toHaveCount(0);
});

///
///Open On Phone, not offered on a phone.
///
///The button shows a QR code of *this page* so a desktop can hand the layout to a phone or a headset. On
///the phone it is a code pointing at the page you are already reading - the one device it can do nothing
///for - and it is the widest of the three on the narrowest screen they have to fit on.
///
///Emulated rather than merely narrow, because the rule is `hover: none and pointer: coarse` and not a
///width: a touchscreen laptop has a coarse pointer and a mouse, and should keep a button it can use. A
///viewport size alone would not tell the two apart, so a resize would pass this whether the rule worked
///or not.
///
test.describe('on a touch device', () => {
    //
    //The three properties the rule keys on, named rather than a whole device profile: a profile also carries
    //defaultBrowserType, which cannot be set on a describe because it would need its own worker. isMobile is
    //the one that makes Chromium report the coarse pointer and the missing hover.
    //
    test.use({ viewport: { width: 393, height: 851 }, hasTouch: true, isMobile: true });

    test('Open On Phone is not offered, and the other two still are', async ({ page }) => {
        //The pair the rule actually keys on, so a failure below says which half went wrong.
        const touch = await page.evaluate(() => ({
            hoverNone: matchMedia('(hover: none)').matches,
            coarse: matchMedia('(pointer: coarse)').matches
        }));

        expect(touch).toEqual({ hoverNone: true, coarse: true });

        //Still in the markup - the interop finds it by id to insert VR and AR before it - and not shown.
        await expect(page.locator('#openOnPhone')).toHaveCount(1);
        await expect(page.locator('#openOnPhone')).toBeHidden();

        //A phone can genuinely do AR, so those two stay.
        await expect.poll(async () => page.locator('#xrButtons button:visible').count(), { timeout: 30000 })
            .toBe(2);
    });
});

///
///The 3D bar is the same bar as the rest of the app, with the same parts.
///
///It was the last one still built the old way: two words written over two dropdowns, a download that was a
///PNG on a bare <img> with a cursor over it, and Admire held apart in the middle of the row. Every one of
///those has an answer already in use elsewhere in the bar, so this says the 3D view is using them and not
///quietly keeping its own.
///
test.describe('the 3D view puts its controls in the bar the way everything else does', () => {
    ///
    ///The model download and the format it writes are one control, exactly as the GDS download and its
    ///.gds/.oas picker are - no seam, one height, the caps on the outside corners.
    ///
    test('the model download and its file type are joined', async ({ page }) => {
        const joined = await page.evaluate(() => {
            const button = document.getElementById('downloadModel');
            const picker = document.getElementById('modelFormat');

            const left = button.getBoundingClientRect();
            const right = picker.getBoundingClientRect();

            return {
                seam: Math.round(right.left - left.right),
                heights: [Math.round(left.height), Math.round(right.height)],
                buttonCorners: getComputedStyle(button).borderRadius,
                pickerCorners: getComputedStyle(picker).borderRadius,

                //A button rather than a picture with a click handler, which is the part a keyboard cares
                //about: an <img @onclick> is not focusable and not reachable without a pointer.
                tag: button.tagName,
                glyph: button.querySelector('svg') != null
            };
        });

        expect(joined.seam).toBe(0);
        expect(joined.heights[0]).toBe(joined.heights[1]);
        expect(joined.buttonCorners).toMatch(/^3px 0px 0px 3px/);
        expect(joined.pickerCorners).toMatch(/^0px 3px 3px 0px/);

        expect(joined.tag).toBe('BUTTON');
        expect(joined.glyph).toBe(true);
    });

    ///
    ///Admire is not in the bar at all. It is in the corner of the view, with the other two controls that
    ///are about the view rather than about the file.
    ///
    ///It was a word in a row of file formats and backgrounds, which is the wrong company: what it does is
    ///move the camera, and the two controls that already sit on the canvas are the ways of taking this view
    ///somewhere else. Same height as them, same margin off the edge, same look - and in the far corner,
    ///since their row is centered and holds the ways of leaving rather than of looking.
    ///
    test('Admire sits on the canvas with the other view controls', async ({ page }) => {
        //Out of the bar entirely, not merely moved along it.
        await expect(page.locator('.viewToolbar #admire')).toHaveCount(0);

        const placed = await page.evaluate(() => {
            const admire = document.querySelector('#admire').getBoundingClientRect();
            const phone = document.querySelector('#openOnPhone').getBoundingClientRect();
            const view = document.querySelector('#container').getBoundingClientRect();

            const style = getComputedStyle(document.querySelector('#admire'));

            return {
                height: [Math.round(admire.height), Math.round(phone.height)],

                //Sitting on the same line as them, off the same edge.
                bottoms: [Math.round(view.bottom - admire.bottom), Math.round(view.bottom - phone.bottom)],

                //And in the far corner rather than in their row, which is centered.
                pastTheRow: admire.left > phone.right,
                fromTheRight: Math.round(view.right - admire.right),

                //Their look, not the bar's blue: a wash with a dark outline and dark lettering on it.
                border: style.borderTopWidth,
                color: style.color,
                background: style.backgroundColor
            };
        });

        expect(placed.height[0]).toBe(placed.height[1]);
        expect(placed.bottoms[0]).toBe(placed.bottoms[1]);

        expect(placed.pastTheRow).toBe(true);
        expect(placed.fromTheRight).toBeLessThan(40);

        //
        //A wash with an outline, which is what the three.js buttons beside it are.
        //
        //Not a solid panel: these sit over the layout, and four filled slabs across the foot of the view
        //are the loudest thing on screen when what they are is three ways out of it and a camera toggle.
        //Drawn in ink rather than in white, since the default scene is light gray.
        //
        expect(placed.border).toBe('1px');
        expect(placed.background).toMatch(/^rgba\(255, 255, 255, 0\./);

        const ink = placed.color.match(/\d+/g).map(Number);

        expect(Math.max(...ink)).toBeLessThan(80);
    });

    ///<summary>And it says when it is running, which the picture cannot.</summary>
    test('Admire lights up while it is running', async ({ page }) => {
        const admire = page.locator('#admire');

        await expect(admire).toHaveAttribute('aria-pressed', 'false');

        await admire.click();

        await expect(admire).toHaveAttribute('aria-pressed', 'true');
        await expect(admire).toHaveClass(/admireOn/);

        await admire.click();

        await expect(admire).toHaveAttribute('aria-pressed', 'false');
        await expect(admire).not.toHaveClass(/admireOn/);
    });

    ///
    ///No words over the dropdowns.
    ///
    ///Both said what the box under them already said - "Background" over a box reading No Background, "File
    ///Type" over one reading STL - and the bar stopped heading anything the day Tool, Grid, Join and View
    ///lost theirs.
    ///
    test('neither control is headed by a word', async ({ page }) => {
        //Both are here, so an empty list below is the words being gone rather than the bar being.
        await expect(page.locator('#backgroundPicker')).toBeVisible();
        await expect(page.locator('#modelFormat')).toBeVisible();

        //Nothing in this bar is headed, which is the whole statement - the two were the last that were.
        await expect(page.locator('.toolbarLabel')).toHaveCount(0);

        //Still named for anything not reading the screen, which is what the words were doing for them.
        await expect(page.locator('#backgroundPicker')).toHaveAttribute('aria-label', 'Background');
        await expect(page.locator('#modelFormat')).toHaveAttribute('aria-label', '3D file type');
    });

    ///And the backdrop picker is the height of the row it is in, which a bare select was not.
    test('the backdrop picker is a bar-height control', async ({ page }) => {
        const heights = await page.evaluate(() => {
            const of = (selector) => Math.round(document.querySelector(selector).getBoundingClientRect().height);

            return [of('#backgroundPicker'), of('#downloadModel')];
        });

        expect(heights[0]).toBe(heights[1]);
    });
});

///
///Which backdrop, shown rather than named.
///
///"Future Room" and "Neon" are what somebody called two photographs, and no arrangement of words says what
///one looks like - which is the whole question the control is asking. So the menu carries a picture of
///each, generated once from the backdrop and committed beside it: the backdrops themselves run to nine
///megabytes, and a menu that showed those would fetch thirteen to open.
///
test.describe('the backdrop picker', () => {
    test('it opens a menu with a picture of each backdrop', async ({ page }) => {
        await expect(page.locator('#backgroundMenu')).toHaveCount(0);

        await page.locator('#backgroundPicker').click();

        await expect(page.locator('#backgroundMenu')).toBeVisible();

        const rows = page.locator('#backgroundMenu button');

        await expect(rows).toHaveCount(5);

        //Every one carries a swatch: four photographs and the plain scene's own gray.
        await expect(page.locator('#backgroundMenu .backgroundSwatch')).toHaveCount(5);
        await expect(page.locator('#backgroundMenu img.backgroundSwatch')).toHaveCount(4);

        //And the names are kept, since they are what a returning user recognizes.
        const names = await page.locator('#backgroundMenu .backgroundName').allTextContents();

        expect(names.map(one => one.trim()))
            .toEqual(['No Background', 'White', 'White Room', 'Future Room', 'Neon']);
    });

    ///
    ///The pictures are the small ones, not the backdrops.
    ///
    ///The whole reason they exist. Asserted by what is fetched rather than by the path in the markup: a
    ///thumbnail that pointed at the full image would still look right and would still cost the megabytes.
    ///
    test('opening the menu fetches kilobytes rather than megabytes', async ({ page }) => {
        const fetched = [];

        page.on('response', response => {
            if (response.url().includes('/resources/Images/Background/'))
                fetched.push(response.url());
        });

        await page.locator('#backgroundPicker').click();

        await expect(page.locator('#backgroundMenu')).toBeVisible();

        //Give the images a moment to be asked for.
        await expect.poll(async () => fetched.length, { timeout: 15000 }).toBeGreaterThan(3);

        //Every one of them out of the preview folder, and not one of the backdrops themselves.
        for (const url of fetched)
            expect(url).toContain('/preview/');
    });

    ///Choosing one puts it on the scene and closes the menu, which is what a menu is for.
    test('choosing one dresses the scene', async ({ page }) => {
        await captureScene(page);

        await selectBackground(page, 'background1.jpg');

        await expect.poll(async () => page.evaluate(() => window.__gdsScene?.background != null), { timeout: 30000 })
            .toBe(true);
    });

    ///
    ///A press somewhere else puts it away, and does not choose anything on the way past.
    ///
    ///Through the listener the 2D view's own menus share. The bar is the shell's, so the dismissal has to
    ///travel out to the shell before it is real - which is the thing that was silently missing when this
    ///menu first would not open at all.
    ///
    ///
    ///And it goes when the pointer leaves, the way every other panel in the bar does.
    ///
    ///The reachable half is the part that is easy to get wrong: the menu hangs twelve pixels below the
    ///column, and if that twelve is a gap rather than part of the menu's own box then crossing it *is*
    ///leaving, and the menu shuts on the way in. Stepped, or the move is one event at the destination.
    ///
    test('the pointer can reach it, and it goes when the pointer leaves', async ({ page }) => {
        const button = await page.locator('#backgroundPicker').boundingBox();

        await page.locator('#backgroundPicker').hover();

        await expect(page.locator('#backgroundMenu')).toBeVisible();

        const menu = await page.locator('#backgroundMenu').boundingBox();

        await page.mouse.move(button.x + (button.width / 2), menu.y + 30, { steps: 20 });

        await expect(page.locator('#backgroundMenu')).toBeVisible();

        await page.mouse.move(4, 4);

        await expect(page.locator('#backgroundMenu')).toHaveCount(0);
    });

    test('a press outside puts it away', async ({ page }) => {
        //The scene has to be reachable before it can be asked what it is wearing.
        await captureScene(page);

        await page.locator('#backgroundPicker').click();

        await expect(page.locator('#backgroundMenu')).toBeVisible();

        await page.locator('#layerSidebar').click({ position: { x: 5, y: 5 } });

        await expect(page.locator('#backgroundMenu')).toHaveCount(0);

        //And nothing was dressed on the way past.
        expect(await page.evaluate(() => window.__gdsScene?.background)).toBeNull();
    });
});
