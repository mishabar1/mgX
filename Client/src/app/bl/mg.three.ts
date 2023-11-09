import * as THREE from 'three';
import {Group} from 'three/src/objects/Group';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader';
import {STLLoader} from 'three/examples/jsm/loaders/STLLoader';
import {OBJLoader} from 'three/examples/jsm/loaders/OBJLoader';
import {AnimationMixer, BufferGeometry, Line, Matrix4, Raycaster, TextureLoader, Vector3} from 'three';
import {FontLoader} from 'three/examples/jsm/loaders/FontLoader';
import {InteractionManager} from '../services/mg.interaction.manager';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment';
import * as ThreeMeshUI from 'three-mesh-ui';
import * as TWEEN from '@tweenjs/tween.js';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController';
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory';
import {ThreeHelper} from './three.helper';
import {forEach} from 'lodash';

export class MgThree{

  scene!: THREE.Scene;
  cameraGroup!: Group;
  camera!: THREE.PerspectiveCamera;
  audioListener!: THREE.AudioListener;
  renderer!: THREE.WebGLRenderer;
  orbitControls!: OrbitControls;
  gltfLoader!: GLTFLoader;
  stlLoader!: STLLoader;
  objLoader!: OBJLoader;
  textureLoader!: TextureLoader;
  fontLoader!: FontLoader;
  interactionManager!: InteractionManager;

  controllers: any;
  selectedObject: any;
  interactionObjects: any = [];
  selectedObjectDistance: any;
  objectUnselectedColor = "red";
  objectSelectedColor = "blue";


  controller: any;
  reticle: any;
  box: any;
  hitTestSourceRequested: any;
  hitTestSource: any;

  clock = new THREE.Clock();
  rendererContainerElement!:HTMLDivElement;
  animationMixers:AnimationMixer[]=[];

  constructor() {

    // let x = ThreeHelper.func1(1);

  }

  initThree(nativeElement: any,onFinish:any) {
    this.rendererContainerElement = nativeElement;
    // Initialize scene
    this.scene = new THREE.Scene();
    // this.scene.background = new THREE.Color(0xffffff)
    const loader = new THREE.CubeTextureLoader();
    // 'ft', 'bk', 'up', 'dn', 'rt', 'lf'
    const texture = loader.load([
      'assets/skyboxes/afterrain/afterrain_ft.jpg',
      'assets/skyboxes/afterrain/afterrain_bk.jpg',
      'assets/skyboxes/afterrain/afterrain_up.jpg',
      'assets/skyboxes/afterrain/afterrain_dn.jpg',
      'assets/skyboxes/afterrain/afterrain_rt.jpg',
      'assets/skyboxes/afterrain/afterrain_lf.jpg'
    ]);
    this.scene.background = texture;


    // Initialize camera
    this.camera = new THREE.PerspectiveCamera(75, this.rendererContainerElement.clientWidth / this.rendererContainerElement.clientHeight, 0.1, 5000);
    // this.camera.position.set(-1.8, 0.6, 2.7);

    // create an AudioListener and add it to the camera
    this.audioListener = new THREE.AudioListener();
    this.camera.add(this.audioListener);

    // Initialize renderer
    this.renderer = new THREE.WebGLRenderer({antialias: true});
    this.renderer.xr.enabled = true;
    this.renderer.setSize(this.rendererContainerElement.clientWidth, this.rendererContainerElement.clientHeight);
    this.rendererContainerElement.appendChild(this.renderer.domElement);

    this.renderer.xr.addEventListener("sessionstart", () => {


      this.renderer.xr.getCamera().position.copy(this.camera.position);
      this.renderer.xr.getCamera().lookAt(this.orbitControls.target);

      // const xrManager = this.renderer.xr,
      //   camera = this.camera,
      //   baseReferenceSpace = xrManager.getReferenceSpace(),
      //   offsetPosition = camera.position,
      //   offsetRotation = camera.quaternion;
      //
      // // const transform = new XRRigidTransform( offsetPosition, { x: this.config.xrTiltOffset ? offsetRotation.x : 0, y: -(offsetRotation.y - this.config.xrPanOffset), z: offsetRotation.z, w: offsetRotation.w } ),
      // //   //const transform = new XRRigidTransform( offsetPosition, { x: offsetRotation.x, y: -(offsetRotation.y - 0.5) , z: offsetRotation.z, w: offsetRotation.w } ),
      // //   teleportSpaceOffset = baseReferenceSpace.getOffsetReferenceSpace( transform );
      //
      // const transform = new XRRigidTransform(offsetPosition, {
      //     x: offsetRotation.x,
      //     y: offsetRotation.y,
      //     z: offsetRotation.z,
      //     w: offsetRotation.w,
      //   }),
      //   teleportSpaceOffset = baseReferenceSpace!.getOffsetReferenceSpace( transform );
      //
      // xrManager.setReferenceSpace( teleportSpaceOffset );

      // this.orbitControls.update();
      //
      // const baseReferenceSpace = this.renderer.xr.getReferenceSpace();
      //
      // const offsetPosition = this.camera.position;
      //
      // //const offsetRotation = camera.rotation;
      //
      // const offsetRotation = this.camera.quaternion;
      //
      // // const transform = new XRRigidTransform( offsetPosition, { x: offsetRotation.x, y: -(offsetRotation.y), z: offsetRotation.z, w: offsetRotation.w } );
      // const transform = new XRRigidTransform( offsetPosition, { x: offsetRotation.x, y: -(offsetRotation.y - 0.85), z: offsetRotation.z, w: offsetRotation.w } );
      // const teleportSpaceOffset = baseReferenceSpace!.getOffsetReferenceSpace( transform );
      //
      // this.renderer.xr.setReferenceSpace( teleportSpaceOffset );

      // this.orbitControls.update();
      //
      // const baseReferenceSpace = this.renderer.xr.getReferenceSpace();
      //
      // const offsetPosition = this.camera.position;
      //
      //
      // const offsetRotation = this.camera.quaternion;
      //
      // const transform = new XRRigidTransform( offsetPosition, { x: offsetRotation.x, y: -(offsetRotation.y), z: offsetRotation.z, w: offsetRotation.w } );
      //
      // const teleportSpaceOffset = baseReferenceSpace!.getOffsetReferenceSpace( transform );
      //
      // this.renderer.xr.setReferenceSpace( teleportSpaceOffset );

    });
    this.renderer.xr.addEventListener('sessionend', () => {
      // this.camera.position
      // TODO !!!!
      debugger
    });


    // Add ambient light
    // const ambientLight = new THREE.AmbientLight(0xffffff,2);
    // this.scene.add(ambientLight);
    const pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmremGenerator.fromScene(new RoomEnvironment(), 0).texture;

    // this.labelRenderer = new CSS2DRenderer();
    // this.labelRenderer.setSize( window.innerWidth, window.innerHeight );
    // this.labelRenderer.domElement.style.position = 'absolute';
    // this.labelRenderer.domElement.style.top = '0px';
    // document.body.appendChild( this.labelRenderer.domElement );

    // Initialize OrbitControls
    this.orbitControls = new OrbitControls(this.camera, this.renderer.domElement);
    this.orbitControls.addEventListener('change', () => {
      this.renderer.render(this.scene, this.camera);

      //console.log("CAMERA",this.camera.position);

    });
    this.orbitControls.enableZoom = true
    this.orbitControls.update();

    this.interactionManager = new InteractionManager(this.renderer, this.camera, this.renderer.domElement);

    this.clock = new THREE.Clock();

    // Start the animation loop
    this.renderer.setAnimationLoop(() => {

      if (this.animationMixers.length ){
        let delta = this.clock.getDelta();
        forEach(this.animationMixers,mixer=>{
          // mixer.update(this.clock.getDelta());
          if ( mixer ) mixer.update( delta );
        })
      }

      // Don't forget, ThreeMeshUI must be updated manually.
      // This has been introduced in version 3.0.0 in order
      // to improve performance
      ThreeMeshUI.update()

      if (this.controllers) {
        this.controllers.forEach((controller: any) => {
          this.handleController(controller);
        })
      }

      this.orbitControls.update();
      this.interactionManager.update();
      this.renderer.render(this.scene, this.camera);

      TWEEN.update();
      // this.animationsObjects.forEach((mesh:any) => {
      //   if (mesh.userData.clock && mesh.userData.mixer) {
      //     mesh.userData.mixer.update(mesh.userData.clock.getDelta());
      //   }
      // });
      //console.log("running");



    });

    // Instantiate a loader
    this.gltfLoader = new GLTFLoader();
    this.stlLoader = new STLLoader();
    this.objLoader = new OBJLoader();
    this.textureLoader = new TextureLoader();
    this.fontLoader = new FontLoader();

    // const x = VRButton.createButton( this.renderer )
    // document.body.appendChild( x );

    const gridHelper = new THREE.GridHelper( 100, 100 ,0xff0000);
    this.scene.add( gridHelper );

    onFinish();

  }

  handleController(controller: XRTargetRaySpace) {
    if (controller.userData["selectPressed"]) {
      if (!controller.userData["selectPressedPrev"]) {
        // Select pressed
        controller.children[0].scale.z = 10;
        const rotationMatrix = new Matrix4();
        rotationMatrix.extractRotation(controller.matrixWorld);
        const raycaster = new Raycaster();
        raycaster.ray.origin.setFromMatrixPosition(controller.matrixWorld);
        raycaster.ray.direction.set(0, 0, -1).applyMatrix4(rotationMatrix);
        const intersects = raycaster.intersectObjects(this.interactionObjects);
        if (intersects.length > 0) {
          controller.children[0].scale.z = intersects[0].distance;
          this.selectedObject = intersects[0].object;
          this.selectedObject.material.color = this.objectSelectedColor;
          this.selectedObjectDistance = this.selectedObject.position.distanceTo(controller.position);
        }
      } else if (this.selectedObject) {
        // Move selected object so it's always the same distance from controller
        const moveVector = controller.getWorldDirection(new Vector3()).multiplyScalar(this.selectedObjectDistance).negate();
        this.selectedObject.position.copy(controller.position.clone().add(moveVector));
      }
    } else if (controller.userData["selectPressedPrev"]) {
      // Select released
      controller.children[0].scale.z = 10;
      if (this.selectedObject != null) {
        this.selectedObject.material.color = this.objectUnselectedColor;
        this.selectedObject = null;
      }
    }
    controller.userData["selectPressedPrev"] = controller.userData["selectPressed"];
  }

  resizeCanvasToDisplaySize() {
    const canvas = this.renderer.domElement;
    // look up the size the canvas is being displayed
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;

    // you must pass false here or three.js sadly fights the browser
    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();

    // update any render target sizes here
  }

  currentSession: any = null;



  onSelectStart(x: any) {
    console.log("onSelectStart", x);
    // this refers to the controller
    // this.children[0].scale.z = 10;
    // this.userData.selectPressed = true;
  }

  onSelectEnd(x: any) {
    console.log("onSelectEnd", x);
    // this refers to the controller
    // this.children[0].scale.z = 0;
    // this.userData.selectPressed = false;
  }

  buildControllers() {
    const controllerModelFactory = new XRControllerModelFactory();

    const geometry = new BufferGeometry().setFromPoints([
      new Vector3(0, 0, 0),
      new Vector3(0, 0, -1)
    ]);

    const line = new Line(geometry);
    line.scale.z = 10;

    const controllers = [];

    for (let i = 0; i < 2; i++) {
      const controller: XRTargetRaySpace = this.renderer.xr.getController(i);
      controller.add(line.clone());
      controller.userData["selectPressed"] = false;
      controller.userData["selectPressedPrev"] = false;
      this.scene.add(controller);
      controllers.push(controller);

      const grip = this.renderer.xr.getControllerGrip(i);
      grip.add(controllerModelFactory.createControllerModel(grip));
      this.scene.add(grip);
    }

    return controllers;
  }

  startVr() {
    this.controllers = this.buildControllers();
    this.controllers.forEach((controller: any) => {
      controller.addEventListener('selectstart', this.onSelectStart);
      controller.addEventListener('selectend', this.onSelectEnd);
    });

    const sessionInit = {optionalFeatures: ['local-floor', 'bounded-floor', 'hand-tracking', 'layers']};
    // @ts-ignore
    window.navigator.xr.requestSession('immersive-vr', sessionInit).then(async session => {
      //session.addEventListener( 'end', this.onSessionEnded );
      await this.renderer.xr.setSession(session);
      this.currentSession = session;
    });
  }
}
