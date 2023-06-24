// We select the SVG into the page
let svg;
let ratio;

const initialViewBox = {
    x: -2000,
    y: - 1000,
    width: 4000,
    height: 4000
};

// We save the original values from the viewBox
let viewBox = {
    x: initialViewBox.x,
    y: initialViewBox.y,
    width: initialViewBox.width,
    height: initialViewBox.height
};

// The distances calculated from the pointer will be stored here
var newViewBox = {
    x: 0,
    y: 0
};

// This variable will be used later for move events to check if pointer is down or not
let isPointerDown = false;
let isFirstClick = true;

// This variable will contain the original coordinates when the user start pressing the mouse or touching the screen
let pointerOrigin = {
    x: 0,
    y: 0
};


function registerSVGEvents() {
    svg = document.querySelector('svg');
    
    if (window.PointerEvent) {
        //If browser supports pointer events.
        svg.addEventListener('pointerdown', onPointerDown);
        svg.addEventListener('pointerup', onPointerUp);
        svg.addEventListener('pointerleave', onPointerUp);
        svg.addEventListener('pointermove', onPointerMove);
    } else {
        //Else add mouse events listeners ...
        svg.addEventListener('mousedown', onPointerDown);
        svg.addEventListener('mouseup', onPointerUp);
        svg.addEventListener('mouseleave', onPointerUp);
        svg.addEventListener('mousemove', onPointerMove);

        //.. and touch events listeners.
        svg.addEventListener('touchstart', onPointerDown);
        svg.addEventListener('touchend', onPointerUp);
        svg.addEventListener('touchmove', onPointerMove);
    }

    //Calculate the ratio based on the viewBox width and the SVG width.
    ratio = viewBox.width / svg.getBoundingClientRect().width;
    window.addEventListener('resize', function () {
        ratio = viewBox.width / svg.getBoundingClientRect().width;
    });
    
    //Add scrool event for zoom.
    document.getElementById("svgWrapper").addEventListener("wheel", function (e) {
        e.preventDefault();

        if (e.deltaY > 0) {
            viewBox.height += 200;
            viewBox.width += 200;
        } else {
            viewBox.height -= 200;
            viewBox.width -= 200;
        }

        var viewBoxString = `${newViewBox.x} ${newViewBox.y} ${viewBox.width} ${viewBox.height}`;

        //Apply new viewBox coordinates.
        svg.setAttribute('viewBox', viewBoxString);
    });
}

function getPointFromEvent(event) {
    var point = { x: 0, y: 0 };
    
    if (event.targetTouches) {
        //If event is triggered by a touch, get the position of the first finger like so:
        point.x = event.targetTouches[0].clientX;
        point.y = event.targetTouches[0].clientY;
    } else { 
        //Else get the mouse position like so:
        point.x = event.clientX;
        point.y = event.clientY;
    }

    return point;
}

function onPointerMove(event) {

    if (!isPointerDown)
        return;

    //Prevent user from making a selection on the page.
    event.preventDefault();

    const pointerPosition = getPointFromEvent(event);

    const scaleRatio = initialViewBox.height / viewBox.height;

    //Calculate the distance between the pointer origin and the current position.
    //Ratio accounts for viewBox height/width change in case of resize.
    //Meanwhile scaleRatio adjust the "step size" or "move sensitivity" according to the scale/zoom. 
    newViewBox.x = viewBox.x - ((pointerPosition.x - pointerOrigin.x) * ratio / scaleRatio);
    newViewBox.y = viewBox.y - ((pointerPosition.y - pointerOrigin.y) * ratio / scaleRatio);

    //Avoids a jump of the image on the first move by centering it.
    if (isFirstClick) {
        newViewBox.x = viewBox.width/2;
        newViewBox.y = viewBox.height/2;

        isFirstClick = false;
    }

    const viewBoxString = `${newViewBox.x} ${newViewBox.y} ${viewBox.width} ${viewBox.height}`;

    //Apply new viewBox coordinates.
    svg.setAttribute('viewBox', viewBoxString);
}

function onPointerDown(event) {
    isPointerDown = true;

    //Get the starting click/touchdown on the start of the drag.
    var pointerPosition = getPointFromEvent(event);
    pointerOrigin.x = pointerPosition.x;
    pointerOrigin.y = pointerPosition.y;
}

function onPointerUp() {
    isPointerDown = false;

    //Save the new viewBox coordinates based on the last pointer position.
    viewBox.x = newViewBox.x;
    viewBox.y = newViewBox.y;
}




function BlazorDownloadFile(filename, contentType, content) {
    //Create the URL
    const file = new File([content], filename, { type: contentType });
    const exportUrl = URL.createObjectURL(file);

    // Create the <a> element and click on it
    const a = document.createElement("a");
    document.body.appendChild(a);
    a.href = exportUrl;
    a.download = filename;
    a.target = "_self";
    a.click();

    // We don't need to keep the object URL, let's release the memory
    // On older versions of Safari, it seems you need to comment this line...
    URL.revokeObjectURL(exportUrl);
}