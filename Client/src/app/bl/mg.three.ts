import * as THREE from 'three';
import {Group} from 'three/src/objects/Group.js';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls.js';
import {CSS3DRenderer, CSS3DObject} from 'three/examples/jsm/renderers/CSS3DRenderer.js';
import {EffectComposer} from 'three/examples/jsm/postprocessing/EffectComposer.js';
import {RenderPass} from 'three/examples/jsm/postprocessing/RenderPass.js';
import {OutlinePass} from 'three/examples/jsm/postprocessing/OutlinePass.js';
import {OutputPass} from 'three/examples/jsm/postprocessing/OutputPass.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader.js';
import {STLLoader} from 'three/examples/jsm/loaders/STLLoader.js';
import {OBJLoader} from 'three/examples/jsm/loaders/OBJLoader.js';
import {AnimationMixer, BufferGeometry, Line, Matrix4, Raycaster, TextureLoader, Vector3} from 'three';
import {FontLoader} from 'three/examples/jsm/loaders/FontLoader.js';
import {InteractionManager} from '../services/mg.interaction.manager';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment.js';
import * as ThreeMeshUI from 'three-mesh-ui';
import * as TWEEN from '@tweenjs/tween.js';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController.js';
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory.js';
import {ThreeHelper} from './three.helper';
import {forEach} from 'lodash';

export class MgThree{

  scene!: THREE.Scene;

  // CSS3D layer: real HTML panels transformed into the 3D scene.
  css3dRenderer?: CSS3DRenderer;
  css3dScene?: THREE.Scene;

  // "Onscreen" holder: a screen-anchored HUD layer (like hand/table, but fixed to the viewport
  // side, not the world) — stays put when the camera moves. Panels appended here get their own
  // pointer-events; the empty column around them stays click-through to the canvas.
  onscreenHolder?: HTMLDivElement;

  // Postprocessing: an OutlinePass draws a glowing CONTOUR around selected objects (instead of
  // recolouring the whole model). The main scene renders through this composer.
  composer?: EffectComposer;
  outlinePass?: OutlinePass;
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

  // Cache textures by URL so re-created items (e.g. cards rebuilt every action) reuse the
  // same THREE.Texture instead of re-fetching/decoding the image each time.
  private texCache: { [url: string]: { tex: any, ready: boolean, waiters: ((t: any) => void)[] } } = {};
  getTexture(url: string, onReady?: (t: any) => void): any {
    let e = this.texCache[url];
    if (!e) {
      e = { tex: null, ready: false, waiters: [] };
      this.texCache[url] = e;
      e.tex = this.textureLoader.load(url, (t: any) => { e.ready = true; e.waiters.forEach(w => w(t)); e.waiters = []; });
    }
    if (onReady) { if (e.ready) onReady(e.tex); else e.waiters.push(onReady); }
    return e.tex;
  }

  controllers: any;
  selectedObject: any;
  interactionObjects: any = [];
  selectedObjectDistance: any;

  // Meshes currently gliding to a new position (smooth piece movement).
  movers: { mesh: THREE.Object3D; target: THREE.Vector3 }[] = [];
  animateTo(mesh: THREE.Object3D, target: THREE.Vector3) {
    const existing = this.movers.find(m => m.mesh === mesh);
    if (existing) existing.target.copy(target);
    else this.movers.push({ mesh, target: target.clone() });
  }
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
  gridHelper!: THREE.GridHelper;

  // --- magnifier loupe (top-left of the screen) ---------------------------
  // A second small renderer draws the same scene through a "view offset" camera that
  // samples just the square of screen under the mouse, blown up into a circular canvas.
  private magRenderer?: THREE.WebGLRenderer;
  private magCamera?: THREE.PerspectiveCamera;
  private magElement?: HTMLCanvasElement;
  private magCloseBtn?: HTMLDivElement;    // ✕ to hide the loupe
  private magShowBtn?: HTMLDivElement;     // 🔍 to bring it back
  private magZoomBar?: HTMLDivElement;     // x2 / x4 / … zoom-level buttons
  private magSize = 280;                    // on-screen size of the loupe (css px)
  private magZoom = 4;                      // magnification factor (chosen via the zoom bar)
  private magMouse: { x: number, y: number } | null = null;
  magEnabled = true;
  private magMouseMove = (e: MouseEvent) => {
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.magMouse = { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };
  private magMouseLeave = () => { this.magMouse = null; };

  setDebugHelpers(on: boolean) {
    if (this.gridHelper) this.gridHelper.visible = on;
  }

  // Keep the canvas and camera in sync with the container/window size on resize.
  onWindowResize = () => {
    if (!this.renderer || !this.camera || !this.rendererContainerElement) return;
    const w = this.rendererContainerElement.clientWidth;
    const h = this.rendererContainerElement.clientHeight;
    if (!w || !h) return;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
    this.css3dRenderer?.setSize(w, h);
    this.composer?.setSize(w, h);
    this.outlinePass?.setSize(w, h);
    this.renderer.render(this.scene, this.camera);
  };

  // Draw the glowing selection contour around these objects (empty = none).
  setOutlined(objects: THREE.Object3D[]) {
    if (this.outlinePass) this.outlinePass.selectedObjects = objects;
  }

  // Mount an HTML element into the 3D scene as a CSS3D panel. Returns the object so the caller
  // can reposition/remove it. `scale` converts CSS pixels → world units (e.g. 0.02).
  mountCssPanel(element: HTMLElement, pos: THREE.Vector3, rot: THREE.Euler, scale: number): CSS3DObject | null {
    if (!this.css3dScene) return null;
    const obj = new CSS3DObject(element);
    obj.position.copy(pos);
    obj.rotation.copy(rot);
    obj.scale.setScalar(scale);
    this.css3dScene.add(obj);
    return obj;
  }

  removeCssPanel(obj: CSS3DObject | null | undefined) {
    if (obj && this.css3dScene) this.css3dScene.remove(obj);
  }

  // Render a single framed snapshot of a glb/gltf model to a PNG data URL — used for the DM
  // console's thumbnails. Reuses one small offscreen renderer; returns '' on failure.
  private _thumbRenderer?: THREE.WebGLRenderer;
  async renderModelThumbnail(assetRelUrl: string, size = 150): Promise<string> {
    try {
      const url = '\\assets\\games\\' + assetRelUrl;
      const gltf: any = await new Promise((res, rej) => this.gltfLoader.load(url, res, undefined, rej));
      const model = gltf.scene;
      const scene = new THREE.Scene();
      scene.add(new THREE.HemisphereLight(0xffffff, 0x555566, 1.3));
      const dir = new THREE.DirectionalLight(0xffffff, 1.1); dir.position.set(3, 6, 5); scene.add(dir);
      scene.add(model);

      const box = new THREE.Box3().setFromObject(model);
      const sphere = box.getBoundingSphere(new THREE.Sphere());
      const r = sphere.radius || 1;
      const fov = 35;
      const dist = (r / Math.sin((fov / 2) * Math.PI / 180)) * 1.1;
      const cam = new THREE.PerspectiveCamera(fov, 1, 0.01, 2000);
      cam.position.set(sphere.center.x + dist * 0.25, sphere.center.y + dist * 0.3, sphere.center.z + dist);
      cam.lookAt(sphere.center);

      if (!this._thumbRenderer) this._thumbRenderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
      this._thumbRenderer.setSize(size, size);
      this._thumbRenderer.setClearColor(0x000000, 0);
      this._thumbRenderer.render(scene, cam);
      return this._thumbRenderer.domElement.toDataURL('image/png');
    } catch { return ''; }
  }

  // Tear down when leaving the view so old renderers stop drawing / listening.
  dispose() {
    window.removeEventListener('resize', this.onWindowResize);
    try { this.renderer?.setAnimationLoop(null); } catch {}
    try {
      this.renderer?.domElement.removeEventListener('mousemove', this.magMouseMove);
      this.renderer?.domElement.removeEventListener('mouseleave', this.magMouseLeave);
      this.magRenderer?.dispose();
      this.magElement?.remove();
      this.magCloseBtn?.remove();
      this.magShowBtn?.remove();
      this.magZoomBar?.remove();
    } catch {}
  }

  constructor() {

    // let x = ThreeHelper.func1(1);

  }

  initThree(nativeElement: any,onFinish:any) {
    this.rendererContainerElement = nativeElement;
    // Cache fetched assets (fonts, models) so re-created items — e.g. the chess
    // turn-indicator text rebuilt each move — don't re-download every time.
    THREE.Cache.enabled = true;
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
    // Soft shadows give the pieces contact/inter-piece shadows so they read as
    // separate 3D forms instead of one flat blob.
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.setSize(this.rendererContainerElement.clientWidth, this.rendererContainerElement.clientHeight);
    this.rendererContainerElement.appendChild(this.renderer.domElement);
    // Let the canvas consume touch gestures (orbit/zoom + tap-to-select) instead of the
    // browser scrolling/zooming the page — needed for phones/tablets.
    (this.renderer.domElement.style as any).touchAction = 'none';

    // CSS3D overlay: renders HTML panels (the DM console) transformed with the camera, layered
    // over the WebGL canvas. pointer-events:none so it never blocks orbit/zoom; the panel itself
    // re-enables pointer-events. It has no depth vs the WebGL scene (always drawn on top).
    this.css3dScene = new THREE.Scene();
    this.css3dRenderer = new CSS3DRenderer();
    this.css3dRenderer.setSize(this.rendererContainerElement.clientWidth, this.rendererContainerElement.clientHeight);
    const cssEl = this.css3dRenderer.domElement;
    cssEl.style.position = 'absolute';
    cssEl.style.top = '0';
    cssEl.style.left = '0';
    cssEl.style.pointerEvents = 'none';
    if (!this.rendererContainerElement.style.position) this.rendererContainerElement.style.position = 'relative';
    this.rendererContainerElement.appendChild(cssEl);

    // Screen-anchored HUD holder on the right edge. The column is click-through (pointer-events
    // none); panels added into it opt back in, so the board behind stays draggable/zoomable.
    this.onscreenHolder = document.createElement('div');
    this.onscreenHolder.style.cssText =
      'position:absolute;top:0;right:0;height:100%;z-index:20;pointer-events:none;' +
      'display:flex;flex-direction:column;justify-content:center;align-items:flex-end;padding:14px;';
    this.rendererContainerElement.appendChild(this.onscreenHolder);

    // Postprocessing composer with an OutlinePass for the selection contour.
    const w0 = this.rendererContainerElement.clientWidth, h0 = this.rendererContainerElement.clientHeight;
    this.composer = new EffectComposer(this.renderer);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.outlinePass = new OutlinePass(new THREE.Vector2(w0, h0), this.scene, this.camera);
    this.outlinePass.edgeStrength = 6;
    this.outlinePass.edgeGlow = 0.7;
    this.outlinePass.edgeThickness = 2;
    this.outlinePass.pulsePeriod = 2;
    this.outlinePass.visibleEdgeColor.set('#3dff6a');
    this.outlinePass.hiddenEdgeColor.set('#124a24');
    this.composer.addPass(this.outlinePass);
    this.composer.addPass(new OutputPass());   // re-apply tone-mapping + sRGB so the scene isn't dark

    // Refresh the canvas whenever the window resizes.
    window.addEventListener('resize', this.onWindowResize);

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


    // ---- Lighting -----------------------------------------------------
    // Keep the image-based environment only as a SUBTLE fill — on its own it lit
    // everything evenly, which made the white pieces read as one flat white block.
    const pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmremGenerator.fromScene(new RoomEnvironment(), 0).texture;
    this.scene.environmentIntensity = 0.35;

    // Gentle sky/ground ambient so shadowed sides aren't pure black.
    const hemiLight = new THREE.HemisphereLight(0xdfeaff, 0x2b2b33, 0.5);
    hemiLight.position.set(0, 20, 0);
    this.scene.add(hemiLight);

    // Key light: the main directional source. This is what gives each piece a
    // light-to-dark shading gradient (and casts the shadows) so its shape reads.
    const keyLight = new THREE.DirectionalLight(0xffffff, 2.0);
    keyLight.position.set(6, 12, 4);
    keyLight.castShadow = true;
    keyLight.shadow.mapSize.set(2048, 2048);
    keyLight.shadow.camera.near = 0.5;
    keyLight.shadow.camera.far = 60;
    keyLight.shadow.camera.left = -8;
    keyLight.shadow.camera.right = 8;
    keyLight.shadow.camera.top = 8;
    keyLight.shadow.camera.bottom = -8;
    keyLight.shadow.bias = -0.0004;
    keyLight.shadow.normalBias = 0.02;
    this.scene.add(keyLight);

    // Soft fill from the opposite side to keep the shadowed faces readable.
    const fillLight = new THREE.DirectionalLight(0xffffff, 0.5);
    fillLight.position.set(-6, 6, -5);
    this.scene.add(fillLight);

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

      this.animationLoop();


    });

    // Instantiate a loader
    this.gltfLoader = new GLTFLoader();
    this.stlLoader = new STLLoader();
    this.objLoader = new OBJLoader();
    this.textureLoader = new TextureLoader();
    this.fontLoader = new FontLoader();

    // const x = VRButton.createButton( this.renderer )
    // document.body.appendChild( x );

    this.gridHelper = new THREE.GridHelper( 100, 100 ,0xff0000);
    this.gridHelper.visible = false;   // debug grid — off unless DEBUG toggled
    this.scene.add( this.gridHelper );

    this.initMagnifier();

    onFinish();

  }

  animationLoop(){
    // Glide any moving pieces toward their target (smooth movement, no "jump").
    for (let i = this.movers.length - 1; i >= 0; i--) {
      const mv = this.movers[i];
      mv.mesh.position.lerp(mv.target, 0.25);
      if (mv.mesh.position.distanceTo(mv.target) < 0.02) {
        mv.mesh.position.copy(mv.target);
        this.movers.splice(i, 1);
      }
    }

    if (this.animationMixers.length ){
      let delta = this.clock.getDelta();
      forEach(this.animationMixers,mixer=>{
        if ( mixer ) mixer.update( delta );
      })
    }

    ThreeMeshUI.update()

    if (this.controllers) {
      this.controllers.forEach((controller: any) => {
        this.handleController(controller);
      })
    }

    this.orbitControls.update();
    this.interactionManager.update();
    if (this.composer) this.composer.render(); else this.renderer.render(this.scene, this.camera);
    if (this.css3dRenderer && this.css3dScene) this.css3dRenderer.render(this.css3dScene, this.camera);
    this.updateMagnifier();

    TWEEN.update();
  }

  // Create the loupe canvas + its dedicated renderer/camera and start tracking the mouse.
  private initMagnifier() {
    const size = this.magSize;
    const canvas = document.createElement('canvas');
    canvas.width = size; canvas.height = size;
    Object.assign(canvas.style, {
      position: 'absolute', top: '12px', left: '12px',
      width: size + 'px', height: size + 'px',
      borderRadius: '8px', border: '3px solid rgba(255,255,255,0.85)',
      boxShadow: '0 2px 12px rgba(0,0,0,0.55)',
      pointerEvents: 'none', zIndex: '20', display: 'none',
    } as any);
    if (!this.rendererContainerElement.style.position)
      this.rendererContainerElement.style.position = 'relative';
    this.rendererContainerElement.appendChild(canvas);
    this.magElement = canvas;

    const r = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    r.setPixelRatio(window.devicePixelRatio || 1);
    r.setSize(size, size, false);
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFSoftShadowMap;
    this.magRenderer = r;

    this.magCamera = new THREE.PerspectiveCamera(75, 1, 0.1, 5000);

    this.renderer.domElement.addEventListener('mousemove', this.magMouseMove);
    this.renderer.domElement.addEventListener('mouseleave', this.magMouseLeave);

    // ✕ button to hide the loupe (sits in its top-right corner).
    const closeBtn = document.createElement('div');
    closeBtn.textContent = '✕';
    closeBtn.title = 'Hide magnifier';
    Object.assign(closeBtn.style, {
      position: 'absolute', top: '16px', left: (15) + 'px',
      width: '22px', height: '22px', lineHeight: '22px', textAlign: 'center',
      borderRadius: '50%', background: 'rgba(0,0,0,0.6)', color: '#fff',
      font: 'bold 14px sans-serif', cursor: 'pointer', zIndex: '21',
      userSelect: 'none', display: 'block',   // always visible while the loupe is enabled
    } as any);
    this.rendererContainerElement.appendChild(closeBtn);
    this.magCloseBtn = closeBtn;

    // 🔍 button shown once hidden, to bring the loupe back.
    const showBtn = document.createElement('div');
    showBtn.textContent = '🔍';
    showBtn.title = 'Show magnifier';
    Object.assign(showBtn.style, {
      position: 'absolute', top: '12px', left: '12px',
      width: '34px', height: '34px', lineHeight: '34px', textAlign: 'center',
      borderRadius: '8px', background: 'rgba(0,0,0,0.55)', color: '#fff',
      fontSize: '18px', cursor: 'pointer', zIndex: '21',
      userSelect: 'none', display: 'none',
    } as any);
    this.rendererContainerElement.appendChild(showBtn);
    this.magShowBtn = showBtn;

    // Zoom-level bar (x2 / x4 / x6 / x8 / x10) under the loupe.
    const zoomBar = document.createElement('div');
    Object.assign(zoomBar.style, {
      position: 'absolute', top: (12 + size + 6) + 'px', left: '12px',
      width: size + 'px', display: 'flex', gap: '4px', justifyContent: 'center',
      zIndex: '21', userSelect: 'none',
    } as any);
    const levels = [2, 4, 6, 8, 10];
    const zoomBtns: HTMLDivElement[] = [];
    const refreshZoom = () => zoomBtns.forEach((b, i) =>
      b.style.background = this.magZoom === levels[i] ? 'rgba(59,130,246,0.95)' : 'rgba(0,0,0,0.55)');
    levels.forEach(lv => {
      const b = document.createElement('div');
      b.textContent = 'x' + lv;
      Object.assign(b.style, {
        padding: '3px 9px', borderRadius: '6px', color: '#fff',
        font: 'bold 12px sans-serif', cursor: 'pointer', background: 'rgba(0,0,0,0.55)',
      } as any);
      b.addEventListener('click', () => { this.magZoom = lv; refreshZoom(); });
      zoomBar.appendChild(b);
      zoomBtns.push(b);
    });
    this.rendererContainerElement.appendChild(zoomBar);
    this.magZoomBar = zoomBar;
    refreshZoom();

    closeBtn.addEventListener('click', () => {
      this.magEnabled = false;
      canvas.style.display = 'none';
      closeBtn.style.display = 'none';
      zoomBar.style.display = 'none';
      showBtn.style.display = 'block';
    });
    showBtn.addEventListener('click', () => {
      this.magEnabled = true;
      showBtn.style.display = 'none';
      closeBtn.style.display = 'block';   // ✕ back; the loupe canvas reappears on next mouse move
      zoomBar.style.display = 'flex';
    });

    // Start CLOSED — the user opens the magnifier with 🔍 only when they want it.
    this.magEnabled = false;
    canvas.style.display = 'none';
    closeBtn.style.display = 'none';
    zoomBar.style.display = 'none';
    showBtn.style.display = 'block';
  }

  // Each frame: point the loupe camera at the square of screen under the mouse and render.
  private updateMagnifier() {
    const el = this.magElement, r = this.magRenderer, cam = this.magCamera;
    if (!this.magEnabled || !el || !r || !cam) return;
    if (this.renderer.xr.isPresenting || !this.magMouse) { el.style.display = 'none'; return; }
    const W = this.renderer.domElement.clientWidth;
    const H = this.renderer.domElement.clientHeight;
    if (!W || !H) return;

    const region = this.magSize / this.magZoom;          // sampled screen area (css px)
    let x = this.magMouse.x - region / 2;
    let y = this.magMouse.y - region / 2;
    x = Math.max(0, Math.min(W - region, x));
    y = Math.max(0, Math.min(H - region, y));

    // Copy the main camera exactly, then window the projection to the sampled square.
    cam.position.copy(this.camera.position);
    cam.quaternion.copy(this.camera.quaternion);
    cam.fov = this.camera.fov;
    cam.near = this.camera.near;
    cam.far = this.camera.far;
    cam.aspect = this.camera.aspect;
    cam.clearViewOffset();
    cam.setViewOffset(W, H, x, y, region, region);   // square sub-rect → undistorted zoom
    cam.updateProjectionMatrix();

    el.style.display = 'block';
    r.render(this.scene, cam);
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
