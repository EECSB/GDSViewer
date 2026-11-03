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

//Which view built it - see registerThree and unregisterThree.
let appOwner = 0;

/////////////////////////////////////////////////



//Constants//////////////////////////////////////

//Where the desktop planes start, before there is a layout to put them around. From the first frame with one
//they are worked out again on every frame - see fitDepthRange - because a fixed pair suits one size of layout:
//these two are a standard cell's, and every bundled design opened past the far one.
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

//How far the headset row has to stay off the axis indicator in the corner to count as clear of it, in pixels - the
//gap the column it becomes otherwise keeps between its own buttons. See fitXrButtons.
const XR_ROW_CLEARANCE = 8;

//Every extrusion is built in the layout's own XY plane and then tipped onto its back, which is what
//turns a flat layout into a stack with the layer offsets running up Y. Named here because labels have to
//be placed by the same amount to land on the geometry they name.
//
//**A right angle exactly, and it has to be one.** This read 1.5 for a long time, which is 4.06 degrees
//short of pi/2, so every layout in this view leaned that far towards the camera. The comment that used to
//sit here recorded the lean as a property of the view rather than as the typo it was.
//
//It did not stay a display quirk. Three's exporters write world transforms, so the lean went into every
//STL, OBJ and GLTF the app produced - and a model tilted off level has no flat face anywhere in it.
//Measured against an independent extrusion of the same cell, the exported sky130_fd_sc_hd__nand2_1 had
//2966 tilted facets and not one horizontal cap, and its Z took over two hundred continuous values where
//the layer heights allow exactly ten. Every facet normal in it was sin(1.5), -cos(1.5), which is what
//pointed at this line.
//
//A lean is something a camera does. It is not something geometry carries.
const LAYOUT_ROTATION_X = Math.PI / 2;

//A label is drawn to a canvas at this pixel height, then mapped onto a camera-facing quad sized in
//layout units. Drawing bigger than it usually appears keeps the glyphs sharp when the camera comes in
//close, and the padding leaves room for the halo to not be clipped at the edges.
const LABEL_PIXEL_HEIGHT = 64;
const LABEL_PIXEL_PADDING = 10;
const LABEL_HALO_WIDTH = 7;
const LABEL_FONT = `bold ${LABEL_PIXEL_HEIGHT}px sans-serif`;

//
//The sheet a drawing's label pictures are packed onto: this wide, and as tall as they need up to as tall
//again, after which another is started. See packLabelSheets.
//
//The gap is left around every picture on it. A GPU draws a far-off label from a smaller copy of the sheet,
//each half the size of the one before, and every halving blurs a picture's edge a little further out - so
//without room around it, a label seen from far away would pick up the edge of its neighbor.
//
const LABEL_SHEET_SIZE = 2048;
const LABEL_SHEET_GAP = 6;

//
//How a label is drawn: a quad turned to face the camera and sized in layout units, the way a sprite is - but
//one of many in a single draw, each label's anchor, size, hang point and part of the sheet coming in per
//instance. See drawLabels.
//
//**A sprite's own arithmetic**, so a label lands where its sprite did. The anchor is carried into the
//camera's space, and the corner is put off it there by the label's size and by how far its hang point is from
//the middle. Times the scale the mesh is drawn at, which is what a headset changes - see fitForXr.
//
const LABEL_VERTEX_SHADER = `
attribute vec3 anchor;
attribute vec2 size;
attribute vec2 hang;
attribute vec4 cell;

varying vec2 picture;

void main() {
    picture = cell.xy + (uv * cell.zw);

    vec4 placed = modelViewMatrix * vec4(anchor, 1.0);
    vec2 scale = vec2(length(modelMatrix[0].xyz), length(modelMatrix[1].xyz));

    placed.xy += (position.xy - (hang - vec2(0.5))) * size * scale;

    gl_Position = projectionMatrix * placed;
}`;

//The picture as it is, put into the color space the canvas is shown in - which is what a sprite does too.
const LABEL_FRAGMENT_SHADER = `
uniform sampler2D sheet;

varying vec2 picture;

void main() {
    gl_FragColor = texture2D(sheet, picture);

    #include <encodings_fragment>
}`;

//How far a label floats above the top of the layer it names, as a fraction of that layer's own depth.
//A fraction rather than a fixed distance so it stays in proportion to the slab: enough to sit clear of
//the surface, not so much that it drifts towards the layer above when the stack is closed right up.
const LABEL_CLEARANCE = 0.2;

//The corner axis indicator: the square of canvas it is drawn into, how far that square sits off the two
//edges it hugs, and the arm and letter sizes inside a camera that frames AXES_EXTENT either side of the
//origin. All in the indicator's own units - it has no scale in common with the layout.
const AXES_VIEWPORT = 88;
const AXES_MARGIN = 12;
const AXES_ARM = 1;
const AXES_HEAD_LENGTH = 0.26;
const AXES_HEAD_RADIUS = 0.09;
const AXES_LETTER_AT = 1.42;
const AXES_LETTER_SIZE = 0.62;
const AXES_EXTENT = 1.8;
const AXES_CAMERA_BACK = 10;
const AXES_LETTER_PIXELS = 64;

///
///Which way each of the *layout's* axes points in this scene, and the color that names it.
///
///**The scene is not in the layout's axes, so an AxesHelper would letter this wrong.** Every extrusion is
///built flat in the layout's own XY and then tipped by LAYOUT_ROTATION_X, which lands the layout's Y along
///the scene's +Z and leaves the process stack - the layout's Z, the direction layer offsets grow along -
///running up the scene's +Y. Three's own names for those directions are Y and Z respectively, and neither
///is the name anybody reading a layout uses for them.
///
///So each arm is *placed* by the scene direction and *lettered* by the layout axis that direction carries,
///and the colors follow the letter rather than the direction: the usual X red, Y green, Z blue. What the
///indicator reports is therefore the same X, Y and Z the 2D view's ruler and the coordinate readout do.
///
const AXES = [
    { letter: 'X', direction: new THREE.Vector3(1, 0, 0), color: '#d64545' },
    { letter: 'Y', direction: new THREE.Vector3(0, 0, 1), color: '#3f9d4f' },
    { letter: 'Z', direction: new THREE.Vector3(0, 1, 0), color: '#3f6fd6' }
];

///
///Turns every triangle in an unindexed geometry the other way round.
///
///Paired with the axis swap in buildForExport. Swapping two axes puts the geometry exactly where it should
///be and leaves every face pointing the wrong way, because which side of a triangle is outside is said by
///the order of its corners and by nothing else. Exchanging the first and last of the three is the whole of
///it.
///
///Unindexed only: three positions per triangle, so the corners are the coordinates themselves. Indexed
///geometry would need its index turned instead, and buildForExport unindexes first for this reason.
///
function reverseWinding(geometry) {
    const position = geometry.getAttribute('position');
    const points = position.array;

    for (let at = 0; at + 9 <= points.length; at += 9) {
        for (let axis = 0; axis < 3; axis++) {
            const held = points[at + axis];

            points[at + axis] = points[at + 6 + axis];
            points[at + 6 + axis] = held;
        }
    }

    position.needsUpdate = true;
}

//
//How a layer answers being pointed at: three beats over a second and a quarter. See pulseLayer.
//
//Long enough to be seen as three rather than as one flicker, short enough that it is over before anybody
//wonders whether they broke something. The strength is emissive on a lit material, so it brightens the
//layer's own color rather than replacing it - which is the point, since the color is how the row and the
//slab are matched up in the first place.
//
const PULSE_BEATS = 3;
const PULSE_MS = 1250;
const PULSE_STRENGTH = 0.55;

//
//The frame flashed round what the cell tree put the camera on - see markBox: the color the 2D view marks what is chosen
//with, so the two views say "this one" the same way, beating as a layer's pulse does.
//
const MARK_COLOR = 0xd81b60;

//
//**What a layer's surface looks like**, worked out where it is drawn rather than painted on: a slab has no texture
//coordinates, and a picture stretched over a shape a millimeter across or a micron would be a blur on one and a
//pattern of dots on the other. See surfaceOf.
//
//**A grid on every top**, in the layout's own units - a line every power of ten, the finest that is still SURFACE_GRID_PIXELS
//apart on screen, and the next finest fading in as the view comes closer. A layer was one flat color, so a top was a
//plane with nothing on it: nothing went past a car driving over it, and nothing said how far off anything was.
//
//**The walls darker than the tops, and darker still at their feet, with a bright lip along the top edge** - so where a
//shape ends is a line on screen. The top and a wall facing the light came out the same color, and a wire's edge with them.
//
//**And the metal polished**: tinted by its own color, darker in itself and bright with what it reflects, brushed along
//x. A sky is what it reflects - light overhead, a bright band at the horizon and dark below - since there is no room
//round the chip to reflect, and a metal that reflects nothing is a dark one. Which layers are metal - see isMetal.
//
const SURFACE_GRID_PIXELS = 9;
const SURFACE_GRID_SHADE = 0.2;

//
//**The chip game's neon**, while its panel's Neon glow is ticked - see Viewer3D.setNeon: every layer's edges a line of
//light in the layer's own color, its faces darkened under a grid lit in the same color - the way a synthwave grid is
//lines of light over the dark - and a glow round everything bright, drawn over the scene by NeonGlow.
//
//**Lit past white, for the glow to find.** The scene is drawn into half floats, where a color can be brighter than white
//rather than stopping at it, and the glow is made of what is past NEON_THRESHOLD - so what glows is what is lit for it:
//the edges at NEON_EDGE times their color and the grid at NEON_GRID times it, and the electrons, the shots and the flame
//- see ChipGame.heatParts. A face, a label or the backdrop stops at white and does not, past NEON_MIST of everything, let
//in as a haze of light over the rest. A soft knee NEON_KNEE either side of the threshold, so a color coming up to it
//starts to glow rather than switching on. Where the browser cannot draw into half floats nothing is past white, and the
//glow is made of what is past NEON_THRESHOLD_BYTES instead.
//
//The faces at NEON_FACE of what they were, which is what has the lines stand out.
//
const NEON_EDGE = 5;
const NEON_GRID = 1.5;
const NEON_FACE = 0.35;
const NEON_THRESHOLD = 1;
const NEON_THRESHOLD_BYTES = 0.75;
const NEON_KNEE = 0.5;
const NEON_MIST = 0.08;

//
//How much of the glow goes over the scene, and how many times it is halved on its way down from half the view: six, to a
//sixty-fourth of it, which is what spreads it the width of a hand round a line on screen. See NeonGlow.
//
const NEON_STRENGTH = 0.55;
const NEON_LEVELS = 6;

//
//The color a layer's edges and grid are lit in: its own hue, at least NEON_SATURATION full and NEON_LIGHTNESS light, the
//way gas in a tube makes a color - so a dull brown lights up orange and a pale blue a blue. A layer grayer than NEON_GRAY
//stays gray, which lit is white. See neonColorOf.
//
const NEON_SATURATION = 0.9;
const NEON_LIGHTNESS = 0.6;
const NEON_GRAY = 0.12;

//
//The violet behind the scene under the neon when no picture is up, and what the haze over the distance fades into - see
//NeonGlow.drawScene and ChipGame.fitLens. A light gray behind lines of light washed them out.
//
//**And a picture behind the scene dimmed to NEON_BACKDROP of itself**, so the lines are the brightest thing on screen
//rather than the sky: the Neon backdrop a game is played against came out a pale cyan as bright as anything lit. **The
//haze in front of a picture is the picture's own color round its horizon**, dimmed with it and NEON_HAZE_TINT of the way
//to the violet - see toneOf: in the violet alone, the far side of the chip was a flat wall of it against a teal sky.
//
const NEON_HAZE = 0x4a2470;
const NEON_BACKDROP = 0.3;
const NEON_HAZE_TINT = 0.3;

const SURFACE_VERTEX_PARS = `
varying vec3 vLayout;
varying vec3 vLayoutNormal;`;

const SURFACE_VERTEX = `
vLayout = position;
vLayoutNormal = normal;`;

const SURFACE_FRAGMENT_PARS = `
varying vec3 vLayout;
varying vec3 vLayoutNormal;
uniform float surfaceDepth;
uniform float surfaceMetal;
uniform sampler2D surfaceMap;
uniform float surfaceMapped;
uniform float surfaceTile;
uniform float surfaceNeon;
uniform vec3 surfaceNeonColor;

float surfaceHash(vec2 p) {
    vec3 q = fract(vec3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

float surfaceNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - (2.0 * f));

    float bottom = mix(surfaceHash(i), surfaceHash(i + vec2(1.0, 0.0)), u.x);
    float top = mix(surfaceHash(i + vec2(0.0, 1.0)), surfaceHash(i + vec2(1.0, 1.0)), u.x);

    return mix(bottom, top, u.y);
}

float surfaceLines(vec2 at, float spacing) {
    vec2 cells = at / spacing;
    vec2 off = abs(fract(cells - 0.5) - 0.5) / max(fwidth(cells), vec2(1e-6));

    return 1.0 - min(min(off.x, off.y), 1.0);
}`;

//
//The walls and the lip, onto the color before it is lit, and the grid and the brushing worked out for after - see
//SURFACE_LIT. surfaceFine is how far the finer of the two grids has faded in.
//
//**A layer's own picture in place of its color, where it has one** - see Viewer3D.setTexture. Tiled by where it is on
//the layout, since a slab has no texture coordinates of its own: across x and y on the tops, and on a wall along
//whichever of the two it runs and up its height - a repeat every surfaceTile, in the layout's units. The walls are
//still shaded and lipped over it, so the edges read as they do without one.
//
const SURFACE_COLOR = `
float surfaceLid = abs(vLayoutNormal.z);
float surfaceWall = 1.0 - surfaceLid;

if (surfaceMapped > 0.5) {
    vec2 surfaceAlong = vec2(vLayout.x, vLayout.z);

    if (abs(vLayoutNormal.x) > abs(vLayoutNormal.y))
        surfaceAlong = vec2(vLayout.y, vLayout.z);

    vec2 surfaceAt = mix(surfaceAlong, vLayout.xy, step(0.5, surfaceLid)) / max(surfaceTile, 1e-6);

    diffuseColor.rgb = texture2D(surfaceMap, surfaceAt).rgb;
}

vec2 surfacePixel = fwidth(vLayout.xy);
float surfaceLevel = log(max(max(surfacePixel.x, surfacePixel.y), 1e-6) * ${SURFACE_GRID_PIXELS.toFixed(1)}) / log(10.0);
float surfaceFine = ceil(surfaceLevel) - surfaceLevel;
float surfaceSpacing = pow(10.0, ceil(surfaceLevel));
float surfaceGrid = max(surfaceLines(vLayout.xy, surfaceSpacing) * surfaceFine, surfaceLines(vLayout.xy, surfaceSpacing * 10.0));

float surfaceBrushed = mix(
    surfaceNoise(vec2(vLayout.x / (surfaceSpacing * 20.0), vLayout.y / (surfaceSpacing * 0.4))),
    surfaceNoise(vec2(vLayout.x / (surfaceSpacing * 2.0), vLayout.y / (surfaceSpacing * 0.04))),
    surfaceFine);

float surfaceDown = clamp(vLayout.z / max(surfaceDepth, 1e-6), 0.0, 1.0);
float surfaceLip = surfaceWall * (1.0 - smoothstep(0.0, 1.5 * fwidth(vLayout.z), vLayout.z));

diffuseColor.rgb *= mix(1.0, mix(0.82, 0.5, surfaceDown), surfaceWall);
diffuseColor.rgb = mix(diffuseColor.rgb, vec3(1.0), 0.45 * surfaceLip);`;

//
//The light that leaves the surface. **The metal's finish first**: less of its own light, and the sky it reflects -
//tinted by its color and whiter at a glancing angle, the way metal is - brushed into streaks along x. **Then the grid**,
//on everything lit, highlights and reflections included, so its lines are as plain on polished metal as on silicon.
//What the pulse adds is kept out of both, so a layer pointed at in the sidebar flashes as bright as it did.
//
//**Neither on a layer drawn with its own picture**, which is the finish somebody chose for it.
//
//**Under the game's neon**, all of that at NEON_FACE of itself, and the grid lit past white in the layer's neon color
//rather than shaded - see neonLayer, and NeonGlow for the glow it is lit for.
//
const SURFACE_LIT = `
vec3 surfaceOwn = outgoingLight - totalEmissiveRadiance;

if (surfaceMetal > 0.5 && surfaceMapped < 0.5) {
    vec3 surfaceView = normalize(vViewPosition);
    vec3 surfaceSeen = inverseTransformDirection(reflect(-surfaceView, normal), viewMatrix);
    float surfaceSky = smoothstep(-0.25, 0.9, surfaceSeen.y);
    float surfaceBand = exp(-pow((surfaceSeen.y - 0.12) * 5.0, 2.0));
    float surfaceGlance = pow(1.0 - max(dot(surfaceView, normal), 0.0), 3.0);
    vec3 surfaceTint = mix(diffuseColor.rgb, vec3(1.0), 0.15 + (0.4 * surfaceGlance));
    vec3 surfaceReflected = vec3(0.15 + (0.4 * surfaceSky) + (0.3 * surfaceBand)) * surfaceTint;

    surfaceReflected *= 0.75 + (0.5 * surfaceBrushed);
    surfaceOwn = (surfaceOwn * 0.55) + (surfaceReflected * 0.7);
}

float surfaceGridded = surfaceGrid * surfaceLid * (1.0 - surfaceMapped);

surfaceOwn *= 1.0 - (${SURFACE_GRID_SHADE.toFixed(2)} * surfaceGridded * (1.0 - surfaceNeon));
surfaceOwn = mix(surfaceOwn, (surfaceOwn * ${NEON_FACE.toFixed(2)}) + (surfaceNeonColor * (${NEON_GRID.toFixed(2)} * surfaceGridded)), surfaceNeon);

outgoingLight = surfaceOwn + totalEmissiveRadiance;`;

///
///Whether a layer is metal - wiring, a via, a contact - by what a layermap or somebody has said it is.
///
///**By its name first**, which is the more specific of the two: sky130's met1 to met5, li1, mcon, via to via4 and the
///licon under them, and the usual spellings elsewhere - metal1, M3, Via12, TopMetal - with a pin or label purpose after a
///dot still the metal it marks. **Then by its role**: a via is metal whatever it is called, and a conductor is unless
///it is poly or diffusion, which carry a net and are silicon. A layer nothing has said anything about is not metal,
///which for a file opened without a layermap is every layer - nothing in a GDSII file says what its numbers are.
///
function isMetal(layer) {
    //The letters and the number it starts with: met1 out of met1.pin, Metal2 out of Metal2.drawing or metal2_drawing.
    const stem = ((layer.name || '').toLowerCase().match(/^[a-z]+\d*/) || [''])[0];

    if (/^(met|metal|m|me|mt|topmetal|tm)\d*$/.test(stem) && stem !== 'm' && stem !== 'me')
        return true;

    if (/^(li|via|v|vi|mcon|licon|cont|contact|capm|pad|rdl|topvia|tv)\d*$/.test(stem) && stem !== 'v')
        return true;

    if (layer.role === 'Via')
        return true;

    if (layer.role === 'Conductor')
        return !/poly|diff|active|tap|well|nsd|psd/.test(stem);

    return false;
}

///
///A layer's material, made ready for its surface - see SURFACE_COLOR and SURFACE_LIT - and given it now if `on` says so.
///The surface is spliced into three's own Phong shader rather than replacing it, so its lights, its fog, the pulse and
///the headset all go on working as they did.
///
///**Two programs for every layer between them**: the surfaced one and the plain one, the shader three gives any Phong
///material. Which a material is drawn with is userData.surfaced - see setSurface - and what differs between layers is
///in uniforms of each material's own.
///
///**Show textures**, since 2026-10-14: the 3D view draws the flat colors it always had until its menu or the game puts
///the surfaces on - see Viewer3D.applyView.
///
///**And the layer's own picture, where it names one** - see Viewer3D.setTexture and bindTexture: until it arrives, or
///if it never does, the layer is surfaced the way the rest are.
///
function surfaceOf(material, layer, on) {
    const uniforms = {
        surfaceDepth: { value: Math.max(layer.depth, 1) },
        surfaceMetal: { value: 0 },
        surfaceMap: { value: SURFACE_NO_MAP },
        surfaceMapped: { value: 0 },
        surfaceTile: { value: Math.max(Number(layer.textureSize) || 0, 1e-6) },
        surfaceNeon: { value: 0 },
        surfaceNeonColor: { value: neonColorOf(layer.color) }
    };

    //The highlights it has plain, to be put back when the surface comes off.
    material.userData.plain = { specular: material.specular.clone(), shininess: material.shininess };
    material.userData.metal = null;

    //Which picture it is drawn with - by name, as the layers hand them over - or empty for none.
    material.userData.textureName = layer.texture || '';

    //
    //Surfaced, its highlights are in its own color, which is the other half of what makes a surface read as metal - at a
    //little under half its strength, since at all of it a highlight on a light color was a glare.
    //
    if (isMetal(layer)) {
        uniforms.surfaceMetal.value = 1;

        material.userData.metal = { specular: new THREE.Color(layer.color).multiplyScalar(0.45), shininess: 60 };
    }

    material.userData.surface = uniforms;
    material.userData.surfaced = null;

    //The grid and the lip are measured in pixels, which a WebGL 1 context has to be asked for.
    material.extensions = { derivatives: true };

    //Only for the surfaced program: the plain one is three's shader as it comes.
    material.onBeforeCompile = shader => {
        if (!material.userData.surfaced)
            return;

        shader.uniforms.surfaceDepth = uniforms.surfaceDepth;
        shader.uniforms.surfaceMetal = uniforms.surfaceMetal;
        shader.uniforms.surfaceMap = uniforms.surfaceMap;
        shader.uniforms.surfaceMapped = uniforms.surfaceMapped;
        shader.uniforms.surfaceTile = uniforms.surfaceTile;
        shader.uniforms.surfaceNeon = uniforms.surfaceNeon;
        shader.uniforms.surfaceNeonColor = uniforms.surfaceNeonColor;

        shader.vertexShader = shader.vertexShader
            .replace('#include <common>', `#include <common>${SURFACE_VERTEX_PARS}`)
            .replace('#include <begin_vertex>', `#include <begin_vertex>${SURFACE_VERTEX}`);

        shader.fragmentShader = shader.fragmentShader
            .replace('#include <common>', `#include <common>${SURFACE_FRAGMENT_PARS}`)
            .replace('#include <color_fragment>', `#include <color_fragment>${SURFACE_COLOR}`)
            .replace('#include <envmap_fragment>', `${SURFACE_LIT}\n#include <envmap_fragment>`);
    };

    //Two keys, so three keeps a program for each and a material goes back and forth without compiling again.
    material.customProgramCacheKey = () => {
        if (material.userData.surfaced)
            return 'gdsSurface';

        return 'gdsPlain';
    };

    setSurface(material, on);

    return material;
}

///
///A layer's surface on or off - see surfaceOf: the program it is drawn with, and the metal's highlights with it. Asked
///for again on the next frame it is drawn in, which for one already drawn both ways is the program it already has.
///
function setSurface(material, on) {
    if (material.userData.surfaced === on)
        return;

    material.userData.surfaced = on;

    highlight(material);

    material.needsUpdate = true;
}

///
///A layer's own picture on its surface - see surfaceOf. A uniform rather than a program, so it goes on without a
///compile; and its highlights go back to the plain ones, since a metal's own-color shine is a finish the picture has
///taken the place of.
///
function bindTexture(material, texture) {
    const uniforms = material.userData.surface;

    uniforms.surfaceMap.value = texture;
    uniforms.surfaceMapped.value = 1;

    highlight(material);
}

///The highlights a layer's material has now: in its own color for the metal surfaced without a picture, plain otherwise.
function highlight(material) {
    let highlights = material.userData.plain;

    if (material.userData.surfaced && material.userData.metal != null && material.userData.surface.surfaceMapped.value < 0.5)
        highlights = material.userData.metal;

    material.specular.copy(highlights.specular);
    material.shininess = highlights.shininess;
}

//
//What a layer with no picture samples - one white pixel - so the surfaced program has a texture to read on every layer.
//It reads nothing from it: see surfaceMapped in SURFACE_COLOR.
//
const SURFACE_NO_MAP = new THREE.DataTexture(new Uint8Array([255, 255, 255, 255]), 1, 1);

SURFACE_NO_MAP.needsUpdate = true;

//
//The passes of the glow over the game's neon - see NeonGlow. Each covers the view with one triangle, put straight into
//clip space.
//
const NEON_VERTEX = `
varying vec2 vUv;

void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}`;

//
//**Down a level**: thirteen taps round each of its pixels in the level above, weighted so the lot is the average of what
//they cover - and on the first, from the scene, only what is past the threshold, with the knee and the mist.
//
const NEON_DOWN = `
uniform sampler2D source;
uniform vec2 texel;
uniform float first;
uniform float threshold;
uniform float knee;
uniform float mist;

varying vec2 vUv;

vec3 tap(float x, float y) {
    return texture2D(source, vUv + (vec2(x, y) * texel)).rgb;
}

void main() {
    vec3 color = tap(0.0, 0.0) * 0.125;

    color += (tap(-2.0, 2.0) + tap(2.0, 2.0) + tap(-2.0, -2.0) + tap(2.0, -2.0)) * 0.03125;
    color += (tap(0.0, 2.0) + tap(-2.0, 0.0) + tap(2.0, 0.0) + tap(0.0, -2.0)) * 0.0625;
    color += (tap(-1.0, 1.0) + tap(1.0, 1.0) + tap(-1.0, -1.0) + tap(1.0, -1.0)) * 0.125;

    if (first > 0.5) {
        color = min(color, vec3(64.0));

        float bright = max(color.r, max(color.g, color.b));
        float soft = clamp(bright - threshold + knee, 0.0, 2.0 * knee);

        soft = (soft * soft) / ((4.0 * knee) + 0.0001);

        color *= (max(soft, bright - threshold) / max(bright, 0.0001)) + mist;
    }

    gl_FragColor = vec4(color, 1.0);
}`;

//**Up a level**: nine taps of the level below in a tent, which blurs it again on the way, added onto the level above.
const NEON_UP = `
uniform sampler2D source;
uniform vec2 texel;

varying vec2 vUv;

vec3 tap(float x, float y) {
    return texture2D(source, vUv + (vec2(x, y) * texel)).rgb;
}

void main() {
    vec3 color = tap(0.0, 0.0) * 4.0;

    color += (tap(0.0, 1.0) + tap(-1.0, 0.0) + tap(1.0, 0.0) + tap(0.0, -1.0)) * 2.0;
    color += tap(-1.0, 1.0) + tap(1.0, 1.0) + tap(-1.0, -1.0) + tap(1.0, -1.0);

    gl_FragColor = vec4(color / 16.0, 1.0);
}`;

//
//**Onto the canvas**: the scene with the glow over it, put into the canvas's colors - and dithered by a step either way,
//since a glow is a slow fall of color over the dark, and in the canvas's 256 steps a slow fall is bands.
//
const NEON_MIX = `
uniform sampler2D picture;
uniform sampler2D glow;
uniform float strength;

varying vec2 vUv;

void main() {
    vec3 color = texture2D(picture, vUv).rgb + (texture2D(glow, vUv).rgb * strength);

    gl_FragColor = linearToOutputTexel(vec4(color, 1.0));

    float noise = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);

    gl_FragColor.rgb += (noise - 0.5) / 255.0;
}`;

///
///The color a layer's edges and grid are lit in under the game's neon - its own hue, as vivid as gas in a tube makes it.
///See NEON_SATURATION.
///
function neonColorOf(color) {
    const hsl = {};

    new THREE.Color(color).getHSL(hsl, THREE.SRGBColorSpace);

    let saturation = hsl.s;

    if (saturation > NEON_GRAY)
        saturation = Math.max(saturation, NEON_SATURATION);

    return new THREE.Color().setHSL(hsl.h, saturation, NEON_LIGHTNESS, THREE.SRGBColorSpace);
}

///
///**A layer's edges, as lines**, for the game's neon to light: round the top of every ring, round its underside, and up
///each corner - read out of the block the way extrudeLayerSteps reads it, and in the slab's own space, so they go
///wherever the slab does. A corner in the middle of a straight run has no edge up it, and gets no line.
///
///**Lines rather than the lip the surface draws along a wall's top**: a wall turned away from the camera is not drawn,
///and its lip with it, so the far side of every top had no edge.
///
function edgesOf(words, start, end, depth) {
    const rings = [];

    for (let at = start; at < end;) {
        const count = words[at];
        const holeCount = words[at + 1];

        at += 2;

        const outline = cornersOf(words, at, count);

        at += count * 2;

        const holes = [];

        for (let hole = 0; hole < holeCount; hole++) {
            const corners = words[at];

            holes.push(cornersOf(words, at + 1, corners));

            at += 1 + (corners * 2);
        }

        //Nothing the view drew either.
        if (outline.length < 3)
            continue;

        rings.push(outline);

        for (const hole of holes) {
            if (hole.length >= 3)
                rings.push(hole);
        }
    }

    //Whether the ring turns at its corner `i`, rather than going straight on through it.
    function turns(ring, i) {
        const before = ring[(i + ring.length - 1) % ring.length];
        const corner = ring[i];
        const after = ring[(i + 1) % ring.length];

        return ((corner.x - before.x) * (after.y - corner.y)) - ((corner.y - before.y) * (after.x - corner.x)) !== 0;
    }

    //Counted first, so the array is made once at the size it needs: two lines a corner, and one more up each turn.
    let lines = 0;

    for (const ring of rings) {
        lines += ring.length * 2;

        for (let i = 0; i < ring.length; i++) {
            if (turns(ring, i))
                lines++;
        }
    }

    if (lines === 0)
        return null;

    const position = new Float32Array(lines * 6);

    let written = 0;

    function line(x1, y1, z1, x2, y2, z2) {
        position[written++] = x1;
        position[written++] = y1;
        position[written++] = z1;
        position[written++] = x2;
        position[written++] = y2;
        position[written++] = z2;
    }

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const before = ring[(i + ring.length - 1) % ring.length];
            const corner = ring[i];

            line(before.x, before.y, 0, corner.x, corner.y, 0);
            line(before.x, before.y, depth, corner.x, corner.y, depth);

            if (turns(ring, i))
                line(corner.x, corner.y, 0, corner.x, corner.y, depth);
        }
    }

    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute('position', new THREE.BufferAttribute(position, 3));

    return geometry;
}

///
///A layer's slab under the game's neon, or out from under it - see Viewer3D.setNeon: its faces darkened and the grid on
///its tops lit, in its surfaced program - see SURFACE_LIT - and its edges a line of light in its neon color, made the
///first time and let go of again with the neon off. The lines are the slab's own child, so they go wherever it does.
///
///**The faces a hair further off than they are while the lines are on them**, so a line along an edge is drawn over the
///face it bounds rather than in and out of it.
///
function neonLayer(mesh, on) {
    const material = mesh.material;
    const uniforms = material.userData.surface;

    uniforms.surfaceNeon.value = 0;

    if (on)
        uniforms.surfaceNeon.value = 1;

    material.polygonOffset = on;
    material.polygonOffsetFactor = 1;
    material.polygonOffsetUnits = 1;

    if (!on) {
        disposeNeonEdges(mesh);

        return;
    }

    if (mesh.userData.neonEdges != null || mesh.userData.words == null)
        return;

    const geometry = edgesOf(mesh.userData.words, mesh.userData.wordsFrom, mesh.userData.wordsTo, mesh.userData.depth);

    if (geometry == null)
        return;

    const edges = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color: uniforms.surfaceNeonColor.value.clone().multiplyScalar(NEON_EDGE) }));

    //Named, so the specs can find them.
    edges.name = 'neonEdges';

    mesh.add(edges);
    mesh.userData.neonEdges = edges;
}

///Takes a slab's neon edges off it, if it has them, and lets go of what they were drawn with.
function disposeNeonEdges(mesh) {
    const edges = mesh.userData.neonEdges;

    if (edges == null)
        return;

    mesh.remove(edges);
    edges.geometry.dispose();
    edges.material.dispose();

    mesh.userData.neonEdges = null;
}

//
//The chip game - see ChipGame after the viewer below, with GameSound and GameMusic after it, and js/chipGame.js
//for its physics.
//
//The keys it takes while it runs, by KeyboardEvent.code rather than by the character, so the car drives off
//the same four keys on a keyboard that is not laid out QWERTY.
//
const GAME_KEYS = new Set([
    'KeyW', 'KeyA', 'KeyS', 'KeyD',
    'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
    'Space', 'ShiftLeft', 'ShiftRight', 'KeyR', 'KeyM', 'KeyP', 'KeyV', 'KeyH', 'KeyF', 'KeyG', 'Escape'
]);

//
//What the game's panel sets, as it starts the first time: the top speed in car widths a second, the car's size as a
//multiple of the one the layout gets, how small the size slider goes, the draw distance, what is being played and
//from where, which way the plane's stick goes, lock on, limit jump, the sound, the music, the neon - and whether the
//panel is open at all. **The music is on to begin with**: a tune for each of the three, see GameMusic.
//
//**So is the neon** (2026-11-01, asked for): the layers' edges and grids lit in their own colors, a glow round everything
//bright, the backdrop dimmed and a haze over the distance - see Viewer3D.setNeon and ChipGame.showNeon. It is how the game was asked
//to look, against the Neon backdrop it is played against; Neon glow in the panel takes it off.
//Kept in this browser under GAME_OPTIONS_KEY once any of them is changed, the way the theme is - a preference about
//the game rather than about a file.
//
//**Half the car the layout gets is the largest there is, and a tenth of it is where it starts** - the slider's least,
//unless the box after the slider says less: anything from a hundredth to 0.45, in hundredths, which is the slider's
//step. It started at the half, until that read as too big to play with. The whole of the layout's car was the largest
//once; a size kept from then is held to a half, as one from when it went up to three was held to one - see
//loadGameOptions.
//
//**The top speed is from half a car width a second to fifteen**, in halves, and starts at four - it went to six, then
//ten, from seven. The widths are the car's own - see chipGame.carWidth - so a car made small in the panel is slow
//with it; they were the layout's car's, and a car a tenth the size went ten of its own widths for every one the slider
//said.
//
//**The vehicle is the car, the plane or the jet** - a list rather than the tick the plane had, which a kept `plane`
//still reads as - and **the view is third person or first**: behind it, or from the driver's seat.
//
//**Apply texture is the layers' surfaces**, the view's Show textures - see VIEW_OPTIONS - which the game turns on as it
//starts, and leaves however the bar or the panel has set it until it stops.
//
//**The draw distance is a share of the chip's width**, from a twentieth to all of it: all of it draws everything,
//and less takes the far plane in and fades the far side into the background - see ChipGame.fitLens, and
//Viewer3D.fitDepthRange for the view without a game. A share rather than a length, since a layout's units are its
//own and the car is sized off the layout; any chip is from twelve to three hundred of the layout's cars across. It is
//the view's rather than the game's since 2026-10-14 - see VIEW_OPTIONS - and the game has it at All.
//
//**The plane's stick is a stick**: up, and the mouse forward, push the nose down - see ChipGame.update. Invert pitch
//turns both round, for anybody who reads up as up.
//
const GAME_OPTIONS_KEY = 'gdsviewer.game';
const GAME_OPTIONS = {
    topSpeed: 4,
    size: 0.1,
    smallest: 0.1,
    vehicle: 'car',
    view: 'third',
    invertPitch: false,
    locked: false,
    limitJump: false,
    sound: true,
    music: true,
    neon: true,
    hazeReach: 0,
    hazeClimbs: true,
    panel: true
};

//
//**The 3D view's own settings**, from the menu its bar has - see Viewer3D.razor - and kept in this browser: how far
//it draws, as a share of the chip's width up to All, and whether the layers show their textures - see surfaceOf.
//All, and the flat colors, to begin with.
//
//**The game's panel has the same two**, and they are these: a game turns the textures on and the draw distance up to
//All as it starts, has them while it runs - from its panel or from the bar - and puts them back the way they were when
//it stops. See Viewer3D.applyView and ChipGame.start.
//
const VIEW_OPTIONS_KEY = 'gdsviewer.view3d';
const VIEW_OPTIONS = { drawDistance: 1, textures: false };
const GAME_TOP_SPEEDS = { least: 0.5, most: 15 };
const GAME_CAR_SIZES = { least: 0.01, most: 0.5 };
const GAME_SMALLEST_SIZES = { least: 0.01, most: 0.45 };
const GAME_DRAW_DISTANCES = { least: 0.05, most: 1 };
const GAME_VEHICLES = ['car', 'plane', 'jet'];
const GAME_VIEWS = ['third', 'first'];

//
//The mouse on the plane's stick: radians of bank, and of nose, for a pixel the pointer is from the middle of the view.
//
//**From the middle, not from where the pointer was.** Where the pointer is on the view is where the stick is held - see
//ChipGame.onPointerMove - so the middle is level flight whatever the pointer did before. It was the pointer's movement,
//put on as it moved, and so the middle was wherever the pointer stood as the game began: a game started from the
//Settings menu had it up in the corner, and the middle of the view was a hard bank to the left.
//
//**Half what they were**, 0.004 and 0.003, at which a sweep across most of the view was most of a right angle of bank
//and the nose went with every twitch of the hand: asked to be less sensitive, the mouse above all. The plane comes
//round to where the stick is held over chipGame's stickEase, which takes the shake out of a hand.
//
//GAME_PLANE_STICK_MIDDLE pixels round the middle are level, so level is somewhere a hand can find; and the bank goes no
//further than GAME_PLANE_STICK_BANK either way, a wing straight down, however wide the view is.
//
//**Past GAME_PLANE_STICK_PULL of the way to the top or the foot of the view, the stick is pulled past what it holds**: the
//nose stops being held at a place and keeps coming round, from nothing there to the stick's whole rate at the edge -
//which held is a loop, as the stick held back on the keys is. Holding a place, the mouse could not loop at all: the most
//it held the nose up at, at the foot of the view, was a climb of about twenty degrees in a 1280 by 720 window.
//
const GAME_PLANE_ROLL_PER_PIXEL = 0.002;
const GAME_PLANE_PITCH_PER_PIXEL = 0.0015;
const GAME_PLANE_STICK_MIDDLE = 12;
const GAME_PLANE_STICK_BANK = Math.PI / 2;
const GAME_PLANE_STICK_PULL = 0.8;

//
//**The arrow keys on the plane's stick, damped**: a key moves the stick GAME_PLANE_KEY_SHARE of the way over, not all of
//it, and gets there over GAME_PLANE_KEY_EASE seconds rather than at once - and lets go the same way. A key had been the
//whole of the craft's rate from the frame it went down, and was too sensitive: asked to be calmer on 2026-11-03. The
//mouse keeps the whole rate, at the edges of the view, so it still loops as it did.
//
const GAME_PLANE_KEY_SHARE = 0.6;
const GAME_PLANE_KEY_EASE = 0.3;

//Which bit of MouseEvent.buttons each MouseEvent.button is: the main one, the middle one and the other one.
const GAME_BUTTON_BITS = [1, 4, 2];

//
//The jet's HUD - see drawJetHud: its green, and the dark under it that keeps it readable over a bright backdrop; and
//how many degrees of nose the glass is tall, which is the pitch ladder's scale - a rung every ten of them.
//
const GAME_HUD_GREEN = 'rgba(110, 255, 150, 0.95)';
const GAME_HUD_SHADE = 'rgba(0, 24, 10, 0.55)';
const GAME_HUD_DEGREES = 46;

//How long HOT is on, and then off, over a weapon on the HUD while it cools from overheated - in milliseconds.
const GAME_HUD_FLASH_MS = 250;

//How far along the nose the crosshair marks, in car widths: where the gun's rounds are a third of a second out.
const GAME_AIM_AHEAD = 20;

//
//The electrons as they are drawn: their yellow, how far they bob, in car widths, how fast, and the glow round each, in
//pixels. **The gun's rounds are red** beside them, so what is fired and what it is fired at are not the one color: the
//rounds were the yellow the electrons are now.
//
const GAME_ELECTRON_COLOR = 0xffe14a;
const GAME_ELECTRON_BOB = 0.08;
const GAME_ELECTRON_BOB_RATE = 2.5;
const GAME_ELECTRON_GLOW = 22;
const GAME_ROUND_COLOR = 0xff4a2e;

//The flare behind a missile, in pixels - see ChipGame.buildField.
const GAME_MISSILE_FLARE = 16;

//How many of each the scene has room for, drawn as one instanced mesh apiece - see ChipGame.buildField.
const GAME_ELECTRONS_DRAWN = 64;
const GAME_SHOTS_DRAWN = 96;
const GAME_FLASHES = 16;

//Which way a round and a missile point before they are turned along their way: +X, the way every model faces.
const GAME_NOSE_AXIS = new THREE.Vector3(1, 0, 0);

//How much of the draw distance is clear before the haze starts, as a share of it.
const GAME_HAZE_FROM = 0.6;

//
//**The neon's haze** over the distance - see ChipGame.fitLens: half thick GAME_NEON_HAZE_WIDTHS car widths off, thick by
//the square of the distance, so what is round the car is all but clear of it and the horizon is gone into it. The far
//edges had piled up there into a band of white. **Thinner the higher the camera is over the stack** - half thick no
//nearer than GAME_NEON_HAZE_ABOVE times as far as it is up - since the chip under a jet flown high was gone into it too,
//and looking down from up there is looking through less of it.
//
//And how much brighter than white the neon has what the game puts in the world - the electrons, the shots, the flame,
//the lamps and the outline under the car - for the glow to find. See ChipGame.heatParts.
//
const GAME_NEON_HAZE_WIDTHS = 150;
const GAME_NEON_HAZE_ABOVE = 3;

//
//**How far the neon's haze reaches, from the panel**: the distance it is half thick at, times two to this power - so 0 is
//the haze above, each step left of it twice as thick and each right of it half as. The far end has none at all. And
//whether it thins as the camera climbs, which is what took away the thick haze a game starts in as the jet went up:
//asked for on 2026-11-03 by somebody who liked that look and wanted to keep it.
//
const GAME_HAZE_REACHES = { least: -3, most: 3 };
const GAME_NEON_HOT = 3;

//
//The mouse looks round the car: radians a pixel of movement turns the camera about it, and how far it may tip
//above and below where it usually sits. Left alone for GAME_LOOK_HOLD seconds, it swings back behind the car at
//GAME_LOOK_RETURN a second - the chase view the game has always had.
//
//**Half a turn for about nine hundred pixels**, most of the view's width. It was 0.006, at which a sweep across
//the view took the camera all the way round the car and a small move was a big swing - hard to aim.
//
const GAME_LOOK_PER_PIXEL = 0.0035;
const GAME_LOOK_UP = 0.9;
const GAME_LOOK_DOWN = 0.35;
const GAME_LOOK_HOLD = 1.2;
const GAME_LOOK_RETURN = 2.5;

//How quickly the camera keeps up while the mouse is moving it, per second - stiffer than following the car.
const GAME_CAMERA_LOOKING = 14;

//Nose up on the side of a wall it is climbing, in radians, and how quickly the car tips to a new angle.
const GAME_CLIMB_PITCH = 1.2;
const GAME_PITCH_EASE = 10;

//
//When the tires are heard: a turn harder than this, as the steering times the share of the top speed, or the
//brakes at more than this share of it. Either way the squeal grows from nothing there to its loudest a little past.
//
const GAME_SQUEAL_TURN = 0.45;
const GAME_SQUEAL_BRAKE = 0.35;

//The chase camera, in car widths: how far behind the car it sits, how far above, and how far ahead of the
//car it looks. The view's own field of view is a hundred degrees, which is a fisheye this close in.
const GAME_CAMERA_BACK = 5;
const GAME_CAMERA_UP = 2.2;
const GAME_CAMERA_AHEAD = 1.5;
const GAME_FOV = 70;

//How quickly the camera swings round after the car and follows it up and down, per second, and how quickly it backs
//out again once a layer that pulled it in is no longer in the way. Higher is stiffer. Never how far behind the car
//it is, which is fixed - see ChipGame.frameCamera.
const GAME_CAMERA_FOLLOW = 5;
const GAME_CAMERA_RETURN = 2;

//How far the wheel can pull the camera in and push it out, as a multiple of where it starts.
const GAME_ZOOM_MIN = 0.4;
const GAME_ZOOM_MAX = 4;

//The color the slab under the car is outlined in, and how long a message stays up.
const GAME_OUTLINE = 0xffffff;
const GAME_TOAST_MS = 1800;

//
//A landing after at least this many seconds in the air is worth a cheer. A jump on its own is about a second now
//that it eases off the ground and rounds over the top, so the cheer is for more than one.
//
const GAME_AIRTIME_WORTH_SAYING = 1.5;

//
//The music - see GameMusic: how many seconds of it are given their time ahead of the clock, from each frame, and how
//loud all of it is. **About as loud as the car's engine at speed** - measured off the game's own nodes - so it is
//heard over the engine at rest and under the shots, which say what the game is doing.
//
const GAME_MUSIC_AHEAD = 0.3;
const GAME_MUSIC_LOUDNESS = 0.4;

//
//**The jet's gun sounds like an A-10's** - see GameSound: rounds a second as heard, which is the GAU-8's 3,900 a minute,
//and how far under it the barrels start and stop, spinning up as the trigger is pulled and down as it is let go of.
//
const GAME_CANNON_RATE = 65;
const GAME_CANNON_SPIN = 0.6;

/////////////////////////////////////////////////



//Helpers////////////////////////////////////////

//Where a label hangs from lives in js/viewGeometry.js, which has no DOM or three.js dependency so that
//it can be unit-tested under Node. It is a plain script loaded before this module, so it is on window
//by the time anything here runs.

///
///The chip game's options as this browser last left them, or as they start - see GAME_OPTIONS_KEY.
///
///Each one held to what the panel can set, so a value from an older build or a hand-edited one cannot put the
///car somewhere the sliders do not reach. Storage that throws - a private window, blocked site data - is the
///same as storage that is empty.
///
function loadGameOptions() {
    const options = Object.assign({}, GAME_OPTIONS);

    try {
        const kept = JSON.parse(localStorage.getItem(GAME_OPTIONS_KEY) || 'null');

        if (kept != null) {
            options.topSpeed = clampGameOption(Number(kept.topSpeed), GAME_TOP_SPEEDS, GAME_OPTIONS.topSpeed);
            options.smallest = clampGameOption(Number(kept.smallest), GAME_SMALLEST_SIZES, GAME_OPTIONS.smallest);
            options.size = clampGameOption(Number(kept.size), carSizes(options), GAME_OPTIONS.size);
            options.invertPitch = kept.invertPitch === true;
            options.locked = kept.locked === true;
            options.limitJump = kept.limitJump === true;
            options.sound = kept.sound !== false;
            options.music = kept.music !== false;
            options.neon = kept.neon !== false;
            options.hazeReach = clampGameOption(Number(kept.hazeReach), GAME_HAZE_REACHES, GAME_OPTIONS.hazeReach);
            options.hazeClimbs = kept.hazeClimbs !== false;
            options.panel = kept.panel !== false;

            //The plane was a tick before the jet made it a list.
            if (GAME_VEHICLES.includes(kept.vehicle))
                options.vehicle = kept.vehicle;
            else if (kept.plane === true)
                options.vehicle = 'plane';

            if (GAME_VIEWS.includes(kept.view))
                options.view = kept.view;
        }
    }
    catch { }

    return options;
}

function saveGameOptions(options) {
    try {
        localStorage.setItem(GAME_OPTIONS_KEY, JSON.stringify(options));
    }
    catch { }
}

///The 3D view's own settings as this browser last left them, or as they start - see VIEW_OPTIONS_KEY.
function loadViewOptions() {
    const options = Object.assign({}, VIEW_OPTIONS);

    try {
        const kept = JSON.parse(localStorage.getItem(VIEW_OPTIONS_KEY) || 'null');

        if (kept != null) {
            options.drawDistance = clampGameOption(Number(kept.drawDistance), GAME_DRAW_DISTANCES, VIEW_OPTIONS.drawDistance);
            options.textures = kept.textures === true;
        }
    }
    catch { }

    return options;
}

function saveViewOptions(options) {
    try {
        localStorage.setItem(VIEW_OPTIONS_KEY, JSON.stringify({ drawDistance: options.drawDistance, textures: options.textures }));
    }
    catch { }
}

///A number from the panel or from storage, held inside its range - or the default when it is not a number at all.
function clampGameOption(value, range, fallback) {
    if (!isFinite(value))
        return fallback;

    return Math.min(Math.max(value, range.least), range.most);
}

///A draw distance as the bar's menu and the game's panel say it: All at the end that draws everything, a percentage short of it.
function drawDistanceSaid(share) {
    if (share >= GAME_DRAW_DISTANCES.most)
        return 'All';

    return `${Math.round(share * 100)}%`;
}

///How far the neon's haze reaches as the panel says it: None at the far end, and otherwise how many times as far as it starts.
function hazeReachSaid(reach) {
    if (reach >= GAME_HAZE_REACHES.most)
        return 'None';

    return `${Math.round((2 ** reach) * 100) / 100}×`;
}

///The sizes the car can be with these options: from however small the box lets it go, up to the layout's own.
function carSizes(options) {
    return { least: Math.max(options.smallest, GAME_CAR_SIZES.least), most: GAME_CAR_SIZES.most };
}

///
///How far the plane's stick is held from the middle, for a pointer this many pixels from the middle of the view: nothing
///within GAME_PLANE_STICK_MIDDLE of it, and from there on out, so the stick moves off level without a step.
///
function stickTravel(pixels) {
    const past = Math.abs(pixels) - GAME_PLANE_STICK_MIDDLE;

    if (past <= 0)
        return 0;

    return Math.sign(pixels) * past;
}

///
///How far the plane's stick is pulled past what it holds, for a pointer this many pixels from the middle of a view `half`
///pixels from its middle to its edge: nothing short of GAME_PLANE_STICK_PULL of the way out, and from there to the edge,
///from nothing to all of it - signed the way the pointer is from the middle.
///
function stickPull(pixels, half) {
    const from = half * GAME_PLANE_STICK_PULL;
    const band = half - from;

    if (!(band > 0) || Math.abs(pixels) <= from)
        return 0;

    return Math.sign(pixels) * Math.min(1, (Math.abs(pixels) - from) / band);
}

///
///**The jet's HUD**, drawn into `pen` - a 2D context `width` by `height` in CSS pixels - from `reading`; see
///ChipGame.jetReading.
///
///**The pitch ladder** is a rung every ten degrees of nose, tipped with the bank and moved with the nose, round the
///waterline mark that is the jet itself: solid above the horizon and dashed below it, each rung's ends turned towards the
///horizon, its degrees beside them. **The heading** runs along the top, a tick every five degrees and a number every ten,
///and the heading itself boxed over them. **The speed** is boxed on the left, with the lever and the afterburner under
///it, and **the height** over what is under the jet on the right - both in the jet's own widths, the game's measure -
///with **the gun and the missiles** under it: how hot each is, HOT flashing over one that has overheated, and whether a
///missile is ready. **The bank** is a scale along the foot, its pointer where down is.
///
///Every line and word drawn twice, dark and wide under the green, so it reads over a bright backdrop as well as a dark
///one.
///
function drawJetHud(pen, width, height, reading) {
    const middleX = width / 2;
    const middleY = height / 2;
    const perDegree = height / GAME_HUD_DEGREES;

    pen.font = '600 12px Consolas, "Courier New", monospace';
    pen.textBaseline = 'middle';
    pen.lineCap = 'round';
    pen.lineJoin = 'round';

    //
    //The ladder, in a frame of its own turned with the bank - and kept off the heading along the top, the boxes at the
    //sides and the bank's scale along the foot.
    //
    pen.save();
    pen.beginPath();
    pen.rect(width * 0.2, 46, width * 0.6, height - 100);
    pen.clip();
    pen.translate(middleX, middleY);
    pen.rotate(-reading.roll * Math.PI / 180);

    const above = new Path2D();
    const below = new Path2D();
    const rungsSaid = [];
    const gap = 24;
    const half = Math.min(width * 0.16, 70);

    for (let rung = -90; rung <= 90; rung += 10) {
        const y = (reading.pitch - rung) * perDegree;

        if (Math.abs(y) > height)
            continue;

        if (rung === 0) {
            above.moveTo(-width, y);
            above.lineTo(-gap, y);
            above.moveTo(gap, y);
            above.lineTo(width, y);

            continue;
        }

        let rungs = above;
        let toward = 6;

        if (rung < 0) {
            rungs = below;
            toward = -6;
        }

        rungs.moveTo(-gap - half, y + toward);
        rungs.lineTo(-gap - half, y);
        rungs.lineTo(-gap, y);
        rungs.moveTo(gap, y);
        rungs.lineTo(gap + half, y);
        rungs.lineTo(gap + half, y + toward);

        rungsSaid.push([String(Math.abs(rung)), -gap - half - 5, y, 'right']);
        rungsSaid.push([String(Math.abs(rung)), gap + half + 5, y, 'left']);
    }

    strokeHud(pen, above, []);
    strokeHud(pen, below, [6, 4]);

    for (const [text, x, y, align] of rungsSaid)
        sayHud(pen, text, x, y, align);

    pen.restore();

    //The waterline: the jet, where its nose points, fixed in the middle - a W with its wings out level.
    const jet = new Path2D();

    jet.moveTo(middleX - 26, middleY);
    jet.lineTo(middleX - 12, middleY);
    jet.lineTo(middleX - 6, middleY + 7);
    jet.lineTo(middleX, middleY);
    jet.lineTo(middleX + 6, middleY + 7);
    jet.lineTo(middleX + 12, middleY);
    jet.lineTo(middleX + 26, middleY);

    strokeHud(pen, jet, []);

    //The heading: thirty degrees either way across the middle three fifths, and the heading itself in a box over it.
    const tape = new Path2D();
    const perMark = (width * 0.6) / 60;

    for (let mark = Math.ceil((reading.heading - 30) / 5) * 5; mark <= reading.heading + 30; mark += 5) {
        const x = middleX + ((mark - reading.heading) * perMark);

        let tall = 4;

        if (mark % 10 === 0) {
            tall = 8;

            sayHud(pen, compassSaid(mark), x, 41, 'center');
        }

        tape.moveTo(x, 26);
        tape.lineTo(x, 26 + tall);
    }

    tape.rect(middleX - 22, 3, 44, 17);
    tape.moveTo(middleX - 4, 20);
    tape.lineTo(middleX, 25);
    tape.lineTo(middleX + 4, 20);

    strokeHud(pen, tape, []);
    sayHud(pen, compassSaid(reading.heading), middleX, 12, 'center');

    //The speed on the left and the height on the right, boxed, with what goes with each under it.
    const boxes = new Path2D();
    const speedX = Math.max(width * 0.1, 32);
    const heightX = width - speedX;

    boxes.rect(speedX - 28, middleY - 10, 56, 20);
    boxes.rect(heightX - 28, middleY - 10, 56, 20);

    strokeHud(pen, boxes, []);

    let heightSaid = '---';

    if (reading.height != null)
        heightSaid = reading.height.toFixed(1);

    sayHud(pen, 'SPD', speedX, middleY - 20, 'center');
    sayHud(pen, reading.speed.toFixed(1), speedX, middleY, 'center');
    sayHud(pen, `THR ${Math.round(reading.lever * 100)}`, speedX, middleY + 22, 'center');
    sayHud(pen, 'ALT', heightX, middleY - 20, 'center');
    sayHud(pen, heightSaid, heightX, middleY, 'center');

    if (reading.burning)
        sayHud(pen, 'AB', speedX, middleY + 38, 'center');

    //
    //The gun and the missiles under the height: each one's heat a bar that fills as it fires, and HOT flashing over one
    //for as long as it cools from overheated - see chipGame's warm - with whether the next missile is ready.
    //
    let gunSaid = 'GUN';
    let missileSaid = 'MSL ---';

    if (reading.ready)
        missileSaid = 'MSL RDY';

    if (reading.gunHot && reading.flash)
        gunSaid = 'GUN HOT';

    if (reading.missileHot && reading.flash)
        missileSaid = 'MSL HOT';

    sayHud(pen, gunSaid, heightX, middleY + 22, 'center');
    barHud(pen, heightX - 28, middleY + 30, 56, 7, reading.gunHeat);
    sayHud(pen, missileSaid, heightX, middleY + 47, 'center');
    barHud(pen, heightX - 28, middleY + 55, 56, 7, reading.missileHeat);

    //
    //The bank, along the foot: a scale from sixty degrees either way, a tick at ten, twenty, thirty, forty-five and
    //sixty, and the pointer where down is - which goes the way the jet banks, and stops at the scale's ends.
    //
    const scale = new Path2D();
    const radius = (height / 2) - 22;
    const quarter = Math.PI / 2;

    scale.arc(middleX, middleY, radius, quarter - (Math.PI / 3), quarter + (Math.PI / 3));

    for (const tick of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
        const angle = quarter + (tick * Math.PI / 180);

        let tall = 5;

        if (tick % 30 === 0)
            tall = 9;

        scale.moveTo(middleX + (Math.cos(angle) * radius), middleY + (Math.sin(angle) * radius));
        scale.lineTo(middleX + (Math.cos(angle) * (radius + tall)), middleY + (Math.sin(angle) * (radius + tall)));
    }

    const down = quarter - (Math.min(Math.max(reading.roll, -65), 65) * Math.PI / 180);
    const tipX = middleX + (Math.cos(down) * (radius - 2));
    const tipY = middleY + (Math.sin(down) * (radius - 2));
    const backX = middleX + (Math.cos(down) * (radius - 11));
    const backY = middleY + (Math.sin(down) * (radius - 11));
    const sideX = -Math.sin(down) * 5;
    const sideY = Math.cos(down) * 5;

    scale.moveTo(tipX, tipY);
    scale.lineTo(backX + sideX, backY + sideY);
    scale.lineTo(backX - sideX, backY - sideY);
    scale.closePath();

    strokeHud(pen, scale, []);
}

///A path in the HUD's green, over a wider stroke of its dark - `dash` the pattern, or none.
function strokeHud(pen, path, dash) {
    pen.setLineDash(dash);
    pen.strokeStyle = GAME_HUD_SHADE;
    pen.lineWidth = 3.5;
    pen.stroke(path);
    pen.strokeStyle = GAME_HUD_GREEN;
    pen.lineWidth = 1.5;
    pen.stroke(path);
    pen.setLineDash([]);
}

///A bar in the HUD's green, `share` of it filled from the left, in an outline drawn as its lines are.
function barHud(pen, x, y, width, height, share) {
    const outline = new Path2D();

    outline.rect(x, y, width, height);

    strokeHud(pen, outline, []);

    const filled = (width - 4) * Math.min(Math.max(share, 0), 1);

    if (!(filled > 0))
        return;

    pen.fillStyle = GAME_HUD_GREEN;
    pen.fillRect(x + 2, y + 2, filled, height - 4);
}

///Words in the HUD's green, over a wider stroke of its dark.
function sayHud(pen, text, x, y, align) {
    pen.textAlign = align;
    pen.lineWidth = 3;
    pen.strokeStyle = GAME_HUD_SHADE;
    pen.strokeText(text, x, y);
    pen.fillStyle = GAME_HUD_GREEN;
    pen.fillText(text, x, y);
}

///A heading the way a compass says one: three figures, from 000 to 359.
function compassSaid(degrees) {
    return String(((Math.round(degrees) % 360) + 360) % 360).padStart(3, '0');
}

///A weapon's heat as the meter shows it, in hundredths, and whether it has overheated - see ChipGame.showHeat.
function heatSaid(weapon) {
    return `${Math.round(weapon.heat * 100)} ${weapon.hot}`;
}

///One weapon's part of the heat meter: its bar as full as the weapon is hot, and red while it has overheated.
function showWeaponHeat(part, weapon) {
    part.classList.toggle('gameHeatHot', weapon.hot);
    part.querySelector('.gameHeatFill').style.width = `${Math.round(weapon.heat * 100)}%`;
}

///
///A wave shaper's curve, `points` long, that rounds a signal's peaks off rather than cutting them flat - the grit in the
///jet's gun; see GameSound. Scaled so the loudest that goes in still comes out at one.
///
function softClip(points) {
    const curve = new Float32Array(points);
    const top = Math.tanh(2);

    for (let i = 0; i < points; i++) {
        const x = ((i * 2) / (points - 1)) - 1;

        curve[i] = Math.tanh(x * 2) / top;
    }

    return curve;
}

///
///A round glow, white in the middle and gone by the edge, for the electrons' points to be tinted by. Drawn into a
///canvas rather than fetched, which keeps the app whole offline.
///
function glowTexture() {
    const size = 64;
    const canvas = document.createElement('canvas');

    canvas.width = size;
    canvas.height = size;

    const context = canvas.getContext('2d');
    const glow = context.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);

    glow.addColorStop(0, 'rgba(255, 255, 255, 1)');
    glow.addColorStop(0.3, 'rgba(255, 255, 255, 0.75)');
    glow.addColorStop(1, 'rgba(255, 255, 255, 0)');

    context.fillStyle = glow;
    context.fillRect(0, 0, size, size);

    return new THREE.CanvasTexture(canvas);
}

///One of the colors the chip game's neon lights past white, with what it is without it - see ChipGame.heatParts.
function heated(color) {
    return { color: color, was: color.clone() };
}

///
///The color a picture behind the scene is round its horizon - the band an eighth of its height across the middle of the
///equirectangular picture, averaged - as the scene draws it: the bytes as they are, since the backdrop is drawn without
///being decoded from sRGB. What the neon's haze fades into in front of it - see NEON_HAZE_TINT. Null where the picture
///cannot be read.
///
function toneOf(image) {
    try {
        const canvas = document.createElement('canvas');

        canvas.width = 64;
        canvas.height = 32;

        const pen = canvas.getContext('2d', { willReadFrequently: true });

        pen.drawImage(image, 0, 0, canvas.width, canvas.height);

        const band = pen.getImageData(0, 14, canvas.width, 4).data;
        const sum = [0, 0, 0];

        for (let i = 0; i < band.length; i += 4) {
            sum[0] += band[i];
            sum[1] += band[i + 1];
            sum[2] += band[i + 2];
        }

        const count = (band.length / 4) * 255;

        return new THREE.Color().setRGB(sum[0] / count, sum[1] / count, sum[2] / count, THREE.LinearSRGBColorSpace);
    }
    catch {
        return null;
    }
}

///
///The block of corners the 3D view hands over, as the 32-bit words it was written in. See packShapes in
///Viewer3D.razor for what is in it.
///
///A view onto the same bytes where they start on a four-byte boundary, and a copy where they do not: an
///array of words cannot begin part of the way into one.
///
function wordsOf(points) {
    if (points == null)
        return new Int32Array(0);

    if (points.byteOffset % 4 === 0)
        return new Int32Array(points.buffer, points.byteOffset, points.byteLength / 4);

    return new Int32Array(points.slice().buffer);
}

///
///One ring of corners out of the block, as the triangulator takes them.
///
///**A corner repeating the one before it is dropped, and so is a closing corner** - the one a ring ends on
///when it ends where it began. Neither is a corner, and both were dropped before this existed, the first by
///Path.getPoints and the second by the triangulator. Kept, either would wall an edge of no length.
///
///Every other corner is kept, and that is the one thing here that has gone wrong before: building a Shape
///with a moveTo for every point once dropped the first corner of every outline, and a rectangle came out of
///this view as a triangle. The 2D comparison in render-3d.spec.js is what catches that.
///
function cornersOf(words, at, count) {
    const corners = [];

    for (let i = 0; i < count; i++) {
        const x = words[at + (i * 2)];
        const y = words[at + (i * 2) + 1];

        if (corners.length > 0) {
            const last = corners[corners.length - 1];

            if (last.x === x && last.y === y)
                continue;
        }

        corners.push(new THREE.Vector2(x, y));
    }

    if (corners.length > 1 && corners[0].equals(corners[corners.length - 1]))
        corners.pop();

    return corners;
}

///
///One layer's outlines, extruded into one geometry.
///
///**One geometry rather than one per outline**, which is the whole point: a mesh is a draw call on every
///frame, and the big designs had a hundred thousand of them. See packShapes in Viewer3D.razor.
///
///**The same slabs ExtrudeGeometry made, built without it.** For every outline it made a Shape with a curve
///object per edge, grew plain arrays a corner at a time, generated texture coordinates nothing reads, and
///worked every normal out afterwards from the triangles. Here the corners come straight out of the block, the
///arrays are made once at the size they need, and each normal is known as its face is written.
///
///**What decides the shape is kept exactly.** The ring is turned clockwise and each hole the other way, the
///same triangulator splits the lids, the lid at z 0 takes its triangles reversed and the one at z depth takes
///them as they come, and each wall is the quad between an edge at z 0 and the same edge at z depth, walked in
///the same order. A slab is the one it was, corner for corner. Once the layout is tipped onto its back - see
///LAYOUT_ROTATION_X - the lid at z 0 is the slab's top and the one at z depth its underside.
///
///**Squared off.** ExtrudeGeometry beveled by default, three segments 0.2 deep and 0.1 wide in units it knew
///nothing about - nanometers here - which turned every edge into tilted strips, roughly quadrupled the vertex
///count and made each slab 0.4nm taller than its layer. That was switched off before it was replaced, and
///there is nothing here to switch on.
///
///**Indexed**, unlike what it replaces: a lid's corners are written once and shared by every triangle in it.
///A wall still has four corners of its own, since its normal is not its neighbors'.
///
///Hands back the geometry and, for each outline in the order they were read, the vertex it starts at.
///
function extrudeLayer(words, start, end, depth) {
    return toTheEnd(extrudeLayerSteps(words, start, end, depth));
}

///
///How many outlines a layer is extruded in at a time when the draw pauses - see extrudeLayerSteps. A few
///milliseconds of triangulating, so a pause is never far off.
///
const EXTRUDE_STEP = 64;

///
///The same extrusion, as steps: it yields every EXTRUDE_STEP outlines, so a draw that pauses can let the page in
///between and stop altogether when its load is given up - see drawPaced. Driven to the end in one go it is
///extrudeLayer, so the two cannot build different slabs.
///
function* extrudeLayerSteps(words, start, end, depth) {
    const outlines = [];
    let vertices = 0;
    let indices = 0;
    let stepped = 0;

    //Read and triangulated first, so the arrays below can be made once at the size they need.
    for (let at = start; at < end;) {
        stepped++;

        if (stepped % EXTRUDE_STEP === 0)
            yield;

        const count = words[at];
        const holeCount = words[at + 1];

        at += 2;

        const contour = cornersOf(words, at, count);

        at += count * 2;

        const holes = [];

        for (let hole = 0; hole < holeCount; hole++) {
            const corners = words[at];

            holes.push(cornersOf(words, at + 1, corners));

            at += 1 + (corners * 2);
        }

        if (contour.length < 3)
            continue;

        if (!THREE.ShapeUtils.isClockWise(contour))
            contour.reverse();

        const rings = [contour];

        for (const hole of holes) {
            if (hole.length < 3)
                continue;

            if (THREE.ShapeUtils.isClockWise(hole))
                hole.reverse();

            rings.push(hole);
        }

        const faces = THREE.ShapeUtils.triangulateShape(contour, rings.slice(1));

        let corners = 0;

        for (const ring of rings)
            corners += ring.length;

        outlines.push({ rings: rings, faces: faces });

        //Each corner on both lids, and four more for the wall that starts at it.
        vertices += corners * 6;
        indices += (faces.length * 6) + (corners * 6);
    }

    if (outlines.length === 0)
        return null;

    const position = new Float32Array(vertices * 3);
    const normal = new Float32Array(vertices * 3);
    const index = new Uint32Array(indices);
    const starts = new Uint32Array(outlines.length);

    let vertex = 0;
    let written = 0;

    function put(x, y, z, nx, ny, nz) {
        const at = vertex * 3;

        position[at] = x;
        position[at + 1] = y;
        position[at + 2] = z;

        normal[at] = nx;
        normal[at + 1] = ny;
        normal[at + 2] = nz;

        return vertex++;
    }

    for (let n = 0; n < outlines.length; n++) {
        const outline = outlines[n];

        if (n > 0 && n % EXTRUDE_STEP === 0)
            yield;

        starts[n] = vertex;

        //Every corner on each lid, contour first and then each hole: the order the triangulator numbered
        //them in, so its faces index straight into these.
        const atZero = vertex;

        for (const ring of outline.rings) {
            for (const corner of ring)
                put(corner.x, corner.y, 0, 0, 0, -1);
        }

        const atDepth = vertex;

        for (const ring of outline.rings) {
            for (const corner of ring)
                put(corner.x, corner.y, depth, 0, 0, 1);
        }

        for (const face of outline.faces) {
            index[written++] = atZero + face[2];
            index[written++] = atZero + face[1];
            index[written++] = atZero + face[0];
        }

        for (const face of outline.faces) {
            index[written++] = atDepth + face[0];
            index[written++] = atDepth + face[1];
            index[written++] = atDepth + face[2];
        }

        //The walls, last corner first, each between a corner and the one before it. Its normal is the edge from
        //the corner before to this one, turned a quarter to the left: on a clockwise ring that points out of the
        //slab, and on a hole, which runs the other way, out of the slab and into the hole.
        for (const ring of outline.rings) {
            for (let i = ring.length - 1; i >= 0; i--) {
                let k = i - 1;

                if (k < 0)
                    k = ring.length - 1;

                const from = ring[i];
                const to = ring[k];

                //Never of no length: cornersOf took out every corner that repeats the one before it.
                const length = Math.hypot(from.x - to.x, from.y - to.y);
                const nx = (to.y - from.y) / length;
                const ny = (from.x - to.x) / length;

                const a = put(from.x, from.y, 0, nx, ny, 0);
                const b = put(to.x, to.y, 0, nx, ny, 0);
                const c = put(to.x, to.y, depth, nx, ny, 0);
                const d = put(from.x, from.y, depth, nx, ny, 0);

                index[written++] = a;
                index[written++] = b;
                index[written++] = d;

                index[written++] = b;
                index[written++] = c;
                index[written++] = d;
            }
        }
    }

    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute('position', new THREE.BufferAttribute(position, 3));
    geometry.setAttribute('normal', new THREE.BufferAttribute(normal, 3));
    geometry.setIndex(new THREE.BufferAttribute(index, 1));

    return { geometry: geometry, starts: starts };
}

///A generator's steps all taken at once, and what it hands back at the end.
function toTheEnd(steps) {
    let step = steps.next();

    while (!step.done)
        step = steps.next();

    return step.value;
}

///
///The pictures a drawing's labels wear - one per text and color, however many places it hangs - packed onto
///as few sheets as hold them, and each sheet made a texture.
///
///**One sheet rather than a picture each, so a layer's labels can be one draw**: a draw reads one texture. A
///placed design is the same few cells over and over, each carrying its own pin names, so the RGB mixer hangs
///19,005 labels and between them they need 80 pictures - a sheet and a half wide, a few rows deep.
///
///**Placed first and drawn afterwards**, since a canvas loses what is on it when it is resized and a sheet's
///height is not known until everything is placed. Rows of one height, because every picture is: the font's
///height and the padding round it. A text too wide for a sheet is drawn smaller rather than cut, so it keeps
///its shape and only some of its sharpness.
///
///Hands back the textures, and by text and color where each picture is: its sheet, its shape, and its part of
///the sheet in the 0..1 coordinates a shader reads it in.
///
function packLabelSheets(labels, largestTexture) {
    const side = Math.min(LABEL_SHEET_SIZE, largestTexture);
    const pictureHeight = LABEL_PIXEL_HEIGHT + (LABEL_PIXEL_PADDING * 2);
    const rowHeight = pictureHeight + LABEL_SHEET_GAP;

    const measuring = document.createElement('canvas').getContext('2d');

    measuring.font = LABEL_FONT;

    const looks = new Map();
    const sheets = [];
    let sheet = null;
    let x = 0;
    let y = 0;

    for (const label of labels) {
        const key = label.color + '\n' + label.text;

        if (looks.has(key))
            continue;

        //Measured with the final font, or the picture comes out too narrow and clips the text.
        const pictureWidth = Math.ceil(measuring.measureText(label.text).width) + (LABEL_PIXEL_PADDING * 2);

        let scale = 1;

        if (pictureWidth + (LABEL_SHEET_GAP * 2) > side)
            scale = (side - (LABEL_SHEET_GAP * 2)) / pictureWidth;

        const across = Math.ceil(pictureWidth * scale);

        if (sheet != null && x + LABEL_SHEET_GAP + across + LABEL_SHEET_GAP > side) {
            x = 0;
            y += rowHeight;
        }

        if (sheet == null || y + rowHeight + LABEL_SHEET_GAP > side) {
            sheet = { looks: [], height: 0 };
            sheets.push(sheet);

            x = 0;
            y = 0;
        }

        const look = {
            text: label.text,
            color: label.color,
            sheet: sheets.length - 1,
            left: x + LABEL_SHEET_GAP,
            top: y + LABEL_SHEET_GAP,
            width: pictureWidth,
            scale: scale,
            aspect: pictureWidth / pictureHeight,
            cell: null
        };

        sheet.looks.push(look);
        sheet.height = y + rowHeight + LABEL_SHEET_GAP;

        looks.set(key, look);

        x += LABEL_SHEET_GAP + across;
    }

    const textures = sheets.map(one => {
        const canvas = document.createElement('canvas');

        canvas.width = side;
        canvas.height = one.height;

        const context = canvas.getContext('2d');

        context.font = LABEL_FONT;
        context.textAlign = 'center';
        context.textBaseline = 'middle';

        //A white halo under the glyphs, the same trick the 2D view uses, so a label stays legible against
        //its own layer's color.
        context.lineWidth = LABEL_HALO_WIDTH;
        context.strokeStyle = '#ffffff';

        for (const look of one.looks) {
            context.save();
            context.translate(look.left, look.top);
            context.scale(look.scale, look.scale);

            context.strokeText(look.text, look.width / 2, pictureHeight / 2);

            context.fillStyle = look.color;
            context.fillText(look.text, look.width / 2, pictureHeight / 2);

            context.restore();

            //Up from the bottom, because a canvas goes up to the GPU upside down: its top row of pixels is the
            //top of the texture, which is 1 rather than 0.
            look.cell = [
                look.left / side,
                1 - ((look.top + (pictureHeight * look.scale)) / one.height),
                (look.width * look.scale) / side,
                (pictureHeight * look.scale) / one.height
            ];
        }

        const texture = new THREE.CanvasTexture(canvas);

        texture.colorSpace = THREE.SRGBColorSpace;

        return texture;
    });

    return { textures: textures, looks: looks };
}

///
///The quad every label is drawn on: a unit square about its own middle, facing down the camera's line of sight,
///with the whole of the picture across it. The same one three gives a sprite.
///
function labelQuad() {
    const geometry = new THREE.InstancedBufferGeometry();

    geometry.setIndex([0, 1, 2, 0, 2, 3]);
    geometry.setAttribute('position', new THREE.Float32BufferAttribute([-0.5, -0.5, 0, 0.5, -0.5, 0, 0.5, 0.5, 0, -0.5, 0.5, 0], 3));
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute([0, 0, 1, 0, 1, 1, 0, 1], 2));

    return geometry;
}

/////////////////////////////////////////////////



//C# Interop Functions///////////////////////////

//Init./////////////////////

window.registerThree = function (owner) {

    //Leaving the 3D view and coming back mounts a new container, so this runs again. The previous viewer
    //has to be gone by then: its animation loop would otherwise keep rendering every frame to a canvas no
    //longer in the document, its resize handler would stay on window, and its WebGL context would stay
    //alive - and a browser allows only a handful of those before it starts dropping the oldest, which kills
    //the view that is on screen.
    //
    //It normally is, because the view takes it down as it goes - see unregisterThree. This is for a view
    //that went without saying so.
    if (app != null)
        app.dispose();

    app = null;
    appOwner = 0;

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
    appOwner = owner;
}

//
//Takes the viewer down when the view that built it is left, rather than when the next one is built.
//
//**It went on drawing.** registerThree was the only place a viewer was disposed, so switching to the 2D
//view left the animation loop rendering the whole scene into a canvas no longer in the page, every frame,
//with its WebGL context and everything it had put on the GPU held - until the 3D view was opened again or
//the page reloaded. Measured on the RGB mixer: 840 draws a second into a canvas nobody could see.
//
//**Only the viewer that view built.** Its teardown is a call that arrives whenever it arrives, and the view
//replacing it may have built a viewer of its own by then - so a request to take down "the viewer" could
//take down the one on screen. The number is the view's own; see Viewer3D.razor.
//
window.unregisterThree = function (owner) {
    if (app == null || appOwner !== owner)
        return;

    app.dispose();

    app = null;
    appOwner = 0;
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


//
//Says whose viewer the scene went into: the number registerThree was given, or 0 when there was none to draw
//into yet - which is what tells the view that its scene is not built. See OnDrawn in Viewer3D.razor.
//
window.drawInterOp = function (data) {
    if (app == null)
        return 0;

    //A scene built at a load's pace is older news than this one, which is drawn now - see drawPacedInterOp.
    if (drawingPaced != null)
        drawingPaced.canceled = true;

    app.draw(data);

    return appOwner;
}

///
///How long building a scene runs before it lets the page in, when it is built at a load's pace - the same slice
///the C# side runs to. See Pacer.SliceMilliseconds.
///
const DRAW_SLICE_MS = 80;

///<summary>The scene being built at a load's pace, so a cancel can reach it - or null when none is.</summary>
let drawingPaced = null;

//
//The same draw, at the pace of a load: the page is let in every slice while the layers are extruded, and the
//draw stops when cancelDrawInterOp says its load was given up - see Viewer3D.drawPaced. Answers as drawInterOp
//does, with the viewer it drew into, or 0 for none: given up, or the viewer replaced while it was built.
//
//**Building a chip's scene is JavaScript, two and a half seconds of the RGB mixer's**, and a published build's C#
//is quick enough that this is the longest part of opening one in this view. Unpaced, the note's Cancel could not
//be pressed through it.
//
window.drawPacedInterOp = async function (data) {
    if (app == null)
        return 0;

    if (drawingPaced != null)
        drawingPaced.canceled = true;

    const ticket = { canceled: false, since: performance.now() };
    const viewer = app;
    const owner = appOwner;

    drawingPaced = ticket;

    let drew = false;

    try {
        drew = await viewer.drawPaced(data, ticket);
    }
    finally {
        if (drawingPaced === ticket)
            drawingPaced = null;
    }

    if (!drew || viewer !== app)
        return 0;

    return owner;
}

///<summary>Gives up the scene being built at a load's pace, which stops at its next slice and leaves the scene as it was.</summary>
window.cancelDrawInterOp = function () {
    if (drawingPaced != null)
        drawingPaced.canceled = true;
}


///
///Moves the layers already in the scene to new heights, without building anything.
///
///`offsets` is one height per layer, in the stacking order - so a mesh tagged with its layer's place looks
///its own new height up by index. See Viewer3D.restackLayers for why the spacing slider comes through here
///rather than through a redraw: a redraw is the whole scene marshaled and rebuilt, and this is a Y write
///per object.
///
///Shifted by the difference rather than assigned, because a layer's labels sit at its offset plus a thickness
///and a clearance - so what is stored on each object is the offset that went *into* it, and the delta is the
///only thing that can be applied to both a slab and a layer's labels with one line.
///
window.restackLayers = function (offsets) {
    if (app == null || offsets == null)
        return;

    app.restack(offsets);
}


///Flashes one layer's slabs, so a row in the sidebar and a slab in the stack can be told to be the same one.
window.pulseLayerInterOp = function (at) {
    if (app == null || at == null)
        return;

    app.pulseLayer(at);
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

        //Nor while the game is on, which flies the camera behind the car - and puts it back when it stops.
        if (cameraKey == null || app == null || app.cinematicViewToggle || app.game != null)
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

    //Asked for rather than left to the change update() may or may not send - see centerCameraInterOp.
    app.wantFrame();

    return true;
}

//
//Puts the whole stack back in the middle of the window, for when it is no longer there - and frames a file
//the first time it is drawn, which is the same fit.
//
//**The opening angle, not the current one.** This looks down at the middle of the layout from in front and
//above rather than framing it from wherever the camera happens to be - a view somebody has orbited under the
//floor is exactly the state this button exists to get out of, and centering it in place would leave it there.
//
//**The distance is worked out from the field of view rather than picked**, corner by corner, a tenth over so
//nothing sits against the edge - see framedCamera in viewGeometry.js.
//
//**Looking down, at forty-five degrees.** It looked straight across the layout until this, which shows a cell's
//layers side by side and a chip edge-on. Seen from that low, a die is a line across the window.
//
window.centerCameraInterOp = function () {
    if (app == null || app.camera == null || app.controls == null || app.chipObjectsGroup == null)
        return false;

    //
    //The corners of each layer's box, and of a box around each layer's labels, rather than of one box around the
    //lot - which has corners where nothing is drawn. See framedCamera.
    //
    //Both are a box per object, since a layer's labels are one mesh - see drawLabels. They were a sprite each,
    //tens of thousands of them on a design, and framing on every one took 450 ms where a box per layer takes a few.
    //
    const boxes = [];

    app.chipObjectsGroup.updateMatrixWorld(true);

    for (const drawn of app.chipObjectsGroup.children) {
        const bounds = new THREE.Box3().setFromObject(drawn);

        if (!bounds.isEmpty())
            boxes.push(bounds);
    }

    const points = [];

    for (const box of boxes)
        for (const x of [box.min.x, box.max.x])
            for (const y of [box.min.y, box.max.y])
                for (const z of [box.min.z, box.max.z])
                    points.push({ x: x, y: y, z: z });

    if (points.length === 0)
        return false;

    const framed = window.viewGeometry.framedCamera(points, app.camera.fov, app.camera.aspect);

    if (framed == null)
        return false;

    app.camera.position.set(framed.position.x, framed.position.y, framed.position.z);
    app.controls.target.set(framed.target.x, framed.target.y, framed.target.z);

    app.controls.update();

    //
    //Said rather than left to the controls.
    //
    //update() dispatches change only when it decides the camera moved, and the report is what puts this
    //framing into the session and the address. Pressing center and coming back to the old view would be a
    //quiet way to lose it, and the timer behind this is debounced, so saying it twice costs nothing.
    //
    //The frame likewise: a frame is drawn only when one is asked for - see animate.
    //
    reportCameraWhenStill();

    app.wantFrame();

    return true;
}

//
//What the scene has drawn, for the Debug info panel: frames since it was built, and the draw calls and triangles of
//the last one. Null without a viewer, which is every view but this one now that a viewer goes with its view.
//
//**three.js keeps no frame rate.** Its renderer counts frames, draw calls and triangles - renderer.info - and a rate
//is something measured over time, which is what frameMeter.js does with these. Counted on the scene's own render
//rather than off the renderer's frame count, which goes up twice a frame: the corner indicator is a second render.
//
window.threeFrameStats = function () {
    if (app == null)
        return null;

    return { frames: app.framesDrawn, calls: app.drawCalls, triangles: app.trianglesDrawn };
}

//
//Zooms from the buttons beside Center: the camera moved along its line to what the orbit turns around, by the 2D
//view's wheel notches - see viewGeometry.zoomedDistance - so a press is the same amount in either view.
//
//Toward the orbit's target, which is how the wheel zooms here too, and within the distances the controls allow.
//Not while Admire is on, which moves the camera itself every frame, nor in a headset, which owns it.
//
window.zoomCameraInterOp = function (notches) {
    if (app == null || app.camera == null || app.controls == null || notches === 0)
        return;

    if (app.cinematicViewToggle || app.renderer.xr.isPresenting)
        return;

    const offset = app.camera.position.clone().sub(app.controls.target);
    const wanted = window.viewGeometry.zoomedDistance(offset.length(), notches);
    const distance = THREE.MathUtils.clamp(wanted, app.controls.minDistance, app.controls.maxDistance);

    if (!(distance > 0))
        return;

    offset.setLength(distance);

    app.camera.position.copy(app.controls.target).add(offset);
    app.controls.update();

    //Said rather than left to the controls, for the reason centerCameraInterOp gives.
    reportCameraWhenStill();

    app.wantFrame();
}

//
//Puts the camera on a box of the layout - left, bottom, right and top in its own units - and flashes a frame round it:
//a placement or a shape pressed in the cell tree beside this view. See the viewer's frameOn, and CellTree3D.razor.
//
//Not while Admire turns the camera, a game drives it or a headset has it, each of which would take it straight back.
//Says whether it moved.
//
window.frameCameraOnInterOp = function (left, bottom, right, top) {
    if (app == null || app.camera == null || app.controls == null || app.chipObjectsGroup == null)
        return false;

    if (app.cinematicViewToggle || app.game != null || app.renderer.xr.isPresenting)
        return false;

    return app.frameOn(left, bottom, right, top);
}

//
//Starts the chip game, and says whether it did: false when nothing is drawn to drive on.
//
//`handle` is the component's, to be told when the game is stopped by something other than the Settings menu.
//`layerNames` is each layer's name in stacking order, which is what every slab knows its layer by.
//
//Admire is turned off by the component before this is called rather than here, since the switch that shows
//it is on is the component's to turn off.
//
window.startGameInterOp = function (handle, layerNames) {
    if (app == null || app.renderer.xr.isPresenting)
        return false;

    if (app.game != null)
        return true;

    const game = new ChipGame(app, handle, layerNames);

    if (!game.start())
        return false;

    app.game = game;

    return true;
}

window.stopGameInterOp = function () {
    if (app == null || app.game == null)
        return;

    app.game.stop(false);
}

//A picture for the layers that name it - see Viewer3D.setTexture. `bytes` arrives as a Uint8Array.
window.setTextureInterOp = function (name, bytes) {
    if (app == null)
        return;

    app.setTexture(name, bytes);
}

//The game's panel opened again - Settings, Game controls - after its × put it away.
window.showGamePanelInterOp = function () {
    if (app == null || app.game == null)
        return;

    app.game.setPanel(true);
}

//
//The 3D view's own settings, for the bar's menu to show - see Viewer3D.razor - as they are on the scene now: a game's,
//while one runs. As this browser keeps them when there is no viewer yet.
//
window.viewOptionsInterOp = function () {
    let options = loadViewOptions();

    if (app != null)
        options = app.view;

    return { drawDistance: options.drawDistance, textures: options.textures };
}

//
//One of them set from the bar's menu: `name` is drawDistance or textures. See Viewer3D.setViewOption. **And with no 3D
//view up**, kept for the one that comes: a texture given a layer in the 2D view's layer settings turns Show textures
//on, so the layer shows it when the 3D view opens.
//
window.setViewOptionInterOp = function (name, value) {
    if (app != null) {
        app.setViewOption(name, value);

        return;
    }

    const options = loadViewOptions();

    if (name === 'drawDistance')
        options.drawDistance = clampGameOption(Number(value), GAME_DRAW_DISTANCES, VIEW_OPTIONS.drawDistance);
    else if (name === 'textures')
        options.textures = value === true;

    saveViewOptions(options);
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

        //The scene's own counts, kept by animate - see threeFrameStats.
        this.framesDrawn = 0;
        this.drawCalls = 0;
        this.trianglesDrawn = 0;

        //Whether something has changed since the last frame drawn, which is what has the next one drawn - see animate.
        this.frameWanted = true;

        //The chip game while it is running, and null the rest of the time. See ChipGame.
        this.game = null;

        //
        //The view's own settings - how far it draws and whether the layers show their textures - and the haze the far
        //side fades into short of All, in the color behind the scene. See VIEW_OPTIONS and fitDepthRange.
        //
        this.view = loadViewOptions();
        this.viewHaze = new THREE.Fog(DESKTOP_CLEAR_COLOR, 1, 2);

        //Whether the layers are drawn with their surfaces now - Show textures. See setSurfaces.
        this.surfaces = this.view.textures;

        //Whether the chip game's neon is on, and the glow the scene is drawn through then - made the first time. See setNeon.
        this.neon = false;
        this.neonGlow = null;

        //The color the picture behind the scene is round its horizon, for the neon's haze - see toneOf - or null with none up.
        this.backdropTone = null;

        //The pictures layers' textures are drawn with, by the names the layers give them - see setTexture.
        this.textures = new Map();

        //When the last frame was drawn, so the game can be stepped by how long it has actually been.
        this.lastFrame = null;

        //Whether the running session composites onto a camera feed, which changes both how the layout is
        //positioned and whether anything may be drawn behind it.
        this.isArSession = false;
        this.backgroundBeforeXr = null;

        //The Enter VR and Enter AR buttons, which three.js builds and this viewer only places.
        this.xrButtons = [];

        //What re-measures the row they are in when three.js relabels one. See fitXrButtons.
        this.xrRowWatch = null;

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

        //A context the browser took away and gave back holds nothing until a frame is drawn into it.
        this.renderer.domElement.addEventListener('webglcontextrestored', () => this.wantFrame());

        //Create a scene
        this.scene = new THREE.Scene();

        //Create and init. a camera and add it to the scene.
        this.camera = new THREE.PerspectiveCamera(100, this.settings.containerSizeX / this.settings.containerSizeY, DESKTOP_NEAR, DESKTOP_FAR);
        this.camera.position.z = 2000;
        this.scene.add(this.camera);

        //The corner axis indicator, in a scene of its own so it is never lit, exported, fitted or picked
        //along with the layout. Orthographic, so an arm pointing at the camera is not foreshortened into
        //a dot the way a perspective one is.
        this.axesScene = this.buildAxesIndicator();
        this.axesCamera = new THREE.OrthographicCamera(-AXES_EXTENT, AXES_EXTENT, AXES_EXTENT, -AXES_EXTENT, 0.1, 100);

        //Held rather than made each time: the indicator reads the canvas size once a frame, and a vector
        //allocated in the animation loop is one the collector has to take back sixty times a second.
        this.axesCanvasSize = new THREE.Vector2();

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

        //And a frame for every move, since nothing is drawn that nothing asked for - see animate.
        this.controls.addEventListener('change', () => this.wantFrame());

        //Init. a texture loader. It will get used by changeBackground().
        this.textureLoader = new THREE.TextureLoader();

        //Where the layout is, taken whenever what is drawn or where it stands changes. The depth range and
        //Admire's orbit both work from it, and the depth range does so on every frame - which is why it is
        //kept rather than measured there. See measureLayout.
        this.layoutBounds = null;

        //Admire's orbit, centered and sized on the layout by measureLayout - see sizeCinematicView.
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

        //three.js relabels and resizes both a frame or two from now, when isSessionSupported resolves - and the
        //labels are what decide whether the row fits along the foot. So the row is measured again whenever a
        //button in it changes, as well as whenever the view does (resizeToContainer). Only style, not class:
        //the class is what the measuring sets.
        const row = document.getElementById('xrButtons');

        if (row != null) {
            this.xrRowWatch = new MutationObserver(() => this.fitXrButtons());
            this.xrRowWatch.observe(row, { subtree: true, childList: true, characterData: true, attributeFilter: ['style'] });
        }

        this.fitXrButtons();



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

    ///
    ///Whether the headset row still fits along the foot of the view, clear of the axis indicator in its bottom-left
    ///corner, or goes up the right-hand side.
    ///
    ///**Measured, because no width written in CSS is right for every browser.** The row is as wide as the
    ///labels three.js gives it: 150 pixels a button for NOT SUPPORTED in a desktop Chromium, 180 for WEBXR NOT
    ///AVAILABLE in a browser with no WebXR at all. A container query wide enough for the second stacked the
    ///first where it fits, and one narrow enough for the first let the second run into what was beside it.
    ///
    ///So the row is laid out as a row and asked: clear of the indicator by XR_ROW_CLEARANCE, and not hanging off
    ///the right of the view - the row is centered, so that is the same question asked of each side. Until
    ///2026-10-17 it was Admire's button on the right it had to clear; Admire is a switch in the view's settings now.
    ///Taking the class off, measuring and putting it back all happen before anything is painted, so a row that
    ///does not fit is never seen. The indicator's place does not depend on the answer, and neither do the
    ///buttons' widths, so asking again gives the same answer rather than flipping.
    ///
    fitXrButtons() {
        const row = document.getElementById('xrButtons');
        const view = this.settings.containerElement.getBoundingClientRect();

        //A view that is not on screen measures zero, which answers nothing - so the row is left as it was, and
        //asked again when the view is shown and resizeToContainer hears about it.
        if (row == null || view.width <= 0)
            return;

        row.classList.remove('stacked');

        const across = row.getBoundingClientRect();

        if (across.width <= 0)
            return;

        //The indicator is drawn into the canvas rather than laid over it - see drawAxesIndicator - so its edge is
        //worked out from where it is drawn, the square AXES_MARGIN in from the corner.
        const indicatorEnds = view.left + AXES_MARGIN + AXES_VIEWPORT + XR_ROW_CLEARANCE;

        if (across.left < indicatorEnds || across.right > view.right)
            row.classList.add('stacked');
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
        this.wantFrame();

        //requestAnimationFrame(this.startRender3D.bind(this));//setAnimationLoop() needs to be used for VR.
        this.renderer.setAnimationLoop(this.animate.bind(this));
    }

    animate() {
        const now = performance.now();
        const elapsed = (now - (this.lastFrame ?? now)) / 1000;

        this.lastFrame = now;

        //Perform animations of objects or movements of camera. The game and Admire both fly the camera, so
        //only one of them can have it - and the component turns Admire off before it starts a game.
        if (this.game != null)
            this.game.update(elapsed);
        else
            this.runCinematicView();

        this.runPulse();
        this.runMark();

        //After anything that moves the camera, and before the frame it moved for.
        this.fitDepthRange();

        //
        //**Drawn only when something has changed.** The scene was drawn on every frame the browser showed,
        //standing still included: on the RGB mixer that was 2,463 WebGL draws a second, each frame most of the
        //GPU's time, to put the same picture back. A canvas keeps the last picture drawn into it, so a frame
        //not drawn shows the one before, and the loop goes on running to see whether one is wanted.
        //
        //What asks is wantFrame: the camera moving, a scene drawn, a restack, a background, a resize, a
        //headset coming or going. What moves on its own draws for as long as it moves - see drawsThisFrame.
        //
        if (!this.drawsThisFrame())
            return;

        this.frameWanted = false;

        //
        //**Through the glow while the game has its neon on** - see setNeon and NeonGlow: the scene into a picture of its
        //own, and the picture onto the canvas with the glow over it. Not in a headset, which draws each eye itself.
        //
        let glow = null;

        if (this.neon && !this.renderer.xr.isPresenting) {
            if (this.neonGlow == null)
                this.neonGlow = new NeonGlow(this.renderer);

            glow = this.neonGlow;
        }

        //Call renderer to render the scene.
        if (glow != null)
            glow.drawScene(this.scene, this.camera);
        else
            this.renderer.render(this.scene, this.camera);

        //What that frame of the scene took, for the Debug info panel - read now, because the corner indicator's
        //render below starts the renderer's counts again. See threeFrameStats.
        this.framesDrawn++;
        this.drawCalls = this.renderer.info.render.calls;
        this.trianglesDrawn = this.renderer.info.render.triangles;

        //The glow after the counts, since its passes are not the scene's.
        if (glow != null)
            glow.drawGlow();

        //After the scene, because it is drawn over the top of it.
        this.drawAxesIndicator();
    }

    ///
    ///Whether this frame is drawn: one was asked for, or something is moving by itself - Admire turning the
    ///camera, a layer pulsing, the tree's frame beating, the chip game's car and the camera chasing it, or a
    ///headset, which shows a frame of its own every time and moves the camera with the head.
    ///
    drawsThisFrame() {
        if (this.frameWanted || this.cinematicViewToggle || this.pulsing != null || this.marking != null || this.game != null)
            return true;

        return this.renderer.xr.isPresenting;
    }

    ///Has the next frame drawn. Called by everything that changes what the scene shows - see animate.
    wantFrame() {
        this.frameWanted = true;
    }

    //The corner axis indicator//////////////////////////////

    ///
    ///Builds the indicator's scene: three arms out of one origin, each with a head and a letter.
    ///
    ///Basic materials throughout, and its own scene, so it is unlit - a shaded gizmo goes dark exactly when
    ///the camera is somewhere the lights are not, which is the moment somebody is most likely to be looking
    ///at it to work out where they are.
    ///
    buildAxesIndicator() {
        const scene = new THREE.Scene();
        const origin = new THREE.Vector3(0, 0, 0);

        for (const axis of AXES) {
            const end = axis.direction.clone().multiplyScalar(AXES_ARM);

            const line = new THREE.Line(
                new THREE.BufferGeometry().setFromPoints([origin, end]),
                new THREE.LineBasicMaterial({ color: axis.color }));

            scene.add(line);

            //A cone stands up its own +Y, so it is turned from there onto the arm it caps.
            const head = new THREE.Mesh(
                new THREE.ConeGeometry(AXES_HEAD_RADIUS, AXES_HEAD_LENGTH, 12),
                new THREE.MeshBasicMaterial({ color: axis.color }));

            head.position.copy(end);
            head.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), axis.direction);

            scene.add(head);

            const letter = this.buildAxisLetter(axis.letter, axis.color);

            letter.position.copy(axis.direction.clone().multiplyScalar(AXES_LETTER_AT));

            scene.add(letter);
        }

        return scene;
    }

    ///<summary>One axis letter as a camera-facing sprite, haloed the way the layer labels are.</summary>
    buildAxisLetter(letter, color) {
        const canvas = document.createElement('canvas');

        canvas.width = AXES_LETTER_PIXELS;
        canvas.height = AXES_LETTER_PIXELS;

        const context = canvas.getContext('2d');

        context.font = `bold ${Math.round(AXES_LETTER_PIXELS * 0.72)}px sans-serif`;
        context.textAlign = 'center';
        context.textBaseline = 'middle';

        //The same white halo the layer labels carry, and for the same reason: the indicator sits over the
        //layout rather than over a background of its own, so a letter has to stay readable against a slab
        //of any color that happens to be behind it.
        context.lineWidth = LABEL_HALO_WIDTH;
        context.strokeStyle = '#ffffff';
        context.strokeText(letter, canvas.width / 2, canvas.height / 2);

        context.fillStyle = color;
        context.fillText(letter, canvas.width / 2, canvas.height / 2);

        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;

        //depthWrite off, depth test left on: the letters are transparent quads, and letting them write
        //depth lets whichever drew first cut a rectangular hole in whatever comes after it.
        const sprite = new THREE.Sprite(new THREE.SpriteMaterial({
            map: texture,
            transparent: true,
            depthWrite: false
        }));

        sprite.scale.set(AXES_LETTER_SIZE, AXES_LETTER_SIZE, 1);

        return sprite;
    }

    ///
    ///Draws the indicator into the bottom-left corner of the canvas the scene was just drawn into.
    ///
    ///**It turns with the camera and never moves with it.** The little camera takes the main one's
    ///orientation exactly - quaternion rather than a lookAt, so a rolled camera rolls the indicator too -
    ///and sits back along its own local Z at a fixed distance. The arms are therefore seen from the angle
    ///the layout is seen from, which is the whole of what the indicator has to say.
    ///
    ///**Depth is cleared and color is not.** The indicator has to sit in front of the layout without
    ///punching a hole in it, and a viewport that cleared color would do exactly that - the scene is
    ///already drawn by the time this runs.
    ///
    drawAxesIndicator() {
        //An XR session renders one viewport per eye and owns setViewport while it does. A flat overlay
        //pinned to a corner of the canvas means nothing in a headset anyway.
        if (this.renderer.xr.isPresenting)
            return;

        //Released by dispose, which stops the loop first - so this only ever guards a frame already in
        //flight when the view went away.
        if (this.axesScene == null)
            return;

        this.axesCamera.quaternion.copy(this.camera.quaternion);
        this.axesCamera.position.set(0, 0, AXES_CAMERA_BACK).applyQuaternion(this.camera.quaternion);

        //Read back rather than remembered: this is what the renderer is currently sized to, whatever the
        //last resize made of it.
        const size = this.renderer.getSize(this.axesCanvasSize);

        this.renderer.autoClear = false;
        this.renderer.clearDepth();
        this.renderer.setViewport(AXES_MARGIN, AXES_MARGIN, AXES_VIEWPORT, AXES_VIEWPORT);

        this.renderer.render(this.axesScene, this.axesCamera);

        //Put the viewport back, or the next frame's scene is drawn into the corner as well.
        this.renderer.setViewport(0, 0, size.x, size.y);
        this.renderer.autoClear = true;
    }

    //Pointing a layer out///////////////////////////////////////

    ///
    ///Lights one layer's slabs for a moment, for a press on its row in the sidebar.
    ///
    ///**The answer to "which one is this?"**, which is the only question a row can ask in this view: there is
    ///nothing to draw on in 3D, so the press that picks a layer to draw on in 2D has nothing to do here. A
    ///stack of nine slabs seen at an angle does not say which of them is `met1`, and the name in the list
    ///does not say which slab it is - so the two have to be joined by something, and a flash is the cheapest
    ///thing that joins them without changing what is on screen.
    ///
    ///Keyed on the same `stackAt` restack uses, since that is already what a slab knows about the layer it
    ///belongs to. A layer is one mesh with a material of its own, so lighting it cannot light anything else.
    ///
    pulseLayer(at) {
        if (this.chipObjectsGroup == null)
            return;

        //Whatever was flashing stops where it is, so pressing a second row restarts rather than fights.
        this.endPulse();

        const lit = [];

        for (const drawn of this.chipObjectsGroup.children) {
            if (drawn.userData.stackAt !== at)
                continue;

            //A layer's labels are a picture and nothing else, with no emissive to raise - see drawLabels.
            if (drawn.material == null || drawn.material.emissive == null)
                continue;

            lit.push({ material: drawn.material, was: drawn.material.emissive.getHex() });
        }

        if (lit.length === 0)
            return;

        this.pulsing = { lit: lit, started: performance.now() };
    }

    ///
    ///One frame of that, from the render loop.
    ///
    ///A sine over the whole run, squared. Squaring is what makes it read as a pulse rather than a flicker:
    ///it holds near nothing for most of each beat and swells briefly, and it starts and ends at exactly
    ///nothing, so the layer is left the color it was without a step at either end.
    ///
    runPulse() {
        if (this.pulsing == null)
            return;

        const gone = performance.now() - this.pulsing.started;

        if (gone >= PULSE_MS) {
            this.endPulse();

            return;
        }

        const wave = Math.sin((gone / PULSE_MS) * Math.PI * PULSE_BEATS);
        const strength = wave * wave * PULSE_STRENGTH;

        for (const one of this.pulsing.lit)
            one.material.emissive.setScalar(strength);
    }

    ///Puts the colors back. Called when the run ends, when another starts, and when the scene is thrown away.
    endPulse() {
        if (this.pulsing == null)
            return;

        for (const one of this.pulsing.lit)
            one.material.emissive.setHex(one.was);

        this.pulsing = null;

        //One frame more, or the last one drawn is the layer still lit.
        this.wantFrame();
    }

    ///
    ///The camera on a box of the layout, in its own units, and a frame flashed round it - a placement or a shape the
    ///cell tree was pressed on. See frameCameraOnInterOp.
    ///
    ///**Framed the way Center frames the whole layout**, looking down at forty-five degrees from in front, on the box's
    ///corners from the foot of the stack to its top - see framedCamera. The stack's whole height rather than only the
    ///layers drawn in the box: a cell a few microns across comes out as the column of layers it is.
    ///
    frameOn(left, bottom, right, top) {
        const stack = this.stackHeights();

        if (stack == null)
            return false;

        //The layout's corners as the scene has them - its Y along Z and the heights up Y - out through the group,
        //which centers the stack about its middle.
        this.chipObjectsGroup.updateMatrixWorld(true);

        const points = [];

        for (const x of [left, right])
            for (const y of [stack.low, stack.high])
                for (const z of [bottom, top]) {
                    const corner = new THREE.Vector3(x, y, z).applyMatrix4(this.chipObjectsGroup.matrixWorld);

                    points.push({ x: corner.x, y: corner.y, z: corner.z });
                }

        const framed = window.viewGeometry.framedCamera(points, this.camera.fov, this.camera.aspect);

        if (framed == null)
            return false;

        this.camera.position.set(framed.position.x, framed.position.y, framed.position.z);
        this.controls.target.set(framed.target.x, framed.target.y, framed.target.z);
        this.controls.update();

        //Said rather than left to the controls, for the reason centerCameraInterOp gives.
        reportCameraWhenStill();

        this.markBox(points);
        this.wantFrame();

        return true;
    }

    ///The foot of the stack and its top, in the group's own heights - or null, with nothing drawn.
    stackHeights() {
        let low = null;
        let high = null;

        for (const drawn of this.chipObjectsGroup.children) {
            const at = drawn.userData.stackOffset;

            if (at == null)
                continue;

            const top = at + (drawn.userData.depth ?? 0);

            if (low == null || at < low)
                low = at;

            if (high == null || top > high)
                high = top;
        }

        if (low == null)
            return null;

        return { low: low, high: high };
    }

    ///
    ///A frame round the corners given, beating three times and gone - the answer to "which one is it?" once the tree
    ///has put the camera on a placement: a standard cell looks like every other one in its row, and where the camera
    ///points says only roughly which. Drawn through the slabs, since they are inside it.
    ///
    markBox(points) {
        this.endMark();

        const box = new THREE.Box3();

        for (const point of points)
            box.expandByPoint(new THREE.Vector3(point.x, point.y, point.z));

        const frame = new THREE.Box3Helper(box, new THREE.Color(MARK_COLOR));

        frame.material.transparent = true;
        frame.material.opacity = 0;
        frame.material.depthTest = false;
        frame.renderOrder = 999;

        this.scene.add(frame);

        this.marking = { frame: frame, started: performance.now() };
    }

    ///One frame of that, from the render loop: the layer pulse's beat, on the frame's opacity.
    runMark() {
        if (this.marking == null)
            return;

        const gone = performance.now() - this.marking.started;

        if (gone >= PULSE_MS) {
            this.endMark();

            return;
        }

        const wave = Math.sin((gone / PULSE_MS) * Math.PI * PULSE_BEATS);

        this.marking.frame.material.opacity = wave * wave;
    }

    ///Takes the frame away. Called when the run ends, when another starts, and when the scene is thrown away.
    endMark() {
        if (this.marking == null)
            return;

        this.scene.remove(this.marking.frame);
        this.marking.frame.geometry.dispose();
        this.marking.frame.material.dispose();
        this.marking = null;

        this.wantFrame();
    }

    //XR///////////////////////////////////////////////////////

    onXrSessionStart() {
        //The headset has the camera now, and the game drives it from a keyboard nobody wearing one can see.
        if (this.game != null)
            this.game.stop(true);

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

        this.wantFrame();
    }

    onXrSessionEnd() {
        //Back to the layout's own units, centered on the origin where the orbit controls expect it.
        this.chipObjectsGroup.scale.set(1, 1, 1);
        this.chipObjectsGroup.position.set(0, 0, 0);

        //And the stack about its own middle again, which the line above undoes: the fit had moved the whole
        //group, and putting it back at zero left the stack higher than the camera was framed on by half its
        //own height.
        this.centerStack();

        this.measureLayout();

        //The planes go back to the desktop's, and the next frame puts them around the layout.
        this.camera.near = DESKTOP_NEAR;
        this.camera.far = DESKTOP_FAR;
        this.camera.updateProjectionMatrix();

        if (this.isArSession) {
            this.renderer.setClearColor(DESKTOP_CLEAR_COLOR, 1);
            this.scene.background = this.backgroundBeforeXr;
            this.backgroundBeforeXr = null;
        }

        this.isArSession = false;

        //The page's canvas still holds whatever was drawn before the headset took over.
        this.wantFrame();
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

    ///
    ///Takes where the layout is again, for everything that works from it: the depth range on every frame, and
    ///Admire's orbit.
    ///
    ///Called wherever what is drawn or where it stands changes - a draw, a restack, the end of a headset
    ///session - rather than on every frame, because measuring walks every object in the group.
    ///
    measureLayout() {
        const bounds = new THREE.Box3().setFromObject(this.chipObjectsGroup);

        if (bounds.isEmpty()) {
            this.layoutBounds = null;
            return;
        }

        this.layoutBounds = bounds;

        this.sizeCinematicView();
    }

    ///
    ///Puts the near and far planes either side of the layout, wherever the camera has got to.
    ///
    ///**Every frame, because the camera is somewhere else on every frame** - an orbit, a zoom, Admire, a camera put
    ///back from a link. The arithmetic is eight corners, and the projection is only rebuilt when it changed. See
    ///depthRange in viewGeometry.js.
    ///
    ///Not in a headset, which has planes of its own for a layout scaled to half a meter - see onXrSessionStart.
    ///
    ///**Nor while the chip game is on**, which sets its own - see ChipGame.frameCamera. The box is the layout's,
    ///and the car is not in it: jumped high over the stack, with the camera above it, the nearest corner of the
    ///box is further off than the car is, so a near plane put just short of the box cuts the car out of the view.
    ///
    fitDepthRange() {
        if (this.renderer.xr.isPresenting || this.layoutBounds == null || this.game != null)
            return;

        const range = window.viewGeometry.depthRange(this.layoutBounds, this.camera.position, this.camera.fov, this.camera.aspect);

        if (range == null)
            return;

        //
        //**And the draw distance**, short of All: the far plane brought in to that share of the chip's width past its
        //nearest point, with the far side faded into the background - see drawReach in viewGeometry.js. A picture
        //behind the scene has no one color to fade into, so there the far side is cut instead, as in the game.
        //
        let far = range.far;
        let haze = null;

        const reach = window.viewGeometry.drawReach(this.layoutBounds, this.camera.position, this.view.drawDistance);

        if (reach != null && reach.far < far) {
            far = reach.far;

            if (this.scene.background == null) {
                this.viewHaze.near = reach.hazeFrom;
                this.viewHaze.far = reach.far;

                haze = this.viewHaze;
            }
        }

        if (this.scene.fog !== haze)
            this.scene.fog = haze;

        if (range.near === this.camera.near && far === this.camera.far)
            return;

        this.camera.near = range.near;
        this.camera.far = far;
        this.camera.updateProjectionMatrix();
    }

    ///
    ///Centers Admire's orbit on the layout and sizes it to it.
    ///
    ///**Sized, because a fixed orbit only suited a cell.** It ran 10,000 units out whatever was open, which put
    ///the camera inside every bundled design. **And kept up to date**, because it is measured with the layout
    ///rather than when Admire is pressed: a file opened, or a layer switched off, while it is running used to
    ///leave it circling where the old layout's middle had been.
    ///
    sizeCinematicView() {
        if (this.layoutBounds == null)
            return;

        const reach = window.viewGeometry.admireReach(this.layoutBounds);

        this.layoutBounds.getCenter(this.cinematicSettings.chipCenterPoint);
        this.cinematicSettings.camera_offset = { x: reach, y: reach, z: reach };
    }

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

        const words = wordsOf(data.points);

        for (const layer of data.layers || []) {
            const built = extrudeLayer(words, layer.start, layer.end, layer.depth);

            //Nothing on it encloses anything, once repeated corners are taken out.
            if (built == null)
                continue;

            this.chipObjectsGroup.add(this.layerMesh(built, layer, words));
        }

        this.drawn(data);
    }

    ///
    ///The same scene, built at the pace of a load: the layers are extruded a few dozen outlines at a time, and once
    ///a slice has run the page is let in for a turn - so it answers while a big chip is built, and Cancel is heard.
    ///See drawPacedInterOp, and Pacer in the C#, which paces everything before this.
    ///
    ///**Built on the side and put in whole.** The meshes go into the scene only once every one of them is made, so
    ///a draw given up halfway leaves the scene as it was rather than half of the new one; what it had made is let
    ///go of. Says whether it drew.
    ///
    async drawPaced(data, ticket) {
        const words = wordsOf(data.points);
        const meshes = [];

        for (const layer of data.layers || []) {
            const steps = extrudeLayerSteps(words, layer.start, layer.end, layer.depth);
            let step = steps.next();

            while (!step.done) {
                if (performance.now() - ticket.since > DRAW_SLICE_MS) {
                    await new Promise(resolve => setTimeout(resolve, 0));

                    ticket.since = performance.now();
                }

                if (ticket.canceled)
                    break;

                step = steps.next();
            }

            if (ticket.canceled)
                break;

            if (step.value != null)
                meshes.push(this.layerMesh(step.value, layer, words));
        }

        if (ticket.canceled) {
            for (const mesh of meshes) {
                disposeNeonEdges(mesh);

                mesh.geometry.dispose();
                mesh.material.dispose();
            }

            return false;
        }

        this.clearChipObjects();

        for (const mesh of meshes)
            this.chipObjectsGroup.add(mesh);

        this.drawn(data);

        return true;
    }

    ///
    ///One layer's slab as a mesh: lit, ready for its surface and given it if the layers have theirs now - see surfaceOf
    ///and setSurfaces - tipped onto its back, and placed at its height in the stack.
    ///
    layerMesh(built, layer, words) {
        const material = surfaceOf(new THREE.MeshPhongMaterial({
            color: layer.color,
            wireframe: false,
            shininess: 150
        }), layer, this.surfaces);

        //Its picture, if it names one this viewer has - and if not yet, setTexture puts it on when it arrives.
        const texture = this.textures.get(material.userData.textureName);

        if (texture != null)
            bindTexture(material, texture);

        const mesh = new THREE.Mesh(built.geometry, material);

        mesh.rotation.set(LAYOUT_ROTATION_X, 0.0, 0);

        //
        //**Its offset plus its thickness, because the slab hangs downward from wherever it is put.**
        //
        //A slab is built from local z 0 to z depth - see extrudeLayer - and tipping the layout onto its back
        //maps local +Z onto world -Y, so a mesh placed at its offset occupies offset-depth to offset, and
        //the process table means the opposite: a layer *starts* at its height and is thick upward.
        //
        //Left uncorrected, every layer sat below where it belonged by its own thickness - and since
        //thicknesses differ, that is not a stack shifted down but a stack pulled apart. Measured against
        //an independent extrusion of the same cells: licon1 came out at -940..0 where the table puts it
        //at 0..940, with li1 floating at 840..940 - so the contact did not reach the metal it exists to
        //join. Both cells came out 460 and 440nm taller than the process they were built from.
        //
        //Placing the top face at offset + depth is what puts the underside back on offset.
        //
        mesh.position.set(0, layer.offset + layer.depth, 0);

        //Which layer it is and the height it was built at, so restackLayers can move it without the scene
        //being handed over again. See restackLayers.
        mesh.userData.stackAt = layer.at;
        mesh.userData.stackOffset = layer.offset;

        //How many outlines went into it and where each one's vertices begin. The merge still decides what a
        //slab is, and this is what lets anything outside count them or take one back out.
        mesh.userData.outlines = built.starts.length;
        mesh.userData.outlineStarts = built.starts;

        //Where its outlines are in the block, and its thickness, for the chip game to drive on - see
        //ChipGame.collect. The block is the one every layer shares rather than a copy, so keeping it costs a
        //reference: a corner is two words in it against the thirty-six floats the slab built from it holds.
        mesh.userData.words = words;
        mesh.userData.wordsFrom = layer.start;
        mesh.userData.wordsTo = layer.end;
        mesh.userData.depth = layer.depth;

        //Under the game's neon, while it is on - see setNeon.
        if (this.neon)
            neonLayer(mesh, true);

        return mesh;
    }

    ///
    ///The view's settings - whichever of `drawDistance` and `textures` `options` has - put on the scene: the textures
    ///on the layers at once, the draw distance on the next frame's planes. Not kept in this browser, which is what
    ///setViewOption does as well - so a game can have them while it runs and put them back. See VIEW_OPTIONS.
    ///
    applyView(options) {
        if (options.drawDistance != null)
            this.view.drawDistance = clampGameOption(Number(options.drawDistance), GAME_DRAW_DISTANCES, VIEW_OPTIONS.drawDistance);

        if (options.textures != null) {
            this.view.textures = options.textures === true;

            this.setSurfaces(this.view.textures);
        }

        this.showViewMenu();
        this.wantFrame();
    }

    ///
    ///The two places these are set, made to say what they are now: the bar's menu, if it is open - see Viewer3D.razor -
    ///and the game's panel, while there is a game. Said from here rather than by the component, which would draw the
    ///whole shell again for every step of a drag; and so a change in either shows in the other.
    ///
    showViewMenu() {
        for (const at of [['viewDrawDistance', 'viewDrawDistanceSaid', 'viewTextures'], ['gameDrawDistance', 'gameDrawDistanceSaid', 'gameTexture']]) {
            const reach = document.getElementById(at[0]);
            const said = document.getElementById(at[1]);
            const textures = document.getElementById(at[2]);

            if (reach != null && Number(reach.value) !== this.view.drawDistance)
                reach.value = String(this.view.drawDistance);

            if (said != null)
                said.textContent = drawDistanceSaid(this.view.drawDistance);

            if (textures != null)
                textures.checked = this.view.textures;
        }
    }

    ///
    ///One of the view's settings from the bar's menu - see Viewer3D.razor - put on, and kept in this browser unless a
    ///game is running: the game has the settings while it runs, and puts them back the way they were when it stops.
    ///
    setViewOption(name, value) {
        const options = {};

        options[name] = value;

        this.applyView(options);

        if (this.game == null)
            saveViewOptions(this.view);
    }

    ///
    ///A picture for the layers that name it, out of the browser's store - see Viewer3D.razor's sendTextures. `bytes` is
    ///the file as it was chosen: anything an img can show.
    ///
    ///**Tiled**, repeated across the layer however big it is - see SURFACE_COLOR - and **mipmapped**, as three does for
    ///any texture, so a picture a few pixels across on screen is its average rather than a shimmer. Put on every slab
    ///that names it once it has loaded, which is after the draw that asked for it.
    ///
    setTexture(name, bytes) {
        const url = URL.createObjectURL(new Blob([bytes]));
        const image = new Image();

        image.onload = () => {
            URL.revokeObjectURL(url);

            const texture = new THREE.Texture(image);

            texture.wrapS = THREE.RepeatWrapping;
            texture.wrapT = THREE.RepeatWrapping;
            texture.colorSpace = THREE.SRGBColorSpace;
            texture.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy());
            texture.needsUpdate = true;

            const was = this.textures.get(name);

            this.textures.set(name, texture);

            if (was != null)
                was.dispose();

            for (const drawn of this.chipObjectsGroup.children) {
                if (drawn.isMesh && drawn.material != null && drawn.material.userData.textureName === name)
                    bindTexture(drawn.material, texture);
            }

            this.wantFrame();
        };

        image.onerror = () => URL.revokeObjectURL(url);
        image.src = url;
    }

    ///
    ///The layers' surfaces on or off - every slab drawn now, and every one drawn after it. Show textures, from the bar's
    ///menu or the game's Apply texture - see applyView.
    ///
    setSurfaces(on) {
        this.surfaces = on;

        for (const drawn of this.chipObjectsGroup.children) {
            if (drawn.isMesh && drawn.material != null && drawn.material.userData.surface != null)
                setSurface(drawn.material, on);
        }

        this.wantFrame();
    }

    ///
    ///**The chip game's neon** on or off - its panel's Neon glow, see ChipGame.showNeon: every slab drawn now and every
    ///one drawn after it under the neon or out from under it - see neonLayer - and the scene drawn through the glow -
    ///see animate and NeonGlow. Off, the glow's pictures are let go of: at the size of the view, a few tens of megabytes.
    ///
    setNeon(on) {
        if (this.neon === on)
            return;

        this.neon = on;

        for (const drawn of this.chipObjectsGroup.children) {
            if (drawn.isMesh && drawn.material != null && drawn.material.userData.surface != null)
                neonLayer(drawn, on);
        }

        //The picture behind the scene dimmed under it - see NEON_BACKDROP.
        this.scene.backgroundIntensity = 1;

        if (on)
            this.scene.backgroundIntensity = NEON_BACKDROP;

        if (!on && this.neonGlow != null) {
            this.neonGlow.dispose();
            this.neonGlow = null;
        }

        this.wantFrame();
    }

    ///Everything a draw does once its slabs are in: the labels, the stack centered and measured, and a frame asked for.
    drawn(data) {
        this.drawLabels(data.labels);

        //And the stack sits about its own middle - see centerStack.
        this.centerStack();

        //Where it ended up, for the depth range and Admire. Before the headset fit below, which measures for
        //itself and then scales the group to half a meter.
        this.measureLayout();

        //A game in progress drives on what has just been drawn, rather than on meshes that are gone.
        if (this.game != null)
            this.game.rebuild();

        //The fit depends on the geometry's bounding box, so loading a file or moving the layer slider
        //while in a headset has to redo it.
        if (this.renderer.xr.isPresenting)
            this.fitForXr();

        this.wantFrame();
    }

    ///
    ///Lets go of the indicator's geometries and of the canvas texture behind each letter.
    ///
    ///Its objects are never handed to clearChipObjects - they are not the layout - so nothing else in here
    ///would ever release them, and a view rebuilt on every file open would leak a set each time.
    ///
    disposeAxesIndicator() {
        if (this.axesScene == null)
            return;

        this.axesScene.traverse(drawn => {
            if (drawn.geometry != null)
                drawn.geometry.dispose();

            if (drawn.material == null)
                return;

            if (drawn.material.map != null)
                drawn.material.map.dispose();

            drawn.material.dispose();
        });

        this.axesScene = null;
    }

    //Releases everything this viewer holds, so a replacement can be built without the old one lingering.
    //Order matters at the top: stopping the animation loop before anything is disposed means no frame is
    //ever rendered against a half-released renderer.
    dispose() {
        this.renderer.setAnimationLoop(null);

        //A scene still being built for this viewer is for nothing now - see drawPacedInterOp.
        if (drawingPaced != null)
            drawingPaced.canceled = true;

        //Before the scene goes: the game holds keys on window, and its car is in the scene being thrown away.
        if (this.game != null)
            this.game.stop(false);

        this.resizeObserver.disconnect();
        this.renderer.xr.removeEventListener('sessionstart', this.onXrSessionStartHandler);
        this.renderer.xr.removeEventListener('sessionend', this.onXrSessionEndHandler);

        this.clearChipObjects();

        if (this.neonGlow != null)
            this.neonGlow.dispose();

        this.neonGlow = null;

        //The layers' pictures, which no scene uses now - see setTexture.
        for (const texture of this.textures.values())
            texture.dispose();

        this.textures.clear();

        this.disposeAxesIndicator();

        this.controls.dispose();

        if (this.scene.background != null && this.scene.background.dispose)
            this.scene.background.dispose();

        this.renderer.dispose();

        //dispose() releases three's own objects but leaves the context to be collected whenever the
        //browser gets round to it. The limit is on live contexts, so it is given up explicitly.
        if (this.renderer.forceContextLoss)
            this.renderer.forceContextLoss();

        this.renderer.domElement.remove();

        //Before the buttons go, or taking them out is a change to the row that measures it for a viewer that is gone.
        if (this.xrRowWatch != null)
            this.xrRowWatch.disconnect();

        this.xrRowWatch = null;

        for (const button of this.xrButtons)
            button.remove();

        this.xrButtons = [];
    }

    //Empties the group and releases what the GPU was holding for it. The scene is drawn again whenever what
    //it shows changes, so leaving the old textures and buffers behind would pile them up as it does.
    clearChipObjects() {
        //Before anything is disposed: a pulse in flight holds materials from this group, and putting their
        //colors back after they have been thrown away is writing to something that is gone.
        this.endPulse();

        //And the tree's frame, which is round something in the scene being thrown away.
        this.endMark();

        //A label sheet's material is shared by every layer's labels drawn off it, so each is let go once.
        //Nothing outside this group uses any of them.
        const released = new Set();

        for (let i = this.chipObjectsGroup.children.length - 1; i >= 0; --i) {
            const child = this.chipObjectsGroup.children[i];

            this.chipObjectsGroup.remove(child);

            //The game's neon edges, the one thing a slab carries of its own - see neonLayer.
            disposeNeonEdges(child);

            if (child.geometry)
                child.geometry.dispose();

            if (child.material && !released.has(child.material)) {
                released.add(child.material);

                if (child.material.map)
                    child.material.map.dispose();

                //A label sheet is the shader's own uniform rather than a map - see drawLabels.
                if (child.material.uniforms != null && child.material.uniforms.sheet != null)
                    child.material.uniforms.sheet.value.dispose();

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
    ///Anything with no place recorded is left alone. Every slab and label in the group carries one today; what
    ///is not layout has no layer, so it has no business moving with one.
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

        this.measureLayout();

        //And the car goes up and down with whatever it is standing on.
        if (this.game != null)
            this.game.restacked();

        //The fit is computed off the bounding box, which has just changed - and in a headset the whole scene
        //is placed by it, so pulling the stack open without refitting walks the layout off into the room.
        if (this.renderer.xr.isPresenting)
            this.fitForXr();

        this.wantFrame();
    }

    //Pin labels are camera-facing quads rather than extruded glyphs. A label has to stay readable from
    //any orbit angle, and text lying flat in the stack is edge-on from most of them - so a billboard
    //reads better here than geometry would, and it costs one quad instead of a mesh per glyph. It also
    //means no font has to be vendored: the browser draws the text, and the layout keeps its own units.
    //
    //**A layer's labels are one draw.** They were a sprite each, and every sprite is a draw call of its own on
    //every frame: the RGB mixer hangs 19,005 of them, and they took its frames from the 17 ms its slabs need
    //to 43 on a GPU. Now each layer's labels are one mesh of quads drawn together, each wearing its picture
    //off a shared sheet - see packLabelSheets - and the turn to face the camera that a sprite made for itself
    //is made for all of them in the shader.
    //
    drawLabels(labels) {
        if (!labels || labels.length === 0)
            return;

        const packed = packLabelSheets(labels, this.renderer.capabilities.maxTextureSize);

        //
        //A material per sheet, shared by every layer drawn off it.
        //
        //Depth tested and not written, like the axis letters and for their reason: a label is a picture on a
        //transparent quad, and writing depth would let whichever label was drawn first cut a hole the shape of
        //its quad in any label behind it. A sprite could write it, being drawn on its own after every label
        //further away; a layer's labels are drawn together, in no order but their own.
        //
        const materials = packed.textures.map(texture => new THREE.ShaderMaterial({
            uniforms: { sheet: { value: texture } },
            vertexShader: LABEL_VERTEX_SHADER,
            fragmentShader: LABEL_FRAGMENT_SHADER,
            transparent: true,
            depthWrite: false
        }));

        //By layer, since a layer's labels move with it - see restack - and by sheet, since a draw reads one.
        const groups = new Map();

        for (const label of labels) {
            const look = packed.looks.get(label.color + '\n' + label.text);
            const key = `${label.at} ${label.offset} ${label.depth} ${look.sheet}`;

            if (!groups.has(key))
                groups.set(key, { at: label.at, offset: label.offset, depth: label.depth, sheet: look.sheet, labels: [] });

            groups.get(key).labels.push({ label: label, look: look });
        }

        //The tip the extrusions carry, worked out once rather than per label.
        const tilt = new THREE.Quaternion().setFromEuler(new THREE.Euler(LAYOUT_ROTATION_X, 0, 0));
        const anchor = new THREE.Vector3();

        for (const group of groups.values()) {
            const count = group.labels.length;
            const anchors = new Float32Array(count * 3);
            const sizes = new Float32Array(count * 2);
            const hangs = new Float32Array(count * 2);
            const cells = new Float32Array(count * 4);

            const bounds = new THREE.Box3();
            const quad = new THREE.Box3();
            const half = new THREE.Vector3();
            let reach = 0;

            for (let i = 0; i < count; i++) {
                const { label, look } = group.labels[i];

                //
                //The point it hangs from, in its own 0..1 space with Y up - which is exactly what the PRESENTATION
                //justification names, so it maps across without any arithmetic.
                //
                const hangX = window.viewGeometry.labelCenterX(label.horizontal);
                const hangY = window.viewGeometry.labelCenterY(label.vertical);

                //
                //Placed through the same transform the extrusions carry, since a quad that turns to the camera
                //has no rotation of its own: the anchor sits on the top face of its layer's slab, then the layout
                //tips onto its back and the layer's stacking offset lifts it - which is the mesh's own height, so
                //that a restack moves all of a layer's labels at once.
                //
                //**z is 0, and the slab's thickness is added in world Y.** An extrusion is built running from z 0
                //to z depth, and the tip maps local +Z onto world -Y - so a slab hangs below the plane it was
                //drawn on. That used to mean the slab's top face was its offset and a label anchored there landed
                //on it; the meshes are placed at offset + depth now, so the top face is offset + depth and the
                //label has to be lifted by the same amount or it is buried again. Measured before that was
                //understood: a label sat at y 189.9 inside a slab spanning 149.4 to 220.9.
                //
                anchor.set(label.x, label.y, 0).applyQuaternion(tilt);

                //
                //And by how much of the billboard hangs below its own anchor, which is a matter of justification
                //rather than of height: the anchor is the top edge for a label justified to the top, the bottom
                //edge for one justified to the bottom, and the middle otherwise.
                //
                //Lifting by that much is what actually clears the geometry. Raising the *anchor* alone is not
                //enough and was the first attempt at this: Top is the format's default, so a label hung its whole
                //height back down through the slab it had just been lifted off - clear anchor, buried text.
                //
                anchor.y += hangY * label.height;

                anchors[i * 3] = anchor.x;
                anchors[(i * 3) + 1] = anchor.y;
                anchors[(i * 3) + 2] = anchor.z;

                //Sized in layout units, keeping the picture's aspect ratio so the text is not stretched.
                sizes[i * 2] = label.height * look.aspect;
                sizes[(i * 2) + 1] = label.height;

                hangs[i * 2] = hangX;
                hangs[(i * 2) + 1] = hangY;

                cells.set(look.cell, i * 4);

                //The box a sprite here had: its quad standing about the anchor, square to the layout's axes.
                half.set(sizes[i * 2] / 2, sizes[(i * 2) + 1] / 2, 0);
                quad.min.copy(anchor).sub(half);
                quad.max.copy(anchor).add(half);

                bounds.union(quad);
                reach = Math.max(reach, Math.hypot(sizes[i * 2], sizes[(i * 2) + 1]));
            }

            const geometry = labelQuad();

            geometry.setAttribute('anchor', new THREE.InstancedBufferAttribute(anchors, 3));
            geometry.setAttribute('size', new THREE.InstancedBufferAttribute(sizes, 2));
            geometry.setAttribute('hang', new THREE.InstancedBufferAttribute(hangs, 2));
            geometry.setAttribute('cell', new THREE.InstancedBufferAttribute(cells, 4));
            geometry.instanceCount = count;

            //
            //**Where they are, said rather than worked out.** Three would work it out from the quad every label
            //is drawn on, which is a unit square at the origin.
            //
            //The box is what frames the camera, fits the depth range and a headset, and it is the one the
            //labels had as sprites - each quad standing about its anchor - so a file opens framed exactly as it
            //did. The sphere is what decides whether the mesh is drawn at all, and is wider by the longest
            //label's reach, since a quad that turns to the camera can lie any way round its anchor and a label
            //at the edge of the view must not be culled for poking in sideways.
            //
            geometry.boundingBox = bounds;
            geometry.boundingSphere = bounds.clone().expandByScalar(reach).getBoundingSphere(new THREE.Sphere());

            const mesh = new THREE.Mesh(geometry, materials[group.sheet]);

            //And then a gap, so the glyphs sit off the surface rather than resting on it - co-planar is the
            //case that flickers, and touching reads as painted on.
            mesh.position.set(0, group.offset + group.depth + (group.depth * LABEL_CLEARANCE), 0);

            //The same two a slab carries, for the same reason. The labels sit at their layer's offset plus a
            //thickness and a gap, so what is stored is the offset that went into them rather than where they
            //ended up.
            mesh.userData.stackAt = group.at;
            mesh.userData.stackOffset = group.offset;

            //How many it draws, which is also what says this mesh is labels rather than a slab.
            mesh.userData.labels = count;

            this.chipObjectsGroup.add(mesh);
        }
    }

    /////////////////////////////////////////////////////////



    //Other//////////////////////////////////////////////////

    cinematicView(cinematicViewToggleInterOp) {
        this.cinematicViewToggle = cinematicViewToggleInterOp;

        //Already centered and sized on the layout, whenever it was last measured - see sizeCinematicView.

        this.wantFrame();
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

        //Before the check below, because a view shown again at the size it was hidden at is the same size to
        //the renderer and still a view the headset row has not been measured against.
        this.fitXrButtons();

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

        //A canvas given a new size is cleared, so there is nothing on it until the next frame drawn.
        this.wantFrame();
    }

    changeBackground(backgroundName) {
        this.wantFrame();

        //
        //**The last one asked for is the one that stands**, however the pictures arrive: a game started and stopped
        //before its Neon had come put the view's own back, and then had Neon land over it when it did.
        //
        this.backdropWanted = backgroundName;

        if (!backgroundName || backgroundName.toLowerCase() === "none") {
            this.backdropTone = null;
            this.showBackdrop(null);
            return;
        }

        //Relative, so the browser resolves it against the <base href> index.html sets - which is what
        //keeps it working when the app is served from a subdirectory.
        //
        //This was built from window.location.href, which only ever worked because the address had nothing
        //after the host: appending a path to a URL that carries a query string buries the path inside the
        //query, so once ?file= and ?view= arrived the image was fetched from a nonsense address, the
        //loader's callback never fired, and the background silently never appeared.
        //And another frame when the picture arrives, which is after any frame asked for above.
        const texture = this.textureLoader.load('resources/Images/Background/' + backgroundName, () => {
            if (this.backdropWanted !== backgroundName) {
                texture.dispose();

                return;
            }

            const rt = new THREE.WebGLCubeRenderTarget(texture.image.height);
            rt.fromEquirectangularTexture(this.renderer, texture);
            this.backdropTone = toneOf(texture.image);
            this.showBackdrop(rt.texture);

            this.wantFrame();
        });
    }

    ///
    ///What the scene is drawn against - or, while an AR session is on, what goes back up when it ends, since anything
    ///behind the layout there covers the room. **A game stopped by the headset asks for one** as the session starts:
    ///stopping puts the view's own backdrop back, after the game's - see Viewer3D.putBackdropBack.
    ///
    showBackdrop(texture) {
        if (this.isArSession)
            this.backgroundBeforeXr = texture;
        else
            this.scene.background = texture;
    }

    ///
    ///A Z-up copy of the chip, for handing to an exporter.
    ///
    ///**The scene is Y-up and every tool that reads these files is not.** three.js puts Y up, so the layout
    ///is laid on its back with the stack running up Y. STL, OBJ and glTF are opened by CAD and EDA tools
    ///that put Z up, and a cell exported straight out of the scene arrives in all of them lying on its
    ///side. Measured against an independent extrusion of the same cells: X matched to the nanometer, the
    ///scene's Z was the reference's Y, and the scene's Y was its Z.
    ///
    ///**Y and Z change places rather than being rotated into position.** A rotation about X cannot do this
    ///job - a quarter turn one way inverts the height and the other way mirrors the footprint, and neither
    ///is the same object. The swap lands every coordinate exactly where the reference has it.
    ///
    ///**Which costs the winding, so the winding is paid back.** Swapping two axes is a reflection: its
    ///determinant is -1, so it turns every triangle inside out and leaves a solid whose faces point into
    ///themselves. STL says which side is outside by the order of the corners and nothing else, so each
    ///triangle is wound back the other way and the normals are computed again from the result.
    ///
    ///**Clones, and only the chip.** The view is still on screen while this runs, so nothing here may move
    ///what is being drawn - and the scene carries a camera, lights and a backdrop that are furniture rather
    ///than layout. What an exported model is for is the geometry the file describes.
    ///
    buildForExport() {
        const group = new THREE.Group();

        if (this.chipObjectsGroup == null)
            return group;

        const swap = new THREE.Matrix4().set(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);

        this.chipObjectsGroup.updateMatrixWorld(true);

        for (const drawn of this.chipObjectsGroup.children) {
            if (!drawn.isMesh)
                continue;

            //A layer's labels are a mesh too, but of quads that only turn to face the camera in the shader and
            //only show anything through it. Written out, each would be one blank unit square at the layout's
            //origin, so they are left out, as the sprites they replaced always were.
            if (drawn.userData.labels != null)
                continue;

            //Unindexed first, so a triangle is three positions of its own: the winding can then be turned
            //by moving coordinates, and computeVertexNormals gives each face its own normal rather than
            //averaging across the corners a box shares.
            let geometry = drawn.geometry;

            if (geometry.getIndex() != null)
                geometry = geometry.toNonIndexed();
            else
                geometry = geometry.clone();

            //The mesh's own placement first - where the slab actually sits - and then the axes.
            geometry.applyMatrix4(drawn.matrixWorld);
            geometry.applyMatrix4(swap);

            reverseWinding(geometry);
            geometry.computeVertexNormals();

            group.add(new THREE.Mesh(geometry, drawn.material));
        }

        return group;
    }

    ///<summary>Lets go of the geometries buildForExport cloned, which nothing else is holding.</summary>
    disposeExport(group) {
        for (const drawn of group.children)
            drawn.geometry.dispose();
    }

    download3DModel(fileName, fileType) {
        const model = this.buildForExport();

        let exporter;
        let data;

        switch (fileType) {
            case ".stl":
                exporter = new STLExporter();
                data = exporter.parse(model);
                BlazorDownloadFile(fileName + '.stl', 'application/octet-stream', data);
                break;
            case ".obj":
                exporter = new OBJExporter();
                data = exporter.parse(model);
                BlazorDownloadFile(fileName + '.obj', 'application/octet-stream', data);
                break
            case ".gltf":
                exporter = new GLTFExporter();
                exporter.parse(model, function (gltf) {
                    data = JSON.stringify(gltf, null, 2);
                    BlazorDownloadFile(fileName + '.gltf', 'application/octet-stream', data);
                });
                break;
        }

        //
        //**Not disposed for glTF**, whose parse takes a callback and is still reading the geometry when
        //this returns. The other two are done by the time their BlazorDownloadFile has been called.
        //
        if (fileType !== ".gltf")
            this.disposeExport(model);
    }

    /////////////////////////////////////////////////////////
}



///
///**The glow over the chip game's neon** - see Viewer3D.setNeon, which lights the layers' edges and grids past white for
///it, and ChipGame.heatParts, which lights the electrons, the shots and the flame.
///
///**Light spread out round what is bright.** The scene is drawn into a picture of its own rather than onto the canvas, in
///half floats, so what is lit past white stays past it. What is past the threshold is taken out of it at half the size -
///see NEON_DOWN - and that halved again and again, NEON_LEVELS times in all, each a little blurred on the way down; then
///each is added back onto the one above it, blurred again as it goes - see NEON_UP - so the smallest, the most spread out,
///reaches furthest round what lit it. The picture goes onto the canvas with NEON_STRENGTH of that over it. Thirteen taps
///down and nine up, the way a game's bloom has been made since Call of Duty: Advanced Warfare, which does not shimmer as
///a line a pixel wide moves.
///
///**The app's own, with three's render targets and materials**: three's bloom is an addon the app does not vendor, and
///this is the handful of passes it needs. Smoothed the way the canvas is - four samples a pixel, where WebGL 2 has them -
///since a picture drawn into is not unless it is asked to be, and its lines were stairs.
///
class NeonGlow {

    constructor(renderer) {
        this.renderer = renderer;

        const webgl2 = renderer.capabilities.isWebGL2;

        //Half floats where the browser can draw into them, and bytes where it cannot - see NEON_THRESHOLD_BYTES.
        let floats = renderer.extensions.has('EXT_color_buffer_half_float');

        if (webgl2)
            floats = renderer.extensions.has('EXT_color_buffer_float');

        let type = THREE.UnsignedByteType;
        let samples = 0;

        this.threshold = NEON_THRESHOLD_BYTES;

        if (floats) {
            type = THREE.HalfFloatType;

            this.threshold = NEON_THRESHOLD;
        }

        if (webgl2)
            samples = 4;

        this.picture = new THREE.WebGLRenderTarget(1, 1, { type: type, samples: samples });
        this.levels = [];

        for (let i = 0; i < NEON_LEVELS; i++)
            this.levels.push(new THREE.WebGLRenderTarget(1, 1, { type: type, depthBuffer: false }));

        //The size they were made at, and the color behind the scene, kept while the neon's is put there.
        this.width = 0;
        this.height = 0;
        this.size = new THREE.Vector2();
        this.clearWas = new THREE.Color();

        //One triangle that covers the view, and a camera to draw it with, which it does not use - see NEON_VERTEX.
        const cover = new THREE.BufferGeometry();

        cover.setAttribute('position', new THREE.Float32BufferAttribute([-1, -1, 0, 3, -1, 0, -1, 3, 0], 3));
        cover.setAttribute('uv', new THREE.Float32BufferAttribute([0, 0, 2, 0, 0, 2], 2));

        this.cover = new THREE.Mesh(cover);
        this.cover.frustumCulled = false;
        this.camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);

        this.down = new THREE.ShaderMaterial({
            uniforms: {
                source: { value: null },
                texel: { value: new THREE.Vector2() },
                first: { value: 0 },
                threshold: { value: this.threshold },
                knee: { value: NEON_KNEE },
                mist: { value: NEON_MIST }
            },
            vertexShader: NEON_VERTEX,
            fragmentShader: NEON_DOWN,
            depthTest: false,
            depthWrite: false
        });

        this.up = new THREE.ShaderMaterial({
            uniforms: {
                source: { value: null },
                texel: { value: new THREE.Vector2() }
            },
            vertexShader: NEON_VERTEX,
            fragmentShader: NEON_UP,
            blending: THREE.AdditiveBlending,
            depthTest: false,
            depthWrite: false
        });

        this.mix = new THREE.ShaderMaterial({
            uniforms: {
                picture: { value: null },
                glow: { value: null },
                strength: { value: NEON_STRENGTH }
            },
            vertexShader: NEON_VERTEX,
            fragmentShader: NEON_MIX,
            depthTest: false,
            depthWrite: false
        });
    }

    ///The picture and the levels at the size the canvas is drawn at, each level half the one before - made again only when that changes.
    fit() {
        const size = this.renderer.getDrawingBufferSize(this.size);
        const width = Math.max(1, Math.round(size.x));
        const height = Math.max(1, Math.round(size.y));

        if (width === this.width && height === this.height)
            return;

        this.width = width;
        this.height = height;

        this.picture.setSize(width, height);

        for (let i = 0; i < this.levels.length; i++)
            this.levels[i].setSize(Math.max(1, width >> (i + 1)), Math.max(1, height >> (i + 1)));
    }

    ///
    ///The scene drawn into the picture rather than onto the canvas - against the neon's violet where no picture is behind
    ///it, which is what its haze fades into. See NEON_HAZE.
    ///
    drawScene(scene, camera) {
        const renderer = this.renderer;

        this.fit();

        renderer.getClearColor(this.clearWas);

        const alphaWas = renderer.getClearAlpha();

        if (scene.background == null)
            renderer.setClearColor(NEON_HAZE, 1);

        renderer.setRenderTarget(this.picture);
        renderer.render(scene, camera);
        renderer.setRenderTarget(null);

        renderer.setClearColor(this.clearWas, alphaWas);
    }

    ///The glow made out of the picture, and the two of them onto the canvas.
    drawGlow() {
        const renderer = this.renderer;
        const down = this.down.uniforms;
        const up = this.up.uniforms;

        let from = this.picture;

        this.cover.material = this.down;

        for (let i = 0; i < this.levels.length; i++) {
            down.source.value = from.texture;
            down.texel.value.set(1 / from.width, 1 / from.height);
            down.first.value = 0;

            if (i === 0)
                down.first.value = 1;

            renderer.setRenderTarget(this.levels[i]);
            renderer.render(this.cover, this.camera);

            from = this.levels[i];
        }

        //Added onto what each level holds already, so nothing is cleared on the way up.
        const autoClearWas = renderer.autoClear;

        renderer.autoClear = false;
        this.cover.material = this.up;

        for (let i = this.levels.length - 1; i > 0; i--) {
            up.source.value = this.levels[i].texture;
            up.texel.value.set(1 / this.levels[i].width, 1 / this.levels[i].height);

            renderer.setRenderTarget(this.levels[i - 1]);
            renderer.render(this.cover, this.camera);
        }

        renderer.autoClear = autoClearWas;

        this.mix.uniforms.picture.value = this.picture.texture;
        this.mix.uniforms.glow.value = this.levels[0].texture;
        this.cover.material = this.mix;

        renderer.setRenderTarget(null);
        renderer.render(this.cover, this.camera);
    }

    ///Lets go of the pictures and the passes.
    dispose() {
        this.picture.dispose();

        for (const level of this.levels)
            level.dispose();

        this.cover.geometry.dispose();
        this.down.dispose();
        this.up.dispose();
        this.mix.dispose();
    }
}



///
///The chip game: a little car driven over the tops of the layers, jumping from one to the next.
///
///**The physics is not here.** It is in js/chipGame.js, which has no three.js in it so Node can test it, and
///which works in the layout's own coordinates. What this class does is everything around it: read the slabs
///off the meshes the view already drew, take the keys, put the car and its shadow where the physics says,
///and fly the camera behind it.
///
///**It drives the scene that is on screen rather than a copy of it.** The slabs are the view's own meshes,
///so a layer switched off is gone from under the car, and the Distance slider lifts what the car is
///standing on - see rebuild and restacked, which the view calls after a draw and after a restack.
///
///The car, the shadow and the camera are the scene's, never the chip group's: the group is what is cleared,
///exported, fitted and restacked, and a car in it would be all four.
///
class ChipGame {

    constructor(viewer, handle, layerNames) {
        this.viewer = viewer;

        //The component's handle, to say the game has stopped when Escape stops it rather than the menu.
        this.handle = handle;

        //Each layer's name by its place in the stack, which is the one thing a slab knows about its layer.
        this.layerNames = layerNames || [];

        this.keys = new Set();
        this.zoom = 1;

        //How much of the way back to its usual place the camera is, after a layer in the way pulled it in.
        this.reach = 1;

        this.world = null;
        this.player = null;
        this.meshes = [];
        this.car = null;
        this.aircraft = null;
        this.propeller = null;
        this.jet = null;
        this.flame = null;
        this.shadow = null;
        this.lit = null;
        this.outline = null;
        this.toastTimer = null;

        //The electrons, the rounds and missiles in flight, and the flashes things go out in - see buildField.
        this.electronMesh = null;
        this.electronGlow = null;
        this.bulletMesh = null;
        this.missileMesh = null;
        this.exhaustMesh = null;
        this.missileGlow = null;
        this.flashes = [];

        //Held rather than made each frame, for the reason the axis indicator's vector is.
        this.carAt = new THREE.Vector3();
        this.lookingAt = new THREE.Vector3();
        this.eyeAt = new THREE.Vector3();
        this.eyeTurn = new THREE.Quaternion();
        this.eyeAngles = new THREE.Euler(0, 0, 0, 'YXZ');
        this.placing = new THREE.Matrix4();
        this.placedAt = new THREE.Vector3();
        this.placedTurn = new THREE.Quaternion();
        this.placedSize = new THREE.Vector3();
        this.pointing = new THREE.Vector3();
        this.onScreen = new THREE.Vector3();

        //
        //The camera behind the plane - see chasePlane: the turn it is eased round in and the one it is easing to, and what
        //they are worked out with; and whether the last frame was framed that way, which is where it eases round from.
        //
        this.chaseTurn = new THREE.Quaternion();
        this.chaseWanted = new THREE.Quaternion();
        this.chaseBasis = new THREE.Matrix4();
        this.chaseAhead = new THREE.Vector3();
        this.chaseUp = new THREE.Vector3();
        this.chaseSide = new THREE.Vector3();
        this.chaseAt = new THREE.Vector3();
        this.chaseFrom = new THREE.Vector3();
        this.chaseEye = new THREE.Vector3();
        this.chased = false;

        //
        //The triggers - the mouse's main button on the view, which F is as well, for the gun; and its other button, or G,
        //for the jet's missiles - and whether either or H has been pressed since the last frame, so a tap shorter than a
        //frame is still a shot or a toot. What the HUD last said, so it is only written when it changes.
        //
        this.firing = false;
        this.triggered = false;
        this.launching = false;
        this.launched = false;
        this.honked = false;
        this.burning = false;
        this.shown = { score: -1, jumps: '', heat: '', crosshair: '', lock: '' };

        //The haze the far side fades into short of All, in the color behind the scene - see frameCamera.
        this.haze = new THREE.Fog(DESKTOP_CLEAR_COLOR, 1, 2);

        //
        //The neon's haze, while the panel has it on - see fitLens - and what the game puts in the world that the neon
        //lights past white, each with the color it has without it - see heatParts.
        //
        this.neonHaze = new THREE.FogExp2(NEON_HAZE, 0);
        this.neonViolet = new THREE.Color(NEON_HAZE);
        this.hotParts = [];

        //What the panel sets - see GAME_OPTIONS_KEY - and whether its controls have been shown what that is yet.
        this.options = loadGameOptions();
        this.optionsShown = false;

        //
        //Where the mouse has turned the camera: round the car from behind it, and up or down from its usual
        //height, both in radians - and when it last did, in seconds, which is what says when to swing back.
        //
        this.lookYaw = 0;
        this.lookPitch = 0;
        this.lookedAt = -Infinity;

        //How far the car is tipped nose up, eased towards what it is doing rather than set to it.
        this.carPitch = 0;

        //
        //Where the pointer is on the view, from its middle, in pixels - or null while it is off the view - and whether the
        //mouse holds the plane's stick, side to side and back and forth: from when it moves on the view until a key moves
        //that way of the stick, or the pointer leaves the view. See onPointerMove and flown.
        //
        this.pointerAt = null;
        this.mouseRolls = false;
        this.mousePitches = false;

        //Where the arrow keys have the plane's stick, back and forth and side to side, as it eases - and when it last did.
        this.keyPitch = 0;
        this.keyRoll = 0;
        this.keysAt = null;

        //How far round the plane's propeller has turned, in radians.
        this.propellerTurn = 0;

        //
        //What the camera eases, where its distance from the car is fixed - see frameCamera: the heading it frames
        //the car from, mouse and all, and its tip up or down, in radians; the height it frames the car at, in
        //layout units; and the zoom the wheel has set, as it is shown.
        //
        this.swungHeading = 0;
        this.swungPitch = 0;
        this.followedZ = 0;
        this.shownZoom = 1;

        //The engine and the tires. Made when the game starts, and nothing if the browser has no Web Audio.
        this.sound = null;

        //Bound once, so the same functions can be taken off again.
        this.onKeyDownHandler = this.onKeyDown.bind(this);
        this.onKeyUpHandler = this.onKeyUp.bind(this);
        this.onBlurHandler = this.onBlur.bind(this);
        this.onWheelHandler = this.onWheel.bind(this);
        this.onPointerMoveHandler = this.onPointerMove.bind(this);
        this.onPointerLeaveHandler = this.onPointerLeave.bind(this);
        this.onPointerDownHandler = this.onPointerDown.bind(this);
        this.onPointerUpHandler = this.onPointerUp.bind(this);
        this.onContextMenuHandler = this.onContextMenu.bind(this);
        this.onOptionHandler = this.onOption.bind(this);
        this.onClickHandler = this.onClick.bind(this);
    }

    ///Starts the game, or says it could not: false when there is nothing drawn to drive on.
    start() {
        if (!this.build())
            return false;

        const viewer = this.viewer;

        //
        //**The view's two settings, the game's way**: everything drawn, and the textures on - which is how a game is
        //played. Its panel and the bar change them while it runs; stop puts back what the view had. See VIEW_OPTIONS.
        //
        this.viewWas = { drawDistance: viewer.view.drawDistance, textures: viewer.view.textures };

        viewer.applyView({ drawDistance: GAME_DRAW_DISTANCES.most, textures: true });

        //Everything the game changes about the camera - and the scene's haze, which goes with the draw distance - so
        //stopping can put back exactly what was there.
        this.cameraWas = {
            position: viewer.camera.position.clone(),
            up: viewer.camera.up.clone(),
            target: viewer.controls.target.clone(),
            fov: viewer.camera.fov,
            near: viewer.camera.near,
            far: viewer.camera.far,
            fog: viewer.scene.fog
        };

        //The orbit controls answer the mouse on the same canvas, and would turn the camera back the other way
        //every time the game moved it.
        viewer.controls.enabled = false;

        //
        //**Out of whatever started it.** A button with focus is pressed by Space, which is the jump - so a
        //control left holding the focus would be pressed by the first jump.
        //
        if (document.activeElement != null && document.activeElement.blur)
            document.activeElement.blur();

        //Capture, on window: ahead of anything else in the page that listens for the same keys.
        window.addEventListener('keydown', this.onKeyDownHandler, true);
        window.addEventListener('keyup', this.onKeyUpHandler, true);
        window.addEventListener('blur', this.onBlurHandler);
        viewer.renderer.domElement.addEventListener('wheel', this.onWheelHandler, { passive: false });
        viewer.renderer.domElement.addEventListener('pointermove', this.onPointerMoveHandler);
        viewer.renderer.domElement.addEventListener('pointerleave', this.onPointerLeaveHandler);

        //
        //The triggers are pressed on the view and let go of anywhere, which may be off it by then. **The other button is
        //the missiles' trigger, not a menu**: the orbit controls keep the browser's menu off the view, and they are off.
        //
        viewer.renderer.domElement.addEventListener('pointerdown', this.onPointerDownHandler);
        viewer.renderer.domElement.addEventListener('contextmenu', this.onContextMenuHandler);
        window.addEventListener('pointerup', this.onPointerUpHandler);

        //
        //**On the document, for controls that are not there yet.** The panel is rendered by the component once
        //this has returned and said the game started, so its sliders cannot be listened to here - an event that
        //comes up from one of them can. Its × as well.
        //
        document.addEventListener('input', this.onOptionHandler, true);
        document.addEventListener('change', this.onOptionHandler, true);
        document.addEventListener('click', this.onClickHandler, true);

        //Made inside the press that started the game, which is what a browser lets make a sound.
        this.sound = GameSound.make();

        //Placed before the camera is, which in first person is at the driver's eyes.
        this.buildCar();
        this.placeCar();
        this.frameCamera(true);

        //Where a spec finds the game that is running: the scene is what it can already reach. Let go of on stop.
        viewer.scene.userData.chipGame = this;

        return true;
    }

    ///
    ///Stops the game and puts the view back the way it was.
    ///
    ///`notify` is whether the component has to be told, which is when something other than the component
    ///stopped the game - Escape, or a headset. Not when the view has been left: the component is gone.
    ///
    stop(notify) {
        const viewer = this.viewer;

        window.removeEventListener('keydown', this.onKeyDownHandler, true);
        window.removeEventListener('keyup', this.onKeyUpHandler, true);
        window.removeEventListener('blur', this.onBlurHandler);
        viewer.renderer.domElement.removeEventListener('wheel', this.onWheelHandler);
        viewer.renderer.domElement.removeEventListener('pointermove', this.onPointerMoveHandler);
        viewer.renderer.domElement.removeEventListener('pointerleave', this.onPointerLeaveHandler);
        viewer.renderer.domElement.removeEventListener('pointerdown', this.onPointerDownHandler);
        viewer.renderer.domElement.removeEventListener('contextmenu', this.onContextMenuHandler);
        window.removeEventListener('pointerup', this.onPointerUpHandler);
        document.removeEventListener('input', this.onOptionHandler, true);
        document.removeEventListener('change', this.onOptionHandler, true);
        document.removeEventListener('click', this.onClickHandler, true);

        if (viewer.scene.userData.chipGame === this)
            delete viewer.scene.userData.chipGame;

        if (this.sound != null) {
            this.sound.close();
            this.sound = null;
        }

        if (this.toastTimer != null)
            clearTimeout(this.toastTimer);

        this.unlight();
        this.disposeCar();

        //The view's own draw distance and textures back, as they were before the game - see start.
        if (this.viewWas != null)
            viewer.applyView(this.viewWas);

        //And the neon off, which is the game's alone - see showNeon.
        viewer.setNeon(false);

        //Its up as well, which the camera behind the plane turns over with a loop - see chasePlane.
        if (this.cameraWas != null) {
            viewer.camera.position.copy(this.cameraWas.position);
            viewer.camera.up.copy(this.cameraWas.up);
            viewer.controls.target.copy(this.cameraWas.target);
            viewer.camera.fov = this.cameraWas.fov;
            viewer.camera.near = this.cameraWas.near;
            viewer.camera.far = this.cameraWas.far;
            viewer.camera.updateProjectionMatrix();

            viewer.scene.fog = this.cameraWas.fog;
        }

        viewer.controls.enabled = true;
        viewer.controls.update();

        viewer.game = null;

        //The view draws only when asked once the game is not drawing every frame - see animate - and the frame
        //without the car in it, from where the camera was, is one that has to be asked for.
        viewer.wantFrame();

        //The handle may already have been let go of, if the component went first. Nothing to tell then.
        if (notify && this.handle != null)
            this.handle.invokeMethodAsync('OnGameEnded').catch(() => { });
    }

    ///
    ///The slabs as the physics wants them, off the meshes the view drew.
    ///
    ///**A slab per outline, and a mesh per layer.** The view merges a layer's outlines into one mesh - see
    ///draw - so each one is read back out of the block the mesh was built from, the same way extrudeLayer
    ///reads it, and `meshes` holds each slab's layer mesh at the slab's index. That is what the layer's name
    ///and the Distance slider's heights are looked up by.
    ///
    ///A mesh's Y is its top - the view places each one at its offset plus its thickness - and its underside
    ///is a thickness below that. Both are in the chip group's own space, which is the layout's stack heights;
    ///the group's own lift is added back only where the car is put on screen.
    ///
    collect() {
        const slabs = [];
        const meshes = [];

        for (const drawn of this.viewer.chipObjectsGroup.children) {
            if (!drawn.isMesh || drawn.userData.words == null)
                continue;

            const words = drawn.userData.words;
            const bottom = drawn.position.y - drawn.userData.depth;
            const top = drawn.position.y;

            for (let at = drawn.userData.wordsFrom; at < drawn.userData.wordsTo;) {
                const count = words[at];
                const holeCount = words[at + 1];

                at += 2;

                const outline = cornersOf(words, at, count);

                at += count * 2;

                const holes = [];

                for (let hole = 0; hole < holeCount; hole++) {
                    const corners = words[at];
                    const ring = cornersOf(words, at + 1, corners);

                    if (ring.length >= 3)
                        holes.push(ring);

                    at += 1 + (corners * 2);
                }

                //Nothing to stand on, and nothing the view drew either - see extrudeLayer.
                if (outline.length < 3)
                    continue;

                slabs.push({ outline: outline, holes: holes, bottom: bottom, top: top });
                meshes.push(drawn);
            }
        }

        return { slabs: slabs, meshes: meshes };
    }

    build() {
        const found = this.collect();
        const world = window.chipGame.buildWorld(found.slabs);

        if (world.empty)
            return false;

        this.world = world;
        this.meshes = found.meshes;
        this.player = window.chipGame.spawn(world);

        this.applyOptions();

        return true;
    }

    ///
    ///After the view has drawn again: a file opened, a layer switched on or off.
    ///
    ///The meshes are all new, so the world is built again from them - and the car stays where it was rather
    ///than going back to the start, so switching off the layer it is on drops it onto the one underneath.
    ///A layout with nothing left in it to drive on ends the game.
    ///
    rebuild() {
        const was = this.player;
        const unitWas = this.world.unit;

        this.unlight();

        if (!this.build()) {
            this.stop(true);

            return;
        }

        if (was != null) {
            const start = this.player.start;

            Object.assign(this.player, was);

            this.player.start = start;
            this.player.grounded = false;
            this.player.standing = -1;
        }

        //A different set of layers is a different typical wire, and so a different size of car.
        if (this.world.unit !== unitWas) {
            this.disposeCar();
            this.buildCar();
        }
    }

    ///After the Distance slider has moved the meshes up or down, which changes heights and nothing else.
    restacked() {
        const heights = [];

        for (const drawn of this.meshes)
            heights.push({ bottom: drawn.position.y - drawn.userData.depth, top: drawn.position.y });

        window.chipGame.restack(this.world, this.player, heights);
    }

    //The panel/////////////////////////////////////////

    ///
    ///What the panel has set, onto the car. The physics reads them off the player - see chipGame.spawn - and the plane
    ///or the jet is boarded or left the way P does it, from where the one there is now is. The neon onto the scene - see
    ///showNeon. The draw distance and the textures are the view's - see onOption.
    ///
    applyOptions() {
        if (this.player == null)
            return;

        this.player.topSpeed = this.options.topSpeed;
        this.player.size = this.options.size;
        this.player.locked = this.options.locked;
        this.player.limitJump = this.options.limitJump;

        window.chipGame.board(this.world, this.player, this.options.vehicle);

        this.showVehicle();
        this.showNeon();
    }

    ///Whether what is being played flies - the plane or the jet.
    flying() {
        return this.options.vehicle !== 'car';
    }

    ///The model of whatever is being played: the car's, the plane's or the jet's.
    vehicleModel() {
        if (this.options.vehicle === 'plane')
            return this.aircraft;

        if (this.options.vehicle === 'jet')
            return this.jet;

        return this.car;
    }

    ///
    ///The car, the plane or the jet on screen, and the panel's keys and ticks for it - see .gameForPlane in
    ///Viewer3D.razor.css. Any of them can be asked for before the rest exists: the models are built once the game has
    ///started, and the panel once the component has rendered it.
    ///
    ///**The propeller is not there in first person**, where it would be a bar across the middle of the view a dozen
    ///times a second. And the panel is put away if its × has closed it.
    ///
    showVehicle() {
        const vehicle = this.options.vehicle;

        if (this.car != null)
            this.car.visible = vehicle === 'car';

        if (this.aircraft != null)
            this.aircraft.visible = vehicle === 'plane';

        if (this.jet != null)
            this.jet.visible = vehicle === 'jet';

        if (this.propeller != null)
            this.propeller.visible = this.options.view !== 'first';

        const hud = document.getElementById('gameHud');

        if (hud == null)
            return;

        hud.classList.toggle('gameHudPlane', vehicle === 'plane');
        hud.classList.toggle('gameHudJet', vehicle === 'jet');
        hud.classList.toggle('gameHudClosed', !this.options.panel);
    }

    ///
    ///Into the car, the plane or the jet - P, or the panel's list - and said, and kept. The physics takes it from
    ///wherever the one there was - see chipGame.board.
    ///
    ///**Boarded with the stick let go of**, so the plane climbs away from what it was on as it boards, until the mouse
    ///next moves and takes the stick where the pointer is.
    ///
    setVehicle(vehicle) {
        if (!GAME_VEHICLES.includes(vehicle))
            return;

        this.options.vehicle = vehicle;
        this.mouseRolls = false;
        this.mousePitches = false;

        this.applyOptions();

        const list = document.getElementById('gameVehicle');

        if (list != null)
            list.value = vehicle;

        if (vehicle === 'plane')
            this.say('Into the plane');
        else if (vehicle === 'jet')
            this.say('Into the jet');
        else
            this.say('Back in the car');

        saveGameOptions(this.options);
    }

    ///
    ///First person or third - V, or the panel's list - and said, and kept. The camera is put where it goes at once,
    ///rather than swung there: a cut is what a change of view is.
    ///
    setView(view) {
        if (!GAME_VIEWS.includes(view))
            return;

        this.options.view = view;

        this.showVehicle();
        this.frameCamera(true);

        const list = document.getElementById('gameView');

        if (list != null)
            list.value = view;

        if (view === 'first')
            this.say('First person');
        else
            this.say('Third person');

        saveGameOptions(this.options);
    }

    ///The panel open, or put away by its × - and kept, so the next game starts the way this one was left.
    setPanel(open) {
        this.options.panel = open;

        this.showVehicle();

        saveGameOptions(this.options);
    }

    ///
    ///**The neon** (2026-11-01, asked for) on or off, as the panel's Neon glow has it: the layers' edges and grids lit and
    ///the glow over the scene, and the backdrop dimmed - see Viewer3D.setNeon - what the game puts in the world lit for it
    ///- see heatParts - and a haze over the distance - see fitLens.
    ///
    showNeon() {
        this.viewer.setNeon(this.options.neon);

        this.heatParts();
    }

    ///How much brighter than its own color the neon has what the game puts in the world: GAME_NEON_HOT under it, and itself without.
    hotness() {
        if (this.options.neon)
            return GAME_NEON_HOT;

        return 1;
    }

    ///
    ///What the game puts in the world lit past white under the neon, so the glow finds it - the electrons, the shots, the
    ///jet's flame and the car's lamps, each at hotness times its own color - and the outline round the slab under the car.
    ///
    heatParts() {
        const hot = this.hotness();

        for (const part of this.hotParts)
            part.color.copy(part.was).multiplyScalar(hot);

        if (this.outline != null)
            this.outline.material.color.setHex(GAME_OUTLINE).multiplyScalar(hot);
    }

    ///
    ///The panel's controls set to what the options are, and the numbers beside the sliders to what they say.
    ///
    ///Asked every frame until the panel is there, since it is rendered after the game has started - see start.
    ///
    showOptions() {
        const speed = document.getElementById('gameTopSpeed');
        const size = document.getElementById('gameCarSize');
        const smallest = document.getElementById('gameCarSmallest');
        const reach = document.getElementById('gameDrawDistance');
        const lock = document.getElementById('gameLockOn');
        const sound = document.getElementById('gameSound');

        if (speed == null || size == null || smallest == null || reach == null || lock == null || sound == null)
            return;

        speed.value = String(this.options.topSpeed);

        //The least before the value: a browser holds a range's value inside the least it has at that moment.
        size.min = String(this.options.smallest);
        size.value = String(this.options.size);
        smallest.value = String(this.options.smallest);
        reach.value = String(this.viewer.view.drawDistance);
        lock.checked = this.options.locked;
        sound.checked = this.options.sound;

        //Newer than the rest, so a page from before them is not left waiting for them.
        const vehicle = document.getElementById('gameVehicle');
        const view = document.getElementById('gameView');
        const invert = document.getElementById('gameInvertPitch');
        const limit = document.getElementById('gameLimitJump');
        const texture = document.getElementById('gameTexture');
        const music = document.getElementById('gameMusic');
        const neon = document.getElementById('gameNeon');
        const haze = document.getElementById('gameHaze');
        const climbs = document.getElementById('gameHazeClimbs');

        if (vehicle != null)
            vehicle.value = this.options.vehicle;

        if (view != null)
            view.value = this.options.view;

        if (invert != null)
            invert.checked = this.options.invertPitch;

        if (limit != null)
            limit.checked = this.options.limitJump;

        if (texture != null)
            texture.checked = this.viewer.view.textures;

        if (music != null)
            music.checked = this.options.music;

        if (neon != null)
            neon.checked = this.options.neon;

        if (haze != null)
            haze.value = String(this.options.hazeReach);

        if (climbs != null)
            climbs.checked = this.options.hazeClimbs;

        this.sayOptions();
        this.showVehicle();

        this.optionsShown = true;
    }

    ///
    ///The numbers beside the sliders.
    ///
    ///The size to as many places as it has, up to the slider's hundredths: 1×, 0.5×, 0.05×. The draw distance as a
    ///share of the chip's width, and All at the end that draws everything.
    ///
    sayOptions() {
        const speed = document.getElementById('gameTopSpeedSaid');
        const size = document.getElementById('gameCarSizeSaid');
        const reach = document.getElementById('gameDrawDistanceSaid');

        if (speed != null)
            speed.textContent = String(this.options.topSpeed);

        if (size != null)
            size.textContent = `${Math.round(this.options.size * 100) / 100}×`;

        if (reach != null)
            reach.textContent = drawDistanceSaid(this.viewer.view.drawDistance);

        //The haze is the neon's, so without it the two that set it have nothing to set.
        const haze = document.getElementById('gameHaze');
        const hazeSaid = document.getElementById('gameHazeSaid');
        const climbs = document.getElementById('gameHazeClimbs');

        if (hazeSaid != null)
            hazeSaid.textContent = hazeReachSaid(this.options.hazeReach);

        if (haze != null)
            haze.disabled = !this.options.neon;

        if (climbs != null)
            climbs.disabled = !this.options.neon || this.options.hazeReach >= GAME_HAZE_REACHES.most;
    }

    ///A slider moved or a box ticked in the panel. Anything else on the page is somebody else's.
    onOption(event) {
        const control = event.target;

        if (control == null)
            return;

        //The box after the size, which is set on a change and not as it is typed - see setSmallest.
        if (control.id === 'gameCarSmallest') {
            if (event.type === 'change')
                this.setSmallest(control);

            return;
        }

        //The two lists say so as they change, the way P and V do - see setVehicle and setView.
        if (control.id === 'gameVehicle') {
            if (event.type === 'change')
                this.setVehicle(control.value);

            return;
        }

        if (control.id === 'gameView') {
            if (event.type === 'change')
                this.setView(control.value);

            return;
        }

        //
        //**The draw distance and the textures are the view's**, the same two its bar's menu sets - so they go on the
        //view, and are not kept with the game's: it has them at All and on as it starts. See VIEW_OPTIONS.
        //
        if (control.id === 'gameDrawDistance' || control.id === 'gameTexture') {
            if (control.id === 'gameDrawDistance')
                this.viewer.applyView({ drawDistance: Number(control.value) });
            else
                this.viewer.applyView({ textures: control.checked });

            this.sayOptions();

            return;
        }

        if (control.id === 'gameTopSpeed')
            this.options.topSpeed = clampGameOption(Number(control.value), GAME_TOP_SPEEDS, GAME_OPTIONS.topSpeed);
        else if (control.id === 'gameCarSize')
            this.options.size = clampGameOption(Number(control.value), carSizes(this.options), GAME_OPTIONS.size);
        else if (control.id === 'gameInvertPitch')
            this.options.invertPitch = control.checked;
        else if (control.id === 'gameLockOn')
            this.options.locked = control.checked;
        else if (control.id === 'gameLimitJump')
            this.options.limitJump = control.checked;
        else if (control.id === 'gameSound')
            this.options.sound = control.checked;
        else if (control.id === 'gameMusic')
            this.options.music = control.checked;
        else if (control.id === 'gameNeon')
            this.options.neon = control.checked;
        else if (control.id === 'gameHaze')
            this.options.hazeReach = clampGameOption(Number(control.value), GAME_HAZE_REACHES, GAME_OPTIONS.hazeReach);
        else if (control.id === 'gameHazeClimbs')
            this.options.hazeClimbs = control.checked;
        else
            return;

        this.applyOptions();
        this.sayOptions();

        saveGameOptions(this.options);
    }

    ///A click anywhere in the page while the game runs: the panel's × is the one this is listening for.
    onClick(event) {
        if (event.target == null || event.target.closest == null)
            return;

        if (event.target.closest('#gameHudClose') == null)
            return;

        this.setPanel(false);
    }

    ///
    ///How small the size slider goes, from the box after it - the way the layers' Distance has a box for how far its
    ///slider reaches.
    ///
    ///**A car smaller than the new least comes up to it**, so the slider and the car go on agreeing; lowering it moves
    ///nothing, since the thumb only gets a longer track. **Taken on a change, not as it is typed**: typing 0.05 passes
    ///through 0 and 0.0, and each would have taken the car to the least there is and left it there. To hundredths,
    ///the slider's step, so a least is always somewhere the thumb can stand. An empty box is taken back to what it said.
    ///
    setSmallest(box) {
        if (box.value.trim() === '') {
            box.value = String(this.options.smallest);

            return;
        }

        const given = Math.round(Number(box.value) * 100) / 100;

        this.options.smallest = clampGameOption(given, GAME_SMALLEST_SIZES, GAME_OPTIONS.smallest);
        this.options.size = Math.max(this.options.size, this.options.smallest);

        box.value = String(this.options.smallest);

        const size = document.getElementById('gameCarSize');

        if (size != null) {
            size.min = String(this.options.smallest);
            size.value = String(this.options.size);
        }

        this.applyOptions();
        this.sayOptions();

        saveGameOptions(this.options);
    }

    //Keys////////////////////////////////////////////////

    onKeyDown(event) {
        //
        //**Enter in the panel's box is that it is set.** The box lets go, which is the change that sets it, and the
        //keys are the car's again - a box keeps every key it is given, so without this the car would not answer
        //until somebody clicked the view. See isTyping.
        //
        if (event.key === 'Enter' && event.target != null && event.target.id === 'gameCarSmallest') {
            event.target.blur();

            return;
        }

        if (!GAME_KEYS.has(event.code) || this.isTyping(event))
            return;

        event.preventDefault();
        event.stopPropagation();

        //A key is a press, and a press is what a browser waits for before a page may make a sound.
        if (this.sound != null)
            this.sound.wake();

        if (event.code === 'Escape') {
            this.stop(true);

            return;
        }

        if (event.code === 'KeyR') {
            if (!event.repeat) {
                window.chipGame.respawn(this.world, this.player);

                this.say('Back to the start');
            }

            return;
        }

        if (event.code === 'KeyM') {
            if (!event.repeat) {
                this.options.sound = !this.options.sound;

                saveGameOptions(this.options);

                const box = document.getElementById('gameSound');

                if (box != null)
                    box.checked = this.options.sound;

                if (this.options.sound)
                    this.say('Sound on');
                else
                    this.say('Sound off');
            }

            return;
        }

        //The car, the plane and the jet in turn, and round again.
        if (event.code === 'KeyP') {
            if (!event.repeat) {
                const next = (GAME_VEHICLES.indexOf(this.options.vehicle) + 1) % GAME_VEHICLES.length;

                this.setVehicle(GAME_VEHICLES[next]);
            }

            return;
        }

        if (event.code === 'KeyV') {
            if (!event.repeat) {
                if (this.options.view === 'first')
                    this.setView('third');
                else
                    this.setView('first');
            }

            return;
        }

        //Held is the horn going; pressed is one toot at least, however short - see listen.
        if (event.code === 'KeyH' && !event.repeat)
            this.honked = true;

        //And the triggers the same: a tap is a shot, even one let go of before a frame saw it held - see flown.
        if (event.code === 'KeyF' && !event.repeat)
            this.triggered = true;

        if (event.code === 'KeyG' && !event.repeat)
            this.launched = true;

        this.keys.add(event.code);
    }

    onKeyUp(event) {
        if (!GAME_KEYS.has(event.code))
            return;

        //Let go of whether or not it is taken, so a key released into a text box is not held down forever.
        this.keys.delete(event.code);

        if (this.isTyping(event))
            return;

        event.preventDefault();
        event.stopPropagation();
    }

    ///Every key let go when the window loses focus, since the key-up for any of them will go somewhere else - the triggers too.
    onBlur() {
        this.keys.clear();

        this.firing = false;
        this.launching = false;
    }

    ///
    ///Not a game key when it is somebody typing, or a key with a modifier the browser has a use for.
    ///
    ///Ctrl+R is reload and Ctrl+W closes the tab, and taking those would make the game the one thing on the
    ///page a person could not leave by the usual means.
    ///
    isTyping(event) {
        if (event.ctrlKey || event.metaKey || event.altKey)
            return true;

        const focused = document.activeElement;

        if (focused == null)
            return false;

        if (focused.isContentEditable)
            return true;

        //
        //**A slider or a box is not somebody typing.** The panel's own are dragged and clicked, and keep the
        //focus afterwards - which made every key the game's no longer, so the car stopped answering the moment a
        //slider was let go of. They take none of the game's keys now: the arrows steer rather than move the
        //slider, and the space bar jumps rather than ticks the box.
        //
        if (focused.tagName === 'INPUT') {
            const type = (focused.type || '').toLowerCase();

            return type !== 'range' && type !== 'checkbox';
        }

        //Nor are the panel's two lists, for the same reason: the arrows would go through the vehicles.
        if (focused.tagName === 'SELECT')
            return focused.closest('#gameHud') == null;

        return focused.tagName === 'TEXTAREA';
    }

    ///The wheel pulls the camera in and pushes it out, the way it zooms the view when the game is not on.
    onWheel(event) {
        event.preventDefault();

        if (event.deltaY > 0)
            this.zoom = Math.min(this.zoom * 1.1, GAME_ZOOM_MAX);
        else if (event.deltaY < 0)
            this.zoom = Math.max(this.zoom / 1.1, GAME_ZOOM_MIN);
    }

    ///
    ///The mouse moved over the view: the camera goes round the car with it, and up or down.
    ///
    ///**No button, the way a driving game's camera is** - moving the mouse is looking. Right turns the view
    ///right, which is the camera going round the car the other way; down looks down, from higher up. Left alone,
    ///it goes back behind the car by itself - see frameCamera. A touch moves nothing, since a finger on the view
    ///is not looking anywhere.
    ///
    onPointerMove(event) {
        if (event.pointerType === 'touch')
            return;

        //A second button pressed or let go of while one is held comes as a move rather than a press - see press.
        if (event.button >= 0 && event.button < GAME_BUTTON_BITS.length)
            this.press(event.button, (event.buttons & GAME_BUTTON_BITS[event.button]) !== 0);

        //
        //**In the plane the mouse is the stick**, not a look round - **held where the pointer is**: right of the middle
        //of the view a bank to the right, and above it - forward - the nose down, unless the panel says to invert it.
        //The middle is level. A move takes the stick back from the keys - see flown. The jet's the same.
        //
        if (this.flying()) {
            const box = this.viewer.renderer.domElement.getBoundingClientRect();

            this.pointerAt = {
                across: event.clientX - (box.left + (box.width / 2)),
                down: event.clientY - (box.top + (box.height / 2)),
                half: box.height / 2
            };

            this.mouseRolls = true;
            this.mousePitches = true;

            return;
        }

        const across = event.movementX || 0;
        const down = event.movementY || 0;

        if (across === 0 && down === 0)
            return;

        this.lookYaw += across * GAME_LOOK_PER_PIXEL;
        this.lookPitch = Math.min(Math.max(this.lookPitch + (down * GAME_LOOK_PER_PIXEL * 0.6), -GAME_LOOK_DOWN), GAME_LOOK_UP);

        this.lookedAt = performance.now() / 1000;
    }

    ///
    ///The pointer off the view - onto the panel, the bar, or out of the window: the mouse lets go of the plane's stick,
    ///and the wings come back level by themselves. The nose stays where it was left, as it does for the keys.
    ///
    onPointerLeave(event) {
        if (event.pointerType === 'touch')
            return;

        this.pointerAt = null;
        this.mouseRolls = false;
        this.mousePitches = false;
    }

    ///
    ///A button pressed on the view: a trigger, for as long as it is held - see press. A touch is not a trigger any more
    ///than it is a look.
    ///
    onPointerDown(event) {
        if (event.pointerType === 'touch')
            return;

        this.press(event.button, true);
    }

    ///
    ///Let go of anywhere - on the view or off it, since a pointer can leave the canvas while it is held. **Both triggers
    ///are what the buttons still held say**, so one let go of off the view, where nothing here heard it, is let go of too.
    ///
    onPointerUp(event) {
        this.firing = (event.buttons & GAME_BUTTON_BITS[0]) !== 0;
        this.launching = (event.buttons & GAME_BUTTON_BITS[2]) !== 0;
    }

    ///No menu for the other button on the view while the game runs: it is the jet's missiles.
    onContextMenu(event) {
        event.preventDefault();
    }

    ///
    ///One of the mouse's buttons pressed or let go of. **The main one is the gun's trigger**, the plane's and the jet's,
    ///and **the other one the jet's missiles**. In the car neither does anything.
    ///
    ///A press is a shot even let go of before a frame saw it held - see flown. **And it is only the first of the buttons
    ///a pointer event says pressed**: a second, pressed while the first is held, comes as a move - see onPointerMove.
    ///
    press(button, down) {
        if (button === 0) {
            if (down && !this.firing)
                this.triggered = true;

            this.firing = down;
        }
        else if (button === 2) {
            if (down && !this.launching)
                this.launched = true;

            this.launching = down;
        }
        else
            return;

        if (down && this.sound != null)
            this.sound.wake();
    }

    held(...codes) {
        for (const code of codes) {
            if (this.keys.has(code))
                return true;
        }

        return false;
    }

    //////////////////////////////////////////////////////

    ///
    ///One frame: the physics, then the car, its shadow and the camera where the physics left them.
    ///
    update(dt) {
        //The view was left, and took the canvas with it. Its viewer is still running until the next visit
        //builds a new one, and the keys this game holds belong to whichever view is up now.
        if (!this.viewer.renderer.domElement.isConnected) {
            this.stop(false);

            return;
        }

        let throttle = 0;

        if (this.held('KeyW', 'ArrowUp'))
            throttle += 1;

        if (this.held('KeyS', 'ArrowDown'))
            throttle -= 1;

        //
        //**Left on the keyboard is clockwise in the layout.** The scene is the layout with its Y and its stack
        //swapped - a reflection - so a heading that turns counterclockwise in the layout's own plane turns right
        //as the camera behind the car sees it. The physics speaks the layout's language; this is the one
        //place the screen's is spoken.
        //
        let steer = 0;

        if (this.held('KeyA', 'ArrowLeft'))
            steer -= 1;

        if (this.held('KeyD', 'ArrowRight'))
            steer += 1;

        let input = {
            throttle: throttle,
            steer: steer,
            jump: this.held('Space'),
            boost: this.held('ShiftLeft', 'ShiftRight'),
            fire: false,
            missile: false
        };

        if (this.flying())
            input = this.flown();

        //A tap of a trigger in the car is nothing, rather than a shot the plane fires the moment it is boarded.
        this.triggered = false;
        this.launched = false;

        //The jet's boost is its afterburner, which is seen and heard as well as felt - see placeCar and listen.
        this.burning = this.options.vehicle === 'jet' && input.boost;

        const events = window.chipGame.step(this.world, this.player, input, dt);

        for (const event of events)
            this.hear(event);

        if (!this.optionsShown)
            this.showOptions();

        this.placeCar(dt);
        this.placeField(dt);
        this.frameCamera(false, dt);
        this.light(this.player.standing);
        this.placeOutline();
        this.nameLayer();
        this.showHud();
        this.listen(input);
    }

    ///
    ///The plane's controls this frame - see chipGame's fly: W and S the lever, A and D the rudder, the arrows the stick,
    ///and where the mouse holds it. The same keys mean the same as on screen in the car - D and right turn right - which
    ///in the layout's own plane is counterclockwise; see update. The gun's trigger is the mouse's main button or F, and
    ///the jet's missiles' its other button or G.
    ///
    ///**Up pushes the nose down**, as a stick does: forward is down. Invert pitch turns that round, and the mouse with it.
    ///
    ///**The mouse holds the stick where the pointer is**, from the middle of the view - see onPointerMove - and a key
    ///takes its way of the stick from it until the mouse next moves, since the pointer's place would pull the stick
    ///straight back to it. Space takes both, to level them. **Near the top or the foot of the view it pulls the stick
    ///past what it holds** - see GAME_PLANE_STICK_PULL - and moves the nose the way a key does, round a loop.
    ///
    flown() {
        let throttle = 0;

        if (this.held('KeyW'))
            throttle += 1;

        if (this.held('KeyS'))
            throttle -= 1;

        let yaw = 0;

        if (this.held('KeyA'))
            yaw -= 1;

        if (this.held('KeyD'))
            yaw += 1;

        let pitch = 0;

        if (this.held('ArrowUp'))
            pitch -= 1;

        if (this.held('ArrowDown'))
            pitch += 1;

        if (this.options.invertPitch)
            pitch = -pitch;

        let roll = 0;

        if (this.held('ArrowLeft'))
            roll -= 1;

        if (this.held('ArrowRight'))
            roll += 1;

        const level = this.held('Space');

        if (roll !== 0 || level)
            this.mouseRolls = false;

        if (pitch !== 0 || level)
            this.mousePitches = false;

        //The keys' stick, damped - see GAME_PLANE_KEY_SHARE. Which keys are down is what takes the stick from the mouse, above.
        const now = performance.now() / 1000;
        const easing = 1 - Math.exp(-Math.min(0.1, Math.max(0, now - (this.keysAt ?? now))) / GAME_PLANE_KEY_EASE);

        this.keysAt = now;
        this.keyPitch += ((pitch * GAME_PLANE_KEY_SHARE) - this.keyPitch) * easing;
        this.keyRoll += ((roll * GAME_PLANE_KEY_SHARE) - this.keyRoll) * easing;

        if (pitch === 0 && Math.abs(this.keyPitch) < 0.01)
            this.keyPitch = 0;

        if (roll === 0 && Math.abs(this.keyRoll) < 0.01)
            this.keyRoll = 0;

        pitch = this.keyPitch;
        roll = this.keyRoll;

        let bank = null;
        let nose = null;

        if (this.pointerAt != null && this.mouseRolls) {
            bank = stickTravel(this.pointerAt.across) * GAME_PLANE_ROLL_PER_PIXEL;
            bank = Math.min(Math.max(bank, -GAME_PLANE_STICK_BANK), GAME_PLANE_STICK_BANK);
        }

        if (this.pointerAt != null && this.mousePitches) {
            const pull = stickPull(this.pointerAt.down, this.pointerAt.half);

            if (pull !== 0)
                pitch = pull;
            else
                nose = stickTravel(this.pointerAt.down) * GAME_PLANE_PITCH_PER_PIXEL;

            if (this.options.invertPitch) {
                pitch = -pitch;

                if (nose != null)
                    nose = -nose;
            }
        }

        const fire = this.firing || this.held('KeyF') || this.triggered;
        const missile = this.launching || this.held('KeyG') || this.launched;

        this.triggered = false;
        this.launched = false;

        return {
            throttle: throttle,
            yaw: yaw,
            pitch: pitch,
            roll: roll,
            bank: bank,
            nose: nose,
            level: level,
            jump: level,
            boost: this.held('ShiftLeft', 'ShiftRight'),
            fire: fire,
            missile: missile
        };
    }

    ///
    ///What the engine and the tires sound like this frame.
    ///
    ///**The engine goes with the speed**, as a share of the car's top speed, and a little with the throttle, so
    ///pressing it at a standstill revs. **The tires squeal** on a turn hard enough for the speed - the wheel's
    ///turn times the share of the top speed, past GAME_SQUEAL_TURN - and on the brakes at speed, and only on the
    ///ground; on the side of a wall the engine works and the tires are quiet.
    ///
    ///**The plane is its propeller**: the same engine pitched up and opened out, going with its speed as a share of
    ///its own top one, and no tires. **The jet is a whine with a roar under it**, and the afterburner is more roar.
    ///
    ///**H is the horn**, whatever is being played: for as long as it is held, and a toot at least for a tap too short
    ///for a frame to have seen it held. **The jet's gun is an A-10's**, the same way: for as long as its trigger is held
    ///and it has not overheated - see GameSound.fire and chipGame's warm - where the plane's cracks once a round; see hear.
    ///
    ///**And the music**, the tune of whatever is being played, while the sound and the panel's Music are both on.
    ///
    listen(input) {
        if (this.sound == null)
            return;

        const player = this.player;
        const throttle = Math.abs(input.throttle);

        this.sound.playMusic(this.options.sound && this.options.music, this.options.vehicle);
        this.sound.honk(this.options.sound && (this.held('KeyH') || this.honked));
        this.sound.fire(this.options.sound && player.jet === true && input.fire === true && !player.gun.hot);

        this.honked = false;

        if (player.plane) {
            const flying = Math.min(1.75, player.speed / window.chipGame.planeTop(this.world, player));

            let burning = 0;

            if (this.burning)
                burning = 1;

            this.sound.update(this.options.sound, Math.max(flying, throttle * 0.25), throttle, 0, this.options.vehicle, burning);

            return;
        }

        const top = player.topSpeed * window.chipGame.carWidth(this.world, player);
        const share = Math.min(1.75, Math.abs(player.speed) / top);

        let revs = Math.max(share, throttle * 0.25);

        if (player.climbing)
            revs = 0.7;

        let squeal = 0;

        if (player.grounded) {
            const turning = (Math.abs(player.steer) * share) - GAME_SQUEAL_TURN;
            const braking = input.throttle * player.speed < 0;

            if (turning > 0)
                squeal = Math.min(1, turning / 0.3);

            if (braking && share > GAME_SQUEAL_BRAKE)
                squeal = Math.max(squeal, Math.min(1, (share - GAME_SQUEAL_BRAKE) / 0.35));
        }

        this.sound.update(this.options.sound, revs, throttle, squeal, 'car', 0);
    }

    ///
    ///What the player hears, sees or is told about what just happened: a jump's blip, an electron taken, a shot, a
    ///missile blowing up, the meter run dry, a weapon overheated.
    ///
    hear(event) {
        const sound = this.sound;
        const loud = sound != null && this.options.sound;

        if (event.type === 'jump' || event.type === 'airJump') {
            if (loud)
                sound.blip(event.type === 'airJump');

            return;
        }

        //Driven into, it chimes; shot down, it zaps. Either goes out in a flash of its own yellow.
        if (event.type === 'pickedUp' || event.type === 'shotDown') {
            if (loud && event.type === 'pickedUp')
                sound.chime();
            else if (loud)
                sound.zap();

            this.flash(event, GAME_ELECTRON_COLOR, 0.9, 0.35);

            return;
        }

        //
        //A ball of fire about half as wide as what it takes with it, and a hot core inside it. As wide as all of that, it
        //was a pale disc over half the view, faded out before it read as fire.
        //
        if (event.type === 'blast') {
            if (loud)
                sound.boom();

            this.flash(event, 0xff6a00, window.chipGame.TUNING.missileBlast * 0.5, 0.5);
            this.flash(event, 0xffe27a, window.chipGame.TUNING.missileBlast * 0.25, 0.3);

            return;
        }

        //The jet's rounds are not heard one by one: its gun is a growl for as long as the trigger is held - see listen.
        if (event.type === 'fired') {
            if (loud && event.kind === 'missile')
                sound.whoosh();
            else if (loud && !this.player.jet)
                sound.gun();

            return;
        }

        //A press the meter had no jump for, and the meter full again - see chipGame's charge.
        if (event.type === 'noJump') {
            if (loud)
                sound.refuse();

            this.shakeMeter('gameJumps');

            return;
        }

        //
        //A weapon overheated hisses, and a press of its trigger while it cools is the dull note and shake a jump the
        //meter had nothing for is - see chipGame's warm. A soft note says it is cold again, in the air: in the car,
        //where the last of the cooling can happen, nothing is waiting on it.
        //
        if (event.type === 'overheated') {
            if (loud)
                sound.overheat();

            return;
        }

        if (event.type === 'noShot') {
            if (loud)
                sound.refuse();

            if (event.kind === 'missile')
                this.shakeMeter('gameHeatMissiles');
            else
                this.shakeMeter('gameHeatGun');

            return;
        }

        if (event.type === 'cooled') {
            if (loud && this.player.plane)
                sound.ding();

            return;
        }

        if (event.type === 'recharged') {
            if (loud)
                sound.ding();

            return;
        }

        if (event.type === 'fell') {
            this.say('Off the edge! Back to the start');

            return;
        }

        //A plane brought down onto a layer is a landing worth saying, once it has been up long enough to have flown.
        if (event.type === 'touchdown') {
            if (event.airTime >= GAME_AIRTIME_WORTH_SAYING)
                this.say('Touchdown');

            return;
        }

        if (event.type === 'landed' && event.airTime >= GAME_AIRTIME_WORTH_SAYING)
            this.say(`${event.airTime.toFixed(1)} s of air!`);
    }

    ///
    ///A line across the top of the view for a moment.
    ///
    ///Looked up each time rather than held: the component renders it when the game starts, which is after
    ///the interop call that started the game has already returned.
    ///
    say(text) {
        const toast = document.getElementById('gameToast');

        if (toast == null)
            return;

        toast.textContent = text;
        toast.classList.add('gameToastShown');

        if (this.toastTimer != null)
            clearTimeout(this.toastTimer);

        this.toastTimer = setTimeout(() => {
            this.toastTimer = null;
            toast.classList.remove('gameToastShown');
        }, GAME_TOAST_MS);
    }

    ///The name of the layer the car is on, in the corner. Kept through a jump, since in the air it is on none.
    nameLayer() {
        const player = this.player;

        if (player.standing < 0 || player.standing >= this.meshes.length)
            return;

        const at = this.meshes[player.standing].userData.stackAt;
        const name = this.layerNames[at];

        if (name == null)
            return;

        const label = document.getElementById('gameLayer');

        if (label != null && label.textContent !== name)
            label.textContent = name;
    }

    ///
    ///What is laid over the view: the score, the jump meter while the jumps are limited, and in the plane and the jet the
    ///weapons' heat and the crosshair - with, in the jet, a bracket round the electron a missile would go after. Each
    ///written only when what it says has changed, and looked up each time for the reason say gives.
    ///
    showHud() {
        const player = this.player;
        const shown = this.shown;
        const score = document.getElementById('gameScore');

        if (score != null && player.score !== shown.score) {
            score.textContent = String(player.score);

            //A little jump, for every electron after the first frame's nothing.
            if (shown.score >= 0 && typeof score.animate === 'function')
                score.animate([{ transform: 'scale(1.4)' }, { transform: 'scale(1)' }], { duration: 250, easing: 'ease-out' });

            shown.score = player.score;
        }

        this.showMeter();
        this.showHeat();
        this.showSights();
        this.showJetHud();
    }

    ///
    ///**The jet's HUD**: green lines on the glass in the middle of the view, the way a fighter's are - see drawJetHud.
    ///Drawn every frame while the jet is flown in first person, and hidden otherwise. What it last said is kept in
    ///this.hudSaid, for a spec to read.
    ///
    ///**First person only**, where the glass is what the pilot looks through, and the jet's sight in place of the white
    ///crosshair - see showSights. Behind the jet it sat in the middle of the view over the crosshair, its waterline mark
    ///where the nose was not.
    ///
    showJetHud() {
        const canvas = document.getElementById('gameJetHud');

        if (canvas == null)
            return;

        const on = this.player.jet === true && this.options.view === 'first';

        if (canvas.hidden === on)
            canvas.hidden = !on;

        if (!on)
            return;

        const width = canvas.clientWidth;
        const height = canvas.clientHeight;

        if (width === 0 || height === 0)
            return;

        //Its pixels the screen's, so the lines are as sharp as the page's own.
        const ratio = window.devicePixelRatio || 1;
        const across = Math.round(width * ratio);
        const down = Math.round(height * ratio);

        if (canvas.width !== across || canvas.height !== down) {
            canvas.width = across;
            canvas.height = down;
        }

        const pen = canvas.getContext('2d');

        pen.setTransform(ratio, 0, 0, ratio, 0, 0);
        pen.clearRect(0, 0, width, height);

        this.hudSaid = this.jetReading();

        drawJetHud(pen, width, height, this.hudSaid);
    }

    ///
    ///What the jet's HUD says: its nose and its bank, in degrees; its heading, as a compass says one - zero along the
    ///layout's Y, which is up in the 2D view, and going up with a turn to the right; its speed and its height over what is
    ///under it, in its own widths, and the height null over nothing; the speed as a share of its top, which is the lever;
    ///whether the afterburner is lit and the next missile ready; and how hot the gun and the missiles are, whether either
    ///has overheated, and whether HOT over one that has is on this frame or off.
    ///
    ///**The right way up** - see chipGame.attitude - so over the top of a loop the nose reads its climb or its dive from
    ///the horizon, the ladder turns over with the bank, and the heading is the way the jet is going.
    ///
    jetReading() {
        const world = this.world;
        const player = this.player;
        const width = window.chipGame.carWidth(world, player);
        const ground = window.chipGame.groundBelow(world, player.x, player.y, player.z + (width * 0.01));
        const said = window.chipGame.attitude(player);
        const degrees = 180 / Math.PI;

        let height = null;

        if (Number.isFinite(ground))
            height = (player.z - ground) / width;

        return {
            pitch: said.pitch * degrees,
            roll: said.roll * degrees,
            heading: ((Math.round((said.heading * degrees) - 90) % 360) + 360) % 360,
            speed: player.speed / width,
            height: height,
            lever: player.speed / window.chipGame.planeTop(world, player),
            burning: this.burning,
            ready: player.missiles.wait <= 1e-6 && !player.missiles.hot,
            gunHeat: player.gun.heat,
            gunHot: player.gun.hot,
            missileHeat: player.missiles.heat,
            missileHot: player.missiles.hot,
            flash: Math.floor(performance.now() / GAME_HUD_FLASH_MS) % 2 === 0
        };
    }

    ///
    ///Limit jump's meter: a cell a jump, each as full as that jump is - see chipGame's charge - and red while it fills
    ///from empty, which is the wait before the next jump. Not there in the plane or the jet, nor with the jumps unlimited.
    ///
    showMeter() {
        const meter = document.getElementById('gameJumps');

        if (meter == null)
            return;

        const player = this.player;
        const on = player.limitJump && !player.plane;

        let said = 'off';

        if (on)
            said = `${player.jumps.toFixed(2)} ${player.recharging}`;

        if (said === this.shown.jumps)
            return;

        this.shown.jumps = said;

        meter.hidden = !on;

        if (!on)
            return;

        meter.classList.toggle('gameJumpsRecharging', player.recharging);

        const fills = meter.querySelectorAll('.gameJumpFill');

        for (let i = 0; i < fills.length; i++) {
            const full = Math.min(1, Math.max(0, player.jumps - i));

            fills[i].style.width = `${Math.round(full * 100)}%`;
        }
    }

    ///
    ///The heat of the gun and, in the jet, of the missiles: a bar each, as full as it is hot - see chipGame's warm - and
    ///red and pulsing while it cools from overheated, which is the wait before it fires again. Under the score in the
    ///plane and behind the jet, where the jump meter is in the car; **not in the jet in first person**, whose HUD says it.
    ///
    showHeat() {
        const meter = document.getElementById('gameHeat');
        const gun = document.getElementById('gameHeatGun');
        const missiles = document.getElementById('gameHeatMissiles');

        if (meter == null || gun == null || missiles == null)
            return;

        const player = this.player;
        const cockpit = player.jet === true && this.options.view === 'first';
        const on = player.plane === true && !cockpit;

        let said = 'off';

        if (on)
            said = `${player.jet} ${heatSaid(player.gun)} ${heatSaid(player.missiles)}`;

        if (said === this.shown.heat)
            return;

        this.shown.heat = said;

        meter.hidden = !on;

        if (!on)
            return;

        missiles.hidden = !player.jet;

        showWeaponHeat(gun, player.gun);
        showWeaponHeat(missiles, player.missiles);
    }

    ///
    ///A shake of a meter - the one `id` names - for a press it had nothing for: Limit jump's with no jump in it, and a
    ///weapon's part of the heat while it cools from overheated.
    ///
    shakeMeter(id) {
        const meter = document.getElementById(id);

        if (meter == null || meter.closest('[hidden]') != null || typeof meter.animate !== 'function')
            return;

        const frames = [
            { transform: 'translateX(0)' },
            { transform: 'translateX(-5px)' },
            { transform: 'translateX(5px)' },
            { transform: 'translateX(-3px)' },
            { transform: 'translateX(0)' }
        ];

        meter.animate(frames, { duration: 280, easing: 'ease-out' });
    }

    ///
    ///The crosshair over where the gun's rounds go - GAME_AIM_AHEAD car widths along the nose, from the guns' height -
    ///and in the jet the bracket over what aim picks. In first person the crosshair is at the middle of the view, near
    ///enough, since that is where the nose points; behind the plane it is wherever that point is on screen.
    ///
    ///**Not in the jet in first person**, where the HUD is the sight: its waterline mark is where the nose points, and the
    ///rounds go a few pixels under it - see showJetHud. The bracket is there either way, being the missiles' and not the gun's.
    ///
    showSights() {
        const crosshair = document.getElementById('gameCrosshair');
        const lock = document.getElementById('gameLock');

        if (crosshair == null || lock == null)
            return;

        const player = this.player;

        if (!player.plane) {
            this.pinTo(crosshair, null, 'crosshair');
            this.pinTo(lock, null, 'lock');

            return;
        }

        const world = this.world;
        const width = window.chipGame.carWidth(world, player);
        const lift = this.viewer.chipObjectsGroup.position.y;
        const frame = window.chipGame.axes(player);
        const ahead = GAME_AIM_AHEAD * width;
        const gun = player.z + (0.3 * width);

        if (player.jet && this.options.view === 'first')
            this.pinTo(crosshair, null, 'crosshair');
        else {
            this.onScreen.set(player.x + (frame.fx * ahead), gun + (frame.fz * ahead) + lift, player.y + (frame.fy * ahead));
            this.pinTo(crosshair, this.onScreen, 'crosshair');
        }

        let target = null;

        if (player.jet)
            target = window.chipGame.aim(world, player);

        if (target == null) {
            this.pinTo(lock, null, 'lock');

            return;
        }

        this.onScreen.set(target.x, window.chipGame.electronZ(world, player, target) + lift, target.y);
        this.pinTo(lock, this.onScreen, 'lock');
    }

    ///
    ///One of the HUD's marks over where `at`, in the scene, is on screen - or hidden, for no `at` or one behind the
    ///camera. Moved by a transform, which is drawn without laying the page out again; `key` is what this.shown keeps
    ///its last place under.
    ///
    pinTo(element, at, key) {
        let said = 'hidden';
        let x = 0;
        let y = 0;

        if (at != null) {
            at.project(this.viewer.camera);

            const canvas = this.viewer.renderer.domElement;

            x = Math.round(((at.x + 1) / 2) * canvas.clientWidth);
            y = Math.round(((1 - at.y) / 2) * canvas.clientHeight);

            if (at.z < 1)
                said = `${x} ${y}`;
        }

        if (said === this.shown[key])
            return;

        this.shown[key] = said;

        if (said === 'hidden') {
            element.hidden = true;

            return;
        }

        element.hidden = false;
        element.style.transform = `translate(${x}px, ${y}px) translate(-50%, -50%)`;
    }

    ///
    ///Outlines the slab the car is on, so the wire being driven along is the one that stands out.
    ///
    ///**An outline rather than a glow**, because a layer is one mesh - see draw - and lighting the slab would
    ///light every shape on its layer. A line round its top is that one shape and nothing else. It is the
    ///scene's rather than the chip group's, like the car, and put just clear of the top so the two do not
    ///fight over the same depth.
    ///
    light(index) {
        if (index === this.lit)
            return;

        this.unlight();

        if (index == null || index < 0 || index >= this.world.slabs.length)
            return;

        //Every edge of every ring, holes included, as a pair of ends: one geometry and one draw for the lot.
        const positions = [];

        for (const ring of this.world.slabs[index].rings) {
            const count = ring.length / 2;

            for (let i = 0, j = count - 1; i < count; j = i++)
                positions.push(ring[j * 2], 0, ring[(j * 2) + 1], ring[i * 2], 0, ring[(i * 2) + 1]);
        }

        const geometry = new THREE.BufferGeometry();

        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));

        this.outline = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color: new THREE.Color(GAME_OUTLINE).multiplyScalar(this.hotness()) }));
        this.outline.name = 'chipGameOutline';

        this.viewer.scene.add(this.outline);

        this.lit = index;

        this.placeOutline();
    }

    ///Takes the outline away, and lets go of what it was drawn with.
    unlight() {
        if (this.outline != null) {
            this.viewer.scene.remove(this.outline);
            this.outline.geometry.dispose();
            this.outline.material.dispose();
        }

        this.outline = null;
        this.lit = null;
    }

    ///At the top of its slab, which the Distance slider moves and the stack's centering lifts.
    placeOutline() {
        if (this.outline == null)
            return;

        const top = this.world.slabs[this.lit].top;

        this.outline.position.y = top + this.viewer.chipObjectsGroup.position.y + (this.world.unit * 0.02);
    }

    //The car////////////////////////////////////////////

    ///
    ///A little car, a car width across, with its wheels on the ground at its own origin.
    ///
    ///Short for a car on purpose. What the physics knows of it is a circle a width across, so a long body
    ///would put its nose into every wall it drove up to; this one hangs barely past the circle at either end.
    ///
    buildCar() {
        const unit = this.world.unit;
        const car = new THREE.Group();

        const paint = new THREE.MeshPhongMaterial({ color: 0xffb703, emissive: 0x3a2400, shininess: 90 });
        const glass = new THREE.MeshPhongMaterial({ color: 0x1d2b3a, shininess: 140 });
        const rubber = new THREE.MeshPhongMaterial({ color: 0x161616 });
        const lamp = new THREE.MeshBasicMaterial({ color: 0xfff4c2 });

        const body = new THREE.Mesh(new THREE.BoxGeometry(1.25, 0.34, 0.9), paint);
        body.position.set(0, 0.35, 0);
        car.add(body);

        const cabin = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.26, 0.74), glass);
        cabin.position.set(-0.1, 0.65, 0);
        car.add(cabin);

        //A cylinder stands up its own Y, so each wheel is tipped onto its side to roll along the car's X.
        const wheel = new THREE.CylinderGeometry(0.18, 0.18, 0.14, 16);

        for (const x of [-0.4, 0.4]) {
            for (const z of [-0.45, 0.45]) {
                const one = new THREE.Mesh(wheel, rubber);

                one.rotation.x = Math.PI / 2;
                one.position.set(x, 0.18, z);
                car.add(one);
            }
        }

        //Headlights on the front, which is +X - the way the car drives, and so the way it faces.
        const headlight = new THREE.BoxGeometry(0.04, 0.09, 0.18);

        for (const z of [-0.28, 0.28]) {
            const one = new THREE.Mesh(headlight, lamp);

            one.position.set(0.63, 0.38, z);
            car.add(one);
        }

        car.scale.setScalar(unit);

        //
        //Where the driver's eyes are, for first person: in the cabin, near its front, so the bonnet is the foot of the
        //view - which is what says how wide the car is on a wire. Seen from inside, the cabin's own faces are their
        //backs, and are not drawn.
        //
        car.userData.eye = new THREE.Vector3(0.15, 0.72, 0);

        //
        //**A shadow under it, which is what makes a jump readable at all.** Seen from behind and above, a
        //car in the air and a car on a layer further back look the same; the dark patch on whatever is under
        //it is what says how high it is and where it will come down.
        //
        const shadow = new THREE.Mesh(
            new THREE.CircleGeometry(0.6, 24),
            new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.4, depthWrite: false }));

        shadow.rotation.x = -Math.PI / 2;
        shadow.scale.setScalar(unit);

        //Named, so the specs can find them in the scene without guessing from their shapes.
        car.name = 'chipGameCar';
        shadow.name = 'chipGameShadow';

        this.viewer.scene.add(car);
        this.viewer.scene.add(shadow);

        this.car = car;
        this.shadow = shadow;

        //Everything the car is made of, so it can all be let go of.
        this.carParts = [paint, glass, rubber, lamp, body.geometry, cabin.geometry, wheel, headlight];

        //And what the neon lights past white, the lamps first - see heatParts.
        this.hotParts = [heated(lamp.color)];

        this.buildPlane(paint, glass, rubber);
        this.buildJet(paint, glass, rubber);
        this.buildField();
        this.showVehicle();
        this.heatParts();
    }

    ///
    ///A little plane, in the car's paint: a car width from wingtip to wingtip and a little more, nose along +X like the
    ///car's, and its wheels on the ground at its own origin - so it lands, runs and is put on screen the way the car is.
    ///
    ///**Rolled about its own length and tipped about its wings, then turned** - the order 'YZX' says - so the roll is
    ///the bank the physics means whatever the heading. Its right, as it is flown from behind, is +Z: a bank right is
    ///+X turned so that +Z goes down, which is a positive roll.
    ///
    buildPlane(paint, glass, rubber) {
        const unit = this.world.unit;
        const plane = new THREE.Group();

        const trim = new THREE.MeshPhongMaterial({ color: 0x1d3557, shininess: 60 });
        const blade = new THREE.MeshPhongMaterial({ color: 0x2b2b2b, shininess: 30 });

        //A cylinder stands up its own Y; laid along X, its top is the nose - the wider end.
        const hull = new THREE.CylinderGeometry(0.12, 0.06, 1.1, 14);
        const fuselage = new THREE.Mesh(hull, paint);

        fuselage.rotation.z = -Math.PI / 2;
        fuselage.position.set(0, 0.3, 0);
        plane.add(fuselage);

        const cowl = new THREE.CylinderGeometry(0.07, 0.12, 0.14, 14);
        const nose = new THREE.Mesh(cowl, trim);

        nose.rotation.z = -Math.PI / 2;
        nose.position.set(0.62, 0.3, 0);
        plane.add(nose);

        const span = new THREE.BoxGeometry(0.32, 0.035, 1.4);
        const wings = new THREE.Mesh(span, paint);

        wings.position.set(0.08, 0.26, 0);
        plane.add(wings);

        //Dark tips, so a bank reads at a glance from behind.
        const tip = new THREE.BoxGeometry(0.32, 0.04, 0.12);

        for (const z of [-0.7, 0.7]) {
            const one = new THREE.Mesh(tip, trim);

            one.position.set(0.08, 0.26, z);
            plane.add(one);
        }

        const tailplane = new THREE.BoxGeometry(0.18, 0.03, 0.52);
        const stabilizer = new THREE.Mesh(tailplane, paint);

        stabilizer.position.set(-0.47, 0.32, 0);
        plane.add(stabilizer);

        const finShape = new THREE.BoxGeometry(0.2, 0.24, 0.03);
        const fin = new THREE.Mesh(finShape, trim);

        fin.position.set(-0.47, 0.45, 0);
        plane.add(fin);

        const canopy = new THREE.BoxGeometry(0.24, 0.1, 0.14);
        const cockpit = new THREE.Mesh(canopy, glass);

        cockpit.position.set(0.14, 0.42, 0);
        plane.add(cockpit);

        //Two wheels under the wings on their legs, and one under the tail.
        const tire = new THREE.CylinderGeometry(0.06, 0.06, 0.05, 12);
        const leg = new THREE.BoxGeometry(0.03, 0.14, 0.03);

        for (const z of [-0.17, 0.17]) {
            const one = new THREE.Mesh(tire, rubber);

            one.rotation.x = Math.PI / 2;
            one.position.set(0.14, 0.06, z);
            plane.add(one);

            const strut = new THREE.Mesh(leg, trim);

            strut.position.set(0.14, 0.15, z);
            plane.add(strut);
        }

        const tailWheel = new THREE.Mesh(tire, rubber);

        tailWheel.rotation.x = Math.PI / 2;
        tailWheel.scale.setScalar(0.6);
        tailWheel.position.set(-0.5, 0.04, 0);
        plane.add(tailWheel);

        //The propeller, turned about the nose's axis every frame - see placeCar.
        const prop = new THREE.BoxGeometry(0.02, 0.5, 0.05);
        const propeller = new THREE.Group();

        propeller.add(new THREE.Mesh(prop, blade));
        propeller.position.set(0.7, 0.3, 0);
        plane.add(propeller);

        plane.rotation.order = 'YZX';
        plane.scale.setScalar(unit);
        plane.name = 'chipGamePlane';

        //
        //Over the canopy, for first person, and forward of it: the cowl is a sliver at the foot of the view. Inside the
        //canopy, the fuselage just under the eye filled the bottom half of it.
        //
        plane.userData.eye = new THREE.Vector3(0.3, 0.6, 0);

        this.viewer.scene.add(plane);

        this.aircraft = plane;
        this.propeller = propeller;

        this.carParts.push(trim, blade, hull, cowl, span, tip, tailplane, finShape, canopy, tire, leg, prop);
    }

    ///
    ///A little jet: gray, with the car's paint on its fins, a bubble canopy, delta wings with a missile under each, and
    ///a flame out of its nozzle that is the throttle - and the afterburner, when it is lit. About a car width and three
    ///quarters long, nose along +X and wheels at its own origin like the plane, and turned the plane's way - see
    ///buildPlane and placeCar.
    ///
    buildJet(paint, glass, rubber) {
        const unit = this.world.unit;
        const jet = new THREE.Group();

        const skin = new THREE.MeshPhongMaterial({ color: 0x9aa6b2, shininess: 70 });
        const dark = new THREE.MeshPhongMaterial({ color: 0x2b2d31, shininess: 40 });
        const white = new THREE.MeshPhongMaterial({ color: 0xf2f2f2, shininess: 80 });

        //Laid along X the way the plane's is: the cylinder's top is the front, and the cone's tip.
        const hull = new THREE.CylinderGeometry(0.09, 0.11, 1.2, 16);
        const fuselage = new THREE.Mesh(hull, skin);

        fuselage.rotation.z = -Math.PI / 2;
        fuselage.position.set(0, 0.28, 0);
        jet.add(fuselage);

        const point = new THREE.ConeGeometry(0.09, 0.4, 16);
        const nose = new THREE.Mesh(point, skin);

        nose.rotation.z = -Math.PI / 2;
        nose.position.set(0.8, 0.28, 0);
        jet.add(nose);

        const bubble = new THREE.SphereGeometry(0.08, 16, 10);
        const canopy = new THREE.Mesh(bubble, glass);

        canopy.scale.set(2.2, 0.9, 1);
        canopy.position.set(0.38, 0.36, 0);
        jet.add(canopy);

        //
        //A delta, drawn flat as an outline and given a thickness: drawn in X and Y and laid down so its Y is the jet's Z,
        //across the wings. Both halves in one outline, through the fuselage.
        //
        const delta = new THREE.Shape();

        delta.moveTo(0.25, 0.08);
        delta.lineTo(-0.45, 0.72);
        delta.lineTo(-0.6, 0.72);
        delta.lineTo(-0.6, -0.72);
        delta.lineTo(-0.45, -0.72);
        delta.lineTo(0.25, -0.08);
        delta.closePath();

        const wing = new THREE.ExtrudeGeometry(delta, { depth: 0.03, bevelEnabled: false });

        wing.rotateX(Math.PI / 2);

        const wings = new THREE.Mesh(wing, skin);

        wings.position.set(0, 0.27, 0);
        jet.add(wings);

        //Two fins leaning out from each other, in the car's paint so the jet is the car's family from behind.
        const finShape = new THREE.Shape();

        finShape.moveTo(-0.38, 0);
        finShape.lineTo(-0.55, 0.3);
        finShape.lineTo(-0.68, 0.3);
        finShape.lineTo(-0.66, 0);
        finShape.closePath();

        const finSolid = new THREE.ExtrudeGeometry(finShape, { depth: 0.02, bevelEnabled: false });

        for (const side of [-1, 1]) {
            const fin = new THREE.Mesh(finSolid, paint);

            fin.position.set(0, 0.32, (0.12 * side) - 0.01);
            fin.rotation.x = 0.25 * side;
            jet.add(fin);
        }

        //The nozzle, and the flame out of the back of it - which placeCar stretches with the throttle.
        const can = new THREE.CylinderGeometry(0.1, 0.085, 0.12, 16);
        const nozzle = new THREE.Mesh(can, dark);

        nozzle.rotation.z = -Math.PI / 2;
        nozzle.position.set(-0.66, 0.28, 0);
        jet.add(nozzle);

        //
        //A cone with its tip back along -X and its base at the nozzle's mouth: turned a quarter about Z, which takes its
        //tip from +Y round to -X, and moved so the base is at the origin. Two of them, the hot core inside the flame.
        //
        const plume = new THREE.ConeGeometry(0.075, 0.6, 14, 1, true);

        plume.rotateZ(Math.PI / 2);
        plume.translate(-0.3, 0, 0);

        const core = new THREE.ConeGeometry(0.045, 0.4, 12, 1, true);

        core.rotateZ(Math.PI / 2);
        core.translate(-0.2, 0, 0);

        const fire = new THREE.MeshBasicMaterial({ color: 0xff7a1a, transparent: true, opacity: 0.8, depthWrite: false, side: THREE.DoubleSide });
        const heat = new THREE.MeshBasicMaterial({ color: 0xfff0b0, transparent: true, opacity: 0.95, depthWrite: false, side: THREE.DoubleSide });
        const flame = new THREE.Group();

        flame.add(new THREE.Mesh(plume, fire));
        flame.add(new THREE.Mesh(core, heat));
        flame.position.set(-0.72, 0.28, 0);
        flame.name = 'chipGameFlame';
        jet.add(flame);

        //A missile under each wing, on a rail - its look, rather than how many are left: there is no running out.
        const body = new THREE.CylinderGeometry(0.03, 0.03, 0.42, 10);
        const rail = new THREE.BoxGeometry(0.2, 0.03, 0.02);

        //Under the wing where it is widest, which is where chipGame's fire launches one from.
        for (const side of [-1, 1]) {
            const missile = new THREE.Mesh(body, white);

            missile.rotation.z = -Math.PI / 2;
            missile.position.set(-0.25, 0.2, 0.4 * side);
            jet.add(missile);

            const mount = new THREE.Mesh(rail, dark);

            mount.position.set(-0.25, 0.235, 0.4 * side);
            jet.add(mount);
        }

        //A nose wheel and two under the wings, on legs down from the fuselage.
        const tire = new THREE.CylinderGeometry(0.05, 0.05, 0.04, 12);
        const leg = new THREE.BoxGeometry(0.025, 0.14, 0.025);

        for (const at of [[0.45, 0], [-0.15, -0.16], [-0.15, 0.16]]) {
            const one = new THREE.Mesh(tire, rubber);

            one.rotation.x = Math.PI / 2;
            one.position.set(at[0], 0.05, at[1]);
            jet.add(one);

            const strut = new THREE.Mesh(leg, dark);

            strut.position.set(at[0], 0.14, at[1]);
            jet.add(strut);
        }

        jet.rotation.order = 'YZX';
        jet.scale.setScalar(unit);
        jet.name = 'chipGameJet';

        //Over the front of the bubble, for first person, as the plane's is: the tip of the nose shows at the foot of the view.
        jet.userData.eye = new THREE.Vector3(0.45, 0.52, 0);

        this.viewer.scene.add(jet);

        this.jet = jet;
        this.flame = flame;

        this.carParts.push(skin, dark, white, hull, point, bubble, wing, finSolid, can, plume, core, fire, heat, body, rail, tire, leg);
        this.hotParts.push(heated(fire.color), heated(heat.color));
    }

    ///
    ///What the game puts in the world besides the vehicles: the electrons, the rounds and missiles in flight, and the
    ///flashes things go out in.
    ///
    ///**One draw apiece rather than one each.** The electrons are an instanced mesh of spheres and a cloud of points for
    ///their glow; the rounds, the missiles and the missiles' flames are an instanced mesh each - their places put in
    ///every frame by placeField. Instanced meshes are measured off their one sphere or box at the origin, which is
    ///nowhere near where they are drawn, so they are never left out for being off screen.
    ///
    ///**The glow is a dot of the same size wherever it is**, so an electron far off is still something to aim at: up
    ///close the sphere grows round it and hides it.
    ///
    buildField() {
        const scene = this.viewer.scene;

        //Yellow, glowing from inside: asked for in place of the blue they were.
        const ball = new THREE.SphereGeometry(1, 16, 12);
        const charge = new THREE.MeshPhongMaterial({ color: GAME_ELECTRON_COLOR, emissive: 0xc49000, specular: 0xffffff, shininess: 120 });

        this.electronMesh = new THREE.InstancedMesh(ball, charge, GAME_ELECTRONS_DRAWN);
        this.electronMesh.count = 0;
        this.electronMesh.frustumCulled = false;
        this.electronMesh.name = 'chipGameElectrons';

        const glows = new THREE.BufferGeometry();

        glows.setAttribute('position', new THREE.BufferAttribute(new Float32Array(GAME_ELECTRONS_DRAWN * 3), 3));
        glows.setDrawRange(0, 0);

        const halo = new THREE.PointsMaterial({
            color: 0xffd21f,
            size: GAME_ELECTRON_GLOW,
            sizeAttenuation: false,
            map: glowTexture(),
            transparent: true,
            depthWrite: false
        });

        this.electronGlow = new THREE.Points(glows, halo);
        this.electronGlow.frustumCulled = false;
        this.electronGlow.name = 'chipGameGlow';

        //A round is a streak along its way; a missile, a white body with a flame behind it.
        const streak = new THREE.BoxGeometry(1, 1, 1);
        const tracer = new THREE.MeshBasicMaterial({ color: GAME_ROUND_COLOR });

        this.bulletMesh = new THREE.InstancedMesh(streak, tracer, GAME_SHOTS_DRAWN);
        this.bulletMesh.count = 0;
        this.bulletMesh.frustumCulled = false;
        this.bulletMesh.name = 'chipGameRounds';

        const rocket = new THREE.CylinderGeometry(0.035, 0.035, 0.42, 10);

        rocket.rotateZ(-Math.PI / 2);

        const casing = new THREE.MeshPhongMaterial({ color: 0xf2f2f2, shininess: 80 });

        this.missileMesh = new THREE.InstancedMesh(rocket, casing, GAME_SHOTS_DRAWN);
        this.missileMesh.count = 0;
        this.missileMesh.frustumCulled = false;
        this.missileMesh.name = 'chipGameMissiles';

        const trail = new THREE.ConeGeometry(0.04, 0.35, 10, 1, true);

        trail.rotateZ(Math.PI / 2);
        trail.translate(-0.385, 0, 0);

        const exhaust = new THREE.MeshBasicMaterial({ color: 0xff9a3c, transparent: true, opacity: 0.85, depthWrite: false, side: THREE.DoubleSide });

        this.exhaustMesh = new THREE.InstancedMesh(trail, exhaust, GAME_SHOTS_DRAWN);
        this.exhaustMesh.count = 0;
        this.exhaustMesh.frustumCulled = false;

        //
        //**And a flare behind each missile**, a dot of the same size wherever it is, the way an electron's glow is: a
        //missile is a few hundredths of the view across a car width off, and was gone from sight as soon as it left.
        //
        const flares = new THREE.BufferGeometry();

        flares.setAttribute('position', new THREE.BufferAttribute(new Float32Array(GAME_SHOTS_DRAWN * 3), 3));
        flares.setDrawRange(0, 0);

        const flare = new THREE.PointsMaterial({
            color: 0xff8a1f,
            size: GAME_MISSILE_FLARE,
            sizeAttenuation: false,
            map: glowTexture(),
            transparent: true,
            depthWrite: false
        });

        this.missileGlow = new THREE.Points(flares, flare);
        this.missileGlow.frustumCulled = false;

        scene.add(this.electronMesh);
        scene.add(this.electronGlow);
        scene.add(this.bulletMesh);
        scene.add(this.missileMesh);
        scene.add(this.exhaustMesh);
        scene.add(this.missileGlow);

        //A sphere each that grows and fades, taken by whatever goes out next once it has.
        const puff = new THREE.SphereGeometry(1, 16, 12);

        this.flashes = [];

        for (let i = 0; i < GAME_FLASHES; i++) {
            const mesh = new THREE.Mesh(puff, new THREE.MeshBasicMaterial({ transparent: true, depthWrite: false }));

            mesh.visible = false;
            scene.add(mesh);

            this.flashes.push({ mesh: mesh, age: 0, life: 0, size: 0 });
        }

        this.fieldParts = [ball, charge, glows, halo, halo.map, streak, tracer, rocket, casing, trail, exhaust, flares, flare, flare.map, puff];
        this.hotParts.push(heated(charge.emissive), heated(halo.color), heated(tracer.color), heated(exhaust.color), heated(flare.color));
    }

    disposeCar() {
        if (this.car != null)
            this.viewer.scene.remove(this.car);

        if (this.aircraft != null)
            this.viewer.scene.remove(this.aircraft);

        if (this.jet != null)
            this.viewer.scene.remove(this.jet);

        if (this.shadow != null) {
            this.viewer.scene.remove(this.shadow);
            this.shadow.geometry.dispose();
            this.shadow.material.dispose();
        }

        for (const part of this.carParts || [])
            part.dispose();

        this.disposeField();

        this.car = null;
        this.aircraft = null;
        this.propeller = null;
        this.jet = null;
        this.flame = null;
        this.shadow = null;
        this.carParts = [];
        this.hotParts = [];
    }

    ///Takes the electrons, the shots and the flashes out of the scene, and lets go of what they were drawn with.
    disposeField() {
        const scene = this.viewer.scene;

        for (const drawn of [this.electronMesh, this.electronGlow, this.bulletMesh, this.missileMesh, this.exhaustMesh, this.missileGlow]) {
            if (drawn == null)
                continue;

            scene.remove(drawn);

            if (typeof drawn.dispose === 'function')
                drawn.dispose();
        }

        for (const flash of this.flashes) {
            scene.remove(flash.mesh);
            flash.mesh.material.dispose();
        }

        for (const part of this.fieldParts || [])
            part.dispose();

        this.electronMesh = null;
        this.electronGlow = null;
        this.bulletMesh = null;
        this.missileMesh = null;
        this.exhaustMesh = null;
        this.missileGlow = null;
        this.flashes = [];
        this.fieldParts = [];
    }

    ///
    ///The car where the physics has it, in the scene's axes.
    ///
    ///Layout x is scene X, layout y is scene Z, and height up the stack is scene Y plus however far the chip
    ///group has been lifted to center the stack.
    ///
    placeCar(dt = 0) {
        const player = this.player;
        const world = this.world;
        const lift = this.viewer.chipObjectsGroup.position.y;

        //The size the panel set, which the physics already drives with - see chipGame.carWidth.
        const width = window.chipGame.carWidth(world, player);

        this.carAt.set(player.x, player.z + lift, player.y);

        //
        //**The plane flies as the physics has it**: its heading, its nose and its bank, all three - see buildPlane for
        //the order they go on in - and its propeller round with its speed, from a tick-over to a blur. The jet the
        //same, with its flame for a propeller.
        //
        const flown = this.vehicleModel();

        if (player.plane && flown != null && flown !== this.car) {
            flown.position.copy(this.carAt);
            flown.scale.setScalar(width);
            flown.rotation.set(player.roll, -player.heading, player.pitch);

            if (player.jet)
                this.burn();
            else {
                this.propellerTurn += Math.min(54, 9 + ((player.speed / width) * 3)) * dt;
                this.propeller.rotation.x = this.propellerTurn;
            }
        }

        this.car.position.copy(this.carAt);
        this.car.scale.setScalar(width);

        //Its local +X turned onto the heading. A turn about Y by -heading takes (1, 0, 0) to
        //(cos heading, 0, sin heading), which is the heading put into the scene's X and Z.
        this.car.rotation.y = -player.heading;

        //
        //Nose up on the way up and down on the way down, a little - and well up on the side of a wall it is climbing. The
        //jump is the car's own, times its size - see chipGame.carWidth.
        //
        let pitch = Math.max(-0.35, Math.min(0.35, (player.vz / (world.jumpSpeed * player.size)) * 0.3));

        if (player.climbing)
            pitch = GAME_CLIMB_PITCH;

        this.carPitch += (pitch - this.carPitch) * (1 - Math.exp(-GAME_PITCH_EASE * dt));
        this.car.rotation.z = this.carPitch;

        //
        //Looked for and lifted off the ground by a share of the car rather than of the layout, so a car made small in
        //the panel does not have its shadow standing over it. At the layout's own size the two are the same.
        //
        const ground = window.chipGame.groundBelow(world, player.x, player.y, player.z + (width * 0.01));

        if (!isFinite(ground)) {
            this.shadow.visible = false;

            return;
        }

        //Smaller and fainter the higher the car is over it - over three of its jumps, or ten of the plane's widths.
        let fades = world.jumpHeight * player.size * 3;

        if (player.plane)
            fades = width * 10;

        const above = Math.max(0, player.z - ground);
        const near = Math.max(0.35, 1 - (above / fades));

        this.shadow.visible = true;
        this.shadow.position.set(player.x, ground + lift + (width * 0.02), player.y);
        this.shadow.scale.setScalar(width * near);
        this.shadow.material.opacity = 0.4 * near;
    }

    ///
    ///The jet's flame: as long as the throttle has it going - a share of its top speed - and with the afterburner lit,
    ///two to six times that by the throttle, and fatter. A little longer or shorter every frame, which is what
    ///makes it a flame.
    ///
    burn() {
        if (this.flame == null)
            return;

        const player = this.player;
        const share = Math.min(1, player.speed / window.chipGame.planeTop(this.world, player));
        const flicker = 0.85 + (Math.random() * 0.3);

        let length = (0.3 + (0.5 * share)) * flicker;
        let girth = 0.8 + (0.2 * share);

        if (this.burning) {
            length = 1.8 * flicker;
            girth = 1.4;
        }

        this.flame.scale.set(length, girth, girth);
    }

    ///
    ///The electrons, the shots and the flashes where the physics and the clock have them - see buildField. In the
    ///scene's axes, as the car is: layout y is the scene's Z, and up the stack is its Y plus the chip group's lift.
    ///
    ///**An electron bobs**, a little up and down at a pace of its own, which is the one thing said about it that the
    ///physics does not know - it is taken where it floats.
    ///
    placeField(dt) {
        if (this.electronMesh == null)
            return;

        const world = this.world;
        const player = this.player;
        const width = window.chipGame.carWidth(world, player);
        const lift = this.viewer.chipObjectsGroup.position.y;
        const now = performance.now() / 1000;
        const glows = this.electronGlow.geometry.attributes.position;
        const electrons = world.electrons;
        const count = Math.min(electrons.length, GAME_ELECTRONS_DRAWN);

        this.placedTurn.identity();
        this.placedSize.setScalar(window.chipGame.TUNING.electronRadius * width);

        for (let i = 0; i < count; i++) {
            const one = electrons[i];
            const bob = Math.sin((now * GAME_ELECTRON_BOB_RATE) + one.phase) * GAME_ELECTRON_BOB * width;
            const height = window.chipGame.electronZ(world, player, one) + bob + lift;

            this.placedAt.set(one.x, height, one.y);
            this.placing.compose(this.placedAt, this.placedTurn, this.placedSize);
            this.electronMesh.setMatrixAt(i, this.placing);

            glows.setXYZ(i, one.x, height, one.y);
        }

        this.electronMesh.count = count;
        this.electronMesh.instanceMatrix.needsUpdate = true;
        this.electronGlow.geometry.setDrawRange(0, count);
        glows.needsUpdate = true;

        this.placeShots(width, lift);
        this.fadeFlashes(dt, width, lift);
    }

    ///
    ///Every round and missile in flight, pointed along its way: a round a streak a little over a car width long, and a
    ///missile with its flare half a car width behind it.
    ///
    placeShots(width, lift) {
        const flares = this.missileGlow.geometry.attributes.position;

        let rounds = 0;
        let missiles = 0;

        for (const shot of this.world.shots) {
            this.pointing.set(shot.dx, shot.dz, shot.dy);
            this.placedTurn.setFromUnitVectors(GAME_NOSE_AXIS, this.pointing);
            this.placedAt.set(shot.x, shot.z + lift, shot.y);

            if (shot.kind === 'missile') {
                if (missiles >= GAME_SHOTS_DRAWN)
                    continue;

                this.placedSize.setScalar(width);
                this.placing.compose(this.placedAt, this.placedTurn, this.placedSize);
                this.missileMesh.setMatrixAt(missiles, this.placing);
                this.exhaustMesh.setMatrixAt(missiles, this.placing);

                flares.setXYZ(missiles, shot.x - (shot.dx * width * 0.5), shot.z + lift - (shot.dz * width * 0.5), shot.y - (shot.dy * width * 0.5));

                missiles++;

                continue;
            }

            if (rounds >= GAME_SHOTS_DRAWN)
                continue;

            this.placedSize.set(width * 1.2, width * 0.05, width * 0.05);
            this.placing.compose(this.placedAt, this.placedTurn, this.placedSize);
            this.bulletMesh.setMatrixAt(rounds, this.placing);

            rounds++;
        }

        this.bulletMesh.count = rounds;
        this.bulletMesh.instanceMatrix.needsUpdate = true;
        this.missileMesh.count = missiles;
        this.missileMesh.instanceMatrix.needsUpdate = true;
        this.exhaustMesh.count = missiles;
        this.exhaustMesh.instanceMatrix.needsUpdate = true;
        this.missileGlow.geometry.setDrawRange(0, missiles);
        flares.needsUpdate = true;
    }

    ///
    ///Something going out at `at` in the layout - an electron taken, a missile blowing up: a sphere of `color` that grows
    ///to `size` car widths and fades over `life` seconds. Taken from the pool, or the one furthest gone when none is free.
    ///
    flash(at, color, size, life) {
        if (this.flashes.length === 0)
            return;

        let chosen = this.flashes[0];

        for (const one of this.flashes) {
            if (!one.mesh.visible) {
                chosen = one;

                break;
            }

            if (one.age / one.life > chosen.age / chosen.life)
                chosen = one;
        }

        chosen.x = at.x;
        chosen.y = at.y;
        chosen.z = at.z;
        chosen.age = 0;
        chosen.life = life;
        chosen.size = size;
        chosen.mesh.material.color.setHex(color).multiplyScalar(this.hotness());
        chosen.mesh.visible = true;
    }

    ///Every flash a frame further on: grown and faded, and gone once its time is up.
    fadeFlashes(dt, width, lift) {
        for (const one of this.flashes) {
            if (!one.mesh.visible)
                continue;

            one.age += dt;

            if (one.age >= one.life) {
                one.mesh.visible = false;

                continue;
            }

            const done = one.age / one.life;

            //Out fast and then slower, and solid for most of its time before it goes - a flash rather than a fade.
            one.mesh.position.set(one.x, one.z + lift, one.y);
            one.mesh.scale.setScalar(one.size * width * (0.3 + (0.7 * Math.sqrt(done))));
            one.mesh.material.opacity = 0.95 * (1 - (done * done));
        }
    }

    ///
    ///The camera behind the car and above it, looking a little ahead of it.
    ///
    ///**Fixed to it in distance, eased in everything else.** It is put the same number of the car's widths behind
    ///and above it every frame, so speeding up, braking or a new size never takes it nearer or further. What is
    ///eased is which way round the car it is - so a turn swings the view round after the car rather than snapping
    ///it - how high it frames the car, so a landing does not jolt it, and the wheel's zoom. `immediately` puts it
    ///there in one go, for the first frame.
    ///
    ///It used to ease its whole position towards that spot, which left it behind by the car's speed over the easing
    ///rate: at top speed it sat 6.7 car widths off rather than 5.5, and nearly 12 off a car shrunk to 0.2 - the view
    ///falling back as the car pulled away, and the car shrinking on screen while a slider changed its size.
    ///
    ///**Pulled in when there is a layer in the way**, which is most of the time the car is under another one:
    ///where the camera wants to be is then inside the slab overhead, and the whole view is that slab's color.
    ///It snaps in, since a frame spent inside a layer is a frame of nothing, and eases back out once the line
    ///to the car is clear again. See chipGame.sightLine.
    ///
    ///The near and far planes go with it. The view's own are in layout units - one and fifty thousand - and a
    ///camera a few car widths from the car is closer than one unit on a layout drawn in microns.
    ///
    ///**The mouse takes it round the car** - see onPointerMove. Where it would be is turned about the car by the
    ///mouse's yaw and tipped by its pitch, the same distance off; looked at from the side or the front, the camera
    ///looks at the car rather than ahead of it. Left alone for GAME_LOOK_HOLD, both ease back to nothing, which is
    ///behind the car again.
    ///
    ///Distances are the car's, so a car made smaller in the panel is framed the same.
    ///
    frameCamera(immediately, dt = 0) {
        const world = this.world;
        const player = this.player;
        const camera = this.viewer.camera;
        const width = window.chipGame.carWidth(world, player);
        const lift = this.viewer.chipObjectsGroup.position.y;

        const now = performance.now() / 1000;
        const looking = now - this.lookedAt < 0.15;

        //Round the shorter way, so a camera turned past half a circle comes back the near side rather than the far.
        this.lookYaw = Math.atan2(Math.sin(this.lookYaw), Math.cos(this.lookYaw));

        if (immediately) {
            this.lookYaw = 0;
            this.lookPitch = 0;
        }
        else if (now - this.lookedAt > GAME_LOOK_HOLD) {
            const back = 1 - Math.exp(-GAME_LOOK_RETURN * dt);

            this.lookYaw -= this.lookYaw * back;
            this.lookPitch -= this.lookPitch * back;
        }

        //
        //**First person is the driver's seat** - see placeEye. What the chase camera eases is left as it is, and put
        //behind the car in one go when third person comes back; see setView.
        //
        if (this.options.view === 'first') {
            this.placeEye();
            this.fitLens(immediately, width, Math.hypot(GAME_CAMERA_BACK, GAME_CAMERA_UP) * width);

            return;
        }

        //Behind the plane it turns with it, round a loop and all - see chasePlane.
        if (player.plane) {
            this.chasePlane(immediately, dt);

            return;
        }

        this.chased = false;

        //The swing, the height and the zoom, eased; the distance they are put at is not.
        if (immediately) {
            this.swungHeading = player.heading;
            this.swungPitch = this.lookPitch;
            this.followedZ = player.z;
            this.shownZoom = this.zoom;
        }
        else {
            //Stiffer while the mouse is moving it, so looking round is where the mouse puts it rather than after.
            let rate = GAME_CAMERA_FOLLOW;

            if (looking)
                rate = GAME_CAMERA_LOOKING;

            const swing = 1 - Math.exp(-rate * dt);
            const follow = 1 - Math.exp(-GAME_CAMERA_FOLLOW * dt);
            const turn = (player.heading + this.lookYaw) - this.swungHeading;

            this.swungHeading += Math.atan2(Math.sin(turn), Math.cos(turn)) * swing;
            this.swungPitch += (this.lookPitch - this.swungPitch) * swing;
            this.followedZ += (player.z - this.followedZ) * follow;
            this.shownZoom += (this.zoom - this.shownZoom) * follow;
        }

        const unit = width * this.shownZoom;

        //How far round from behind the car the camera has swung, which is what looking round is.
        const swungYaw = Math.atan2(Math.sin(this.swungHeading - player.heading), Math.cos(this.swungHeading - player.heading));

        const forwardX = Math.cos(player.heading);
        const forwardY = Math.sin(player.heading);

        //In the layout's axes, from just over the car to where the camera goes.
        const around = this.swungHeading + Math.PI;
        const distance = Math.hypot(GAME_CAMERA_BACK, GAME_CAMERA_UP) * unit;
        const rise = Math.atan2(GAME_CAMERA_UP, GAME_CAMERA_BACK) + this.swungPitch;

        //The car's own eye, for the line of sight, and the eased one the camera frames from.
        const eye = player.z + (width * 0.5);
        const framed = this.followedZ + (width * 0.5);

        const backX = player.x + (Math.cos(around) * Math.cos(rise) * distance);
        const backY = player.y + (Math.sin(around) * Math.cos(rise) * distance);
        const backZ = this.followedZ + (Math.sin(rise) * distance);

        //Ahead of the car when behind it, and at the car itself when round to its side or its front.
        const ahead = GAME_CAMERA_AHEAD * unit * Math.max(0, Math.cos(swungYaw));

        const clear = window.chipGame.sightLine(world, player.x, player.y, eye, backX, backY, backZ);

        if (immediately || clear < this.reach)
            this.reach = clear;
        else
            this.reach += (clear - this.reach) * (1 - Math.exp(-GAME_CAMERA_RETURN * dt));

        //And into the scene's: layout y is scene Z, and height is scene Y plus the chip group's lift.
        camera.position.set(
            player.x + ((backX - player.x) * this.reach),
            eye + ((backZ - eye) * this.reach) + lift,
            player.y + ((backY - player.y) * this.reach));

        //Along the ground ahead of the car, the scene's up being up.
        this.lookingAt.set(
            player.x + (forwardX * ahead),
            framed + lift,
            player.y + (forwardY * ahead));

        this.fitLens(immediately, width, distance);

        camera.up.set(0, 1, 0);
        camera.lookAt(this.lookingAt);
    }

    ///
    ///**Behind the plane, the camera turns with it** - the same number of its widths back along its nose and up from it as
    ///the camera keeps behind the car, looking past it along the nose - **round a loop and all**: under it looking up
    ///the climb, over the top with it on its back, and down the far side. Up, for the camera, is the plane's own up with
    ///its wings level, so a bank rolls the plane on screen rather than the world, which keeps the horizon level until the
    ///plane goes over. The mouse is the plane's stick here rather than a look round, so nothing else moves it, and the
    ///height is the plane's own rather than eased: a plane goes up and down smoothly, and lagged it would sink in the view
    ///through every dive.
    ///
    ///**Eased round after it as one turn** - heading, nose and which way is up together - the way a turn of the car swings
    ///the camera round after the car. Angle by angle would not do: the nose goes round past half a turn in a loop, and the
    ///plane said the right way up over the top - see chipGame.attitude - has every angle changed and is the same plane.
    ///Coming from behind the car, it eases round from where that camera had swung to.
    ///
    ///**It tipped with the nose as an angle**, 0.85 of it, and kept the horizon level: straight up there was no level
    ///horizon left to keep, and over the top the view would have turned itself inside out.
    ///
    chasePlane(immediately, dt) {
        const world = this.world;
        const player = this.player;
        const camera = this.viewer.camera;
        const width = window.chipGame.carWidth(world, player);
        const lift = this.viewer.chipObjectsGroup.position.y;
        const follow = 1 - Math.exp(-GAME_CAMERA_FOLLOW * dt);

        //The nose and the up with the wings level - see chipGame.axes - put into the scene's axes, where layout y is Z.
        const ch = Math.cos(player.heading);
        const sh = Math.sin(player.heading);
        const cp = Math.cos(player.pitch);
        const sp = Math.sin(player.pitch);

        this.chaseWanted.setFromRotationMatrix(this.chaseBasisOf(cp * ch, sp, cp * sh, -sp * ch, cp, -sp * sh));

        if (immediately) {
            this.chaseTurn.copy(this.chaseWanted);
            this.shownZoom = this.zoom;
        }
        else {
            if (!this.chased)
                this.chaseTurn.setFromRotationMatrix(this.chaseBasisOf(Math.cos(this.swungHeading), 0, Math.sin(this.swungHeading), 0, 1, 0));

            this.chaseTurn.slerp(this.chaseWanted, follow);
            this.shownZoom += (this.zoom - this.shownZoom) * follow;
        }

        this.chased = true;

        const unit = width * this.shownZoom;
        const distance = Math.hypot(GAME_CAMERA_BACK, GAME_CAMERA_UP) * unit;
        const at = this.chaseAt.set(player.x, player.z + lift, player.y);

        //Where the camera goes, the plane's eye it is pulled in towards, and where it looks - each in the turn, off the plane.
        this.chaseFrom.set(-GAME_CAMERA_BACK * unit, GAME_CAMERA_UP * unit, 0).applyQuaternion(this.chaseTurn).add(at);
        this.chaseEye.set(0, width * 0.5, 0).applyQuaternion(this.chaseTurn).add(at);
        this.lookingAt.set(GAME_CAMERA_AHEAD * unit, width * 0.5, 0).applyQuaternion(this.chaseTurn).add(at);

        const eye = this.chaseEye;
        const from = this.chaseFrom;
        const clear = window.chipGame.sightLine(world, eye.x, eye.z, eye.y - lift, from.x, from.z, from.y - lift);

        if (immediately || clear < this.reach)
            this.reach = clear;
        else
            this.reach += (clear - this.reach) * (1 - Math.exp(-GAME_CAMERA_RETURN * dt));

        camera.position.copy(from).sub(eye).multiplyScalar(this.reach).add(eye);
        camera.up.set(0, 1, 0).applyQuaternion(this.chaseTurn);

        this.fitLens(immediately, width, distance);

        camera.lookAt(this.lookingAt);

        //Where the car's camera takes over from, if the car is next: the heading this one has swung to, at the plane's height.
        const nose = this.chaseFrom.set(1, 0, 0).applyQuaternion(this.chaseTurn);

        if (Math.hypot(nose.x, nose.z) > 1e-6)
            this.swungHeading = Math.atan2(nose.z, nose.x);

        this.swungPitch = 0;
        this.followedZ = player.z;
    }

    ///
    ///A turn whose +X is `ahead` and +Y `up` - unit and square to each other, in the scene's axes - with +Z across them, as
    ///a matrix the turn can be read off. Held rather than made, for the reason the axis indicator's vector is.
    ///
    chaseBasisOf(aheadX, aheadY, aheadZ, upX, upY, upZ) {
        this.chaseAhead.set(aheadX, aheadY, aheadZ);
        this.chaseUp.set(upX, upY, upZ);
        this.chaseSide.crossVectors(this.chaseAhead, this.chaseUp);

        return this.chaseBasis.makeBasis(this.chaseAhead, this.chaseUp, this.chaseSide);
    }

    ///
    ///First person: the camera at the driver's eyes - each model's userData.eye - looking the way the car or the plane
    ///points, and turned with it all three ways, so a bank tips the world rather than the plane. In the car the mouse
    ///turns the head, the way it takes the chase camera round the car, and lets it come back the same way.
    ///
    ///The models are where placeCar put them this frame, which is before this.
    ///
    placeEye() {
        const camera = this.viewer.camera;
        const model = this.vehicleModel();

        if (model == null)
            return;

        model.updateMatrixWorld();

        this.eyeAt.copy(model.userData.eye);
        model.localToWorld(this.eyeAt);

        camera.position.copy(this.eyeAt);

        //
        //A camera looks down its own -Z: a quarter turn about Y has it look down the model's +X, which is the way every
        //model faces. The head is turned and tipped from there - right as the mouse goes right, down as it goes down.
        //
        this.eyeAngles.set(-this.lookPitch, -(Math.PI / 2) - this.lookYaw, 0, 'YXZ');
        this.eyeTurn.setFromEuler(this.eyeAngles);

        camera.quaternion.copy(model.quaternion).multiply(this.eyeTurn);
    }

    ///
    ///The camera's near plane, its far plane and the haze, for a camera `distance` off a car `width` wide.
    ///
    fitLens(immediately, width, distance) {
        const world = this.world;
        const camera = this.viewer.camera;

        //
        //**The near plane is the car's, not the layout's.** The camera is a few of the car's widths off it, so a car
        //made a tenth the size in the panel has the camera a tenth as far - inside a near plane set for the layout's
        //car, which cut it out of its own view. Kept to the car every frame, since the size can change mid-game; the
        //projection is only rebuilt when it moved.
        //
        const near = width * 0.05;

        //
        //**The far plane is the draw distance** - the view's, which the panel sets while the game runs; see
        //VIEW_OPTIONS - with the far side faded into the background over the last two fifths of it, so a layer comes
        //into view out of the haze rather than popping in at a line. All of it draws everything, past the chip's far
        //corner and with no haze, which is what the game starts at. A picture behind the scene has no one color to
        //fade into, so there the far side is cut instead.
        //
        //Never in front of the car: three times as far as the camera is from it, whatever the panel says, so a short
        //draw distance on a small chip with the wheel pulled out still has the car and what is round it in the clear.
        //
        const extent = Math.max(world.maxX - world.minX, world.maxY - world.minY);
        const drawDistance = this.viewer.view.drawDistance;

        let far = Math.max(this.cameraWas.far, extent * 4);
        let haze = null;

        if (drawDistance < GAME_DRAW_DISTANCES.most) {
            far = Math.max(extent * drawDistance, distance * 3);

            if (this.viewer.scene.background == null) {
                this.haze.near = far * GAME_HAZE_FROM;
                this.haze.far = far;

                haze = this.haze;
            }
        }

        //
        //**Under the neon, a haze** - see showNeon. Short of All with nothing behind the scene, the one above, into the
        //neon's violet rather than the gray, since the violet is what is behind the scene under it. Anywhere else a haze
        //thick by the square of the distance, half thick GAME_NEON_HAZE_WIDTHS car widths off, or GAME_NEON_HAZE_ABOVE
        //times as far as the camera is over the top of the stack: all but clear round the car, and over the far side -
        //in front of a picture too, which it leaves as it is.
        //
        if (this.options.neon) {
            this.haze.color.setHex(NEON_HAZE);

            //
            //**The panel's Haze reach** stretches it or draws it in, and at its far end takes it off; and unticked, **Thins
            //with height** keeps it as thick as it is down by the chip however high the camera goes. See GAME_HAZE_REACHES.
            //
            if (haze == null && this.options.hazeReach < GAME_HAZE_REACHES.most) {
                const bounds = this.viewer.layoutBounds;

                let above = 0;

                if (bounds != null && this.options.hazeClimbs)
                    above = Math.max(0, camera.position.y - bounds.max.y);

                const half = Math.max(width * GAME_NEON_HAZE_WIDTHS, above * GAME_NEON_HAZE_ABOVE) * (2 ** this.options.hazeReach);

                this.neonHaze.density = Math.sqrt(Math.LN2) / half;
                this.neonHaze.color.copy(this.neonViolet);

                //In front of a picture, the picture's own color round its horizon, a little violet - see NEON_HAZE_TINT.
                if (this.viewer.scene.background != null && this.viewer.backdropTone != null)
                    this.neonHaze.color.copy(this.viewer.backdropTone).multiplyScalar(NEON_BACKDROP).lerp(this.neonViolet, NEON_HAZE_TINT);

                haze = this.neonHaze;
            }
        }
        else
            this.haze.color.setHex(DESKTOP_CLEAR_COLOR);

        if (this.viewer.scene.fog !== haze)
            this.viewer.scene.fog = haze;

        if (immediately)
            camera.fov = GAME_FOV;

        if (immediately || camera.near !== near || camera.far !== far) {
            camera.near = near;
            camera.far = far;
            camera.updateProjectionMatrix();
        }
    }
}

///
///The chip game's sounds - the engines, the tires, the jump, the horn, the gun, the missiles and the electrons - made in
///Web Audio rather than played from files.
///
///**Nothing to fetch**, which keeps the app what it is offline: an engine is a saw and a square an octave under
///it, through a low-pass that opens with the revs and a wobble in its loudness that quickens with them - the firing
///of the cylinders, near enough. A squeal is noise through a narrow band-pass with a tone in the middle of it, and a
///jump is a blip. Everything moves towards where it is asked to be over a few hundredths of a second rather than
///jumping there, since a step in a sound is a click.
///
class GameSound {

    ///One, or null where the browser has no Web Audio or will not make a context.
    static make() {
        const Context = window.AudioContext || window.webkitAudioContext;

        if (Context == null)
            return null;

        try {
            return new GameSound(new Context());
        }
        catch {
            return null;
        }
    }

    constructor(context) {
        this.context = context;

        this.master = context.createGain();
        this.master.gain.value = 0;
        this.master.connect(context.destination);

        this.engineFilter = context.createBiquadFilter();
        this.engineFilter.type = 'lowpass';
        this.engineFilter.frequency.value = 500;
        this.engineFilter.Q.value = 1.2;

        //
        //**Half as loud as it started.** It was the loudest thing in the game by some way, over the tires and the
        //blip both. Its wobble halved with it - see below - which keeps the firing as deep against the engine as it was.
        //
        this.engine = context.createGain();
        this.engine.gain.value = 0.05;

        //The car's engine and the plane's, and silent for the jet - see update.
        this.voice = context.createGain();
        this.voice.gain.value = 1;

        this.engineFilter.connect(this.engine);
        this.engine.connect(this.voice);
        this.voice.connect(this.master);

        this.saw = context.createOscillator();
        this.saw.type = 'sawtooth';
        this.saw.frequency.value = 48;
        this.saw.connect(this.engineFilter);

        const under = context.createGain();

        under.gain.value = 0.5;

        this.square = context.createOscillator();
        this.square.type = 'square';
        this.square.frequency.value = 24;
        this.square.connect(under);
        under.connect(this.engineFilter);

        //Added to the engine's loudness rather than multiplied into it, which is what an AudioParam does with a signal.
        this.wobble = context.createOscillator();
        this.wobble.frequency.value = 14;

        const depth = context.createGain();

        depth.gain.value = 0.015;

        this.wobble.connect(depth);
        depth.connect(this.engine.gain);

        //A second of noise, looped. Long enough that the loop is not heard as one. Kept for the shots and the bangs too.
        const noise = context.createBuffer(1, context.sampleRate, context.sampleRate);
        const samples = noise.getChannelData(0);

        for (let i = 0; i < samples.length; i++)
            samples[i] = (Math.random() * 2) - 1;

        this.noise = noise;

        this.hiss = context.createBufferSource();
        this.hiss.buffer = noise;
        this.hiss.loop = true;

        //
        //**The tires are a squeak with a scrub under it.** The noise through a narrow band is the scrub, and on its
        //own it was more a hiss than a squeal. The squeak is a tone in the middle of that band, shaken a few percent
        //either way twenty-odd times a second, which gives it the edge rubber has. Both go through one loudness.
        //
        this.squealFilter = context.createBiquadFilter();
        this.squealFilter.type = 'bandpass';
        this.squealFilter.frequency.value = 2400;
        this.squealFilter.Q.value = 14;

        this.squeak = context.createOscillator();
        this.squeak.type = 'triangle';
        this.squeak.frequency.value = 2400;

        const squeakLevel = context.createGain();

        squeakLevel.gain.value = 0.15;

        this.shake = context.createOscillator();
        this.shake.frequency.value = 23;

        const shakeDepth = context.createGain();

        shakeDepth.gain.value = 60;

        this.shake.connect(shakeDepth);
        shakeDepth.connect(this.squeak.frequency);

        this.squeal = context.createGain();
        this.squeal.gain.value = 0;

        this.hiss.connect(this.squealFilter);
        this.squealFilter.connect(this.squeal);
        this.squeak.connect(squeakLevel);
        squeakLevel.connect(this.squeal);
        this.squeal.connect(this.master);

        //
        //**The horn is two notes a third apart**, G and B above middle C, on saws through a low-pass with a little bite:
        //a car's, near enough. Always running and held silent, so a press is heard the moment it is made - see honk.
        //
        this.hornFilter = context.createBiquadFilter();
        this.hornFilter.type = 'lowpass';
        this.hornFilter.frequency.value = 2200;
        this.hornFilter.Q.value = 3;

        this.horn = context.createGain();
        this.horn.gain.value = 0;

        this.hornLow = context.createOscillator();
        this.hornLow.type = 'sawtooth';
        this.hornLow.frequency.value = 392;

        this.hornHigh = context.createOscillator();
        this.hornHigh.type = 'sawtooth';
        this.hornHigh.frequency.value = 494;

        this.hornLow.connect(this.hornFilter);
        this.hornHigh.connect(this.hornFilter);
        this.hornFilter.connect(this.horn);
        this.horn.connect(this.master);

        this.honking = false;
        this.honkedAt = 0;

        //
        //**The jet's roar is the same noise through a low-pass** - a rumble rather than a hiss - louder with the throttle,
        //and louder and higher again with the afterburner lit. Silent for the car and the plane.
        //
        this.roarFilter = context.createBiquadFilter();
        this.roarFilter.type = 'lowpass';
        this.roarFilter.frequency.value = 500;
        this.roarFilter.Q.value = 0.7;

        this.roar = context.createGain();
        this.roar.gain.value = 0;

        this.hiss.connect(this.roarFilter);
        this.roarFilter.connect(this.roar);
        this.roar.connect(this.master);

        //
        //**The jet's engine is a turbine's whine over that roar**, rather than the car's engine pitched up - which was
        //heard as the car still running under the jet, the firing of its cylinders and all. Two tones an octave apart,
        //the upper a shade sharp so the pair shimmers, rising with the speed. Silent for the car and the plane.
        //
        this.whine = context.createGain();
        this.whine.gain.value = 0;

        this.whineLow = context.createOscillator();
        this.whineLow.type = 'sine';
        this.whineLow.frequency.value = 900;

        this.whineHigh = context.createOscillator();
        this.whineHigh.type = 'triangle';
        this.whineHigh.frequency.value = 1810;

        const whineUpper = context.createGain();

        whineUpper.gain.value = 0.35;

        this.whineLow.connect(this.whine);
        this.whineHigh.connect(whineUpper);
        whineUpper.connect(this.whine);
        this.whine.connect(this.master);

        //
        //**The jet's gun is an A-10's**: the GAU-8's BRRRT, a tearing growl rather than a run of cracks, its rounds coming
        //too close together to be heard one by one - see GAME_CANNON_RATE. **A saw at that rate is the growl**, driven
        //into a soft clip and through a low-pass; **the noise chopped by the same saw is its edge**, loud as each round
        //goes and dying away to the next. Always running and held silent, as the horn is, so the trigger is heard the
        //moment it is pulled - see fire.
        //
        this.cannon = context.createGain();
        this.cannon.gain.value = 0;

        this.cannonRate = context.createOscillator();
        this.cannonRate.type = 'sawtooth';
        this.cannonRate.frequency.value = GAME_CANNON_RATE * GAME_CANNON_SPIN;

        const drive = context.createGain();
        const clip = context.createWaveShaper();
        const growl = context.createBiquadFilter();

        drive.gain.value = 2.5;
        clip.curve = softClip(1024);
        growl.type = 'lowpass';
        growl.frequency.value = 2000;
        growl.Q.value = 0.9;

        this.cannonRate.connect(drive);
        drive.connect(clip);
        clip.connect(growl);
        growl.connect(this.cannon);

        //The saw ramps up and drops, so taken off a half its edge is a jump up at every round and a fall to the next.
        const tear = context.createBiquadFilter();
        const chop = context.createGain();
        const chopDepth = context.createGain();

        tear.type = 'bandpass';
        tear.frequency.value = 2000;
        tear.Q.value = 0.6;
        chop.gain.value = 0.5;
        chopDepth.gain.value = -0.5;

        this.hiss.connect(tear);
        tear.connect(chop);
        this.cannonRate.connect(chopDepth);
        chopDepth.connect(chop.gain);
        chop.connect(this.cannon);

        this.cannon.connect(this.master);

        this.firing = false;
        this.firedAt = 0;

        this.sources = [this.saw, this.square, this.wobble, this.hiss, this.squeak, this.shake, this.hornLow, this.hornHigh, this.whineLow, this.whineHigh, this.cannonRate];

        for (const source of this.sources)
            source.start();

        //The music, through the same master, so the sound switch silences it with the rest - see GameMusic.
        this.music = new GameMusic(context, this.master, noise);
    }

    ///This frame's music: on or off - the sound and the panel's Music both - and whose tune, 'car', 'plane' or 'jet'.
    playMusic(on, tune) {
        this.music.update(on, tune);
    }

    ///Lets the sound start, if the browser held it back until somebody pressed something. A key is that press.
    wake() {
        if (this.context.state === 'suspended')
            this.context.resume().catch(() => { });
    }

    ///
    ///This frame's sound: whether it is on at all, the revs as a share of the top speed (more on a boost), how
    ///hard the throttle is pressed, how loud the tires are, from nothing to one - and whose it is, 'car', 'plane' or
    ///'jet', with how lit the jet's afterburner is, from nothing to one.
    ///
    ///**The plane's engine is the car's, higher and brighter**: half again the pitch and the filter opened further,
    ///which is a propeller's buzz rather than a car's growl. **The jet's is its own**: a turbine's whine over a roar,
    ///which the afterburner makes louder and harsher, with the car's engine silent. It was that engine higher
    ///again and wide open, under the roar - and heard as the car and the jet both playing at once.
    ///
    update(on, revs, throttle, squeal, kind, afterburner) {
        const now = this.context.currentTime;

        let pitch = 48 + (150 * revs);
        let open = 450 + (2000 * revs) + (500 * throttle);
        let voice = 1;
        let whine = 0;
        let whinePitch = 900;
        let roar = 0;
        let rumble = 500;

        if (kind === 'plane') {
            pitch *= 1.5;
            open += 900;
        }
        else if (kind === 'jet') {
            voice = 0;
            whine = 0.026 + (0.012 * throttle) + (0.01 * Math.min(revs, 1.5));
            whinePitch = 700 + (900 * Math.min(revs, 1.75));
            roar = 0.24 + (0.12 * throttle) + (0.35 * afterburner);
            rumble = 450 + (500 * Math.min(revs, 1)) + (900 * afterburner);
        }

        this.voice.gain.setTargetAtTime(voice, now, 0.1);
        this.whine.gain.setTargetAtTime(whine, now, 0.1);
        this.whineLow.frequency.setTargetAtTime(whinePitch, now, 0.15);
        this.whineHigh.frequency.setTargetAtTime(whinePitch * 2.012, now, 0.15);
        this.roar.gain.setTargetAtTime(roar, now, 0.08);
        this.roarFilter.frequency.setTargetAtTime(rumble, now, 0.1);

        let loud = 0;

        if (on)
            loud = 0.55;

        this.master.gain.setTargetAtTime(loud, now, 0.05);

        this.saw.frequency.setTargetAtTime(pitch, now, 0.06);
        this.square.frequency.setTargetAtTime(pitch / 2, now, 0.06);
        this.engineFilter.frequency.setTargetAtTime(open, now, 0.08);
        this.engine.gain.setTargetAtTime(0.04 + (0.03 * throttle) + (0.02 * Math.min(revs, 1)), now, 0.08);
        this.wobble.frequency.setTargetAtTime(14 + (46 * revs), now, 0.1);

        const squealPitch = 2200 + (900 * squeal);

        this.squeal.gain.setTargetAtTime(0.25 * squeal, now, 0.04);
        this.squealFilter.frequency.setTargetAtTime(squealPitch, now, 0.05);
        this.squeak.frequency.setTargetAtTime(squealPitch, now, 0.05);
    }

    ///
    ///A jump's blip: a square note sliding up an octave as it dies away, about a tenth of a second long - the sound a
    ///jump has made in games for as long as games have had jumps. One in the air starts a fourth higher, so a stack
    ///climbed a press at a time is heard going up.
    ///
    ///Made for each jump and let go of once it has played: a source that has stopped cannot be started again, and
    ///one this short costs nothing to make. Through the master like everything else, so the sound switch silences
    ///it too.
    ///
    blip(inTheAir) {
        const context = this.context;
        const now = context.currentTime;

        let from = 520;

        if (inTheAir)
            from *= 4 / 3;

        const tone = context.createOscillator();
        const level = context.createGain();

        tone.type = 'square';
        tone.frequency.setValueAtTime(from, now);
        tone.frequency.exponentialRampToValueAtTime(from * 2, now + 0.08);

        level.gain.setValueAtTime(0, now);
        level.gain.linearRampToValueAtTime(0.1, now + 0.005);
        level.gain.exponentialRampToValueAtTime(0.0001, now + 0.12);

        tone.connect(level);
        level.connect(this.master);

        tone.onended = () => level.disconnect();

        tone.start(now);
        tone.stop(now + 0.13);
    }

    ///
    ///The horn on or off: up in a hundredth of a second and down in two, and on for an eighth of a second at least, so a
    ///tap is a toot rather than a click. Asked every frame, and only changed when the answer is.
    ///
    honk(on) {
        if (on === this.honking)
            return;

        const now = this.context.currentTime;

        this.honking = on;

        if (on) {
            this.honkedAt = now;
            this.horn.gain.cancelScheduledValues(now);
            this.horn.gain.setTargetAtTime(0.09, now, 0.01);

            return;
        }

        this.horn.gain.setTargetAtTime(0, Math.max(now, this.honkedAt + 0.12), 0.02);
    }

    ///
    ///The jet's gun on or off, for as long as its trigger is held - see the constructor. **The barrels spin up** as it is
    ///pulled, from GAME_CANNON_SPIN of their rate to all of it over a few tenths of a second, the growl rising with them;
    ///**and down** as it is let go of, falling as it dies away, with a low rumble after it, the way the A-10's burst ends.
    ///A tenth of a second at least, so a tap is a short burst rather than a click. Asked every frame, and only changed
    ///when the answer is, as the horn is.
    ///
    fire(on) {
        if (on === this.firing)
            return;

        const now = this.context.currentTime;
        const rate = this.cannonRate.frequency;

        this.firing = on;

        if (on) {
            this.firedAt = now;

            rate.cancelScheduledValues(now);
            rate.setValueAtTime(GAME_CANNON_RATE * GAME_CANNON_SPIN, now);
            rate.setTargetAtTime(GAME_CANNON_RATE, now, 0.12);

            //A few decibels over the jet's engine flat out, weighted the way an ear hears - the plane's cracks are ten under it.
            this.cannon.gain.cancelScheduledValues(now);
            this.cannon.gain.setTargetAtTime(0.35, now, 0.008);

            return;
        }

        const stop = Math.max(now, this.firedAt + 0.1);
        const low = this.context.createBiquadFilter();
        const level = this.context.createGain();

        rate.setTargetAtTime(GAME_CANNON_RATE * GAME_CANNON_SPIN, stop, 0.1);
        this.cannon.gain.setTargetAtTime(0, stop, 0.035);

        low.type = 'lowpass';
        low.frequency.setValueAtTime(380, stop);
        low.frequency.exponentialRampToValueAtTime(90, stop + 0.45);

        level.gain.setValueAtTime(0.0001, stop);
        level.gain.exponentialRampToValueAtTime(0.14, stop + 0.03);
        level.gain.exponentialRampToValueAtTime(0.001, stop + 0.5);

        this.burst(low, level, stop, 0.55);
    }

    ///
    ///One note, made for the sound that wants it and let go of once played, the way the blip is: `type` from `from` to
    ///`to` hertz over `length` seconds starting at `start`, at `loud`, dying away.
    ///
    tone(type, from, to, length, loud, start) {
        const context = this.context;
        const note = context.createOscillator();
        const level = context.createGain();

        note.type = type;
        note.frequency.setValueAtTime(from, start);
        note.frequency.exponentialRampToValueAtTime(to, start + length);

        level.gain.setValueAtTime(0, start);
        level.gain.linearRampToValueAtTime(loud, start + 0.005);
        level.gain.exponentialRampToValueAtTime(0.0001, start + length);

        note.connect(level);
        level.connect(this.master);

        note.onended = () => level.disconnect();

        note.start(start);
        note.stop(start + length + 0.01);
    }

    ///
    ///The noise again, `length` seconds of it from somewhere in its second, through `filter` and `level` - a shot, a
    ///missile away, a bang. Let go of once played, like a note.
    ///
    burst(filter, level, start, length) {
        const source = this.context.createBufferSource();

        source.buffer = this.noise;

        source.connect(filter);
        filter.connect(level);
        level.connect(this.master);

        source.onended = () => level.disconnect();

        source.start(start, Math.random() * Math.max(0, this.noise.duration - length), length);
    }

    ///An electron picked up: two quick bright notes, a fifth apart and going up.
    chime() {
        const now = this.context.currentTime;

        this.tone('sine', 1319, 1319, 0.12, 0.12, now);
        this.tone('sine', 1976, 1976, 0.18, 0.12, now + 0.07);
    }

    ///An electron shot down: a square zap falling a couple of octaves.
    zap() {
        this.tone('square', 1400, 220, 0.16, 0.07, this.context.currentTime);
    }

    ///A press of Space with the jumps run dry: a low, dull note going nowhere.
    refuse() {
        this.tone('triangle', 160, 90, 0.14, 0.18, this.context.currentTime);
    }

    ///The jumps full again after running dry: one soft high note.
    ding() {
        this.tone('sine', 1568, 1568, 0.35, 0.08, this.context.currentTime);
    }

    ///
    ///A round from the plane's gun: a crack of noise through a band in the middle, a sixteenth of a second. The jet's gun
    ///is a sound of its own - see fire.
    ///
    gun() {
        const context = this.context;
        const now = context.currentTime;
        const band = context.createBiquadFilter();
        const level = context.createGain();

        band.type = 'bandpass';
        band.frequency.value = 1800;
        band.Q.value = 0.8;

        level.gain.setValueAtTime(0.25, now);
        level.gain.exponentialRampToValueAtTime(0.001, now + 0.06);

        this.burst(band, level, now, 0.07);
    }

    ///
    ///A missile away: a crack as its motor lights, its roar - noise through a wide band that rises as it goes and falls
    ///away as it leaves - and a thump under it, a second long in all, which is as long as the noise is; see burst.
    ///
    ///**As loud at its height as the afterburner**, weighted the way an ear hears: eight or nine decibels over the jet's
    ///engine flat out. It was a narrow band sweeping up from low, at its loudest while the band was still down where
    ///little of the noise is and an ear hears little of it, and ten decibels under the engine - lost under it.
    ///
    whoosh() {
        const context = this.context;
        const now = context.currentTime;
        const crack = context.createBiquadFilter();
        const crackLevel = context.createGain();

        crack.type = 'bandpass';
        crack.frequency.value = 1800;
        crack.Q.value = 0.6;

        crackLevel.gain.setValueAtTime(0.8, now);
        crackLevel.gain.exponentialRampToValueAtTime(0.001, now + 0.09);

        this.burst(crack, crackLevel, now, 0.1);

        const band = context.createBiquadFilter();
        const level = context.createGain();

        band.type = 'bandpass';
        band.Q.value = 0.8;
        band.frequency.setValueAtTime(600, now);
        band.frequency.exponentialRampToValueAtTime(2500, now + 0.25);
        band.frequency.exponentialRampToValueAtTime(1000, now + 0.95);

        level.gain.setValueAtTime(0.0001, now);
        level.gain.exponentialRampToValueAtTime(0.9, now + 0.04);
        level.gain.exponentialRampToValueAtTime(0.54, now + 0.35);
        level.gain.exponentialRampToValueAtTime(0.001, now + 0.95);

        this.burst(band, level, now, 1);
        this.tone('sine', 120, 45, 0.25, 0.35, now);
    }

    ///
    ///A weapon overheated: a hiss of steam - the noise through a high-pass, dying away over most of a second - and a note
    ///falling two octaves under it, the way a machine winds down.
    ///
    overheat() {
        const context = this.context;
        const now = context.currentTime;
        const high = context.createBiquadFilter();
        const level = context.createGain();

        high.type = 'highpass';
        high.frequency.value = 3500;
        high.Q.value = 0.7;

        level.gain.setValueAtTime(0.0001, now);
        level.gain.exponentialRampToValueAtTime(0.3, now + 0.03);
        level.gain.exponentialRampToValueAtTime(0.001, now + 0.9);

        this.burst(high, level, now, 0.95);
        this.tone('square', 440, 110, 0.35, 0.06, now);
    }

    ///A missile blowing up: noise through a low-pass falling from a crack to a rumble, and a thump under it.
    boom() {
        const context = this.context;
        const now = context.currentTime;
        const low = context.createBiquadFilter();
        const level = context.createGain();

        low.type = 'lowpass';
        low.frequency.setValueAtTime(2400, now);
        low.frequency.exponentialRampToValueAtTime(90, now + 0.8);

        level.gain.setValueAtTime(0.6, now);
        level.gain.exponentialRampToValueAtTime(0.001, now + 0.9);

        this.burst(low, level, now, 0.95);
        this.tone('sine', 110, 35, 0.45, 0.5, now);
    }

    ///Silence, and the context let go of. A browser allows only a handful at once.
    close() {
        try {
            for (const source of this.sources)
                source.stop();

            this.context.close();
        }
        catch { }
    }
}

///
///The chip game's music: the tune of whatever is being played - see js/chipGameMusic.js, where they are written - played
///on instruments made here in Web Audio, the way the game's sounds are. **Synthwave in the car**: a pad of detuned saws,
///a bass, an arpeggio through an echo, a saw lead and drums with a room round the snare. **8-bit in the plane and the
///jet**: pulse waves, a triangle and drums out of noise - the voices of a game console of the eighties.
///
///**Played a little ahead of the clock**, GAME_MUSIC_AHEAD seconds of it at a time from each frame - see update - since
///a note has to be given its time before it comes, and a frame is not on time to the millisecond. A frame later than
///that leaves a gap rather than crowding the notes it missed into one moment.
///
///**A change of tune is a crossfade**, from the top of the new one: the car into the plane fades the synthwave out under
///the 8-bit coming in.
///
class GameMusic {

    constructor(context, destination, noise) {
        this.context = context;
        this.noise = noise;

        this.out = context.createGain();
        this.out.gain.value = 0;
        this.out.connect(destination);

        //
        //**A room**, for the synthwave's snare, pad and lead: a couple of seconds of noise dying away, different in each
        //ear, which is all a hall's echo is to a convolver.
        //
        this.room = context.createConvolver();
        this.room.buffer = roomImpulse(context, 2.2);

        const roomLevel = context.createGain();

        roomLevel.gain.value = 0.45;

        this.room.connect(roomLevel);
        roomLevel.connect(this.out);

        //**An echo** a dotted eighth behind, set to each tune's beat as it starts, darker each time round.
        this.echo = context.createDelay(1.5);

        const echoTone = context.createBiquadFilter();
        const echoBack = context.createGain();

        echoTone.type = 'lowpass';
        echoTone.frequency.value = 2200;
        echoBack.gain.value = 0.38;

        this.echo.connect(echoTone);
        echoTone.connect(echoBack);
        echoBack.connect(this.echo);
        echoTone.connect(this.out);

        //The pulse waves an 8-bit lead and its harmony are played on, by how much of each cycle is high.
        this.pulses = { pulse12: pulseWave(context, 0.125), pulse25: pulseWave(context, 0.25), pulse50: pulseWave(context, 0.5) };

        //Each tune as what to play on each sixteenth, worked out the first time it is played.
        this.compiled = {};

        //
        //Which tune is playing, or null; what it goes through - and what its pad and bass go through, which ducks under
        //each kick; and which of its sixteenths is next to be played, and when.
        //
        this.playing = null;
        this.tune = null;
        this.bus = null;
        this.duck = null;
        this.step = 0;
        this.at = 0;
    }

    ///
    ///The music, for a frame: `on` or not, and the tune of `name`, 'car', 'plane' or 'jet'. Anything due before
    ///GAME_MUSIC_AHEAD from now that has not been given its time yet is.
    ///
    update(on, name) {
        const now = this.context.currentTime;

        if (!on || window.chipGameMusic == null || window.chipGameMusic.TRACKS[name] == null) {
            if (this.playing != null)
                this.silence(now);

            return;
        }

        if (name !== this.playing)
            this.begin(name, now);

        const sixteenth = 15 / this.tune.bpm;

        //Behind the clock - the tab was away, or a frame took longer than the music runs ahead - so on from now.
        if (this.at < now) {
            const missed = Math.ceil((now - this.at) / sixteenth);

            this.step += missed;
            this.at += missed * sixteenth;
        }

        while (this.at < now + GAME_MUSIC_AHEAD) {
            for (const one of this.tune.steps[this.step % this.tune.steps.length])
                this.play(one, this.at, sixteenth);

            this.step++;
            this.at += sixteenth;
        }
    }

    ///A tune from its top, faded in over whatever was playing as that fades out.
    begin(name, now) {
        const context = this.context;

        this.fadeOut(now);

        if (this.compiled[name] == null)
            this.compiled[name] = window.chipGameMusic.compile(window.chipGameMusic.TRACKS[name]);

        this.playing = name;
        this.tune = this.compiled[name];

        this.bus = context.createGain();
        this.bus.gain.setValueAtTime(0, now);
        this.bus.gain.setTargetAtTime(1, now, 0.15);
        this.bus.connect(this.out);

        this.duck = context.createGain();
        this.duck.connect(this.bus);

        this.echo.delayTime.setValueAtTime(3 * (15 / this.tune.bpm), now);

        this.out.gain.setTargetAtTime(GAME_MUSIC_LOUDNESS, now, 0.1);

        this.step = 0;
        this.at = now + 0.05;
    }

    ///The music off: everything faded out, and nothing more played until it is on again - from the top.
    silence(now) {
        this.fadeOut(now);

        this.out.gain.setTargetAtTime(0, now, 0.08);

        this.playing = null;
        this.tune = null;
    }

    ///Whatever tune is playing faded out, and let go of once it has - the notes already given their time with it.
    fadeOut(now) {
        const bus = this.bus;

        if (bus == null)
            return;

        bus.gain.cancelScheduledValues(now);
        bus.gain.setTargetAtTime(0, now, 0.12);

        setTimeout(() => bus.disconnect(), 1500);

        this.bus = null;
        this.duck = null;
    }

    ///One thing to play, at `at`: a note or a chord on a voice, or a drum - see chipGameMusic.compile.
    play(one, at, sixteenth) {
        if (one.drum != null) {
            this.drum(one.voice, one.drum, at);

            return;
        }

        const length = one.length * sixteenth;

        for (const pitch of one.pitches) {
            const frequency = window.chipGameMusic.frequencyOf(pitch);

            if (one.voice === 'triangle')
                this.triangle(frequency, at, length, one.level);
            else if (this.pulses[one.voice] != null)
                this.pulse(this.pulses[one.voice], frequency, at, length, one.level, one.lead === true);
            else if (one.voice === 'pad')
                this.pad(frequency, at, length, one.level);
            else if (one.voice === 'bass')
                this.bass(frequency, at, length, one.level);
            else if (one.voice === 'pluck')
                this.pluck(frequency, at, one.level);
            else if (one.voice === 'saw')
                this.lead(frequency, at, length, one.level);
        }
    }

    ///An oscillator from `at` to a little after `until`, let go of once it has stopped - playing into `into`, if given.
    voice(at, until, into) {
        const tone = this.context.createOscillator();

        if (into != null)
            tone.connect(into);

        tone.start(at);
        tone.stop(until + 0.05);
        tone.onended = () => tone.disconnect();

        return tone;
    }

    ///A loudness that comes up in `attack` seconds, holds at `level` and dies away in `release` once the note is over.
    envelope(at, length, level, attack, release, into) {
        const loudness = this.context.createGain();

        loudness.gain.setValueAtTime(0, at);
        loudness.gain.linearRampToValueAtTime(level, at + attack);
        loudness.gain.setValueAtTime(level, Math.max(at + attack, at + length - release));
        loudness.gain.linearRampToValueAtTime(0, at + length);
        loudness.connect(into);

        setTimeout(() => loudness.disconnect(), ((at + length - this.context.currentTime) * 1000) + 500);

        return loudness;
    }

    //8-bit///////////////////////////////////////////////

    ///
    ///A note on a pulse wave, cut a little short, the way a chip tune is played. **A held one in the lead wavers** after
    ///its first moment - six times a second, a sixth of a semitone - as a game's lead always has.
    ///
    pulse(wave, frequency, at, length, level, lead) {
        const loudness = this.envelope(at, length * 0.9, 0.06 * level, 0.004, 0.02, this.bus);
        const tone = this.voice(at, at + length, loudness);

        tone.setPeriodicWave(wave);
        tone.frequency.setValueAtTime(frequency, at);

        if (!lead || length < 0.3)
            return;

        const depth = this.context.createGain();
        const wobble = this.voice(at, at + length, depth);

        wobble.frequency.value = 6;

        depth.gain.setValueAtTime(0, at);
        depth.gain.linearRampToValueAtTime(frequency * 0.01, at + 0.25);
        depth.connect(tone.frequency);
    }

    ///A note on the triangle - the bass, which a console of the time could not make louder or softer, only start and stop.
    triangle(frequency, at, length, level) {
        const loudness = this.envelope(at, length * 0.92, 0.2 * level, 0.003, 0.01, this.bus);
        const tone = this.voice(at, at + length, loudness);

        tone.type = 'triangle';
        tone.frequency.setValueAtTime(frequency, at);
    }

    //Synthwave/////////////////////////////////////////////

    ///
    ///A pad note: two saws a little out of tune with each other, through a low-pass, swelling in and dying slowly - into
    ///the duck, so it pumps with the kick, and some into the room.
    ///
    pad(frequency, at, length, level) {
        const tone = this.context.createBiquadFilter();

        tone.type = 'lowpass';
        tone.frequency.value = 1400;

        const loudness = this.envelope(at, length, 0.035 * level, 0.35, 0.5, this.duck);

        tone.connect(loudness);
        this.send(loudness, this.room, 0.4, at + length);

        for (const cents of [-9, 9]) {
            const saw = this.voice(at, at + length, tone);

            saw.type = 'sawtooth';
            saw.frequency.setValueAtTime(frequency, at);
            saw.detune.value = cents;
        }
    }

    ///A bass note: a saw with a square an octave under it, through a low-pass that closes as the note goes - into the duck.
    bass(frequency, at, length, level) {
        const tone = this.context.createBiquadFilter();

        tone.type = 'lowpass';
        tone.Q.value = 4;
        tone.frequency.setValueAtTime(1600, at);
        tone.frequency.setTargetAtTime(320, at, 0.07);

        const loudness = this.envelope(at, length * 0.85, 0.11 * level, 0.004, 0.03, this.duck);

        tone.connect(loudness);

        const saw = this.voice(at, at + length, tone);

        saw.type = 'sawtooth';
        saw.frequency.setValueAtTime(frequency, at);

        const square = this.voice(at, at + length, tone);

        square.type = 'square';
        square.frequency.setValueAtTime(frequency / 2, at);
    }

    ///A pluck of the arpeggio: a square through a low-pass snapping shut, gone in a quarter of a second - half into the echo.
    pluck(frequency, at, level) {
        const tone = this.context.createBiquadFilter();

        tone.type = 'lowpass';
        tone.Q.value = 3;
        tone.frequency.setValueAtTime(3600, at);
        tone.frequency.setTargetAtTime(600, at, 0.05);

        const loudness = this.context.createGain();

        loudness.gain.setValueAtTime(0, at);
        loudness.gain.linearRampToValueAtTime(0.045 * level, at + 0.003);
        loudness.gain.setTargetAtTime(0, at + 0.003, 0.07);
        loudness.connect(this.bus);

        tone.connect(loudness);
        this.send(loudness, this.echo, 0.6, at + 0.4);

        const square = this.voice(at, at + 0.4, tone);

        square.type = 'square';
        square.frequency.setValueAtTime(frequency, at);

        setTimeout(() => loudness.disconnect(), ((at - this.context.currentTime) * 1000) + 1000);
    }

    ///
    ///A note of the lead: two saws a little apart through a low-pass, held, wavering once it has been held a moment -
    ///into the room and the echo both.
    ///
    lead(frequency, at, length, level) {
        const tone = this.context.createBiquadFilter();

        tone.type = 'lowpass';
        tone.Q.value = 1;
        tone.frequency.value = 2800;

        const loudness = this.envelope(at, length * 0.97, 0.05 * level, 0.03, 0.12, this.bus);

        tone.connect(loudness);
        this.send(loudness, this.room, 0.35, at + length);
        this.send(loudness, this.echo, 0.3, at + length);

        const depth = this.context.createGain();
        const wobble = this.voice(at, at + length, depth);

        wobble.frequency.value = 5.5;

        depth.gain.setValueAtTime(0, at);
        depth.gain.setValueAtTime(0, at + 0.2);
        depth.gain.linearRampToValueAtTime(12, at + 0.5);

        for (const cents of [-6, 6]) {
            const saw = this.voice(at, at + length, tone);

            saw.type = 'sawtooth';
            saw.frequency.setValueAtTime(frequency, at);
            saw.detune.value = cents;

            depth.connect(saw.detune);
        }
    }

    ///Some of `from` into `to` - the room or the echo - at `level`, let go of after `until`.
    send(from, to, level, until) {
        const share = this.context.createGain();

        share.gain.value = level;

        from.connect(share);
        share.connect(to);

        setTimeout(() => share.disconnect(), ((until - this.context.currentTime) * 1000) + 3000);
    }

    //Drums/////////////////////////////////////////////////

    ///A drum of `kit` - 'chip' or 'synth' - at `at`: the kick, the snare or the hat.
    drum(kit, drum, at) {
        if (drum === 'kick')
            this.kick(kit, at);
        else if (drum === 'snare')
            this.snare(kit, at);
        else if (drum === 'hat')
            this.hat(kit, at);
    }

    ///
    ///The kick: a tone dropping fast - deep and long in the synth's kit, short in the chip's. **The synth's ducks the pad
    ///and the bass under it** for a moment, the pump a synthwave mix has.
    ///
    kick(kit, at) {
        let from = 150;
        let to = 48;
        let length = 0.4;
        let level = 0.5;
        let type = 'sine';

        if (kit === 'chip') {
            from = 180;
            to = 55;
            length = 0.12;
            level = 0.35;
            type = 'triangle';
        }

        const loudness = this.context.createGain();

        loudness.gain.setValueAtTime(level, at);
        loudness.gain.exponentialRampToValueAtTime(0.001, at + length);
        loudness.connect(this.bus);

        const tone = this.voice(at, at + length, loudness);

        tone.type = type;
        tone.frequency.setValueAtTime(from, at);
        tone.frequency.exponentialRampToValueAtTime(to, at + (length * 0.35));

        setTimeout(() => loudness.disconnect(), ((at + length - this.context.currentTime) * 1000) + 500);

        if (kit !== 'synth' || this.duck == null)
            return;

        this.duck.gain.setTargetAtTime(0.45, at, 0.004);
        this.duck.gain.setTargetAtTime(1, at + 0.04, 0.09);
    }

    ///
    ///The snare: noise through a band, with a tone under it - and in the synth's kit a long tail of the room, the
    ///eighties' snare.
    ///
    snare(kit, at) {
        let length = 0.22;
        let level = 0.16;
        let band = 1800;

        if (kit === 'chip') {
            length = 0.12;
            level = 0.12;
            band = 2600;
        }

        const loudness = this.noiseHit(at, length, level, 'bandpass', band, 0.7);

        if (kit === 'synth')
            this.send(loudness, this.room, 0.9, at + length);

        const body = this.context.createGain();

        body.gain.setValueAtTime(level * 0.6, at);
        body.gain.exponentialRampToValueAtTime(0.001, at + (length * 0.6));
        body.connect(this.bus);

        const tone = this.voice(at, at + length, body);

        tone.type = 'triangle';
        tone.frequency.setValueAtTime(190, at);

        setTimeout(() => body.disconnect(), ((at + length - this.context.currentTime) * 1000) + 500);
    }

    ///The hat: a tick of noise above everything else.
    hat(kit, at) {
        let level = 0.035;

        if (kit === 'chip')
            level = 0.03;

        this.noiseHit(at, 0.045, level, 'highpass', 7500, 0.7);
    }

    ///A hit of the game's noise through a filter, dying away over `length` - and the loudness it went through.
    noiseHit(at, length, level, type, frequency, q) {
        const context = this.context;
        const source = context.createBufferSource();
        const filter = context.createBiquadFilter();
        const loudness = context.createGain();

        source.buffer = this.noise;

        filter.type = type;
        filter.frequency.value = frequency;
        filter.Q.value = q;

        loudness.gain.setValueAtTime(level, at);
        loudness.gain.exponentialRampToValueAtTime(0.001, at + length);

        source.connect(filter);
        filter.connect(loudness);
        loudness.connect(this.bus);

        source.onended = () => loudness.disconnect();
        source.start(at, Math.random() * Math.max(0, this.noise.duration - length), length);

        return loudness;
    }
}

///
///A pulse wave with `duty` of each cycle high - an eighth, a quarter or a half, the last a square - as the overtones
///Web Audio builds a waveform from. The sharper the pulse, the thinner and brighter the note.
///
function pulseWave(context, duty) {
    const harmonics = 48;
    const real = new Float32Array(harmonics);
    const imag = new Float32Array(harmonics);

    for (let n = 1; n < harmonics; n++) {
        real[n] = Math.sin(2 * Math.PI * n * duty) / (Math.PI * n);
        imag[n] = (1 - Math.cos(2 * Math.PI * n * duty)) / (Math.PI * n);
    }

    return context.createPeriodicWave(real, imag);
}

///A room's echo for a convolver: `seconds` of noise dying away, a little different in each ear.
function roomImpulse(context, seconds) {
    const length = Math.round(context.sampleRate * seconds);
    const impulse = context.createBuffer(2, length, context.sampleRate);

    for (let channel = 0; channel < 2; channel++) {
        const samples = impulse.getChannelData(channel);

        for (let i = 0; i < length; i++)
            samples[i] = ((Math.random() * 2) - 1) * Math.pow(1 - (i / length), 3);
    }

    return impulse;
}
