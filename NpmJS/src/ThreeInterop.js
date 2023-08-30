//Includes///////////////////////////////////////

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { OBJExporter } from 'three/addons/exporters/OBJExporter.js';
import { STLExporter } from 'three/addons/exporters/STLExporter.js';
import * as BufferGeometryUtils from 'three/addons/utils/BufferGeometryUtils.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

/////////////////////////////////////////////////



//Global variables///////////////////////////////

let app;
//let dotNetObjRef; //Not needed here for now.

/////////////////////////////////////////////////



//C# Interop Functions///////////////////////////

//Init./////////////////////

window.registerThree = function () {

    let settings = {
        containerElement: document.getElementById("container"),
        containerSizeX: 0,
        containerSizeY: 0
    };
    settings.containerSizeX = settings.containerElement.clientWidth;
    settings.containerSizeY = settings.containerElement.clientHeight;

    app = new Viewer3D(settings);
}

/*function setDotNetObjRef(ref) {
    dotNetObjRef = ref;
}*/

////////////////////////////

window.startRender3DInterOp = function () {
    app.startRender3D();
}

window.changeBackgroundInterOp = function (backgroundName) {
    app.changeBackground(backgroundName);
}

window.drawInterOp = function (data) {
    app.draw(data);
}

window.cinematicViewInterOp = function (cinematicViewToggleInterOp) {
    app.cinematicView(cinematicViewToggleInterOp);
}

window.download3DModelInterOp = function (CurrentlySelectedFileName, ModelDownloadFileType) {
    app.download3DModel(CurrentlySelectedFileName, ModelDownloadFileType);
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
        //let loader = new GLTFLoader();

        //Init. varibles//////////

        this.cinematicViewToggle = false;

        //////////////////////////

        //Create and init. the renderer.
        this.renderer = new THREE.WebGLRenderer({ antialias: true });
        this.renderer.setSize(this.settings.containerSizeX, this.settings.containerSizeY);
        this.renderer.setClearColor(0xDDDDDD, 1);
        this.renderer.xr.enabled = true; //Enable for VR.
        //
        this.settings.containerElement.appendChild(this.renderer.domElement);
        
        //Create a scene
        this.scene = new THREE.Scene();

        //Create and init. a camera and add it to the scene.
        this.camera = new THREE.PerspectiveCamera(100, this.settings.containerSizeX / this.settings.containerSizeY, 1, 50000);
        this.camera.position.z = 2000;
        this.scene.add(this.camera);

        //Create objects group and add it to the scene.
        this.chipObjectsGroup = new THREE.Group();
        this.scene.add(this.chipObjectsGroup);

        //Create ambient light and add it to the scene.
        const light = new THREE.AmbientLight(0x404040); // soft white light
        this.scene.add(light);

        //Create a directional light and add it to scene.
        const directionalLight = new THREE.DirectionalLight(0xffffff);
        directionalLight.position.set(3, 3, 2);
        this.scene.add(directionalLight);
        
        //Create controls.
        this.controls = new OrbitControls(this.camera, this.renderer.domElement);

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
        this.settings.containerElement.appendChild(VRButton.createButton(this.renderer));   

        //Start animation/render loop.
        //this.startRender3D(); //Will be started by interop call from C# by Viewer3D.razor component.
    }

    setupInitialScene() {
        //Init. background.
        this.changeBackground("background1.jpg");

        //Start the render loop.
        //this.startRender3D(); //Remove??
    }

    /////////////////////////////////////////////////////////



    /////////////////////////////////////////////////////////

    startRender3D() {
        requestAnimationFrame(this.startRender3D.bind(this)); //setAnimationLoop needs to be used for VR.
        //app.renderer.setAnimationLoop(app.startRender3D);

        //Perform animations of objects or movements of camera.
        this.runCinematicView();//todo: optimize
        //this.runVR();

        //Call renderer to render the scene.
        this.renderer.render(this.scene, this.camera);
    }

    runCinematicView() {
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
        } else {
            this.camera.rotation.y += 0.0;
            this.camera.rotation.x += 0.0;
        }
    }

    draw(data) {
        //Remove any previous objects.
        for (var i = this.chipObjectsGroup.children.length - 1; i >= 0; --i)
            this.chipObjectsGroup.remove(this.chipObjectsGroup.children[i]);

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

            this.chipObjectsGroup.add(mesh);
        }
    }

    /////////////////////////////////////////////////////////



    //Other//////////////////////////////////////////////////

    runVR() {
        // Update VR headset position and orientation
        this.vrSession.requestAnimationFrame(startRender3D);
        this.vrSession.onXRFrame((time, frame) => {
            const xrPose = frame.getViewerPose(renderer.xr.getReferenceSpace());
            if (xrPose) {
                const position = xrPose.transform.position;
                const orientation = xrPose.transform.orientation;

                // Update camera position and rotation based on VR headset pose
                this.camera.position.set(position.x, position.y, position.z);
                this.camera.quaternion.set(orientation.x, orientation.y, orientation.z, orientation.w);
            }

            // Render the scene with the updated camera
            this.renderer.render(this.scene, this.camera);
        });
    }

    cinematicView(cinematicViewToggleInterOp) {
        this.cinematicViewToggle = cinematicViewToggleInterOp;

        // Compute the bounding box of the group
        const bbox = new THREE.Box3().setFromObject(this.chipObjectsGroup);

        // Calculate the center point of the bounding box
        bbox.getCenter(this.cinematicSettings.chipCenterPoint);
    }

    onWindowResize() {
        const width = this.settings.containerElement.innerWidth;
        const height = this.settings.containerElement.innerHeight;

        //Update the renderer size and aspect ratio.
        this.renderer.setSize(width, height);
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
    }

    changeBackground(backgroundName) {
        const texture = this.textureLoader.load(window.location.href + '/resources/Images/Background/' + backgroundName, () => {
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