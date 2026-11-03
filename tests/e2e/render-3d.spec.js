//The 3D view: the WebGL scene is built, labels come through as billboards, and leaving and returning
//does not pile up renderers. That last one is the reason this spec exists - a leaked WebGL context per
//visit is invisible until the browser starts dropping the oldest, and nothing but a real browser can
//show it.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, threeCounts, stackOnScreen, onScreenSaid, MOSFET, MOSFET_POLYGONS, MOSFET_MESHES, MOSFET_OUTLINES, MOSFET_LABELS, shapeCount, shapesDrawn, bareNumbers } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET, '3d');

    await expect(page.locator('#container canvas')).toBeVisible();
});

///
///A slab per merged outline rather than per drawn polygon, and a mesh per layer rather than per slab.
///
///The 3D view merges each layer before extruding, because two slabs at the same height fight over which
///is in front. Mosfet.gds has two shapes on 66/20 that meet along an edge, so twenty boundaries come out
///as nineteen outlines covering exactly the same ground.
///
///**And each layer's outlines are one mesh.** A mesh is a draw call on every frame, and at one per outline
///the big designs put a hundred thousand of them in the scene. Both numbers are asserted: the meshes say the
///layers were batched, the outlines say nothing was lost in batching them.
///
///**So are each layer's labels**, for the same reason: at a sprite each, the RGB mixer's 19,005 labels took its
///frames from 17 ms to 43. Mosfet.gds carries its three on one layer, so they are one draw.
///
test('extrudes every element and draws every label', async ({ page }) => {
    const scene = await threeCounts(page);

    expect(scene.meshes).toBe(MOSFET_MESHES);
    expect(scene.outlines).toBe(MOSFET_OUTLINES);
    expect(scene.labels).toBe(MOSFET_LABELS);
    expect(scene.labelMeshes).toBe(1);
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
    //**On bare numbers first, so both views draw the same set of shapes.**
    //
    //The bundled sky130 map is laid over an example as it opens, and a layer that map says nothing about
    //is left out of the 3D view rather than placed at a height nobody measured - see the last test in this
    //file. That is three layers on this cell, so the two sides would differ by exactly those and this
    //would fail for a reason that has nothing to do with the dropped corner it exists to catch.
    //
    await bareNumbers(page);

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

        //
        //Every distinct XY each outline's extrusion actually occupies, rounded to whole layout units.
        //
        //**Split back into outlines first**, since a mesh is a whole layer: it records the vertex each of its
        //outlines starts at, and an outline runs to where the next one starts. Compared per layer instead, a
        //dropped corner could hide behind the same corner of a neighbor. The slabs only - a layer's labels are a
        //mesh too, and carry no outlines.
        //
        return added.filter(object => object.isMesh && object.userData.outlineStarts != null).flatMap(mesh => {
            const position = mesh.geometry.attributes.position;
            const starts = mesh.userData.outlineStarts;
            const outlines = [];

            for (let n = 0; n < starts.length; n++) {
                let end = position.count;

                if (n + 1 < starts.length)
                    end = starts[n + 1];

                const corners = new Set();

                for (let i = starts[n]; i < end; i++)
                    corners.add(`${Math.round(position.getX(i))},${Math.round(position.getY(i))}`);

                outlines.push([...corners].sort().join(' '));
            }

            return outlines;
        }).sort();
    });

    expect(extruded).toEqual(flat);
});

test('labels hang from the point their justification asks for', async ({ page }) => {
    const scene = await threeCounts(page);

    expect(scene.labelHangs).toHaveLength(MOSFET_LABELS);

    //Mosfet.gds carries no PRESENTATION record, so its labels take the format's default of left and
    //top - which is the top-left corner a label hangs from, in its own 0..1 space with Y up.
    for (const hang of scene.labelHangs)
        expect(hang).toEqual([0, 1]);
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
///**On a sky130 cell as well as on Mosfet.gds**, because Mosfet's labels are on a slab thick enough that the gap
///left above it - a fifth of its thickness - is taller than a label is. A label hung down from its anchor
///through that gap still clears the surface, so on Mosfet alone this passed with the hang never added at all.
///The cell's pins are on li1, which is a hundred nanometers.
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

    const placedLabels = async () => page.evaluate(async () => {
        const THREE = await import('three');
        const meshes = [];

        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh && object.userData.labels != null)
                    meshes.push(object);
            }

            return original.apply(this, objects);
        };

        const slider = document.getElementById('layerSpacing');
        slider.value = String(Number(slider.value) + 10);
        slider.dispatchEvent(new Event('input', { bubbles: true }));

        await new Promise(resolve => setTimeout(resolve, 800));

        THREE.Object3D.prototype.add = original;

        //
        //**Only the labels still in the scene**: a draw takes the last one's out before it puts its own in, so these
        //are the ones on screen. Counting every one added was counting draws - one still finishing from the open,
        //or the one the address's own settings ask for, landed in the window now and then and doubled the count.
        //
        const shown = meshes.filter(mesh => mesh.parent != null);

        //
        //Each label off the mesh that draws its layer's labels, against the labels of that layer the draw was
        //handed - by layer rather than one by one, since a layer's labels are drawn together. Both counts are
        //returned and asserted rather than trusted: a pairing with a stale redraw would still produce numbers
        //that look about right, which is the kind of test that passes for the wrong reason.
        //
        const labels = [];

        for (const mesh of shown) {
            const handed = window.__labels.find(label => label.at === mesh.userData.stackAt);
            const anchor = mesh.geometry.getAttribute('anchor');
            const size = mesh.geometry.getAttribute('size');
            const hang = mesh.geometry.getAttribute('hang');

            for (let i = 0; i < mesh.userData.labels; i++) {
                labels.push({
                    //The bottom edge of the text, not the point it hangs from. Those are the same thing
                    //only for a label justified to the bottom - the format's default is the top, where
                    //the anchor is the label's *upper* edge and the glyphs are a whole height below it.
                    //Measuring the anchor is what let the first version of this pass while the text was
                    //still buried. The mesh is lifted to the layer and the anchor sits in it.
                    lowestGlyph: mesh.position.y + anchor.getY(i) - (hang.getY(i) * size.getY(i)),

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
                    //cosine is zero, and the term was a stale constant this test went on multiplying by.
                    //
                    top: handed.offset + handed.depth,
                    depth: handed.depth,
                    height: size.getY(i)
                });
            }
        }

        return { handed: window.__labels.length, labels: labels };
    });

    await gotoExample(page, MOSFET, '3d');
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });

    const mosfet = await placedLabels();

    expect(mosfet.handed).toBe(MOSFET_LABELS);
    expect(mosfet.labels.length).toBe(MOSFET_LABELS);

    await gotoExample(page, 'sky130_fd_sc_hd__nand2_1', '3d');
    await expect(page.locator('#viewLoading')).toHaveCount(0, { timeout: 60000 });

    const cell = await placedLabels();

    expect(cell.labels.length).toBeGreaterThan(0);
    expect(cell.labels.length).toBe(cell.handed);

    //Some of them on a layer thin enough that the gap above it - a fifth of it, LABEL_CLEARANCE - would not
    //clear a label on its own, or the cell adds nothing to Mosfet.
    expect(cell.labels.some(label => label.depth / 5 < label.height)).toBe(true);

    for (const label of [...mosfet.labels, ...cell.labels]) {
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
    expect(scene.labels).toBe(MOSFET_LABELS);
});

///
///Counts the draws into the 3D view's canvas from here on, at the WebGL calls themselves - the one place a canvas
///that has left the page still shows anything. Kept on the context, so the count follows that canvas wherever it
///goes and a canvas built later has a count of its own.
///
async function watchDraws(page) {
    await page.evaluate(() => {
        const canvas = document.querySelector('#container canvas');

        window.watchedScene = canvas.getContext('webgl2') || canvas.getContext('webgl');

        for (const prototype of [WebGL2RenderingContext.prototype, WebGLRenderingContext.prototype]) {
            for (const name of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced']) {
                const draw = prototype[name];

                if (typeof draw !== 'function')
                    continue;

                prototype[name] = function (...rest) {
                    this.drawsCounted = (this.drawsCounted || 0) + 1;

                    return draw.apply(this, rest);
                };
            }
        }
    });
}

///
///How many draws the watched canvas took over what `doing` does and 400 ms after it.
///
///**Something has to be done**, for a count that is meant to be more than nothing: the scene is drawn only when
///something changes - see "the scene draws when something changes" below.
///
async function drawsAfter(page, doing) {
    const before = await page.evaluate(() => window.watchedScene.drawsCounted || 0);

    await doing();
    await page.waitForTimeout(400);

    return (await page.evaluate(() => window.watchedScene.drawsCounted || 0)) - before;
}

///Nothing done, for a count of what the scene draws by itself.
async function standingStill() {
}

///A press on the zoom-in button beside Center, which moves the camera in whichever view is open.
function zoomingIn(page) {
    return () => page.locator('#zoomIn').click();
}

///
///**Leaving the view stops the scene drawing.**
///
///The viewer was only taken down when the 3D view was next opened, so after a switch to 2D the whole scene went
///on being drawn every frame into a canvas no longer in the page - 840 draws a second on the RGB mixer - with its
///WebGL context held, until the next visit or a reload. Nothing on screen shows it, and the test above passes
///either way, since it counts what is alive once the view is back.
///
///The scene drawing before the switch is checked as well, or a count of nothing would pass for a counter that saw
///nothing - and the same press after it, which would have the old scene draw again if it were still listening.
///
test('leaving the view stops the scene drawing', async ({ page }) => {
    await watchDraws(page);

    await expect.poll(() => drawsAfter(page, zoomingIn(page))).toBeGreaterThan(0);

    await selectView(page, 'View2DSvg');

    await expect.poll(() => drawsAfter(page, zoomingIn(page))).toBe(0);

    //And the context given back rather than only left idle: a browser allows only a handful of live ones.
    await expect.poll(() => page.evaluate(() => window.watchedScene.isContextLost())).toBe(true);
});

///
///**The scene is drawn when something changes, and not while it stands still.**
///
///It was drawn on every frame the browser showed, still or not: 2,463 WebGL draws a second on the RGB mixer to
///put the same picture back. A frame is drawn now only when something asks for one, which makes forgetting to
///ask a fault nothing else would show - the old picture stays up, and looks like a change that did nothing. So
///each kind of change is made in turn and has to draw, and the scene has to go quiet again afterwards.
///
test('the scene draws when something changes, and not while it stands still', async ({ page }) => {
    await watchDraws(page);

    //Whatever opening the view asked for has been drawn by now, and nothing since.
    await expect.poll(() => drawsAfter(page, standingStill)).toBe(0);

    //The camera, moved by the buttons beside Center and by a drag across the canvas.
    expect(await drawsAfter(page, zoomingIn(page))).toBeGreaterThan(0);

    expect(await drawsAfter(page, async () => {
        const box = await page.locator('#container canvas').boundingBox();

        await page.mouse.move(box.x + (box.width / 2), box.y + (box.height / 2));
        await page.mouse.down();
        await page.mouse.move(box.x + (box.width / 2) + 60, box.y + (box.height / 2) + 20, { steps: 4 });
        await page.mouse.up();
    })).toBeGreaterThan(0);

    //
    //The layers pulled apart, which moves them without building anything - see restackLayers.
    //
    //Counted only while the slider is moving, and moved faster than it settles: once it stops, the scene is
    //built again for whatever a step left out (Settling.Quiet), and that draws a frame of its own - which would
    //pass for this one if it landed inside the count.
    //
    expect(await page.evaluate(async () => {
        const before = window.watchedScene.drawsCounted || 0;
        const slider = document.getElementById('layerSpacing');

        for (let step = 0; step < 12; step++) {
            slider.value = String(Number(slider.value) + 5);
            slider.dispatchEvent(new Event('input', { bubbles: true }));

            await new Promise(resolve => setTimeout(resolve, 40));
        }

        return (window.watchedScene.drawsCounted || 0) - before;
    })).toBeGreaterThan(0);

    //A layer's row pressed, which lights its slabs for a moment.
    await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBeGreaterThan(0);

    expect(await drawsAfter(page, () => page.evaluate(() => document.querySelectorAll('.layerRow')[0].click()))).toBeGreaterThan(0);

    //The view made a different size, which clears the canvas.
    const size = page.viewportSize();

    expect(await drawsAfter(page, () => page.setViewportSize({ width: size.width - 120, height: size.height - 80 }))).toBeGreaterThan(0);

    //And quiet again once the flash has run its course.
    await expect.poll(() => drawsAfter(page, standingStill), { timeout: 10000 }).toBe(0);
});

///
///**A teardown only takes down the viewer of the view that sent it.**
///
///The view being left and the one replacing it can be a render apart, so the old one's teardown can arrive after the
///new one has built a viewer of its own - and taking down "the viewer" then would blank the one on screen. So each
///view names the viewer it registered. Sent by hand here, naming viewers nobody registered, because the order that
///would deliver a stale one is not one a click can be counted on to produce.
///
test('a teardown naming another viewer leaves this one drawing', async ({ page }) => {
    await watchDraws(page);

    await page.evaluate(() => {
        window.unregisterThree(0);
        window.unregisterThree(-1);
    });

    await expect.poll(() => drawsAfter(page, zoomingIn(page))).toBeGreaterThan(0);

    expect(await page.evaluate(() => window.watchedScene.isContextLost())).toBe(false);
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

        //The slabs, which are the meshes carrying outlines - a layer's labels are a mesh too.
        const original = THREE.Object3D.prototype.add;
        THREE.Object3D.prototype.add = function (...objects) {
            for (const object of objects) {
                if (object.isMesh && object.userData.outlines != null)
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
    //the control being tested. On bare numbers the resting stack is the even one this was written against.
    //
    await bareNumbers(page);

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

        //Slabs and labels both, since a layer's labels are a mesh carrying its offset too.
        const heights = added
            .filter(object => object.isMesh)
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

///
///**The layers are drawn plain**, the flat colors the view always had: their surfaces are the game's since 2026-10-14,
///put on by its Apply texture - see chip-game.spec.js. The metal is still known for when they are on - what the
///layermap says each layer is - and the shader every layer is drawn with compiles, which nothing but a browser can say.
///
///Mosfet.gds opens with the bundled sky130 layermap, which names its layers: li1 and met1 are the wiring, licon1 and
///mcon the contacts up to them, and diff, poly, nsdm and npc are silicon and the implants into it.
///
test('the layers are drawn plain, and the metal among them is known for when their surfaces are on', async ({ page }) => {
    const problems = [];

    //Opened again with the console watched from the start, since a shader is compiled for the first frame it draws.
    page.on('console', message => {
        if (message.type() === 'error' && /shader|WebGLProgram/i.test(message.text()))
            problems.push(message.text());
    });

    await gotoExample(page, MOSFET, '3d');

    await expect.poll(async () => page.locator('.layerRow').count(), { timeout: 60000 }).toBeGreaterThan(3);

    const finishes = await page.evaluate(async () => {
        const THREE = await import('three');
        const added = [];

        //Caught on the way into the scene, as the flash above catches them.
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

        const rows = Array.from(document.querySelectorAll('.layerRow')).map(row => row.textContent);

        return added
            .filter(object => object.isMesh && object.userData.words != null)
            .map(mesh => ({
                row: rows[mesh.userData.stackAt] || '',
                metal: mesh.material.userData.surface?.surfaceMetal.value,
                surfaced: mesh.material.userData.surfaced
            }));
    });

    expect(finishes.length).toBeGreaterThan(0);
    expect(finishes.every(one => one.surfaced === false), 'every layer plain').toBe(true);

    const named = name => finishes.filter(one => one.row.includes(`${name} (`));

    for (const metal of ['licon1', 'li1', 'mcon', 'met1']) {
        expect(named(metal).length, `${metal} is drawn`).toBeGreaterThan(0);
        expect(named(metal).every(one => one.metal === 1), `${metal} is metal`).toBe(true);
    }

    for (const silicon of ['diff', 'poly', 'nsdm', 'npc']) {
        expect(named(silicon).length, `${silicon} is drawn`).toBeGreaterThan(0);
        expect(named(silicon).every(one => one.metal === 0), `${silicon} is not metal`).toBe(true);
    }

    expect(problems).toEqual([]);
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

///
///**Opening a file in 3D frames it**, rather than trusting the stack to be where a fixed camera points.
///
///This is a regression test with a story. The camera opened at a fixed distance down Z and stayed there,
///which worked only for as long as the geometry happened to sit in front of it. It stopped: extruded slabs
///used to hang *below* the plane they were drawn on and now stand on it - the placement the process actually
///describes - so the whole stack rose by its own height and the camera was left aimed at its underside. A
///mapped sky130 cell opened with its metals off the top of the frame, and the only thing that caught it was
///a documentation screenshot looking wrong.
///
///Asserted by projecting the stack's own corners through the camera the scene renders with: inside the
///normalized cube on both axes is "in frame", and it holds wherever the geometry moves to next. Which is the
///point - a camera position tuned to today's stack would pass this while meaning nothing.
///
///**And in depth, which this did not check, and which is how every bundled design came to open on nothing.**
///The planes were fixed at 1 and 50,000 and the designs are framed from further than that, so the whole of one
///was past the far plane: in frame across and up, and not drawn. The same check on a design is in
///designs.spec.js.
///
test('opening a layout in 3D frames the whole stack, between the near and far planes', async ({ page }) => {
    const on = await stackOnScreen(page);

    expect(on.meshes).toBeGreaterThan(0);
    expect(on.camera).toBe(true);

    const said = onScreenSaid(on);

    expect(on.top, `the stack runs off the top: ${said}`).toBeLessThanOrEqual(1);
    expect(on.bottom, `the stack runs off the bottom: ${said}`).toBeGreaterThanOrEqual(-1);
    expect(on.right, `the stack runs off the right: ${said}`).toBeLessThanOrEqual(1);
    expect(on.left, `the stack runs off the left: ${said}`).toBeGreaterThanOrEqual(-1);

    expect(on.nearest, `the stack is in front of the near plane: ${said}`).toBeGreaterThanOrEqual(-1);
    expect(on.farthest, `the stack is past the far plane: ${said}`).toBeLessThanOrEqual(1);

    //And it is actually filling the frame rather than a speck in the middle of it, which would also "fit".
    expect(on.top - on.bottom, `the stack is a speck: ${said}`).toBeGreaterThan(0.2);
});

///
///**From above, at forty-five degrees, and in the middle of the picture.** It opened looking straight across the
///layout, which shows a chip edge-on. Centered on screen rather than aimed at the layout's middle, which from an
///angle puts the near half low and big - see framedCamera in viewGeometry.js.
///
///Loosely, because the spacing step that finds the scene has made the stack a little taller since it was framed.
///
test('opening a layout in 3D looks down on it, and has it in the middle', async ({ page }) => {
    const on = await stackOnScreen(page);

    expect(on.camera).toBe(true);

    expect(on.pitch).toBeCloseTo(45, 0);

    const said = onScreenSaid(on);

    expect(Math.abs(on.left + on.right), `off to one side: ${said}`).toBeLessThan(0.1);
    expect(Math.abs(on.bottom + on.top), `off up or down: ${said}`).toBeLessThan(0.1);
});

///
///The corner axis indicator, which is a second render into a corner of the same canvas.
///
///**The two ways that can go wrong are both invisible to every other test here.** If the viewport is not
///put back afterwards, the *next* frame's whole scene is drawn into the corner square - the layout does
///not disappear, it shrinks into a badge, and no assertion about the scene graph notices. If depth is not
///cleared first, the indicator is drawn inside whatever the layout has in front of it and simply does not
///appear, while every mesh and every count stays exactly as it was.
///
///**Watched at gl.viewport rather than at three's setViewport.** Three assigns setViewport onto each
///renderer as an own property rather than onto the prototype, so there is nothing to patch without a
///handle on the instance - and the instance is private to the interop. The WebGL call underneath it is on
///a prototype, is what actually moves the viewport, and is the same call whichever way three is built.
///
///Pixels would be the other way to ask, and reading a WebGL canvas back needs preserveDrawingBuffer -
///turning that on to measure this would change the thing being measured.
///
test('the axis indicator is drawn into the bottom-left corner, and gives the viewport back', async ({ page }) => {
    const seen = await page.evaluate(async () => {
        const calls = [];
        const undos = [];

        for (const context of [window.WebGL2RenderingContext, window.WebGLRenderingContext]) {
            if (context == null)
                continue;

            const original = context.prototype.viewport;

            context.prototype.viewport = function (x, y, width, height) {
                calls.push({ x, y, width, height });

                return original.apply(this, arguments);
            };

            undos.push(() => { context.prototype.viewport = original; });
        }

        //Several frames through it, asked for by moving the camera a notch in and out again: a scene standing
        //still draws nothing - see animate in ThreeInterop.js.
        for (let step = 0; step < 4; step++) {
            let notches = -1;

            if (step % 2 === 0)
                notches = 1;

            window.zoomCameraInterOp(notches);

            await new Promise(resolve => setTimeout(resolve, 100));
        }

        for (const undo of undos)
            undo();

        //Device pixels, which is what gl.viewport speaks - three multiplies by the pixel ratio on the way
        //down. The canvas is read in the same units so the comparisons below are like for like.
        const canvas = document.querySelector('#container canvas');

        return { calls, width: canvas.width, height: canvas.height };
    });

    //Set twice a frame: once onto the corner, once back onto the whole canvas.
    const corner = seen.calls.filter(one => one.x > 0 && one.y > 0);

    expect(corner.length, 'nothing was ever drawn into a corner viewport').toBeGreaterThan(0);

    //Square, and a badge rather than a second view of the layout.
    expect(corner[0].width).toBe(corner[0].height);
    expect(corner[0].width).toBeLessThan(seen.width / 3);

    //Bottom left specifically. A WebGL viewport measures its origin from the bottom-left of the canvas, so
    //both being small is that corner - and it is the corner the buttons along the foot leave free.
    expect(corner[0].x).toBeLessThan(seen.width / 2);
    expect(corner[0].y).toBeLessThan(seen.height / 2);

    //And every one of them is followed by the whole canvas going back, which is what stops the corner
    //keeping the next frame.
    for (let at = 0; at < seen.calls.length - 1; at++)
        if (seen.calls[at].x > 0 && seen.calls[at].y > 0)
            expect(seen.calls[at + 1], 'the corner viewport was not given back').toMatchObject({ x: 0, y: 0 });
});
