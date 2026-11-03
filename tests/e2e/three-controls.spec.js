//The 3D view's own controls: the QR popup, the scene backgrounds, and the cinematic camera orbit. None of
//these draw geometry, so nothing else in the suite touches them - and all three run entirely in JS the
//build cannot check.
const { test, expect } = require('@playwright/test');
const { gotoExample, captureScene, cameraPosition, openViewSettings, selectBackground, MOSFET } = require('./helpers');

//
//With the page's margins: the view widths below - 543, 747 - and the QR code's share of the view were measured in
//them. Full screen, the default since 2026-11-03, gives the view room enough that the row never has to stack.
//
test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '3d', false, false);

    await expect(page.locator('#container canvas')).toBeVisible();
});

///
///The foot of the view - the headset row, and the axis indicator in its corner - as a picture of whether they collide.
///
///Only the buttons drawn: Open On Phone is in the markup on a phone and not shown. The indicator is drawn into the
///canvas rather than laid over it, so its square is where the interop draws it: 88 pixels, 12 in from the corner -
///AXES_VIEWPORT and AXES_MARGIN in ThreeInterop.js. Inside means within the view's own left and right edges, since a
///row wider than the view is the other way these went wrong. It was Admire, in the other corner, the row had to clear
///until that went into the view's settings.
///
async function footOfTheView(page) {
    return page.evaluate(() => {
        const view = document.querySelector('#container').getBoundingClientRect();
        const boxes = [...document.querySelectorAll('#xrButtons > *')]
            .map(control => control.getBoundingClientRect())
            .filter(box => box.width > 0);

        const indicator = { left: view.left + 12, right: view.left + 100, top: view.bottom - 100, bottom: view.bottom - 12 };
        const all = [...boxes, indicator];

        let overlapping = false;

        for (let i = 0; i < all.length; i++) {
            for (let j = i + 1; j < all.length; j++) {
                const a = all[i];
                const b = all[j];

                if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom)
                    overlapping = true;
            }
        }

        return {
            overlapping: overlapping,
            arrangement: getComputedStyle(document.getElementById('xrButtons')).flexDirection,
            inside: boxes.every(box => box.left >= view.left && box.right <= view.right)
        };
    });
}

test('the QR popup carries the file and the view, though the address no longer names the file', async ({ page }) => {
    //The link that opened it has let go of its file= by now, so the code cannot be copying it off the address.
    await expect.poll(() => new URL(page.url()).searchParams.get('file'), { timeout: 20000 }).toBeNull();

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

    //
    //Each row's run of dark modules is a stretch of one path, so what is counted is the runs.
    //
    //**And the code is the whole of its box.** Its modules were a whole number of units each in a 300-unit box, so a
    //code 53 across was 265 of the 300 and the rest a white band down the right and along the bottom - the code off
    //center in its popup. The finder squares sit in three corners, so a box the code fills is one its drawing
    //reaches from edge to edge.
    //
    const drawn = await qr.evaluate(svg => {
        const path = svg.querySelector('path');
        const reach = path.getBBox();

        return {
            runs: (path.getAttribute('d').match(/M/g) || []).length,
            box: svg.viewBox.baseVal.width + ' x ' + svg.viewBox.baseVal.height,
            reach: reach.x + ',' + reach.y + ' ' + reach.width + ' x ' + reach.height
        };
    });

    expect(drawn.runs).toBeGreaterThan(50);
    expect(drawn.reach).toBe('0,0 ' + drawn.box);

    //And what a phone scanning it opens: this file, in this view.
    const link = new URL(await page.locator('.svgContainer').getAttribute('data-link'));

    expect(link.searchParams.get('file')).toBe(MOSFET);
    expect(link.searchParams.get('view')).toBe('3d');
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
///are the same kind of thing. They are one ordinary column gutter from Open, Save and Compare, because those
///bring files in too - wider than the gap inside the pair, and nothing like the fence past them. And the fence
///goes after Examples, where the bar stops being about getting a file on screen.
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

            //And Compare after it, the last of the file controls.
            compare: box('#compareButton'),

            history: box('#historyButton'),
            examples: box('#examplesButton'),
            view: box('#viewPick')
        };
    });

    //In that order along the bar.
    expect(boxes.compare.left).toBeGreaterThanOrEqual(boxes.downloadEnds.right);
    expect(boxes.history.left).toBeGreaterThanOrEqual(boxes.compare.right);
    expect(boxes.examples.left).toBeGreaterThanOrEqual(boxes.history.right);

    const insideTheFileControls = boxes.downloadStarts.left - boxes.open.right;
    const insideThePair = boxes.examples.left - boxes.history.right;

    //Compare is one of the file controls, a file control's gap from the download.
    expect(Math.round(boxes.compare.left - boxes.downloadEnds.right)).toBe(Math.round(insideTheFileControls));

    const betweenThem = boxes.history.left - boxes.compare.right;
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

test('Admire moves the camera, and switching it off stops it', async ({ page }) => {
    await captureScene(page);

    const before = await cameraPosition(page);

    expect(before).not.toBeNull();

    //A switch in the view's settings, under the cube.
    await openViewSettings(page);

    const admire = page.locator('#viewAdmire');

    await admire.check();

    //Driven from the render loop, so it takes frames rather than a call to move.
    await expect.poll(async () => {
        const now = await cameraPosition(page);

        return now.x !== before.x || now.y !== before.y || now.z !== before.z;
    }, { timeout: 15000 }).toBe(true);

    await admire.uncheck();

    //Settled once toggled off: two readings a moment apart match.
    await page.waitForTimeout(500);

    const stopped = await cameraPosition(page);

    await page.waitForTimeout(500);

    expect(await cameraPosition(page)).toEqual(stopped);
});

test('the layer-spacing slider spreads the stack', async ({ page }) => {
    await captureScene(page);

    //Read off the slabs' own offsets, which is what the slider actually changes. The slabs are the meshes
    //carrying outlines - a layer's labels are a mesh too.
    const spreadOf = () => page.evaluate(() => {
        const meshes = window.__gdsScene?.children
            .flatMap(child => child.children ?? [])
            .filter(child => child.isMesh && child.userData.outlines != null) ?? [];

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

        //The slabs, which are the meshes carrying outlines - a layer's labels are a mesh too.
        const meshes = scene.children
            .flatMap(child => child.children ?? [])
            .filter(child => child.isMesh && child.userData.outlines != null);

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

    ///
    ///And the two that stay go up the side, rather than across the foot of the view over the axis indicator.
    ///
    ///Nothing three.js can say fits along the foot clear of the indicator at this width. What it says here is
    ///what a desktop Chromium gets, NOT SUPPORTED at 150 pixels; a real iPhone has no WebXR at all and gets WEBXR
    ///NOT AVAILABLE at 180 - so the buttons are relabeled the way three.js would and asked again, at the widest
    ///they ever are.
    ///
    test('the headset buttons go up the side instead of over the axis indicator', async ({ page }) => {
        await expect.poll(async () => page.locator('#xrButtons button:visible').count(), { timeout: 30000 })
            .toBe(2);

        await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'column', inside: true });

        await page.evaluate(() => {
            for (const button of document.querySelectorAll('#VRButton, #ARButton')) {
                button.textContent = 'WEBXR NOT AVAILABLE';
                button.style.width = '180px';
            }
        });

        await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'column', inside: true });
    });
});

///
///The same stack on a laptop, once the layer sidebar leaves the view too narrow for the row.
///
///At 1024 by 768 the view is 543 wide, and the three buttons a mouse gets would run 70 pixels over the axis
///indicator - which is why the rule asks how wide the view is rather than the window. At the 1280 by 720 the rest
///of this file runs at, the view is 747 and the row stays a row, which the tests on the row hold. (Not the 1400 the
///config's top-level `use` names: the project's Desktop Chrome brings a viewport of its own, and it wins.)
///
test('on a narrow laptop window the view controls stack rather than overlap', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });

    await expect.poll(async () => page.locator('#xrButtons button').count(), { timeout: 30000 }).toBe(3);

    await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'column', inside: true });
});

///
///And the labels decide it as much as the window does.
///
///With the view 747 wide, the row of NOT SUPPORTED buttons clears the axis indicator by 32 pixels, and the
///WEBXR NOT AVAILABLE a browser without WebXR gets does not: 180 a button is a row sixty pixels wider, which
///leaves it two and a half, inside the eight the row keeps off the indicator. A breakpoint cannot be right for
///both - 800 held the second and stacked the first here, which is what the tests on the row caught - so the row
///is measured, and relabeling the buttons the way three.js does is enough to stack it, and to put it back.
///
///**At 1279 wide, which is what puts the view at 747.** That was the 1280 this file runs at, until the editor
///was kept wide enough for the toolbar from 1280 up (see #mainAppContainer in Viewer.razor.css) and the view
///there grew past where either row could fail to fit. One pixel narrower is the widest window that floor does
///not reach, so the view is the width this was measured at, to the pixel.
///
test('the row stacks for labels that do not fit, and goes back for ones that do', async ({ page }) => {
    await page.setViewportSize({ width: 1279, height: 720 });

    await expect(page.locator('#VRButton')).toHaveText('VR NOT SUPPORTED', { timeout: 30000 });

    await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'row', inside: true });

    await page.evaluate(() => {
        for (const button of document.querySelectorAll('#VRButton, #ARButton')) {
            button.textContent = 'WEBXR NOT AVAILABLE';
            button.style.width = '180px';
        }
    });

    await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'column', inside: true });

    await page.evaluate(() => {
        for (const button of document.querySelectorAll('#VRButton, #ARButton')) {
            button.textContent = button.id.slice(0, 2) + ' NOT SUPPORTED';
            button.style.width = '150px';
        }
    });

    await expect.poll(async () => footOfTheView(page)).toEqual({ overlapping: false, arrangement: 'row', inside: true });
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
    ///The headset row wears the look three.js gives its own buttons, and Admire is not down here with it.
    ///
    ///Admire sat in the far corner of the canvas until 2026-10-17 - a word in a row of file formats and
    ///backgrounds before that. It is a switch in the view's settings now, under the cube, with the other things
    ///that change what this view shows; see view-settings.spec.js.
    ///
    test('the headset row wears the three.js buttons\' look, and Admire is not on the canvas', async ({ page }) => {
        await expect(page.locator('#admire')).toHaveCount(0);

        const style = await page.evaluate(() => {
            const looks = getComputedStyle(document.querySelector('#openOnPhone'));

            return { border: looks.borderTopWidth, color: looks.color, background: looks.backgroundColor };
        });

        //
        //A wash with an outline, which is what the three.js buttons beside it are.
        //
        //Not a solid panel: these sit over the layout, and filled slabs across the foot of the view are the
        //loudest thing on screen when what they are is three ways out of it. Drawn in ink rather than in white,
        //since the default scene is light gray.
        //
        expect(style.border).toBe('1px');
        expect(style.background).toMatch(/^rgba\(255, 255, 255, 0\./);

        const ink = style.color.match(/\d+/g).map(Number);

        expect(Math.max(...ink)).toBeLessThan(80);
    });

    ///
    ///No word over the file type.
    ///
    ///It said what the box under it already said - "File Type" over a box reading STL, as "Background" did over
    ///the backdrop's box when that was in the bar - and the bar stopped heading anything the day Tool, Grid, Join
    ///and View lost theirs.
    ///
    test('the file type is not headed by a word', async ({ page }) => {
        //Here, so an empty list below is the word being gone rather than the bar being.
        await expect(page.locator('#modelFormat')).toBeVisible();

        //Nothing in this bar is headed, which is the whole statement - the two this view had were the last that were.
        await expect(page.locator('.toolbarLabel')).toHaveCount(0);

        //Still named for anything not reading the screen, which is what the word was doing for it.
        await expect(page.locator('#modelFormat')).toHaveAttribute('aria-label', '3D file type');
    });
});

///
///Which backdrop, shown rather than named - in the 3D view's settings, which hang from the cube of the view switch.
///
///"Future Room" and "Neon" are what somebody called two photographs, and no arrangement of words says what
///one looks like - which is the whole question being asked. So each row carries a picture, generated once
///from the backdrop and committed beside it: the backdrops themselves run to nine megabytes, and a menu
///that showed those would fetch thirteen to open.
///
///They had a menu of their own behind a button along the bar, until the settings took them in and the
///cube took the settings.
///
test.describe('the backdrops, in the view\'s settings', () => {
    ///
    ///The pictures are the small ones, not the backdrops.
    ///
    ///The whole reason they exist. Asserted by what is fetched rather than by the path in the markup: a
    ///thumbnail that pointed at the full image would still look right and would still cost the megabytes.
    ///
    test('a picture each, and opening them fetches kilobytes rather than megabytes', async ({ page }) => {
        const fetched = [];

        page.on('response', response => {
            if (response.url().includes('/resources/Images/Background/'))
                fetched.push(response.url());
        });

        await openViewSettings(page);

        //Every row carries a swatch: four photographs and the plain scene's own gray.
        await expect(page.locator('#viewBackground .backgroundSwatch')).toHaveCount(5);
        await expect(page.locator('#viewBackground img.backgroundSwatch')).toHaveCount(4);

        //Give the images a moment to be asked for.
        await expect.poll(async () => fetched.length, { timeout: 15000 }).toBeGreaterThan(3);

        //Every one of them out of the preview folder, and not one of the backdrops themselves.
        for (const url of fetched)
            expect(url).toContain('/preview/');
    });

    ///Choosing one puts it on the scene.
    test('choosing one dresses the scene', async ({ page }) => {
        await captureScene(page);

        await selectBackground(page, 'background1.jpg');

        await expect.poll(async () => page.evaluate(() => window.__gdsScene?.background != null), { timeout: 30000 })
            .toBe(true);
    });

    ///
    ///The pointer arriving at the cube opens them, and they go when it leaves, the way every other panel in the
    ///bar does.
    ///
    ///The reachable half is the part that is easy to get wrong: the menu hangs twelve pixels below the
    ///cube, and if that twelve is a gap rather than part of the menu's own box then crossing it *is*
    ///leaving, and the menu shuts on the way in. Stepped, or the move is one event at the destination.
    ///
    test('the pointer can reach them, and they go when the pointer leaves', async ({ page }) => {
        const cube = await page.locator('#viewPick3D').boundingBox();

        await page.locator('#viewPick3D').hover();

        await expect(page.locator('#viewSettingsMenu')).toBeVisible();

        const backdrops = await page.locator('#viewBackground').boundingBox();

        await page.mouse.move(cube.x + (cube.width / 2), backdrops.y + 30, { steps: 30 });

        await expect(page.locator('#viewSettingsMenu')).toBeVisible();

        await page.mouse.move(4, 4);

        await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);
    });

    ///
    ///A press somewhere else puts them away, and chooses nothing on the way past.
    ///
    ///Through the listener the 2D view's own menus share. The menu is drawn by the shell, so the dismissal has
    ///to travel out to the shell before it is real - which is the thing that was silently missing when the
    ///backdrops' own menu first would not open at all.
    ///
    test('a press outside puts them away', async ({ page }) => {
        //The scene has to be reachable before it can be asked what it is wearing.
        await captureScene(page);

        await openViewSettings(page);

        await page.locator('#layerSidebar').click({ position: { x: 5, y: 5 } });

        await expect(page.locator('#viewSettingsMenu')).toHaveCount(0);

        //And nothing was dressed on the way past.
        expect(await page.evaluate(() => window.__gdsScene?.background)).toBeNull();
    });
});
