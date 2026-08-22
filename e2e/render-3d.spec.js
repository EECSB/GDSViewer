//The 3D view: the WebGL scene is built, labels come through as billboards, and leaving and returning
//does not pile up renderers. That last one is the reason this spec exists - a leaked WebGL context per
//visit is invisible until the browser starts dropping the oldest, and nothing but a real browser can
//show it.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, threeCounts, MOSFET, MOSFET_POLYGONS, MOSFET_MESHES, MOSFET_LABELS, shapeCount, shapesDrawn, clearLayerNames } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
});

///
///One mesh per merged outline, not per drawn polygon.
///
///The 3D view merges each layer before extruding, because two slabs at the same height fight over which
///is in front. Mosfet.gds has two shapes on 66/20 that meet along an edge, so eighteen boundaries come out
///as seventeen slabs covering exactly the same ground.
///
test('extrudes every element and draws every label', async ({ page }) => {
    const scene = await threeCounts(page);

    expect(scene.meshes).toBe(MOSFET_MESHES);
    expect(scene.sprites).toBe(MOSFET_LABELS);
});

///
///An extruded shape covers the same ground as the outline it came from.
///
///The 3D view built its outline by calling moveTo for every point and then lineTo the same point, which
///dropped the first one. A closed ring survived that - its repeated last point closes the same cycle - but
///an outline that is not explicitly closed, which is what PathOutline returns, lost a corner and a
///four-corner rectangle came out as a triangle. Comparing against the 2D view is the check that catches
///it, since both draw the same flattened layout and only one of them was wrong.
///
test('every extruded shape covers the same ground as the 2D outline of it', async ({ page }) => {
    //A standard cell rather than Mosfet.gds, which cannot show this: every shape in that file is a closed
    //boundary, and a closed ring survives losing its first point because its repeated last point closes
    //the same cycle. What breaks is an outline that is not explicitly closed - what PathOutline returns -
    //and for that a cell with paths in it is needed.
    await gotoExample(page, 'sky130_fd_sc_hd__a211oi_1', '2d');

    //
    //**Cleared first, so both views draw the same set of shapes.**
    //
    //The bundled sky130 map is laid over an example as it opens, and a layer that map says nothing about
    //is left out of the 3D view rather than placed at a height nobody measured - see the last test in this
    //file. That is three layers on this cell, so the two sides would differ by exactly those and this
    //would fail for a reason that has nothing to do with the dropped corner it exists to catch.
    //
    await clearLayerNames(page);

    await expect.poll(async () => shapeCount(page)).toBeGreaterThan(50);

    //The 2D view's shapes, built from the same layout by a different path.
    //
    //Corners rather than a bounding box: three corners of a rectangle have the same bounding box as four
    //of them, so a dropped corner is invisible to one. The corner set is what tells a rectangle from the
    //triangle it becomes.
    const flat = (await shapesDrawn(page)).map(shape => {
        const corners = shape.points.map(point => point.map(Math.round).join(','));

        return [...new Set(corners)].sort().join(' ');
    }).sort();

    await selectView(page, 'View3D');
    await expect(page.locator('#container canvas')).toBeVisible();

    const extruded = await page.evaluate(async () => {
        const THREE = await import('three');
        const added = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects)
                added.push(object);

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));
        THREE.Object3D.prototype.add = original;

        return added.filter(object => object.isMesh).map(mesh => {
            const position = mesh.geometry.attributes.position;
            const corners = new Set();

            //Every distinct XY the extrusion actually occupies. The bevel insets by a hair, so these are
            //rounded to whole layout units before being compared with the outline they came from.
            for (let i = 0; i < position.count; i++)
                corners.add(`${Math.round(position.getX(i))},${Math.round(position.getY(i))}`);

            return [...corners].sort().join(' ');
        }).sort();
    });

    expect(extruded).toEqual(flat);
});

test('labels hang from the point their justification asks for', async ({ page }) => {
    const scene = await threeCounts(page);

    //Mosfet.gds carries no PRESENTATION record, so its labels take the format's default of left and
    //top - which on a sprite is the top-left corner it hangs from.
    for (const center of scene.spriteCenters)
        expect(center).toEqual([0, 1]);
});

///
///A label sits above the layer it names rather than inside it.
///
///An extrusion runs from z 0 to z depth, and the tip that lays the layout on its back maps local +Z onto
///world -Y - so the mesh is placed at offset + depth and the slab occupies offset to offset + depth, which
///is where the process puts it. It used to hang *below* the plane it was drawn on, and the label was
///anchored to what was then the top face and is now the buried one. That put every label inside the shape
///it names, which a billboard hides well: it is still drawn, still upright, still legible from some angles,
///and only wrong from the ones where the slab is between it and the camera.
///
///Asserted against the top face worked out from the layout, not from the placement code - the check is
///"above the slab", so it would still hold if the clearance were retuned, and it fails outright if the
///anchor goes back to the buried face.
///
test('labels sit above the layer they name, not inside it', async ({ page }) => {
    //Installed before the app loads, through a property whose getter hands back a fresh wrapper. Replacing
    //window.drawInterOp after the fact does nothing: .NET resolves a JS function by name once and holds
    //the reference, so a later assignment is never seen. Measured, after the obvious version of this hook
    //recorded zero calls while three sprites were being added.
    await page.addInitScript(() => {
        let real = null;
        window.__labels = [];

        Object.defineProperty(window, 'drawInterOp', {
            configurable: true,
            get() {
                return function (data) {
                    window.__labels = (data && data.labels) || [];

                    return real.apply(this, arguments);
                };
            },
            set(fn) { real = fn; }
        });
    });

    await gotoExample(page, MOSFET, '3d');

    const placed = await page.evaluate(async () => {
        const THREE = await import('three');
        const sprites = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isSprite)
                    sprites.push(object);
            }

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));

        THREE.Object3D.prototype.add = original;

        //The same tip the extrusions get. A layer's top face at a given layout point is that point's own
        //Y carried through the rotation, lifted by the layer's place in the stack.
        //
        //Sprite i against label i, which only holds if the two came from the same redraw - so the counts
        //are returned and asserted rather than trusted. A stale pairing here would still produce numbers
        //that look about right, which is the kind of test that passes for the wrong reason.
        return {
            sprites: sprites.length,
            labels: window.__labels.map((label, i) => {
                const sprite = sprites[i];

                return {
                    //The bottom edge of the text, not the point it hangs from. Those are the same thing
                    //only for a label justified to the bottom - the format's default is the top, where
                    //the anchor is the sprite's *upper* edge and the glyphs are a whole height below it.
                    //Measuring the anchor is what let the first version of this pass while the text was
                    //still buried.
                    lowestGlyph: sprite.position.y - (sprite.center.y * sprite.scale.y),

                    //
                    //The top face of the slab this label names, worked out from the layout rather than
                    //read back from the placement code - so the check stays "above the slab" even if the
                    //clearance is retuned, and fails outright if the anchor goes back to a buried face.
                    //
                    //**Its offset plus its thickness.** A layer starts at its offset and is thick upward,
                    //and the meshes are placed to match - see ThreeInterop's mesh.position. This read
                    //`label.offset` alone, from when a slab hung *below* the plane it was drawn on and its
                    //offset was therefore its top.
                    //
                    //**And no term for the layout's own Y.** It carried `label.y * Math.cos(1.5)`, which
                    //was the tilt leaking in: the layout used to be laid back by 1.5 radians rather than a
                    //right angle, so a little of its Y showed up in world Y. It is a right angle now, that
                    //cosine is nought, and the term was a stale constant this test went on multiplying by.
                    //
                    top: label.offset + label.depth,
                    depth: label.depth
                };
            })
        };
    });

    expect(placed.sprites).toBe(MOSFET_LABELS);
    expect(placed.labels.length).toBe(MOSFET_LABELS);

    for (const label of placed.labels) {
        //Clear of the surface...
        expect(label.lowestGlyph).toBeGreaterThan(label.top);

        //...and still sitting on it, rather than floating off towards the layer above.
        expect(label.lowestGlyph - label.top).toBeLessThanOrEqual(label.depth / 2);
    }
});

test('the VR and AR buttons are offered', async ({ page }) => {
    //They report NOT SUPPORTED on a machine without a headset, which is still the addon having loaded
    //and run - the import map resolving three/addons is what this actually covers. By the ids three.js
    //gives them rather than by text, since neither label is the same across states.
    //
    //Where they sit is three-controls.spec.js's business; this is only that they exist at all.
    await expect(page.locator('#VRButton')).toBeVisible();
    await expect(page.locator('#ARButton')).toBeVisible();

    await expect(page.locator('#VRButton')).toHaveText(/VR/);
    await expect(page.locator('#ARButton')).toHaveText(/AR/);
});

test('leaving and returning does not pile up renderers or listeners', async ({ page }) => {
    const counts = await page.evaluate(async () => {
        let contexts = 0;
        const getContext = HTMLCanvasElement.prototype.getContext;
        HTMLCanvasElement.prototype.getContext = function (type, ...rest) {
            if (String(type).startsWith('webgl'))
                contexts++;

            return getContext.call(this, type, ...rest);
        };

        //Observers rather than window listeners.
        //
        //This counted 'resize' on window until the views started watching their own containers instead -
        //at which point it was counting something nothing adds any more, and passed whatever happened.
        //A ResizeObserver leaks the same way a listener did: one per visit, each still measuring an
        //element that left the document, each firing on every layout for the rest of the session.
        let observed = 0;
        let disconnected = 0;
        const observe = ResizeObserver.prototype.observe;
        const disconnect = ResizeObserver.prototype.disconnect;
        ResizeObserver.prototype.observe = function (...rest) {
            observed++;

            return observe.apply(this, rest);
        };
        ResizeObserver.prototype.disconnect = function (...rest) {
            disconnected++;

            return disconnect.apply(this, rest);
        };

        //
        //Switching view from inside the page, because the counting above has to stay in this one call.
        //
        //The switch is three boxes now - a select before that, then a button and a menu - so choosing is
        //one press on the box that names the view.
        //
        const choose = (value) => {
            const box = document.querySelector('#viewPick [data-view="' + value + '"]');

            if (box !== null)
                box.click();
        };

        //Five round trips out of the view and back.
        for (let visit = 0; visit < 5; visit++) {
            choose('View2DSvg');
            await new Promise(resolve => setTimeout(resolve, 250));
            choose('View3D');
            await new Promise(resolve => setTimeout(resolve, 250));
        }

        ResizeObserver.prototype.observe = observe;
        ResizeObserver.prototype.disconnect = disconnect;

        return {
            contexts,
            netObservers: observed - disconnected,
            canvases: document.querySelectorAll('#container canvas').length
        };
    });

    //One live canvas however many times the view was entered, and at most the two observers that should
    //be alive at the end of this - the 3D view's, and the 2D view's from the last visit to it. Without
    //the teardown it was one canvas, one context and one observer per visit, and a browser only allows a
    //handful of contexts.
    expect(counts.canvases).toBe(1);
    expect(counts.netObservers).toBeLessThanOrEqual(2);
    expect(counts.contexts).toBeGreaterThan(0);
});

test('the scene survives being left and returned to', async ({ page }) => {
    await selectView(page, 'View2DSvg');
    await selectView(page, 'View3D');

    await expect(page.locator('#container canvas')).toBeVisible();

    const scene = await threeCounts(page);

    expect(scene.meshes).toBe(MOSFET_MESHES);
    expect(scene.sprites).toBe(MOSFET_LABELS);
});

///
///**The spacing slider still moves the layers, now that the merge behind it is cached.**
///
///`Viewer3D` used to re-flatten the library and re-run `Booleans.MergeByLayer` on every step of this
///slider - measured at 158 ms per step at twenty thousand elements and 5.2 s at half a million, for a
///change that moves no geometry at all. A `flattenedFrom` guard, the one the 2D view already had, removed
///that.
///
///The risk of a cache is caching too much, and this is the shape it would take: the slider would still
///redraw and the slabs would stop moving. So what is checked is that they move, and by the amount asked
///for - the layers are held by reference precisely so that writing new heights onto them is enough.
///
test('the spacing slider still moves the layers apart', async ({ page }) => {
    const heights = async (spacing) => page.evaluate(async (to) => {
        const THREE = await import('three');
        const meshes = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh)
                    meshes.push(object);
            }

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');
        slider.value = String(to);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));

        THREE.Object3D.prototype.add = original;

        //How far apart the highest and lowest slabs sit. One mesh's own height says nothing on its own -
        //the scene is recentered - where the spread across the stack is exactly what the slider sets.
        const centers = meshes.map(mesh => {
            mesh.geometry.computeBoundingBox();

            return mesh.geometry.boundingBox.getCenter(new THREE.Vector3()).y + mesh.position.y;
        });

        return { meshes: meshes.length, spread: Math.max(...centers) - Math.min(...centers) };
    }, spacing);

    //
    //On bare numbers, because this compares a resting stack against a spread one.
    //
    //The shipped mapping now carries sky130's real heights, and those already span seven microns before
    //the slider is touched - so the ratio between the two spreads is dominated by the wafer rather than by
    //the control being tested. Cleared, the resting stack is the even one this was written against.
    //
    await clearLayerNames(page);

    const close = await heights(60);
    const apart = await heights(600);

    //The same scene both times, which is what says the two spreads are comparable at all.
    expect(close.meshes).toBe(MOSFET_MESHES);
    expect(apart.meshes).toBe(MOSFET_MESHES);

    expect(apart.spread).toBeGreaterThan(close.spread * 3);
});


///
///**Nothing is drawn above the top of the wafer, and nothing invents a height to get there.**
///
///This is the regression a person saw and no test could. `sky130_fd_sc_hd__nand2_1` carries twenty-two
///layer/datatype pairs and the shipped map covers nineteen; the three it does not are `areaid.standardc`,
///`text` and the cell outline at `236/0`, none of which is on a wafer at all. For one round those three -
///and five more the map had simply forgotten - were parked above the top of everything measured, a step
///apart. Four of the eight are drawn to the whole cell, so the layout grew a ladder of cell-sized plates
///hanging over it with sky above and below each one.
///
///A layer the map says nothing about is left out rather than placed - see Viewer3D.stacked - and its
///labels go with it, which is why `text` contributes no billboard either.
///
///**Measured against the mapping itself rather than against written-down numbers.** A row the map names
///has a height from it; a row still showing its bare layer/datatype has none. The first set is what the
///scene must be built at and the second is what must be absent, and both are read off the same redraw -
///so this says what it means at whatever spacing the slider happens to be at.
///
test('a layer the process table says nothing about is left out rather than hung above the stack', async ({ page }) => {
    await gotoExample(page, 'sky130_fd_sc_hd__nand2_1', '3d');

    await expect(page.locator('#container canvas')).toBeVisible();

    //
    //The three pairs in this cell that sky130 has no film for, by number rather than by whether the map
    //happens to name them.
    //
    //**Naming is not the test, having a height is.** `text` is on 83/44 and the map does name it - there is
    //no wafer to put it on, so it is named and given nothing, which is exactly the case this asserts about.
    //Reading the sidebar for a name would have counted it as mapped and then failed on it not being drawn.
    //
    const NOT_ON_THE_WAFER = ['81/4', '83/44', '236/0'];

    const seen = await page.evaluate(async (markers) => {
        const THREE = await import('three');
        const added = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects)
                added.push(object);

            return original.apply(this, objects);
        };

        //Every layer's height for this redraw, in the sidebar's own order - which is where the row labels
        //below are read from too, so the two line up by index.
        let offsets = [];

        const restack = window.restackLayers;
        window.restackLayers = function (to) {
            offsets = to;

            return restack(to);
        };

        //One nudge, and the whole scene comes back. Two would not: the settled redraw builds what the
        //scene does not already hold, so a second reading catches a fraction of it.
        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 1500));

        THREE.Object3D.prototype.add = original;
        window.restackLayers = restack;

        //Every row carries its pair, whether or not it also carries a name: "3.nwell (64/20)" and "16.81/4"
        //both end in one. Built without an escape so nothing between here and the file can eat it.
        const pair = new RegExp('[0-9]+/[0-9]+(?![0-9/])');

        const rows = Array.from(document.querySelectorAll('#layerSidebar .layerRow'))
            .map((row, at) => {
                const found = ((row.textContent || '').match(pair) || [''])[0];

                return { onTheWafer: !markers.includes(found), height: offsets[at] };
            });

        const heights = added
            .filter(object => object.isMesh || object.isSprite)
            .map(object => object.userData.stackOffset);

        const distinct = (numbers) => [...new Set(numbers)].sort((a, b) => a - b);

        return {
            drawn: distinct(heights),
            wafer: distinct(rows.filter(row => row.onTheWafer).map(row => row.height)),
            markers: distinct(rows.filter(row => !row.onTheWafer).map(row => row.height))
        };
    }, NOT_ON_THE_WAFER);

    //All three are in the file and each is at a height of its own, or the assertions below are about a set
    //that happens to be empty.
    expect(seen.markers).toHaveLength(3);
    expect(seen.drawn.length).toBeGreaterThan(0);

    //Every slab and every billboard sits at a height the mapping gave, and nothing sits anywhere else. This
    //is also what says the mapping arrived whole: a row it had forgotten would leave a wafer layer here
    //with an index height and no geometry at it.
    expect(seen.drawn).toEqual(seen.wafer);

    //Said the other way round, which is the failure this was written for: none of the three gets a slab.
    for (const height of seen.markers)
        expect(seen.drawn).not.toContain(height);

    //And the top of the scene is the top metal this cell reaches, not something hung above it.
    expect(Math.max(...seen.drawn)).toBe(Math.max(...seen.wafer));
});

///
///**A press on a layer row flashes that layer, and only that one.**
///
///Which slab is which is the one question this view cannot answer on its own: nine of them seen at an
///angle, several the same size, some behind others, and a list of names beside them that says nothing
///about where any of them is. In 2D the same press picks the layer to draw on; here there is nothing to
///draw, so the press was doing nothing at all.
///
///Sampled off the materials rather than off the picture. What a pulse looks like is a brightness that
///comes and goes, and a screenshot at one moment cannot tell "mid-flash" from "this layer is just pale" -
///whereas the emissive channel is the thing being animated, and it starts and ends at exactly nothing.
///
test('a press on a layer row flashes that layer in the stack', async ({ page }) => {
    await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBeGreaterThan(3);

    const flashed = await page.evaluate(async () => {
        const THREE = await import('three');
        const added = [];

        //The same hook threeCounts uses: meshes are not reachable from outside, so they are caught on the
        //way into the scene. A nudge of the spacing slider is the cheapest way to ask for that rebuild.
        const original = THREE.Object3D.prototype.add;

        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects)
                added.push(object);

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');

        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));

        THREE.Object3D.prototype.add = original;

        const meshes = added.filter(object => object.isMesh && object.material != null && object.material.emissive != null);

        //A row in the middle of the stack, so there are layers either side of it to stay dark.
        const at = 2;
        const mine = meshes.filter(mesh => mesh.userData.stackAt === at);
        const others = meshes.filter(mesh => mesh.userData.stackAt !== at);

        const before = mine.map(mesh => mesh.material.emissive.getHex());

        document.querySelectorAll('.layerRow')[at].click();

        let peak = 0;
        let peakElsewhere = 0;

        //Across the run, a frame at a time: the brightness is a curve, so one reading could land anywhere
        //on it - including at either end, where it is nothing by design.
        for (let frame = 0; frame < 90; frame++) {
            await new Promise(resolve => requestAnimationFrame(resolve));

            for (const mesh of mine)
                peak = Math.max(peak, mesh.material.emissive.r);

            for (const mesh of others)
                peakElsewhere = Math.max(peakElsewhere, mesh.material.emissive.r);
        }

        //And well past the end of it.
        await new Promise(resolve => setTimeout(resolve, 1400));

        return {
            slabs: mine.length,
            elsewhere: others.length,
            peak: peak,
            peakElsewhere: peakElsewhere,
            settled: mine.map(mesh => mesh.material.emissive.getHex()),
            before: before
        };
    });

    //The fixture has to have something on that layer and something on another, or this proves nothing.
    expect(flashed.slabs).toBeGreaterThan(0);
    expect(flashed.elsewhere).toBeGreaterThan(0);

    //It lit up.
    expect(flashed.peak).toBeGreaterThan(0.2);

    //Nothing else did.
    expect(flashed.peakElsewhere).toBe(0);

    //And it went back to the color it was, rather than being left bright.
    expect(flashed.settled).toEqual(flashed.before);
});

///Nothing in this view may be edited from the layer list, since neither edit is a thing it can show.
test('the layer list offers no adding or removing in 3D', async ({ page }) => {
    await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBeGreaterThan(0);

    await expect(page.locator('.layerRemove')).toHaveCount(0);
    await expect(page.locator('#addLayer')).toHaveCount(0);

    //
    //And nothing else that changes a layer either.
    //
    //The gear is what sets a color and types a name, and a color lives in the layermap once it is set - so
    //it is an edit, and this view offers none. What is left is a press that flashes a slab and a box that
    //hides one, neither of which changes anything.
    //
    await expect(page.locator('.layerSettingsButton')).toHaveCount(0);

    //The name is a readout in every view now - see the layerName span - so pressing it opens nothing here
    //and nothing in 2D either. Renaming is behind the gear, which is the control this view does not have.
    const named = page.locator('.layerName').first();

    await named.click();

    await expect(page.locator('.layerSettingsPopup, .layerNameBox')).toHaveCount(0);

    //Still a list of what is in the file, with its checkboxes - which is what it is for here.
    await expect(page.locator('.layerEyeButton').first()).toBeVisible();
});

///
///**The press says something where the press was, as well as out in the stack.**
///
///The slab it flashes can be behind another one, off the side of the view, scrolled past, or switched off
///entirely - and a press that appears to do nothing reads as a press that did not register. The row is the
///one part of this that is certainly in front of you, because it is what was just clicked.
///
///**Sampled for the tint rather than for the class**, because the class is only the trigger: a keyframe
///animation that is declared and never runs leaves the class sitting on the element looking perfectly
///correct. Walked a frame at a time for the same reason the slab test does it - the tint is a curve that
///starts and ends at nothing, so a single reading could land on either end of it and prove neither.
///
///The row is re-read each frame rather than held, because the list re-renders while this runs and a handle
///to the node it had before would be measuring something no longer on the page.
///
test('a press on a layer row flashes that row', async ({ page }) => {
    await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBeGreaterThan(3);

    const flash = await page.evaluate(async () => {
        const rowAt = () => document.querySelectorAll('.layerRow')[2];

        rowAt().click();

        let strongest = 0;
        let marked = false;

        for (let frame = 0; frame < 120; frame++) {
            await new Promise(resolve => requestAnimationFrame(resolve));

            const row = rowAt();

            if (row.classList.contains('layerRowPulsing'))
                marked = true;

            const parts = getComputedStyle(row).backgroundColor.match(/[\d.]+/g);

            if (parts !== null && parts.length === 4)
                strongest = Math.max(strongest, Number(parts[3]));
        }

        return { strongest: strongest, marked: marked };
    });

    expect(flash.marked).toBe(true);

    //Well clear of the 0.06 the row carries under the pointer, so a hover could not have produced it.
    expect(flash.strongest).toBeGreaterThan(0.15);

    //And it goes out, rather than leaving one row of the list marked from then on.
    await expect(page.locator('.layerRow').nth(2)).not.toHaveClass(/layerRowPulsing/, { timeout: 5000 });
});
