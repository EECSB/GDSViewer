//Pure helpers for the views: the pan and zoom arithmetic the 2D view applies to its viewBox, and the
//point a 3D label hangs from. No DOM and no three.js, so they can be unit-tested under Node (see
//tests/jstests/). Exposed as window.viewGeometry in the browser and as module.exports under Node.
(function (factory) {
    const api = factory();

    if (typeof module !== 'undefined' && module.exports)
        module.exports = api;

    if (typeof window !== 'undefined')
        window.viewGeometry = api;
})(function () {
    'use strict';

    //
    //How much one scroll notch changes the viewBox by, as a proportion of what is on screen.
    //
    //**Proportional, because a fixed step is only ever right at one size.** This subtracted two hundred
    //layout units a notch, which halves the view of a standard cell in ten and would take two hundred and
    //eighty to halve the view of a die - so a large layout could not practically be zoomed into at all,
    //which is exactly the case the culling behind it was built for. Measured on a generated layout: eighty
    //notches moved the viewBox from 56,276 units across to 40,276, and nothing came into view.
    //
    //An eighth each way, so a notch is noticeable and a dozen of them is an order of magnitude.
    //
    const ZOOM_FACTOR = 1.125;

    //A viewBox narrower than this is refused. Each notch subtracts a fixed amount, so without a floor
    //enough scrolling walks the width to zero and then negative - which is not a viewBox a browser will
    //accept, and the view stops drawing with nothing to say why.
    const MINIMUM_SIZE = 200;

    ///<summary>
    ///Where the viewBox origin moves to for a drag from origin to pointer, both in screen pixels.
    ///
    ///unitsPerPixel is what one screen pixel was in the layout when the drag began, read off the SVG's own
    ///matrix - so the zoom and the size of the view are already in it, and a drag moves the layout by exactly
    ///the pixels dragged. What was under the pointer at the press is still under it at the release.
    ///
    ///**The zoom is not applied a second time.** This took a ratio measured whenever the view last changed
    ///size and damped it by the zoom on top, which was right only if that measurement had been taken at the
    ///starting zoom, on a square view whose width was the tight side. A resize while zoomed in, or a view
    ///wider than it is tall, and every drag after it moved the layout faster or slower than the pointer.
    ///</summary>
    function pannedOrigin(viewBox, origin, pointer, unitsPerPixel) {
        return {
            x: viewBox.x - ((pointer.x - origin.x) * unitsPerPixel),
            y: viewBox.y - ((pointer.y - origin.y) * unitsPerPixel)
        };
    }

    ///<summary>
    ///The CSS transform that makes a drawing fitted to one viewBox look like the same drawing fitted to another:
    ///a scale and a shift, in the element's own pixels, about its top left corner.
    ///
    ///**What moves the 2D view during a gesture.** Setting the viewBox on every pointer move had the browser
    ///raster the whole picture again each frame, and on a real chip that was a tenth of a second a frame. Moving
    ///the picture already drawn costs the compositor next to nothing, so a drag, a wheel and a pinch move it this
    ///way and the viewBox itself is set once, when the gesture stops.
    ///
    ///Exact for the way the SVG fits its box - the default, xMidYMid meet, which scales by the tighter side and
    ///centers along the other. A zoom keeps the box's proportions, so the centering is the same before and after
    ///and the two fits differ only by the scale and the shift.
    ///
    ///width and height are the element's size on the page, untransformed.
    ///</summary>
    function viewBoxShift(applied, target, width, height) {
        const fit = function (box) {
            const scale = Math.min(width / box.width, height / box.height);

            return {
                scale: scale,
                left: (width - (box.width * scale)) / 2,
                top: (height - (box.height * scale)) / 2
            };
        };

        const from = fit(applied);
        const to = fit(target);
        const scale = to.scale / from.scale;

        return {
            scale: scale,
            x: to.left + (to.scale * (applied.x - target.x)) - (scale * from.left),
            y: to.top + (to.scale * (applied.y - target.y)) - (scale * from.top)
        };
    }

    ///<summary>
    ///The viewBox two fingers make, from where they landed to where they are now - a zoom and a drag at once.
    ///
    ///**About the point between them.** The layout point that was under the middle of the two fingers when
    ///they landed ends up under the middle of them now: spreading them zooms in on that spot, and moving them
    ///together carries it along. Which is the wheel's anchored zoom with a scale that varies smoothly, plus
    ///how far the middle has traveled - exact, because the SVG fits its viewBox the default way (xMidYMid
    ///meet) and a pinch scales both sides of the box alike.
    ///
    ///**Held at the wheel's floor rather than refused.** A notch that would go under MINIMUM_SIZE is not
    ///taken; a pinch is one continuous gesture, so it stops at the floor and stays there however much further
    ///the fingers spread.
    ///
    ///from is the viewBox when the fingers landed, anchor the layout point under their first middle, and
    ///unitsPerPixel what a screen pixel was then. The middles and the distances are in screen pixels, and a
    ///distance under a pixel counts as one - two fingers can land on the same spot.
    ///</summary>
    function pinchedViewBox(from, anchor, startMiddle, middle, startDistance, distance, unitsPerPixel) {
        let scale = Math.max(startDistance, 1) / Math.max(distance, 1);

        const smallest = Math.min(from.width, from.height);

        if (scale < 1 && smallest * scale < MINIMUM_SIZE)
            scale = Math.min(1, MINIMUM_SIZE / smallest);

        const perPixel = unitsPerPixel * scale;

        return {
            x: anchor.x - ((anchor.x - from.x) * scale) - ((middle.x - startMiddle.x) * perPixel),
            y: anchor.y - ((anchor.y - from.y) * scale) - ((middle.y - startMiddle.y) * perPixel),
            width: from.width * scale,
            height: from.height * scale
        };
    }

    ///<summary>
    ///The size a viewBox becomes for one scroll notch. Scrolling down zooms out, which is a larger box.
    ///</summary>
    function zoomedSize(viewBox, deltaY) {
        let change = 1 / ZOOM_FACTOR;

        if (deltaY > 0)
            change = ZOOM_FACTOR;

        let width = viewBox.width * change;
        let height = viewBox.height * change;

        if (width < MINIMUM_SIZE || height < MINIMUM_SIZE) {
            width = viewBox.width;
            height = viewBox.height;
        }

        return { width: width, height: height };
    }

    ///<summary>
    ///The viewBox a press of the zoom buttons makes: that many of the wheel's notches, positive in and negative
    ///out, about the middle of the box. A button has no pointer over the drawing to anchor on, and the middle is
    ///the point that stays where it was. Each notch is held at the wheel's floor the way a turn of the wheel is.
    ///</summary>
    function zoomedAboutMiddle(viewBox, notches) {
        const middleX = viewBox.x + (viewBox.width / 2);
        const middleY = viewBox.y + (viewBox.height / 2);
        let size = { width: viewBox.width, height: viewBox.height };
        let deltaY = -1;

        if (notches < 0)
            deltaY = 1;

        for (let i = 0; i < Math.abs(notches); i++)
            size = zoomedSize(size, deltaY);

        return {
            x: middleX - (size.width / 2),
            y: middleY - (size.height / 2),
            width: size.width,
            height: size.height
        };
    }

    ///<summary>
    ///How far the 3D camera stands from what it turns around after the same press: the 2D view's notches, so a
    ///press of the buttons is the same amount in either view. Positive comes closer.
    ///</summary>
    function zoomedDistance(distance, notches) {
        return distance * Math.pow(ZOOM_FACTOR, -notches);
    }

    ///<summary>
    ///What the ruler has measured between two points, both in the layout's own coordinates.
    ///
    ///**The numbers describe the file, not the picture.** This view maps GDSII's upward Y straight onto
    ///SVG's downward Y, so the drawing is flipped: a point that looks higher on screen has a *smaller* Y
    ///in the file. dy is reported the file's way round, because a measurement that agreed with the picture
    ///would disagree with every coordinate in the text view and in the download.
    ///
    ///micronsPerUnit comes from the file's UNITS record. Null when it says nothing usable, in which case
    ///the reading is in database units alone - an invented scale is worse than none, since a number with
    ///a unit on it gets quoted.
    ///</summary>
    function measurement(from, to, micronsPerUnit) {
        const dx = to.x - from.x;
        const dy = to.y - from.y;

        const distance = Math.sqrt((dx * dx) + (dy * dy));

        let microns = null;

        if (typeof micronsPerUnit === 'number' && isFinite(micronsPerUnit) && micronsPerUnit > 0)
            microns = distance * micronsPerUnit;

        return {
            dx: dx,
            dy: dy,
            distance: distance,
            microns: microns,
            label: measurementLabel(distance, microns)
        };
    }

    ///<summary>
    ///The one line the ruler puts on screen.
    ///
    ///Two decimals on the units because the endpoints are on the grid and the diagonal between them is
    ///not; four on the microns because a database unit is usually a nanometer, and three would round a
    ///single-unit measurement away to nothing.
    ///</summary>
    function measurementLabel(distance, microns) {
        const units = distance.toFixed(2) + ' units';

        if (microns === null)
            return units;

        return units + '  (' + microns.toFixed(4) + ' µm)';
    }

    ///<summary>
    ///The horizontal point a label hangs from, in its own 0..1 space - the one a sprite's center is in,
    ///and the 3D view's label shader takes. The names come straight from the PRESENTATION record's
    ///justification.
    ///</summary>
    function labelCenterX(horizontal) {
        if (horizontal === 'Left')
            return 0;

        if (horizontal === 'Right')
            return 1;

        return 0.5;
    }

    ///<summary>
    ///The vertical one. A label justified to the top hangs below its anchor, so the anchor is the
    ///label's top edge - 1 in a space whose Y runs up.
    ///</summary>
    function labelCenterY(vertical) {
        if (vertical === 'Top')
            return 1;

        if (vertical === 'Bottom')
            return 0;

        return 0.5;
    }

    //
    //How far above the layout the 3D view opens, as an angle up from looking straight across it.
    //
    //**Halfway between across and down.** Across is how it opened until this, which shows a standard cell's
    //layers side by side and a chip edge-on: a die is many times wider than its stack is tall, so from beside it
    //there is nothing to see but the edge. Straight down shows the floor plan and none of the stack, which is
    //what the 2D view is for. At forty-five degrees a cell still shows its layers and a chip shows its plan.
    //
    //Seen from the end its Y points to, so the layout's Y runs down the screen, the way the 2D view draws it.
    //
    const OPENING_ELEVATION = Math.PI / 4;

    //How much room the 3D fit leaves around what it frames: a tenth, the margin the 2D fit uses.
    const FRAME_MARGIN = 1.1;

    //
    //The most the far plane may be past the near one, which only matters with the camera in among the layout.
    //
    //Depth is stored most finely near the near plane, so the ratio of the two decides how fine it is at the far
    //one. From outside the layout the near plane goes up to the layout itself and the ratio is ten or so. From
    //inside it nothing is safe to leave out, so the near plane comes down to the far one over this - two units
    //for a design two hundred thousand across, which still tells surfaces a unit apart at a few thousand away.
    //
    const DEPTH_RATIO = 100000;

    //How much of a draw distance past the layout's nearest point is clear before the haze starts - see drawReach.
    const HAZE_FROM = 0.6;

    //
    //How much bigger the near side of a layout may come out than its far side - the far side's depth over the
    //near side's.
    //
    //Framed as tightly as the frame allows, a layout narrower than it is deep has the camera right up against its
    //near edge: sky130_fd_sc_hd__nand2_1 came out with the far side a ninth the scale of the near, and the power
    //rail along the near edge filled the view. In a window of ordinary shape Mosfet comes out at 3 and the
    //designs at 3 to 4, so this leaves them much as they are and stands a narrow cell back. A wider window lets
    //the camera closer, and there it holds a design back too.
    //
    const PERSPECTIVE_LIMIT = 4;

    //How far out Admire's orbit runs, as a multiple of the radius of the sphere around the layout. What it was
    //for Mosfet.gds, the example the app opens with: 10,000 units, around a sphere of 1,881.
    const ADMIRE_REACH = 5.3;

    ///<summary>
    ///Where the 3D camera stands to show the whole of a layout, looking down at it at OPENING_ELEVATION from the
    ///side the layout's Y points to: as close as it can be with every point inside the frame, and those points in
    ///the middle of the picture.
    ///
    ///points are { x, y, z } in the scene's axes - Y up the stack, the layout's Y along Z - and they are the corners
    ///of each layer's box and of a box around each layer's labels. fov is the camera's vertical field of view in
    ///degrees and aspect its width over its height. Hands back { position, target }, or null when there is nothing
    ///to frame. The target is what the camera looks at and what an orbit turns around: halfway along the stretch
    ///of the view that passes through the box around the points, which for a flat layout is the point of it in
    ///the middle of the picture.
    ///
    ///**Each layer's box rather than one box around everything.** A box around the whole layout has corners where
    ///nothing is drawn, and from above at an angle the near ones are the widest thing in the picture. Mosfet's
    ///widest parts are its source and drain pads, towards the back: framed on its whole box it filled 57% of the
    ///width, with the empty near corners at the edges of the frame. A layer's own box comes within a few percent
    ///of what is drawn - measured, exactly as wide as every vertex of Mosfet and of nand2_1.
    ///
    ///**The middle of the picture rather than the middle of the layout.** Seen at an angle, the near half of a
    ///layout is closer and so looks bigger - with this camera's hundred-degree field of view, the near edge of a
    ///design comes out three to four times the width of the far one. Aimed at the layout's middle, a design sat
    ///in the lower two thirds of the view with its near edge behind the buttons along the foot, and the top third
    ///was empty. Aimed at the middle of what is drawn, it is centered and a quarter to a third bigger. The cost
    ///is that the point the view turns around is that much nearer than the layout's middle: under a third of its
    ///depth for a design, and for a cell next to nothing.
    ///
    ///**One pass for how close, then halving for where.** The camera's direction is fixed, so moving it across or
    ///up the screen only slides the picture; what decides how close it can come is two opposite edges of the frame
    ///at once - see closestFor - and PERSPECTIVE_LIMIT. Then it slides to where the points stand equally far out on
    ///either side - see centeredOn. Neither takes the points in pairs, so how many there are costs a pass each, not
    ///their square.
    ///</summary>
    function framedCamera(points, fov, aspect) {
        const spread = Math.tan((fov / 2) * (Math.PI / 180));

        if (!(spread > 0) || !(aspect > 0) || points.length === 0)
            return null;

        const rise = Math.sin(OPENING_ELEVATION);
        const run = Math.cos(OPENING_ELEVATION);

        //Each point in the camera's own directions: how far across the screen, how far up it, and how far along
        //the view. The camera looks down the slope, so up the screen is up and away from it.
        const seen = points.map(point => ({
            across: point.x,
            up: (point.y * run) - (point.z * rise),
            along: -(point.y * rise) - (point.z * run)
        }));

        //How far out from the middle of the picture a point may stand for each unit of its depth, and still be
        //inside the frame with its margin.
        const reachUp = spread / FRAME_MARGIN;
        const reachAcross = reachUp * aspect;

        let forward = Math.min(closestFor(seen, 'up', reachUp), closestFor(seen, 'across', reachAcross));

        //And no closer than leaves the deepest point PERSPECTIVE_LIMIT times as deep as the nearest.
        let nearest = Infinity;
        let deepest = -Infinity;

        for (const point of seen) {
            nearest = Math.min(nearest, point.along);
            deepest = Math.max(deepest, point.along);
        }

        forward = Math.min(forward, ((PERSPECTIVE_LIMIT * nearest) - deepest) / (PERSPECTIVE_LIMIT - 1));

        if (!isFinite(forward))
            return null;

        const up = centeredOn(seen, forward, 'up');
        const across = centeredOn(seen, forward, 'across');

        if (up == null || across == null)
            return null;

        const position = {
            x: across,
            y: (up * run) - (forward * rise),
            z: -(up * rise) - (forward * run)
        };

        const box = { min: { x: Infinity, y: Infinity, z: Infinity }, max: { x: -Infinity, y: -Infinity, z: -Infinity } };

        for (const point of points) {
            for (const axis of ['x', 'y', 'z']) {
                box.min[axis] = Math.min(box.min[axis], point[axis]);
                box.max[axis] = Math.max(box.max[axis], point[axis]);
            }
        }

        //
        //Where the view goes into that box and where it comes out, as distances along it. Across the screen it
        //runs between the points, so only the height and the depth have a say.
        //
        //Not simply where it crosses the middle height, which is the same point for a layout but can be well
        //outside a stack that is taller than it is wide: the view meets a tower's middle height in front of it.
        //
        const enters = Math.max((position.y - box.max.y) / rise, (position.z - box.max.z) / run, 0);
        const leaves = Math.min((position.y - box.min.y) / rise, (position.z - box.min.z) / run);

        //A layout with no extent at all - one label and nothing else - would put the camera inside it.
        if (!(leaves > 0) || leaves < enters)
            return null;

        const into = (enters + leaves) / 2;

        return {
            position: position,
            target: { x: position.x, y: position.y - (into * rise), z: position.z - (into * run) }
        };
    }

    ///<summary>
    ///How far along the view the camera can come with every point between two opposite edges of the frame - for
    ///framedCamera, in one of the screen's directions, with reach how far out a point may stand per unit of depth.
    ///
    ///An edge of the frame is a plane sloping out from the camera, so being inside the top edge is a limit on
    ///where the camera can be: no lower on the screen than the point's height less reach times its depth. The
    ///highest of those limits and the lowest of the bottom edge's leave a gap the camera has to fit in, and coming
    ///forward closes it from both sides at once - so the gap over twice the reach is as far as it can come. Each
    ///point counts once, rather than once against every other.
    ///</summary>
    function closestFor(seen, direction, reach) {
        let highest = -Infinity;
        let lowest = Infinity;

        for (const point of seen) {
            highest = Math.max(highest, point[direction] - (reach * point.along));
            lowest = Math.min(lowest, point[direction] + (reach * point.along));
        }

        return (lowest - highest) / (2 * reach);
    }

    ///<summary>
    ///Where along one of the screen's directions the camera has to be for the points to stand equally far out on
    ///either side of the middle of the picture - for framedCamera, with the camera that far along the view.
    ///
    ///Not halfway between the outermost two. Each point is at its own depth, and one twice as deep moves half as
    ///far on screen for the same slide of the camera, so a slide that brings the near side in pushes the far side
    ///out by less. Found by halving between the outermost two, sixty times, which is as fine as a double goes.
    ///Null when a point is at the camera, which only a layout of no extent can put there.
    ///</summary>
    function centeredOn(seen, forward, direction) {
        let low = Infinity;
        let high = -Infinity;

        for (const point of seen) {
            if (!(point.along - forward > 0))
                return null;

            low = Math.min(low, point[direction]);
            high = Math.max(high, point[direction]);
        }

        for (let step = 0; step < 60; step++) {
            const middle = (low + high) / 2;

            let over = -Infinity;
            let under = -Infinity;

            for (const point of seen) {
                const depth = point.along - forward;

                over = Math.max(over, (point[direction] - middle) / depth);
                under = Math.max(under, (middle - point[direction]) / depth);
            }

            if (over > under)
                low = middle;
            else
                high = middle;
        }

        return (low + high) / 2;
    }

    ///<summary>
    ///The near and far planes for a camera at eye with a box to draw, as { near, far }, or null when there is no
    ///depth to give them. box is { min, max }, each with x, y and z, and fov and aspect are as framedCamera takes
    ///them.
    ///
    ///**Worked out for every frame rather than fixed.** The planes were 1 and 50,000, which suits a standard
    ///cell. Every bundled design is framed from further than 50,000 away, so each one opened on an empty view:
    ///all of it was past the far plane.
    ///
    ///**Far is the furthest corner of the box**, and a twentieth more for a label's billboard, which can stand a
    ///little past the point the box was measured from.
    ///
    ///**Near is as far out as it can go without cutting into the box.** Nothing in the box is closer to the
    ///camera than the nearest point of it, but depth is measured along the view rather than towards the point,
    ///so something that close out in a corner of the frame is at less depth than that - by the cosine of the
    ///angle out to the corner. From inside the box the nearest point is the camera's own, and the near plane
    ///comes down to DEPTH_RATIO under the far.
    ///</summary>
    function depthRange(box, eye, fov, aspect) {
        const spread = Math.tan((fov / 2) * (Math.PI / 180));

        let furthest = 0;

        for (const x of [box.min.x, box.max.x])
            for (const y of [box.min.y, box.max.y])
                for (const z of [box.min.z, box.max.z])
                    furthest = Math.max(furthest, Math.hypot(x - eye.x, y - eye.y, z - eye.z));

        const far = furthest * 1.05;

        if (!isFinite(far) || !(far > 0))
            return null;

        const outX = Math.max(box.min.x - eye.x, 0, eye.x - box.max.x);
        const outY = Math.max(box.min.y - eye.y, 0, eye.y - box.max.y);
        const outZ = Math.max(box.min.z - eye.z, 0, eye.z - box.max.z);

        const nearest = Math.hypot(outX, outY, outZ);
        const toCorner = 1 / Math.sqrt(1 + ((spread * spread) * (1 + (aspect * aspect))));

        return {
            near: Math.max(nearest * toCorner * 0.9, far / DEPTH_RATIO),
            far: far
        };
    }

    ///<summary>
    ///How far the 3D view draws for a draw distance - `share` of the box's width, from a twentieth up to all of it - as
    ///{ far, hazeFrom }: the far plane, and where the haze in front of it starts. Null for all of it, which draws
    ///everything. box and eye are as depthRange takes them.
    ///
    ///**From the nearest point of the box**, rather than from the camera the way the game measures it from the car. The
    ///camera here is mostly outside the layout, looking at it: a far plane a share of its width from the camera would
    ///leave nothing to see from any further off than that. From the nearest point, the near side is always drawn and
    ///the share is how far in from there - and the haze starts HAZE_FROM of the way in, so the near side is clear.
    ///
    ///**The width is the layout's own plane** - the scene's x and z - rather than its height up the stack, which is a
    ///share of a micron where the width is millimeters.
    ///</summary>
    function drawReach(box, eye, share) {
        if (!(share < 1))
            return null;

        const outX = Math.max(box.min.x - eye.x, 0, eye.x - box.max.x);
        const outY = Math.max(box.min.y - eye.y, 0, eye.y - box.max.y);
        const outZ = Math.max(box.min.z - eye.z, 0, eye.z - box.max.z);

        const nearest = Math.hypot(outX, outY, outZ);
        const reach = Math.max(box.max.x - box.min.x, box.max.z - box.min.z) * Math.max(share, 0);

        if (!isFinite(nearest) || !(reach > 0))
            return null;

        return {
            far: nearest + reach,
            hazeFrom: nearest + (reach * HAZE_FROM)
        };
    }

    ///<summary>
    ///How far out Admire's orbit runs for a box, in the scene's units - box as depthRange takes it.
    ///
    ///**In proportion, because a fixed orbit only ever suited one size.** It was 10,000 units whatever was open,
    ///which is what Mosfet.gds still gets, so the example the app opens with looks as it always did. The smallest
    ///bundled design is seventy times Mosfet's size, and 10,000 out from its middle is inside it.
    ///</summary>
    function admireReach(box) {
        const radius = Math.hypot(box.max.x - box.min.x, box.max.y - box.min.y, box.max.z - box.min.z) / 2;

        return radius * ADMIRE_REACH;
    }

    ///
    ///The box a drag makes, squared off when a circle was asked for rather than an ellipse.
    ///
    ///Squared off away from where the drag started, not around the middle of the box: the corner under the
    ///hand is the one that should follow it. The larger of the two sides wins, so holding the modifier grows
    ///the shape to enclose what was dragged rather than shrinking it.
    ///
    function drawnBox(from, to, circle) {
        if (!circle)
            return { from: from, to: to };

        const size = Math.max(Math.abs(to.x - from.x), Math.abs(to.y - from.y));

        let x = from.x + size;
        let y = from.y + size;

        if (to.x < from.x)
            x = from.x - size;

        if (to.y < from.y)
            y = from.y - size;

        return { from: from, to: { x: x, y: y } };
    }

    ///
    ///An ellipse inscribed in that box, as the corners of the polygon that stands in for it.
    ///
    ///**A layout format has no curves.** GDSII knows boundaries and paths and nothing else, so a circle in a
    ///chip layout is a many-sided polygon and always has been - which is why how many sides is a decision
    ///somebody has to make rather than something to hide. Fewer is a smaller file and a coarser shape.
    ///
    ///Starting at the right and going the way the angles do. Nothing downstream depends on where it starts,
    ///but a shape whose first corner moves with the point count is a shape whose records churn for no reason
    ///between two runs that meant the same thing.
    ///
    function ellipseCorners(from, to, segments, circle) {
        const box = drawnBox(from, to, circle);

        const centerX = (box.from.x + box.to.x) / 2;
        const centerY = (box.from.y + box.to.y) / 2;
        const radiusX = Math.abs(box.to.x - box.from.x) / 2;
        const radiusY = Math.abs(box.to.y - box.from.y) / 2;

        const sides = Math.max(3, Math.round(segments));
        const corners = [];

        for (let i = 0; i < sides; i++) {
            const angle = (i / sides) * Math.PI * 2;

            corners.push({
                x: centerX + (radiusX * Math.cos(angle)),
                y: centerY + (radiusY * Math.sin(angle))
            });
        }

        return corners;
    }

    ///
    ///Where the pointer is, as a line to read.
    ///
    ///In microns when the file says what a database unit is, and in database units when it does not - the
    ///same choice the ruler makes, and for the same reason: a made-up scale is worse than none.
    ///
    ///Four decimals, because a database unit is usually a nanometer and three would round a position on the
    ///grid to something that is not on it.
    ///
    function position(point, micronsPerUnit) {
        if (micronsPerUnit == null || !(micronsPerUnit > 0))
            return `${Math.round(point.x)}, ${Math.round(point.y)} units`;

        const x = point.x * micronsPerUnit;
        const y = point.y * micronsPerUnit;

        return `${x.toFixed(4)}, ${y.toFixed(4)} µm`;
    }

    ///
    ///The point on a segment closest to another point, kept between its two ends.
    ///
    ///Clamped rather than allowed to run off the line, because what is being asked is where on this *edge*
    ///the pointer is nearest - and the nearest place on an infinite line through it may be nowhere near the
    ///shape at all.
    ///
    function alongEdge(point, fromX, fromY, toX, toY) {
        const runX = toX - fromX;
        const runY = toY - fromY;

        const length = (runX * runX) + (runY * runY);

        if (length === 0)
            return { x: fromX, y: fromY };

        let along = (((point.x - fromX) * runX) + ((point.y - fromY) * runY)) / length;

        if (along < 0)
            along = 0;

        if (along > 1)
            along = 1;

        return { x: fromX + (along * runX), y: fromY + (along * runY) };
    }

    ///
    ///The nearest corner or edge to a point, or null when nothing is within reach.
    ///
    ///**A corner beats an edge, even a nearer one.** Every corner is also a point on two edges, so a search
    ///that only compared distances would answer with the edge at every corner - by a fraction, and always.
    ///A corner is the more particular thing to have meant, so it wins whenever one is in range at all.
    ///
    ///`corners` is a flat run of four numbers per corner: where it is, and where the edge leaving it goes.
    ///A corner that ends an outline has no edge and carries NaN for it. Flat because this is asked on every
    ///movement of the pointer, and an array of objects allocates one per corner per ask.
    ///
    function nearestSnap(corners, point, near) {
        if (corners == null || near <= 0)
            return null;

        let bestCorner = null;
        let bestCornerAway = near * near;

        let bestEdge = null;
        let bestEdgeAway = near * near;

        for (let i = 0; i + 3 < corners.length; i += 4) {
            const x = corners[i];
            const y = corners[i + 1];

            const away = ((x - point.x) * (x - point.x)) + ((y - point.y) * (y - point.y));

            if (away < bestCornerAway) {
                bestCornerAway = away;
                bestCorner = { x: x, y: y };
            }

            const toX = corners[i + 2];
            const toY = corners[i + 3];

            if (Number.isNaN(toX) || Number.isNaN(toY))
                continue;

            const on = alongEdge(point, x, y, toX, toY);
            const edgeAway = ((on.x - point.x) * (on.x - point.x)) + ((on.y - point.y) * (on.y - point.y));

            if (edgeAway < bestEdgeAway) {
                bestEdgeAway = edgeAway;
                bestEdge = on;
            }
        }

        if (bestCorner != null)
            return bestCorner;

        return bestEdge;
    }

    ///A command letter, or a number as SVG writes one: a sign, digits, a point, an exponent.
    const PATH_TOKENS = /[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/g;

    ///
    ///The shapes a path's data draws, read back as corners: one entry a subpath, with its corners as a flat
    ///run of numbers - x, y, x, y - and whether it is closed.
    ///
    ///**The 2D view writes a shape as its first corner and then steps** - `M1000,2000h400v-300h-400v300Z` -
    ///because nearly every edge of a layout is level or upright, and the step along one is a single short
    ///number where the corner it reaches is two long ones. What reads the picture back wants the corners: the
    ///snapping index, the preview of a corner being dragged, the specs. This is the one place that turns the
    ///one into the other.
    ///
    ///Every straight command SVG has, absolute and relative, so a path written either way reads the same:
    ///M, L, H and V, the pairs after a move, which are lines, and Z. **A close adds no corner.** The writer
    ///puts a ring's closing corner in itself, as the file has it, so what comes back is the element's own
    ///corners, one for one. The layout views write no curves; a curve command's numbers are skipped rather
    ///than read as corners.
    ///
    ///Data that starts without a move reads as though it began with an `M`, which is what a run split off a
    ///path at its moves looks like.
    ///
    function subpaths(data) {
        const tokens = data.match(PATH_TOKENS) || [];
        const found = [];

        let current = null;
        let command = 'M';
        let x = 0;
        let y = 0;

        for (let i = 0; i < tokens.length;) {
            const token = tokens[i];

            if (/[A-Za-z]/.test(token)) {
                command = token;
                i++;

                //The pen goes back to where the subpath began.
                if ((command === 'Z' || command === 'z') && current != null && current.corners.length >= 2) {
                    current.closed = true;
                    x = current.corners[0];
                    y = current.corners[1];
                }

                continue;
            }

            //Where the pen is before this step, which is what a relative number is measured from.
            const penX = x;
            const penY = y;

            let fromX = 0;
            let fromY = 0;

            if (command === command.toLowerCase()) {
                fromX = penX;
                fromY = penY;
            }

            const moving = command === 'M' || command === 'm';

            if (moving || command === 'L' || command === 'l') {
                if (i + 1 >= tokens.length)
                    break;

                x = fromX + Number(tokens[i]);
                y = fromY + Number(tokens[i + 1]);
                i += 2;
            }
            else if (command === 'H' || command === 'h') {
                x = fromX + Number(tokens[i]);
                i++;
            }
            else if (command === 'V' || command === 'v') {
                y = fromY + Number(tokens[i]);
                i++;
            }
            else {
                i++;

                continue;
            }

            if (moving) {
                current = { corners: [], closed: false };
                found.push(current);

                //The pairs after a move are lines, relative if the move was.
                if (command === 'M')
                    command = 'L';
                else
                    command = 'l';
            }
            else if (current == null || current.closed) {
                //A line with no move before it, or one after a close: SVG starts a subpath where the pen is.
                current = { corners: [penX, penY], closed: false };
                found.push(current);
            }

            current.corners.push(x, y);
        }

        return found;
    }

    ///
    ///The same data with every corner written where it is - `Mx,yLx,y...Z` - for something that moves corners by
    ///their position in the text, as the corner-drag preview does. A step moved on its own would take every
    ///corner after it along. See subpaths.
    ///
    function absolutePath(data) {
        const written = [];

        for (const subpath of subpaths(data)) {
            const pairs = [];

            for (let i = 0; i + 1 < subpath.corners.length; i += 2)
                pairs.push(subpath.corners[i] + ',' + subpath.corners[i + 1]);

            let one = 'M' + pairs.join('L');

            if (subpath.closed)
                one += 'Z';

            written.push(one);
        }

        return written.join(' ');
    }

    ///<summary>
    ///Which side of a picture is the shape - 'dark', 'light' or 'opaque' - told by the ground round its edge.
    ///
    ///**The border is the ground.** A logo sits inside its picture with background all round it, so whatever the
    ///edge pixels mostly are is what the logo is not: a light edge makes the dark parts the shape, a dark one
    ///the light parts - a white logo on a dark ground - and an edge mostly see-through makes the shape whatever
    ///is not, whatever its color. It was a box ticked by hand, *Light parts are the shape*, until the picture's
    ///settings went (2026-10-19).
    ///
    ///Read the way litRows reads a pixel: the eye's weighting of the channels, laid over white.
    ///</summary>
    function drawnSide(rgba, width, height) {
        let pixels = 0;
        let cover = 0;
        let shades = 0;

        const count = function (row, column) {
            const at = ((row * width) + column) * 4;
            const covered = rgba[at + 3] / 255;
            const shade = (0.299 * rgba[at]) + (0.587 * rgba[at + 1]) + (0.114 * rgba[at + 2]);

            pixels++;
            cover += covered;
            shades += (shade * covered) + (255 * (1 - covered));
        };

        for (let column = 0; column < width; column++) {
            count(0, column);

            if (height > 1)
                count(height - 1, column);
        }

        for (let row = 1; row < height - 1; row++) {
            count(row, 0);

            if (width > 1)
                count(row, width - 1);
        }

        if (pixels === 0 || cover / pixels < 0.5)
            return 'opaque';

        if (shades / pixels >= 128)
            return 'dark';

        return 'light';
    }

    ///<summary>
    ///Which squares of a picture are lit, as rows of # and . - from its pixels, four bytes each, the way a
    ///canvas hands them out - for the side `drawn` says is the shape (see drawnSide).
    ///
    ///'dark' is lit where the picture is dark, a logo drawn dark on light, which is what ink is; 'light' is lit
    ///where it is light, for a logo drawn the other way round; 'opaque' is lit wherever it is not see-through,
    ///whatever its color, for a logo cut out of a transparent ground.
    ///
    ///**Dark is read laid over white, and light over black**, so a see-through pixel is never lit either way -
    ///the background a transparent logo was cut out of is not part of it - and a half see-through edge counts
    ///for as much as it shows. The shade is the eye's weighting of the three channels rather than their
    ///average, so a saturated blue reads as the dark it looks and a yellow as the light.
    ///</summary>
    function litRows(rgba, width, height, drawn) {
        const rows = [];

        for (let row = 0; row < height; row++) {
            const line = [];

            for (let column = 0; column < width; column++) {
                const at = ((row * width) + column) * 4;
                const covered = rgba[at + 3] / 255;
                const shade = (0.299 * rgba[at]) + (0.587 * rgba[at + 1]) + (0.114 * rgba[at + 2]);

                let lit;

                if (drawn === 'opaque')
                    lit = covered >= 0.5;
                else if (drawn === 'light')
                    lit = shade * covered >= 128;
                else
                    lit = (shade * covered) + (255 * (1 - covered)) < 128;

                if (lit)
                    line.push('#');
                else
                    line.push('.');
            }

            rows.push(line.join(''));
        }

        return rows;
    }

    ///
    ///How many frames the page showed in the second before `now`, and the longest wait between two of them.
    ///
    ///`times` are the frames' timestamps, oldest first, as requestAnimationFrame hands them over. **The longest
    ///frame runs up to `now` as well as between frames**, because a page stuck on one long task shows no new
    ///frame at all: measured only between frames, a freeze still going on would read as nothing wrong. A frame
    ///from before the second still counts as the start of the first wait in it.
    ///
    function frameRate(times, now) {
        const since = now - 1000;
        let frames = 0;
        let longest = 0;
        let previous = null;

        for (const time of times) {
            if (time > now)
                break;

            if (time > since) {
                frames++;

                if (previous != null)
                    longest = Math.max(longest, time - previous);
            }

            previous = time;
        }

        if (previous != null)
            longest = Math.max(longest, now - previous);

        return { fps: frames, longest: longest };
    }

    ///
    ///The top of the frame-rate graph: a rate a display runs at, at or above every sample in it.
    ///
    ///A round figure rather than the highest sample, so the graph does not rescale with every frame dropped, and
    ///one of the rates screens actually refresh at, so a page keeping up with its display draws a flat line along
    ///the top. Past the fastest of them, the next whole minute.
    ///
    function meterScale(samples) {
        let highest = 0;

        for (const sample of samples)
            highest = Math.max(highest, sample);

        for (const rate of [30, 60, 90, 120, 144, 165, 240]) {
            if (rate >= highest)
                return rate;
        }

        return Math.ceil(highest / 60) * 60;
    }

    ///
    ///The frame-rate graph's line, as SVG points: one sample a step across, the newest on the right edge, and up
    ///the screen for more frames. The box is `kept - 1` wide and `scale` high, which is the viewBox it is drawn
    ///in, so a history not yet full starts part of the way across rather than stretching to fill it.
    ///
    function meterPoints(samples, kept, scale) {
        const shown = samples.slice(-kept);
        const first = kept - shown.length;

        return shown.map((sample, i) => `${first + i},${scale - Math.min(Math.max(sample, 0), scale)}`).join(' ');
    }

    ///
    ///The frames a second the 3D scene drew, from its own count read at intervals: the newest reading against the
    ///oldest one kept. Readings are `{ time, frames }`, oldest first, in milliseconds. Null while there is nothing
    ///to measure against, and when the count went backwards - a scene built again starts counting from zero.
    ///
    function sceneRate(readings) {
        if (readings.length < 2)
            return null;

        const oldest = readings[0];
        const newest = readings[readings.length - 1];
        const span = newest.time - oldest.time;

        if (!(span > 0) || newest.frames < oldest.frames)
            return null;

        return Math.round((newest.frames - oldest.frames) * 1000 / span);
    }

    ///<summary>A count the way the panel says it: 9876 as it is, then 98.3 k, then 1.24 M.</summary>
    function shortCount(count) {
        if (count >= 1e6)
            return `${(count / 1e6).toFixed(2)} M`;

        if (count >= 1e4)
            return `${(count / 1e3).toFixed(1)} k`;

        return String(count);
    }

    return {
        frameRate: frameRate,
        meterScale: meterScale,
        meterPoints: meterPoints,
        sceneRate: sceneRate,
        shortCount: shortCount,
        litRows: litRows,
        drawnSide: drawnSide,
        position: position,
        alongEdge: alongEdge,
        nearestSnap: nearestSnap,
        subpaths: subpaths,
        absolutePath: absolutePath,
        ZOOM_FACTOR: ZOOM_FACTOR,
        MINIMUM_SIZE: MINIMUM_SIZE,
        pannedOrigin: pannedOrigin,
        viewBoxShift: viewBoxShift,
        pinchedViewBox: pinchedViewBox,
        zoomedSize: zoomedSize,
        zoomedAboutMiddle: zoomedAboutMiddle,
        zoomedDistance: zoomedDistance,
        measurement: measurement,
        labelCenterX: labelCenterX,
        labelCenterY: labelCenterY,
        OPENING_ELEVATION: OPENING_ELEVATION,
        PERSPECTIVE_LIMIT: PERSPECTIVE_LIMIT,
        framedCamera: framedCamera,
        depthRange: depthRange,
        drawReach: drawReach,
        admireReach: admireReach,
        drawnBox: drawnBox,
        ellipseCorners: ellipseCorners,
    };
});
