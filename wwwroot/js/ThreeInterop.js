//Self-hosted three.js interop (no npm/bundler). This is an ES module, so the bare 'three' and
//'three/addons/' specifiers below are resolved by the import map in index.html, which points them at
//the vendored copies under wwwroot/lib/three.

//Includes///////////////////////////////////////

import * as THREE from 'three';

import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { OBJExporter } from 'three/addons/exporters/OBJExporter.js';
import { STLExporter } from 'three/addons/exporters/STLExporter.js';

import { VRButton } from 'three/addons/webxr/VRButton.js';
import { ARButton } from 'three/addons/webxr/ARButton.js';

import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

/////////////////////////////////////////////////



//Global variables///////////////////////////////

let app;

/////////////////////////////////////////////////



//Constants//////////////////////////////////////

//The desktop view looks at the layout in its own units: a sky130 cell is a few thousand database
//units across, so the camera sits back at 2000 and needs a far plane to match.
const DESKTOP_NEAR = 1;
const DESKTOP_FAR = 50000;
const DESKTOP_CLEAR_COLOR = 0xDDDDDD;

//WebXR measures the world in meters, which those same coordinates are not. Left at 1:1 a cell would
//be kilometers wide and a headset would start inside a single polygon, so the layout is scaled to fit
//a box roughly this big and placed at arm's length in front of the viewer.
const XR_TARGET_SIZE = 0.5;
const XR_DISTANCE = 1.0;

//A VR reference space is floor-relative, so the model is lifted to about chest height. An AR one is
//pinned to wherever the headset started, where zero is already eye level, so it drops slightly.
const XR_VR_HEIGHT = 1.2;
const XR_AR_HEIGHT = -0.2;

//Scaled that small, the desktop near plane would clip anything within arm's reach and the desktop far
//plane would spend all the depth precision on empty space.
const XR_NEAR = 0.1;
const XR_FAR = 1000;

//Every extrusion is built in the layout's own XY plane and then tipped onto its back, which is what
//turns a flat layout into a stack with the layer offsets running up Y. This is the value the view has
//always used - it is a little short of a right angle (pi/2 is 1.5708), so the stack leans very slightly
//towards the camera. Named here because labels have to be placed by the same amount to land on the
//geometry they name.
const LAYOUT_ROTATION_X = 1.5;

//A label is drawn to a canvas at this pixel height, then mapped onto a camera-facing quad sized in
//layout units. Drawing bigger than it usually appears keeps the glyphs sharp when the camera comes in
//close, and the padding leaves room for the halo to not be clipped at the edges.
const LABEL_PIXEL_HEIGHT = 64;
const LABEL_PIXEL_PADDING = 10;
const LABEL_HALO_WIDTH = 7;
const LABEL_FONT = `bold ${LABEL_PIXEL_HEIGHT}px sans-serif`;

//How far a label floats above the top of the layer it names, as a fraction of that layer's own depth.
//A fraction rather than a fixed distance so it stays in proportion to the slab: enough to sit clear of
//the surface, not so much that it drifts towards the layer above when the stack is closed right up.
const LABEL_CLEARANCE = 0.2;

/////////////////////////////////////////////////



//Helpers////////////////////////////////////////

//Where a label hangs from lives in js/viewGeometry.js, which has no DOM or three.js dependency so that
//it can be unit-tested under Node. It is a plain script loaded before this module, so it is on window
//by the time anything here runs.

/////////////////////////////////////////////////



//C# Interop Functions///////////////////////////

//Init./////////////////////

window.registerThree = function () {

    //Leaving the 3D view and coming back mounts a new container, so this runs again. The previous viewer
    //has to be taken down first: its animation loop would otherwise keep rendering every frame to a
    //canvas no longer in the document, its resize handler would stay on window, and its WebGL context
    //would stay alive - and a browser allows only a handful of those before it starts dropping the
    //oldest, which kills the view that is on screen.
    if (app != null)
        app.dispose();

    const containerElement = document.getElementById("container");

    if (containerElement == null)
        return;

    //Fractionally here too, for the same reason as resizeToContainer: clientWidth and clientHeight round,
    //so the first canvas was up to a pixel short until something happened to trigger a resize.
    const box = containerElement.getBoundingClientRect();

    let settings = {
        containerElement: containerElement,
        containerSizeX: box.width,
        containerSizeY: box.height
    };

    app = new Viewer3D(settings);
}

////////////////////////////

//
//Every entry point below can be reached before there is a viewer to talk to, so each one checks.
//
//The shell's OnAfterRenderAsync runs before the view it just mounted has had its own first render, and
//that first render is what calls registerThree. Restoring a session into the 3D view walks straight into
//it: the shell applies the session's settings and asks for a redraw, and app is still undefined - which
//came out as "Cannot read properties of undefined (reading 'draw')" on startup, and only on startup,
//since by the time anything is switched by hand the viewer exists.
//
//Returning quietly is the right answer rather than merely a safe one: the view draws itself as soon as it
//registers, so there is nothing to lose by ignoring a request that arrives before it can.
//
window.startRender3DInterOp = function () {
    if (app == null)
        return;

    app.startRender3D();
}


window.drawInterOp = function (data) {
    if (app == null)
        return;

    app.draw(data);
}


///
///Moves the layers already in the scene to new heights, without building anything.
///
///`offsets` is one height per layer, in the stacking order - so a mesh tagged with its layer's place looks
///its own new height up by index. See Viewer3D.restackLayers for why the spacing slider comes through here
///rather than through a redraw: a redraw is the whole scene marshalled and rebuilt, and this is a Y write
///per object.
///
///Shifted by the difference rather than assigned, because a label's height is its layer's offset plus a hang
///and a clearance - so what is stored on each object is the offset that went *into* it, and the delta is the
///only thing that can be applied to both a slab and a sprite with one line.
///
window.restackLayers = function (offsets) {
    if (app == null || offsets == null)
        return;

    app.restack(offsets);
}


window.changeBackgroundInterOp = function (backgroundName) {
    if (app == null)
        return;

    app.changeBackground(backgroundName);
}

window.cinematicViewInterOp = function (cinematicViewToggleInterOp) {
    if (app == null)
        return;

    app.cinematicView(cinematicViewToggleInterOp);
}

window.download3DModelInterOp = function (CurrentlySelectedFileName, ModelDownloadFileType) {
    if (app == null)
        return;

    app.download3DModel(CurrentlySelectedFileName, ModelDownloadFileType);
}

//
//Where the camera is, so a session can put it back - the pair of the 2D view's OnViewSettled.
//
//Held at module scope rather than on the viewer, because leaving the 3D view and coming back builds a new
//viewer and the handle belongs to the component, which outlives it.
//
let cameraKey = null;
let cameraSettling = null;

window.registerCamera = function (view) {
    cameraKey = view;
}

//
//**On settle, and a long one**, for the reasons written out at the 2D view's reportViewSettledWhenStill:
//nothing is waiting on this, it ends in a session being written, and an orbit is a stream of events where
//only the last one is worth keeping.
//
//**Never during Admire.** That moves the camera itself, frame by frame, around wherever the layout happens
//to be - so what it would save is an arbitrary point on a circle, and the next visit would open on it. It
//sets camera.position directly and never touches the controls, so no event arrives from it either; this is
//belt and braces.
//
function reportCameraWhenStill() {
    if (cameraSettling != null)
        clearTimeout(cameraSettling);

    cameraSettling = setTimeout(function () {
        cameraSettling = null;

        if (cameraKey == null || app == null || app.cinematicViewToggle)
            return;

        const at = app.camera.position;
        const looking = app.controls.target;

        cameraKey.invokeMethodAsync('OnCameraSettled', at.x, at.y, at.z, looking.x, looking.y, looking.z);
    }, 1000);
}

//
//Puts the camera back where a session says it was left.
//
//Six numbers rather than three: where the camera is says nothing about which way it is pointing, and the
//orbit target is what OrbitControls turns around. Restoring the position alone leaves you looking at the
//origin from somewhere you never chose to be.
//
window.applyCameraInterOp = function (text) {
    if (app == null || typeof text !== 'string')
        return false;

    const parts = text.trim().split(/\s+/).map(Number);

    if (parts.length !== 6 || parts.some(one => !isFinite(one)))
        return false;

    app.camera.position.set(parts[0], parts[1], parts[2]);
    app.controls.target.set(parts[3], parts[4], parts[5]);

    //Which is what actually moves the camera to look at the target, and what makes the next drag start
    //from here rather than from where the controls last thought they were.
    app.controls.update();

    return true;
}

//
//Puts the whole stack back in the middle of the window, for when it is no longer there.
//
//**The opening angle, not the current one.** This looks straight down at the middle of the layout rather
//than framing it from wherever the camera happens to be - a view somebody has orbited under the floor is
//exactly the state this button exists to get out of, and centering it in place would leave it there.
//
//**The distance is worked out from the field of view rather than picked.** Half the layout has to fit
//inside half the angle, and it has to fit that way on both axes - the wider of the two answers is the one
//that shows all of it, since a camera framed on height alone crops a layout that is wider than it is tall.
//The stack's own depth is added on top, because the near face is that much closer than the middle is. A
//tenth over, so nothing sits against the edge, which is the margin the 2D fit uses.
//
//The opening position - z at 2000, looking at the origin - is not this. It is a fixed guess that suits a
//layout sitting on the origin at about the size of a standard cell, and a file drawn anywhere else opens
//with the stack off the side of the window. That is what somebody presses this to fix.
//
window.centerCameraInterOp = function () {
    if (app == null || app.camera == null || app.controls == null || app.chipObjectsGroup == null)
        return false;

    const bounds = new THREE.Box3().setFromObject(app.chipObjectsGroup);

    if (bounds.isEmpty())
        return false;

    const middle = bounds.getCenter(new THREE.Vector3());
    const size = bounds.getSize(new THREE.Vector3());

    const half = (app.camera.fov / 2) * (Math.PI / 180);
    const spread = Math.tan(half);

    if (!(spread > 0))
        return false;

    const forHeight = (size.y / 2) / spread;
    const forWidth = (size.x / 2) / (spread * app.camera.aspect);

    const away = (Math.max(forHeight, forWidth) * 1.1) + (size.z / 2);

    //A layout with no extent at all - one label and nothing else - would put the camera inside it.
    if (!isFinite(away) || !(away > 0))
        return false;

    app.camera.position.set(middle.x, middle.y, middle.z + away);
    app.controls.target.copy(middle);

    app.controls.update();

    //
    //Said rather than left to the controls.
    //
    //update() dispatches change only when it decides the camera moved, and the report is what puts this
    //framing into the session and the address. Pressing center and coming back to the old view would be a
    //quiet way to lose it, and the timer behind this is debounced, so saying it twice costs nothing.
    //
    reportCameraWhenStill();

    return true;
}

/////////////////////////////////////////////////



class Viewer3D {

    //Initialize//////////////////////////////////////

    constructor(settings) {
        //Add settings.
        this.settings = settings;

        //Initialize the 3D viewer by adding all the needed compoents(renderer, scene, camera, ...).
        this.initializeViewer();

        //Setsup the initial scene by loading the background and "drawing" the first scene.
        this.setupInitialScene();
    }

    initializeViewer() {
        //Init. variables//////////

        this.cinematicViewToggle = false;

        //Whether the running session composites onto a camera feed, which changes both how the layout is
        //positioned and whether anything may be drawn behind it.
        this.isArSession = false;
        this.backgroundBeforeXr = null;

        //The Enter VR and Enter AR buttons, which three.js builds and this viewer only places.
        this.xrButtons = [];

        //////////////////////////

        //Create and init. the renderer.
        //alpha is needed so the canvas has a channel to be transparent through at all - without it an
        //AR session can only ever composite an opaque frame over the camera feed.
        this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
        this.renderer.setSize(this.settings.containerSizeX, this.settings.containerSizeY);
        this.renderer.setClearColor(DESKTOP_CLEAR_COLOR, 1);
        this.renderer.xr.enabled = true;//Enable for VR and AR.
        //
        this.settings.containerElement.appendChild(this.renderer.domElement);

        //Create a scene
        this.scene = new THREE.Scene();

        //Create and init. a camera and add it to the scene.
        this.camera = new THREE.PerspectiveCamera(100, this.settings.containerSizeX / this.settings.containerSizeY, DESKTOP_NEAR, DESKTOP_FAR);
        this.camera.position.z = 2000;
        this.scene.add(this.camera);

        //Create objects group and add it to the scene.
        this.chipObjectsGroup = new THREE.Group();
        this.scene.add(this.chipObjectsGroup);

        //Create ambient light and add it to the scene.
        const light = new THREE.AmbientLight(0x404040);//soft white light
        this.scene.add(light);

        //Create a directional light and add it to scene.
        const directionalLight = new THREE.DirectionalLight(0xffffff);
        directionalLight.position.set(3, 3, 2);
        this.scene.add(directionalLight);

        //Create controls.
        this.controls = new OrbitControls(this.camera, this.renderer.domElement);

        //And tell C# where the camera came to rest, so the session can put it back. change rather than end,
        //because a wheel is a zoom with no gesture to end - the settle below is what makes it one report.
        this.controls.addEventListener('change', reportCameraWhenStill);

        //Init. a texture loader. It will get used by changeBackground().
        this.textureLoader = new THREE.TextureLoader();

        this.cinematicSettings = {
            chipCenterPoint: new THREE.Vector3(),
            camera_offset: { x: 10000, y: 10000, z: 10000 },
            camera_speed: 0.1,
            clock: new THREE.Clock()//,
            //time: 0
        };



        //Add VR button.
        const vrButton = VRButton.createButton(this.renderer);

        //three.js builds this button itself and gives it only its own label, which reads "VR NOT SUPPORTED"
        //on a desktop without saying what would support it.
        vrButton.title = 'Walk around the layout in VR. Needs a headset, and a browser that supports WebXR';


        //Add AR button.
        //No requiredFeatures on purpose. It used to ask for 'hit-test', which nothing here uses - and a
        //required feature the device cannot provide makes the browser refuse the session outright, so AR
        //failed to start on hardware that would otherwise have run it. If surface placement is added
        //later, hit-test comes back together with the code that consumes it.
        const arButton = ARButton.createButton(this.renderer);

        arButton.title = 'Place the layout in the room around you in AR. Needs a phone or headset with WebXR';

        this.addXrButton(vrButton);
        this.addXrButton(arButton);



        //Entering and leaving a headset needs the layout rescaled and the camera planes moved, so both
        //transitions are handled rather than assuming the desktop setup carries over.
        //Kept rather than bound inline, because removing a listener needs the same function object that
        //was added and bind() returns a new one each call.
        this.onXrSessionStartHandler = this.onXrSessionStart.bind(this);
        this.onXrSessionEndHandler = this.onXrSessionEnd.bind(this);

        this.renderer.xr.addEventListener('sessionstart', this.onXrSessionStartHandler);
        this.renderer.xr.addEventListener('sessionend', this.onXrSessionEndHandler);


        //Watching the container, not the window.
        //
        //The view is sized by the layout rather than by a fixed height, so its box changes without the
        //window changing with it - the sidebar taking a different width, a control above it wrapping, the
        //view being switched to. A window listener hears none of those, and the canvas kept the size it
        //had when it was built while the box around it moved on. This covers the window too, since a
        //smaller window is a smaller container.
        this.resizeObserver = new ResizeObserver(() => this.resizeToContainer());
        this.resizeObserver.observe(this.settings.containerElement);

        //The observer delivers a first entry on its own, but not until after this frame - and the initial
        //size is wanted before anything is drawn.
        this.resizeToContainer();
    }

    ///Puts one of three.js's own XR buttons into the row the view lays out, ahead of the one that is
    ///already in there.
    ///
    ///**Its position has to be taken off it.** createButton returns an element styled absolute and pinned
    ///twenty pixels off the bottom of whatever contains it, and it goes on setting its own left and width
    ///afterwards - isSessionSupported resolves a frame or two later and the button relabels and resizes
    ///itself then. static is what makes all of that inert: left is not a thing a static element has, so
    ///the later assignment lands on a property nothing reads and the row keeps control.
    addXrButton(button) {
        const row = document.getElementById('xrButtons');

        if (row == null)
            return;

        //Kept so dispose can take them out again. The row is Blazor's rather than this viewer's, so it
        //outlives a rebuild - and a button left in it would be joined by a second one that does nothing,
        //wired as it is to a renderer that has already given up its context.
        this.xrButtons.push(button);

        button.style.position = 'static';
        button.style.margin = '0';

        //
        //**And its colors, for the same reason and by the same means.**
        //
        //createButton styles it white on a tenth of black at half opacity, which is a control you can read
        //on a dark scene and cannot on a light one - and this app's default scene is light gray. The two
        //buttons the page owns are a dark panel now; these are the same row and have to be the same
        //button, or three controls side by side come in two designs.
        //
        //Cleared rather than restyled: the class the row's own buttons use says all of it, and inline
        //styles beat a class. What is left inline is what three.js goes on setting afterwards - left and
        //width, both inert on a static element.
        //
        //A button that is not supported says so in words. Faint *and* unreadable said it twice and only
        //one of those was on purpose.
        //
        const ours = document.getElementById('openOnPhone');

        button.classList.add('xrButton');

        //Blazor's isolated CSS keys every rule on an attribute it stamps onto the elements it renders.
        //This button was made by three.js and has none, so the class alone would match nothing - it is
        //copied off the button beside it, which is the one element in this row that Blazor did render.
        if (ours != null)
        {
            for (const attribute of ours.getAttributeNames())
            {
                if (attribute.startsWith('b-'))
                    button.setAttribute(attribute, '');
            }
        }

        button.style.background = '';
        button.style.backgroundColor = '';
        button.style.border = '';
        button.style.color = '';
        button.style.font = '';
        button.style.opacity = '';
        button.style.boxShadow = '';

        //Before ours rather than after, so VR and AR come first in the row and in the tab order both.
        //A null reference is what appendChild does anyway, which is the right fallback.
        row.insertBefore(button, ours);
    }

    setupInitialScene() {
        //Init. background.
        this.changeBackground("none");

        //Start animation/render loop.
        //this.startRender3D();//Will be started by interop call from C# by Viewer3D.razor component.
    }

    /////////////////////////////////////////////////////////



    /////////////////////////////////////////////////////////

    startRender3D() {
        //requestAnimationFrame(this.startRender3D.bind(this));//setAnimationLoop() needs to be used for VR.
        this.renderer.setAnimationLoop(this.animate.bind(this));
    }

    animate() {
        //Perform animations of objects or movements of camera.
        this.runCinematicView();

        //Call renderer to render the scene.
        this.renderer.render(this.scene, this.camera);
    }

    //XR///////////////////////////////////////////////////////

    onXrSessionStart() {
        const session = this.renderer.xr.getSession();

        //A VR session reports an opaque environment; anything else is compositing onto a camera feed.
        this.isArSession = session != null && session.environmentBlendMode !== 'opaque';

        if (this.isArSession) {
            //Nothing may be drawn behind the layout or it covers the real world. The clear color keeps
            //its value and loses its alpha, so leaving the session restores the desktop backdrop.
            this.backgroundBeforeXr = this.scene.background;
            this.scene.background = null;
            this.renderer.setClearColor(DESKTOP_CLEAR_COLOR, 0);
        }

        this.camera.near = XR_NEAR;
        this.camera.far = XR_FAR;
        this.camera.updateProjectionMatrix();

        this.fitForXr();
    }

    onXrSessionEnd() {
        //Back to the layout's own units, centered on the origin where the orbit controls expect it.
        this.chipObjectsGroup.scale.set(1, 1, 1);
        this.chipObjectsGroup.position.set(0, 0, 0);

        this.camera.near = DESKTOP_NEAR;
        this.camera.far = DESKTOP_FAR;
        this.camera.updateProjectionMatrix();

        if (this.isArSession) {
            this.renderer.setClearColor(DESKTOP_CLEAR_COLOR, 1);
            this.scene.background = this.backgroundBeforeXr;
            this.backgroundBeforeXr = null;
        }

        this.isArSession = false;
    }

    //Scales the layout down to something a person can stand next to and puts it in front of them.
    fitForXr() {
        const group = this.chipObjectsGroup;

        //Measure at the layout's own scale, so re-fitting after a new file does not compound.
        group.scale.set(1, 1, 1);
        group.position.set(0, 0, 0);

        const bounds = new THREE.Box3().setFromObject(group);

        if (bounds.isEmpty())
            return;

        const size = bounds.getSize(new THREE.Vector3());
        const largest = Math.max(size.x, size.y, size.z);

        if (largest <= 0)
            return;

        const scale = XR_TARGET_SIZE / largest;
        group.scale.setScalar(scale);

        //GDS coordinates start at a corner rather than the middle, so the layout is centered before it is
        //moved - otherwise "one meter away" would be measured from an arbitrary edge.
        const center = bounds.getCenter(new THREE.Vector3()).multiplyScalar(scale);

        let height = XR_VR_HEIGHT;

        if (this.isArSession)
            height = XR_AR_HEIGHT;

        group.position.set(-center.x, -center.y + height, -center.z - XR_DISTANCE);
    }

    /////////////////////////////////////////////////////////

    runCinematicView() {
        //The headset drives the camera during a session, so moving it from here would fight the device.
        if (this.renderer.xr.isPresenting)
            return;

        if (this.cinematicViewToggle) {
            const clock = this.cinematicSettings.clock;
            const chipCenterPoint = this.cinematicSettings.chipCenterPoint;
            const camera_offset = this.cinematicSettings.camera_offset;
            const camera_speed = this.cinematicSettings.camera_speed;

            clock.getDelta();
            const time = clock.elapsedTime.toFixed(2);

            this.camera.position.x = chipCenterPoint.x + camera_offset.x * (Math.sin(time * camera_speed));
            this.camera.position.z = chipCenterPoint.z + (camera_offset.z * Math.cos(time * 0.1)) * (Math.cos(time * camera_speed));
            this.camera.position.y = chipCenterPoint.y + camera_offset.y * (Math.cos(time * 0.05));

            this.camera.lookAt(chipCenterPoint.x, chipCenterPoint.y, chipCenterPoint.z);
        }
    }

    draw(data) {
        this.clearChipObjects();

        let polygons = data.elements;

        for (let polygon of polygons) {
            let points = polygon.points;

            if (points.length < 3) {
                console.warn("Skipping polygon, it should have more than 3 points, error or it's another type of element.", data)
                continue;
            }

            //One moveTo to start the outline, then a line to each point after it.
            //
            //This used to call moveTo for *every* point and then lineTo the same point again, which
            //produced a run of zero-length segments and silently dropped the first point: the outline
            //began at points[1]. A closed ring survives losing its first point, because the repeated
            //last point closes the same cycle - but an outline that is not explicitly closed, which is
            //what PathOutline returns, lost a corner. A four-corner rectangle came out as a triangle.
            const shape = new THREE.Shape();

            shape.moveTo(points[0].x, points[0].y);

            for (let i = 1; i < points.length; i++)
                shape.lineTo(points[i].x, points[i].y);

            //A hole is its own path rather than a channel cut into the outline. GDSII has to write one as
            //a channel - it has no hole of its own - and that is the shape a triangulator handles worst,
            //since the two sides of the channel lie on top of each other. Here it can just be said.
            for (const hole of polygon.holes || []) {
                if (hole.length < 3)
                    continue;

                const path = new THREE.Path();

                path.moveTo(hole[0].x, hole[0].y);

                for (let i = 1; i < hole.length; i++)
                    path.lineTo(hole[i].x, hole[i].y);

                shape.holes.push(path);
            }

            const extrudeSettings = {
                depth: polygon.layer.depth
            };

            const geometry = new THREE.ExtrudeGeometry(shape, extrudeSettings);
            const material = new THREE.MeshPhongMaterial({
                color: polygon.layer.color,
                wireframe: false,
                shininess: 150
            });


            const mesh = new THREE.Mesh(geometry, material);

            mesh.rotation.set(LAYOUT_ROTATION_X, 0.0, 0);
            mesh.position.set(0, polygon.layer.offset, 0);

            //Which layer it belongs to and the height it was built at, so restackLayers can move it without
            //the scene being handed over again. See restackLayers.
            mesh.userData.stackAt = polygon.layer.at;
            mesh.userData.stackOffset = polygon.layer.offset;

            this.chipObjectsGroup.add(mesh);
        }

        this.drawLabels(data.labels);

        //And the stack sits about its own middle - see centerStack.
        this.centerStack();

        //The fit depends on the geometry's bounding box, so loading a file or moving the layer slider
        //while in a headset has to redo it.
        if (this.renderer.xr.isPresenting)
            this.fitForXr();
    }

    //Releases everything this viewer holds, so a replacement can be built without the old one lingering.
    //Order matters at the top: stopping the animation loop before anything is disposed means no frame is
    //ever rendered against a half-released renderer.
    dispose() {
        this.renderer.setAnimationLoop(null);

        this.resizeObserver.disconnect();
        this.renderer.xr.removeEventListener('sessionstart', this.onXrSessionStartHandler);
        this.renderer.xr.removeEventListener('sessionend', this.onXrSessionEndHandler);

        this.clearChipObjects();

        this.controls.dispose();

        if (this.scene.background != null && this.scene.background.dispose)
            this.scene.background.dispose();

        this.renderer.dispose();

        //dispose() releases three's own objects but leaves the context to be collected whenever the
        //browser gets round to it. The limit is on live contexts, so it is given up explicitly.
        if (this.renderer.forceContextLoss)
            this.renderer.forceContextLoss();

        this.renderer.domElement.remove();

        for (const button of this.xrButtons)
            button.remove();

        this.xrButtons = [];
    }

    //Empties the group and releases what the GPU was holding for it. The layer-spacing slider redraws on
    //every input event, so leaving the old textures and buffers behind would pile them up as it moves.
    clearChipObjects() {
        for (let i = this.chipObjectsGroup.children.length - 1; i >= 0; --i) {
            const child = this.chipObjectsGroup.children[i];

            this.chipObjectsGroup.remove(child);

            if (child.geometry)
                child.geometry.dispose();

            //Every mesh and sprite here is given its own material, so nothing else is still using it.
            if (child.material) {
                if (child.material.map)
                    child.material.map.dispose();

                child.material.dispose();
            }
        }
    }

    ///
    ///Sets every drawn thing to the height its layer is now at.
    ///
    ///By the difference from the height it was built at, not by assignment: a slab's Y *is* its layer's
    ///offset, but a label's is that offset plus how much of the billboard hangs below its anchor plus a
    ///clearance off the surface - so the delta is the one thing that is right for both.
    ///
    ///Anything with no place recorded is left alone. That is the grid, the backdrop, and anything else in the
    ///group that is not layout: they have no layer, so they have no business moving with one.
    ///
    ///
    ///Puts the middle of the stack where the camera is looking, rather than its bottom.
    ///
    ///**Because spreading it used to walk it off the top of the screen.** The offsets run from 0 upwards, so
    ///pulling the layers apart grows the stack in one direction only: at the widest spacing the bundled
    ///transistor stands 5,600 units tall, and a camera 2,000 back with a 100 degree field of view sees about
    ///2,384 either side of what it is aimed at. Five of its nine layers were above the top edge. The spacing
    ///was even the whole time - it is the growing upward that made a nonsense of it.
    ///
    ///Moving the group rather than each object, which is what keeps this from disturbing anything: the per
    ///object heights, the delta arithmetic in restack, and a label's hang below its anchor are all untouched
    ///and still mean what they meant.
    ///
    ///Measured off what is actually drawn rather than off the offsets of every layer, so hiding the top
    ///layer re-centers on what is left instead of leaving a gap where it used to be.
    ///
    centerStack() {
        if (this.chipObjectsGroup == null)
            return;

        let low = null;
        let high = null;

        for (const drawn of this.chipObjectsGroup.children) {
            const at = drawn.userData.stackOffset;

            if (at == null)
                continue;

            if (low == null || at < low)
                low = at;

            if (high == null || at > high)
                high = at;
        }

        if (low == null)
            return;

        this.chipObjectsGroup.position.y = -((low + high) / 2);
    }

    restack(offsets) {
        if (this.chipObjectsGroup == null)
            return;

        for (const drawn of this.chipObjectsGroup.children) {
            const at = drawn.userData.stackAt;

            if (at == null || at < 0 || at >= offsets.length)
                continue;

            const was = drawn.userData.stackOffset;

            if (was == null)
                continue;

            drawn.position.y += offsets[at] - was;
            drawn.userData.stackOffset = offsets[at];
        }

        this.centerStack();

        //The fit is computed off the bounding box, which has just changed - and in a headset the whole scene
        //is placed by it, so pulling the stack open without refitting walks the layout off into the room.
        if (this.renderer.xr.isPresenting)
            this.fitForXr();
    }

    //Pin labels are camera-facing quads rather than extruded glyphs. A label has to stay readable from
    //any orbit angle, and text lying flat in the stack is edge-on from most of them - so a billboard
    //reads better here than geometry would, and it costs one quad instead of a mesh per glyph. It also
    //means no font has to be vendored: the browser draws the text, and the layout keeps its own units.
    drawLabels(labels) {
        if (!labels)
            return;

        for (let label of labels) {
            const sprite = this.buildLabelSprite(label);

            //Placed by hand through the same transform the extrusions carry, since a sprite ignores
            //rotation of its own: the anchor sits on the top face of its layer's slab, then the layout
            //tips onto its back and the layer's stacking offset lifts it.
            //
            //z is 0 rather than the layer's depth, which is the trap here. An extrusion is built running
            //from z 0 to z depth, and the tip maps local +Z onto world -Y - so the slab hangs *below* the
            //plane it was drawn on, and z depth is its underside. Anchoring there buried every label
            //inside the shape it names: measured on Mosfet.gds, a label sat at y 189.9 in a slab spanning
            //149.4 to 220.9.
            const position = new THREE.Vector3(label.x, label.y, 0);
            position.applyEuler(new THREE.Euler(LAYOUT_ROTATION_X, 0, 0));

            //How much of the billboard hangs below its own anchor, which is a matter of justification
            //rather than of height: the anchor is the sprite's top edge for a label justified to the top,
            //its bottom edge for one justified to the bottom, and the middle otherwise.
            //
            //Lifting by that much is what actually clears the geometry. Raising the *anchor* alone is not
            //enough and was the first attempt at this: Top is the format's default, so a label hung its
            //whole height back down through the slab it had just been lifted off - clear anchor, buried
            //text. Reading it off the sprite keeps the three justifications right without three cases.
            const hangsBelow = sprite.center.y * sprite.scale.y;

            //And then a gap, so the glyphs sit off the surface rather than resting on it - co-planar is
            //the case that flickers, and touching reads as painted on.
            position.y += label.offset + hangsBelow + (label.depth * LABEL_CLEARANCE);

            sprite.position.copy(position);

            //The same two, for the same reason. A label sits at its layer.s offset plus a hang and a gap, so
            //what is stored is the offset that went into it rather than where it ended up.
            sprite.userData.stackAt = label.at;
            sprite.userData.stackOffset = label.offset;

            this.chipObjectsGroup.add(sprite);
        }
    }

    buildLabelSprite(label) {
        const canvas = document.createElement('canvas');
        let context = canvas.getContext('2d');

        //Measured with the final font, or the canvas comes out too narrow and clips the text.
        context.font = LABEL_FONT;

        const textWidth = Math.ceil(context.measureText(label.text).width);

        canvas.width = textWidth + (LABEL_PIXEL_PADDING * 2);
        canvas.height = LABEL_PIXEL_HEIGHT + (LABEL_PIXEL_PADDING * 2);

        //Resizing a canvas resets its context, so the font has to be set again before drawing.
        context = canvas.getContext('2d');
        context.font = LABEL_FONT;
        context.textAlign = 'center';
        context.textBaseline = 'middle';

        //A white halo under the glyphs, the same trick the 2D view uses, so a label stays legible
        //against its own layer's color.
        context.lineWidth = LABEL_HALO_WIDTH;
        context.strokeStyle = '#ffffff';
        context.strokeText(label.text, canvas.width / 2, canvas.height / 2);

        context.fillStyle = label.color;
        context.fillText(label.text, canvas.width / 2, canvas.height / 2);

        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;

        const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true }));

        //Sized in layout units, keeping the canvas's aspect ratio so the text is not stretched.
        sprite.scale.set(label.height * (canvas.width / canvas.height), label.height, 1);

        //A sprite's center is the point it hangs from, in its own 0..1 space with Y up - which is exactly
        //what the PRESENTATION justification names, so it maps across without any arithmetic.
        sprite.center.set(window.viewGeometry.labelCenterX(label.horizontal), window.viewGeometry.labelCenterY(label.vertical));

        return sprite;
    }

    /////////////////////////////////////////////////////////



    //Other//////////////////////////////////////////////////

    cinematicView(cinematicViewToggleInterOp) {
        this.cinematicViewToggle = cinematicViewToggleInterOp;

        //Compute the bounding box of the group
        const bbox = new THREE.Box3().setFromObject(this.chipObjectsGroup);

        //Calculate the center point of the bounding box
        bbox.getCenter(this.cinematicSettings.chipCenterPoint);
    }


    //
    //Fits the drawing surface to the box it sits in.
    //
    //Measured fractionally, and with nothing taken off. There was a `- 3` here "to account for border",
    //and #container has no border - the pane two levels up carries it, outside this box entirely. On top of
    //that, offsetWidth and offsetHeight round to whole pixels while the flex chain above hands this a
    //fractional height, so the reading was already short before the three came off it. Measured: a 476.875
    //box with a 474 canvas in it, which is the 2.8px strip of container that showed under the scene.
    //
    resizeToContainer() {
        const box = this.settings.containerElement.getBoundingClientRect();
        const width = box.width;
        const height = box.height;

        //A view that is not on screen measures zero, and an aspect ratio of 0/0 is a NaN the projection
        //matrix never comes back from - the scene is simply gone once it is in there. The last good size
        //is kept instead, to be corrected when there is something to fit.
        if (width <= 0 || height <= 0)
            return;

        //The observer reports every change, including ones that round to the size already applied.
        //Resizing the renderer reallocates its buffers, so it is worth not doing twice for nothing.
        if (width === this.appliedWidth && height === this.appliedHeight)
            return;

        this.appliedWidth = width;
        this.appliedHeight = height;

        //Update the renderer size and aspect ratio.
        this.renderer.setSize(width, height);
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
    }

    changeBackground(backgroundName) {
        if (!backgroundName || backgroundName.toLowerCase() === "none") {
            this.scene.background = null;
            return;
        }

        //Relative, so the browser resolves it against the <base href> index.html sets - which is what
        //keeps it working when the app is served from a subdirectory.
        //
        //This was built from window.location.href, which only ever worked because the address had nothing
        //after the host: appending a path to a URL that carries a query string buries the path inside the
        //query, so once ?file= and ?view= arrived the image was fetched from a nonsense address, the
        //loader's callback never fired, and the background silently never appeared.
        const texture = this.textureLoader.load('resources/Images/Background/' + backgroundName, () => {
            const rt = new THREE.WebGLCubeRenderTarget(texture.image.height);
            rt.fromEquirectangularTexture(this.renderer, texture);
            this.scene.background = rt.texture;
        });
    }

    download3DModel(fileName, fileType) {
        let exporter;
        let data;

        switch (fileType) {
            case ".stl":
                exporter = new STLExporter();
                data = exporter.parse(this.scene);
                BlazorDownloadFile(fileName + '.stl', 'application/octet-stream', data);
                break;
            case ".obj":
                exporter = new OBJExporter();
                data = exporter.parse(this.scene);
                BlazorDownloadFile(fileName + '.obj', 'application/octet-stream', data);
                break
            case ".gltf":
                exporter = new GLTFExporter();
                exporter.parse(this.scene, function (gltf) {
                    data = JSON.stringify(gltf, null, 2);
                    BlazorDownloadFile(fileName + '.gltf', 'application/octet-stream', data);
                });
                break;
        }
    }

    /////////////////////////////////////////////////////////
}
