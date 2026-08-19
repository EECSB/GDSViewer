//The 3D view: the WebGL scene is built, labels come through as billboards, and leaving and returning
//does not pile up renderers. That last one is the reason this spec exists - a leaked WebGL context per
//visit is invisible until the browser starts dropping the oldest, and nothing but a real browser can
//show it.
const { test, expect } = require('@playwright/test');
const { gotoExample, selectView, threeCounts, MOSFET, MOSFET_POLYGONS, MOSFET_MESHES, MOSFET_LABELS, shapeCount, shapesDrawn } = require('./helpers');

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
///world -Y - so a slab hangs below the plane it was drawn on and z depth is its *underside*. Anchoring a
///label there put every one of them inside the shape it names, which a billboard hides well: it is still
///drawn, still upright, still legible from some angles, and only wrong from the ones where the slab is
///between it and the camera.
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
                    top: (label.y * Math.cos(1.5)) + label.offset,
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
        //the scene is recentred - where the spread across the stack is exactly what the slider sets.
        const centers = meshes.map(mesh => {
            mesh.geometry.computeBoundingBox();

            return mesh.geometry.boundingBox.getCenter(new THREE.Vector3()).y + mesh.position.y;
        });

        return { meshes: meshes.length, spread: Math.max(...centers) - Math.min(...centers) };
    }, spacing);

    const close = await heights(60);
    const apart = await heights(600);

    //The same scene both times, which is what says the two spreads are comparable at all.
    expect(close.meshes).toBe(MOSFET_MESHES);
    expect(apart.meshes).toBe(MOSFET_MESHES);

    expect(apart.spread).toBeGreaterThan(close.spread * 3);
});
