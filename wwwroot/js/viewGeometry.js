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
    ///ratio converts a pixel into layout units at the current size, and scaleRatio takes the zoom into
    ///account so that a drag moves what is under the cursor by the same amount however far in you are.
    ///</summary>
    function pannedOrigin(viewBox, initialHeight, origin, pointer, ratio) {
        const scaleRatio = initialHeight / viewBox.height;

        return {
            x: viewBox.x - ((pointer.x - origin.x) * ratio / scaleRatio),
            y: viewBox.y - ((pointer.y - origin.y) * ratio / scaleRatio)
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
    ///The horizontal point a label hangs from, in a sprite's own 0..1 space. The names come straight
    ///from the PRESENTATION record's justification.
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
    ///sprite's top edge - 1 in a space whose Y runs up.
    ///</summary>
    function labelCenterY(vertical) {
        if (vertical === 'Top')
            return 1;

        if (vertical === 'Bottom')
            return 0;

        return 0.5;
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
    ///How far a straight side falls inside the curve it stands in for, as a fraction of the radius.
    ///
    ///Size-independent on purpose, because the count is chosen before anything has been dragged: at sixty-four
    ///sides a shape is within about a tenth of a percent of round, which is a thing somebody can decide about
    ///without knowing yet how big they are going to draw it.
    ///
    function segmentError(segments) {
        const sides = Math.max(3, Math.round(segments));

        return 1 - Math.cos(Math.PI / sides);
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

    return {
        position: position,
        alongEdge: alongEdge,
        nearestSnap: nearestSnap,
        ZOOM_FACTOR: ZOOM_FACTOR,
        MINIMUM_SIZE: MINIMUM_SIZE,
        pannedOrigin: pannedOrigin,
        zoomedSize: zoomedSize,
        measurement: measurement,
        labelCenterX: labelCenterX,
        labelCenterY: labelCenterY,
        drawnBox: drawnBox,
        ellipseCorners: ellipseCorners,
        segmentError: segmentError
    };
});
