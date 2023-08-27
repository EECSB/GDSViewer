import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { OBJExporter } from 'three/addons/exporters/OBJExporter.js';
import { STLExporter } from 'three/addons/exporters/STLExporter.js';
import * as BufferGeometryUtils from 'three/addons/utils/BufferGeometryUtils.js';

var WIDTH = 800; // window.innerWidth;
var HEIGHT = 800; //window.innerHeight;
let renderer;
let scene;
let camera;
let loader;
let controls;



let chipObjectsGroup = new THREE.Group();

let cinematicViewToggle = false;

renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(WIDTH, HEIGHT);
renderer.setClearColor(0xDDDDDD, 1);



scene = new THREE.Scene();

camera = new THREE.PerspectiveCamera(100, WIDTH / HEIGHT, 1, 50000);
camera.position.z = 2000;
scene.add(camera);

scene.add(chipObjectsGroup);

// Create ambient light and add to scene.
var light = new THREE.AmbientLight(0x404040); // soft white light
scene.add(light);

// Create directional light and add to scene.
var directionalLight = new THREE.DirectionalLight(0xffffff);
directionalLight.position.set(3, 3, 2);
scene.add(directionalLight);

controls = new OrbitControls(camera, renderer.domElement);
loader = new GLTFLoader();

const loader2 = new THREE.TextureLoader();
const texture = loader2.load(window.location.href + 'resources/Images/Background/background1.jpg', () => {
        const rt = new THREE.WebGLCubeRenderTarget(texture.image.height);
        rt.fromEquirectangularTexture(renderer, texture);
        scene.background = rt.texture;
});

window.download3DModel = function (fileName, fileType) {
    let exporter;
    let data;

    switch (fileType) {
        case ".stl":
                exporter = new STLExporter();
                data = exporter.parse(scene);
                BlazorDownloadFile(fileName + '.stl', 'application/octet-stream', data);
            break;
        case ".obj":
                exporter = new OBJExporter();
                data = exporter.parse(scene);
                BlazorDownloadFile(fileName + '.obj', 'application/octet-stream', data);
            break
        case ".gltf":
                exporter = new GLTFExporter();
                exporter.parse(scene, function (gltf) {
                    data = JSON.stringify(gltf, null, 2);
                    BlazorDownloadFile(fileName + '.gltf', 'application/octet-stream', data);
                });
            break;
    }
}

window.registerThree = function () {
    const container = document.getElementById('container');
    container.appendChild(renderer.domElement);

    container.addEventListener('resize', onWindowResize, false);
}

window.draw = function (data) {
    //Remove any previous objects.
    for (var i = chipObjectsGroup.children.length - 1; i >= 0; --i)
        chipObjectsGroup.remove(chipObjectsGroup.children[i]);

    let polygons = data.elements;

    for (let polygon of polygons) {
        let points = polygon.points;

        const shape = new THREE.Shape();
        let isFirstIteration = true;
        for (let point of points) {
                shape.moveTo(point.x, point.y);
            if (isFirstIteration) {
                isFirstIteration = false;
            } else {
                shape.lineTo(point.x, point.y);
            }
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

        mesh.rotation.set(1.5, 0.0, 0);
        mesh.position.set(0, polygon.layer.offset, 0);

        chipObjectsGroup.add(mesh);
    }

    render3D();
}

window.render3D = function () {
    animate();

    renderer.render(scene, camera);

    requestAnimationFrame(render3D);
}

window.changeBackgroundInterOp = function (backgroundName) {
    changeBackground(backgroundName);
}

function changeBackground(backgroundName) {
    const texture = loader2.load(window.location.href + '/resources/Images/Background/' + backgroundName, () => {
        const rt = new THREE.WebGLCubeRenderTarget(texture.image.height);
        rt.fromEquirectangularTexture(renderer, texture);
        scene.background = rt.texture;
    });
}


window.cinematicViewInterOp = function (cinematicViewToggleInterOp) {
    cinematicViewToggle = cinematicViewToggleInterOp;

    // Compute the bounding box of the group
    const bbox = new THREE.Box3().setFromObject(chipObjectsGroup);

    // Calculate the center point of the bounding box
    bbox.getCenter(chipCenterPoint);
}

const chipCenterPoint = new THREE.Vector3();
var camera_offset = { x: 10000, y: 10000, z: 10000 };
var camera_speed = 0.1;
const clock = new THREE.Clock();
var time = 0;

function cinematicView() {
    if (cinematicViewToggle) {
        clock.getDelta();
        time = clock.elapsedTime.toFixed(2);

        camera.position.x = chipCenterPoint.x + camera_offset.x * (Math.sin(time * camera_speed));
        camera.position.z = chipCenterPoint.z + (camera_offset.z * Math.cos(time * 0.1)) * (Math.cos(time * camera_speed));
        camera.position.y = chipCenterPoint.y + camera_offset.y * (Math.cos(time * 0.05));

        camera.lookAt(chipCenterPoint.x, chipCenterPoint.y, chipCenterPoint.z);
    } else {
        camera.rotation.y += 0.0;
        camera.rotation.x += 0.0;
    }
}

function animate() {
    cinematicView();
}

function onWindowResize() {
    const container = document.getElementById('container');

    var width = container.innerWidth;
    var height = container.innerHeight;


    // Update the renderer size and aspect ratio
    renderer.setSize(width, height);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
}