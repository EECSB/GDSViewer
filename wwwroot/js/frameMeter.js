//
//The frame rate, for the Debug info switch in the settings: how many frames the page showed in the last second,
//a graph of the last ten, and the longest frame in that second, in a small panel in the drawing's top left.
//
//**Counted off requestAnimationFrame**, which the browser calls once for every frame it shows. That makes it the
//rate of the page, not of one view's own loop: a 2D pan whose frames take a tenth of a second to raster reads
//10, and so does a 3D scene too heavy to keep up. A page that is idle still shows frames, so a still view reads
//the display's own rate - which is the honest answer to whether it is keeping up.
//
//**And written straight into its panel, not through Blazor.** The panel is an empty div each view renders while
//the switch is on, and this fills it four times a second. A readout that went through C# would be adding work
//to what it measures, on exactly the interactions it is there to measure - the same reason the pointer readout
//is written in place. Blazor renders the div with nothing in it, so nothing here is anything it owns.
//
//The arithmetic is viewGeometry's - frameRate, meterScale and meterPoints - where Node tests it.
//
//Its own key in localStorage, the way the theme is: it is a choice about the app on this computer rather than
//about a file, so it has no place in a session or a history entry.
//

const FRAME_METER_KEY = 'gdsviewer.debug';

///The panel each view renders while the switch is on. Must match the id in Viewer2DSvg and Viewer3D.
const FRAME_METER_ID = 'frameMeter';

///How often the panel is brought up to date, and how many of those samples the graph holds: the last 10 s.
const FRAME_METER_EVERY = 250;
const FRAME_METER_KEPT = 40;

const FRAME_METER_SVG = 'http://www.w3.org/2000/svg';

const frameMeter = {
    running: false,

    //Timestamps of the frames in the last second and a little, oldest first.
    frames: [],

    //Frames a second, one a sample, oldest first.
    history: [],

    //The 3D scene's own frame count, read once a sample, for the last second and a little - see sampleScene.
    scene: [],

    request: 0,
    timer: null
};

///Whether the switch was left on, on this computer. Off when nothing was chosen or storage refuses.
function frameMeterChosen() {
    try {
        return localStorage.getItem(FRAME_METER_KEY) === 'on';
    }
    catch (e) {
        return false;
    }
}

///The switch: remembers the choice, and starts or stops counting.
function setFrameMeter(on) {
    try {
        let value = 'off';

        if (on === true)
            value = 'on';

        localStorage.setItem(FRAME_METER_KEY, value);
    }
    catch (e) { }

    if (on === true)
        startFrameMeter();
    else
        stopFrameMeter();
}

///
///Counts every frame from here on, and brings the panel up to date four times a second.
///
///**Nothing runs while it is off**: no loop, no timer. The count is kept across a change of view, so the graph
///carries on through one rather than starting again, which is the moment somebody switching views wants to see.
///
function startFrameMeter() {
    if (frameMeter.running)
        return;

    frameMeter.running = true;
    frameMeter.frames = [];
    frameMeter.history = [];
    frameMeter.scene = [];

    const counted = function (time) {
        if (!frameMeter.running)
            return;

        frameMeter.frames.push(time);
        frameMeter.request = requestAnimationFrame(counted);
    };

    frameMeter.request = requestAnimationFrame(counted);
    frameMeter.timer = setInterval(sampleFrameMeter, FRAME_METER_EVERY);
}

function stopFrameMeter() {
    frameMeter.running = false;

    cancelAnimationFrame(frameMeter.request);
    clearInterval(frameMeter.timer);

    frameMeter.timer = null;
    frameMeter.frames = [];
    frameMeter.history = [];
    frameMeter.scene = [];
}

///One sample: the second just gone, into the history and onto the panel.
function sampleFrameMeter() {
    const now = performance.now();

    //Only the last second is ever read, and one frame from before it, where its first wait began.
    let first = 0;

    while (first < frameMeter.frames.length - 1 && frameMeter.frames[first + 1] <= now - 1000)
        first++;

    if (first > 0)
        frameMeter.frames.splice(0, first);

    const rate = window.viewGeometry.frameRate(frameMeter.frames, now);

    frameMeter.history.push(rate.fps);

    if (frameMeter.history.length > FRAME_METER_KEPT)
        frameMeter.history.shift();

    drawFrameMeter(rate, sampleScene(now));
}

///
///The 3D scene's own count, read once a sample - see threeFrameStats in ThreeInterop.js. Null outside the 3D view.
///
///**A second number, because the first one counts the page.** The rate above is the frames the browser showed,
///which is the honest answer to whether the page keeps up and the same thing three's own Stats panel measures - but
///it cannot say whether the scene drew every one of those frames. This is the scene's own count over the same
///second, so the two can be read against each other: a scene keeping pace while it moves draws a frame for every
///frame shown, and a page held to 30 by the browser reads 30 in both. Standing still it reads nothing, since the
///scene is drawn only when something changes - see animate in ThreeInterop.js.
///
function sampleScene(now) {
    let stats = null;

    if (typeof window.threeFrameStats === 'function')
        stats = window.threeFrameStats();

    if (stats == null) {
        frameMeter.scene = [];

        return null;
    }

    //A scene built again counts from zero, which is no reading to measure the old ones against.
    const last = frameMeter.scene[frameMeter.scene.length - 1];

    if (last != null && stats.frames < last.frames)
        frameMeter.scene = [];

    frameMeter.scene.push({ time: now, frames: stats.frames });

    let first = 0;

    while (first < frameMeter.scene.length - 1 && frameMeter.scene[first + 1].time <= now - 1000)
        first++;

    if (first > 0)
        frameMeter.scene.splice(0, first);

    return {
        fps: window.viewGeometry.sceneRate(frameMeter.scene),
        calls: stats.calls,
        triangles: stats.triangles
    };
}

///
///Puts a sample on the panel, building the panel's insides the first time a view hands over an empty one.
///
///No panel is not a fault: the text view has none, and a view being switched to has not rendered its own yet.
///The count goes on regardless, and the next panel picks the graph up where it is.
///
function drawFrameMeter(rate, scene) {
    const panel = document.getElementById(FRAME_METER_ID);

    if (panel == null)
        return;

    if (panel.firstChild == null)
        buildFrameMeter(panel);

    const scale = window.viewGeometry.meterScale(frameMeter.history);
    const points = window.viewGeometry.meterPoints(frameMeter.history, FRAME_METER_KEPT, scale);

    panel.querySelector('.frameMeterRate').textContent = `${rate.fps} fps`;
    panel.querySelector('.frameMeterTop').textContent = `${scale} fps`;
    panel.querySelector('.frameMeterLongest').textContent = `${Math.round(rate.longest)} ms`;

    drawScene(panel, scene);

    const graph = panel.querySelector('.frameMeterGraph');

    graph.setAttribute('viewBox', `0 0 ${FRAME_METER_KEPT - 1} ${scale}`);

    const line = graph.querySelector('.frameMeterLine');
    const area = graph.querySelector('.frameMeterArea');

    if (frameMeter.history.length < 2) {
        line.removeAttribute('points');
        area.removeAttribute('d');

        return;
    }

    line.setAttribute('points', points);

    //The line closed down to the foot of the graph at both ends, so the area under it can be shaded.
    const first = FRAME_METER_KEPT - Math.min(frameMeter.history.length, FRAME_METER_KEPT);

    area.setAttribute('d', `M${first},${scale}L${points.split(' ').join('L')}L${FRAME_METER_KEPT - 1},${scale}Z`);
}

///
///The scene's rows, in the 3D view and nowhere else: what it drew in the last second, and the draw calls and
///triangles of its last frame. A rate of null is a scene with nothing yet to measure against.
///
function drawScene(panel, scene) {
    const drawn = panel.querySelector('.frameMeterScene');
    const calls = panel.querySelector('.frameMeterCalls');
    const triangles = panel.querySelector('.frameMeterTriangles');

    for (const value of [drawn, calls, triangles])
        value.parentElement.hidden = scene == null;

    if (scene == null)
        return;

    let rate = '...';

    if (scene.fps != null)
        rate = `${scene.fps} fps`;

    drawn.textContent = rate;
    calls.textContent = String(scene.calls);
    triangles.textContent = window.viewGeometry.shortCount(scene.triangles);
}

///A name and a value on one line of the panel, the value kept for drawFrameMeter to fill.
function meterRow(panel, name, valueClass) {
    const row = document.createElement('div');

    row.className = 'frameMeterRow';

    const label = document.createElement('span');

    label.textContent = name;
    row.appendChild(label);

    const value = document.createElement('span');

    value.className = valueClass;
    row.appendChild(value);

    panel.appendChild(row);

    return row;
}

///The rate, the graph with its scale, the longest frame and the scene's rows - built once per panel.
function buildFrameMeter(panel) {
    const rate = document.createElement('div');

    rate.className = 'frameMeterRate';
    panel.appendChild(rate);

    const graph = document.createElementNS(FRAME_METER_SVG, 'svg');

    graph.setAttribute('class', 'frameMeterGraph');
    graph.setAttribute('preserveAspectRatio', 'none');

    const area = document.createElementNS(FRAME_METER_SVG, 'path');

    area.setAttribute('class', 'frameMeterArea');
    graph.appendChild(area);

    const line = document.createElementNS(FRAME_METER_SVG, 'polyline');

    line.setAttribute('class', 'frameMeterLine');
    graph.appendChild(line);

    panel.appendChild(graph);

    const scale = document.createElement('div');

    scale.className = 'frameMeterScale';

    const top = document.createElement('span');

    top.className = 'frameMeterTop';
    scale.appendChild(top);

    const span = document.createElement('span');

    span.textContent = 'last 10 s';
    scale.appendChild(span);

    panel.appendChild(scale);

    meterRow(panel, 'Longest frame', 'frameMeterLongest');

    //Hidden until there is a scene to describe - see drawScene.
    for (const [name, valueClass] of [['Scene drawn', 'frameMeterScene'], ['Draw calls', 'frameMeterCalls'], ['Triangles', 'frameMeterTriangles']])
        meterRow(panel, name, valueClass).hidden = true;
}

if (frameMeterChosen())
    startFrameMeter();
