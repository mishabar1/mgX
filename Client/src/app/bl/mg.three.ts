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
// The input layer @pmndrs/uikit officially asks for. Its vanilla docs are explicit: "since three.js
// ships no event system, no event system is available out of the box", and they point at exactly
// this package. It is the same monorepo (pmndrs/xr) and the same version line as @react-three/xr,
// so it is also the path that carries VR controller rays later.
import {forwardHtmlEvents} from '@pmndrs/pointer-events';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment.js';
import * as ThreeMeshUI from 'three-mesh-ui';
import * as TWEEN from '@tweenjs/tween.js';
import {environment} from '../../environments/environment';

// Game assets are hosted by the SERVER (GameContent/ → /games, /heads). Client just points at it.
export const GAMES_BASE = environment.serverURL + '/games/';
export const HEADS_BASE = environment.serverURL + '/heads/';
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
  hoverOutlinePass?: OutlinePass;   // second pass: a different-colour glow on the hovered clickable item

  // VR fallback highlight (OutlinePass can't run in WebXR): bright rings on the ground under the
  // hovered (cyan) / selected (green) item.
  hoverRing?: THREE.Mesh;
  selectRing?: THREE.Mesh;
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
  /** @pmndrs/pointer-events, forwarding canvas pointer events into the scene. See initThree. */
  pointerEvents?: { update: () => void, destroy: () => void };

  /**
   * In-scene UI that OCCLUDES the board: the control panels (mg.panel3d parents its group to the
   * camera). Registered here so the InteractionManager can tell that a click landed on a panel
   * and must not also reach whatever is behind it. Kept as a list because a seat can have
   * several docked panels under one group.
   */
  uiBlockers: THREE.Object3D[] = [];

  /**
   * Supplies the in-scene UI's clickable objects to the VR controller ray.
   *
   * A recursive raycast CANNOT find them: uikit's Component.raycast always returns false, and
   * three reads that as "do not descend", so the sweep stops at a panel's root Container. Without
   * this the controller ray walked straight past every button and returned the board item BEHIND
   * the panel — so VR panel buttons did nothing and the trigger poked the board through the UI.
   */
  uiHitTargets?: () => any[];

  /**
   * Distance along `ray` to the nearest UI panel, or Infinity if it misses. Wired into
   * InteractionManager.blockerTest in initThree.
   */
  private uiBlockDistance(ray: THREE.Raycaster): number {
    if (!this.uiBlockers.length) return Infinity;
    let best = Infinity;
    for (const b of this.uiBlockers) {
      if (!b.visible) continue;
      // `true` = recurse: the group holds one child group per docked panel, and the uikit
      // Containers inside them override raycast(), so they intersect properly.
      const hits = ray.intersectObject(b, true);
      if (hits.length && hits[0].distance < best) best = hits[0].distance;
    }
    return best;
  }

  private uiHover = false;
  /**
   * Called by the in-scene UI while the cursor is over one of its panels.
   *
   * OrbitControls and the panel listen for wheel and drag on the SAME canvas, and neither yields —
   * @pmndrs/pointer-events deliberately never calls stopPropagation, which is what makes it safe to
   * add but also means a scroll gesture over a panel zooms the camera at the same time. Whoever is
   * under the cursor should win, and that is the panel; the camera gets the rest of the screen.
   */
  setUiHover(over: boolean) {
    if (this.uiHover === over) return;
    this.uiHover = over;
    if (this.orbitControls) this.orbitControls.enabled = !over;
  }

  /** True while the cursor is over an in-scene panel. */
  get isUiHovered(): boolean { return this.uiHover; }

  // Cache textures by URL so re-created items (e.g. cards rebuilt every action) reuse the
  // same THREE.Texture instead of re-fetching/decoding the image each time.
  private texCache: { [url: string]: { tex: any, ready: boolean, waiters: ((t: any) => void)[] } } = {};
  getTexture(url: string, onReady?: (t: any) => void): any {
    let e = this.texCache[url];
    if (!e) {
      e = { tex: null, ready: false, waiters: [] };
      this.texCache[url] = e;
      e.tex = this.textureLoader.load(url, (t: any) => { e.ready = true; e.waiters.forEach(w => w(t)); e.waiters = []; });
      // Art files are authored in sRGB. Without declaring that, three samples them as linear and
      // the sRGB output transform washes them out (pale, low-saturation tiles/cards).
      e.tex.colorSpace = THREE.SRGBColorSpace;
    }
    if (onReady) { if (e.ready) onReady(e.tex); else e.waiters.push(onReady); }
    return e.tex;
  }

  controllers: any;

  /**
   * Notified when a WebXR session starts or ends. Features that must move between the desktop
   * layout and the headset subscribe here instead of polling `xr.isPresenting` every frame —
   * the in-scene panel uses it to hop from the table onto a controller and back.
   */
  onXrSessionChange?: (presenting: boolean) => void;
  // Set by MgGame: dispatch a game click for the item a VR controller pointed at.
  vrClickHandler?: (mesh: any, point: any) => void;
  // Set by MgGame: the item currently under a VR controller ray (or null) — drives the hover glow.
  vrHoverHandler?: (mesh: any) => void;
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
  /**
   * Top offset of the whole loupe cluster (canvas, its ✕, the 🔍 restore button and the zoom bar).
   * Everything used to sit at top:12px — the same corner as the game-play toolbar — so the ✕ landed
   * on top of the "Games list" button. Kept as ONE value so the four elements never drift apart.
   */
  private magTop = 64;                      // clears the toolbar above it
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
    this.hoverOutlinePass?.setSize(w, h);
    this.renderer.render(this.scene, this.camera);
  };

  // Draw the glowing selection contour around these objects (empty = none).
  setOutlined(objects: THREE.Object3D[]) {
    if (this.outlinePass) this.outlinePass.selectedObjects = objects;
  }

  // The hover glow (cyan) around the clickable item under the cursor (empty = none).
  setHovered(objects: THREE.Object3D[]) {
    if (this.hoverOutlinePass) this.hoverOutlinePass.selectedObjects = objects;
  }

  // VR ground-ring highlights (used instead of the OutlinePass while presenting).
  setHoverRing(pos: THREE.Vector3 | null) {
    if (!this.hoverRing) return;
    if (pos) { this.hoverRing.position.set(pos.x, 0.25, pos.z); this.hoverRing.visible = true; }
    else this.hoverRing.visible = false;
  }
  setSelectRing(pos: THREE.Vector3 | null) {
    if (!this.selectRing) return;
    if (pos) { this.selectRing.position.set(pos.x, 0.2, pos.z); this.selectRing.visible = true; }
    else this.selectRing.visible = false;
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
      const url = GAMES_BASE + assetRelUrl;
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
    // Its listeners sit on a canvas we are about to throw away — detach or they leak per view.
    try { this.pointerEvents?.destroy(); this.pointerEvents = undefined; } catch {}
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
    texture.colorSpace = THREE.SRGBColorSpace;   // sky photos are sRGB too
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

    // Hover glow — a cyan contour on whatever clickable item the mouse is over.
    this.hoverOutlinePass = new OutlinePass(new THREE.Vector2(w0, h0), this.scene, this.camera);
    this.hoverOutlinePass.edgeStrength = 4;
    this.hoverOutlinePass.edgeGlow = 0.5;
    this.hoverOutlinePass.edgeThickness = 1.5;
    this.hoverOutlinePass.visibleEdgeColor.set('#38d6ff');
    this.hoverOutlinePass.hiddenEdgeColor.set('#0e4a5c');
    this.composer.addPass(this.hoverOutlinePass);

    this.composer.addPass(new OutputPass());   // re-apply tone-mapping + sRGB so the scene isn't dark

    // VR highlight rings (flat, unlit so they're always bright). Hidden until used.
    const mkRing = (hex: number) => {
      const m = new THREE.Mesh(
        new THREE.TorusGeometry(1.1, 0.13, 12, 48),
        new THREE.MeshBasicMaterial({ color: hex, transparent: true, opacity: 0.9 }));
      m.rotation.x = Math.PI / 2;   // lie flat on the ground
      m.visible = false;
      this.scene.add(m);
      return m;
    };
    this.hoverRing = mkRing(0x38d6ff);
    this.selectRing = mkRing(0x3dff6a);

    // Refresh the canvas whenever the window resizes.
    window.addEventListener('resize', this.onWindowResize);

    // ---- VR view preservation --------------------------------------------------------------
    // Entering VR keeps the player's current viewpoint: remember the desktop camera pose here,
    // then (on the first tracked frame — see alignXrToDesktopView) shift the XR reference space
    // so the headset starts exactly at that position, turned to face the same spot.
    this.renderer.xr.addEventListener("sessionstart", () => {
      this.cameraBeforeVr = { pos: this.camera.position.clone(), target: this.orbitControls.target.clone() };
      this.xrDesiredView  = { pos: this.camera.position.clone(), target: this.orbitControls.target.clone() };
      // Clear any stale pose left from a previous VR session, so alignXrToDesktopView only fires
      // once THIS session's first real headset pose has been written (identity = not yet tracked).
      this.renderer.xr.getCamera().matrixWorld.identity();
      try { this.onXrSessionChange?.(true); } catch (err) { console.error('[xr] onXrSessionChange(true) threw', err); }
    });
    this.renderer.xr.addEventListener('sessionend', () => {
      // Leaving VR: restore the desktop camera to where it was before the session (WebXR has
      // been writing headset poses into it every frame).
      this.xrDesiredView = null;
      if (this.cameraBeforeVr) {
        this.camera.position.copy(this.cameraBeforeVr.pos);
        this.orbitControls.target.copy(this.cameraBeforeVr.target);
        this.orbitControls.update();
        this.cameraBeforeVr = null;
      }
      try { this.onXrSessionChange?.(false); } catch (err) { console.error('[xr] onXrSessionChange(false) threw', err); }
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
    // Board items must not be clickable through a control panel — see uiBlockers above.
    this.interactionManager.blockerTest = (ray: THREE.Raycaster) => this.uiBlockDistance(ray);

    // ---- @pmndrs/pointer-events: the input system @pmndrs/uikit needs (the 3D panel) ----------
    // It only ADDS listeners to the canvas (pointermove / pointerover / pointerdown / pointerup /
    // wheel / pointercancel / pointerleave) and never calls preventDefault or stopPropagation —
    // read out of its own forward.js, not assumed. So OrbitControls above and the
    // InteractionManager keep receiving every event exactly as before; this is additive.
    //
    // SCOPING, and this is the part that matters: `pointerEvents` is INHERITED down the scene graph
    // (`object.pointerEvents ?? parentPointerEvents`), and uikit sets `defaultPointerEvents = 'auto'`
    // on its components. Switching the SCENE off here means board items — meshes that carry click
    // listeners for the InteractionManager — are never targeted by this system, so no action can
    // fire twice. The 3D panel opts its own subtree back in (mg.panel3d.ts sets pointerEvents on the
    // pane root). One system per kind of object, decided in one place.
    (this.scene as any).pointerEvents = 'none';
    this.pointerEvents = forwardHtmlEvents(this.renderer.domElement, () => this.camera, this.scene, {
      // NOT the library default (false). The default only re-intersects the scene when the POINTER
      // moves, which assumes the scene holds still while the pointer does. Ours does the opposite:
      // the panel is parented to the CAMERA, so it moves whenever the view moves, and it is rebuilt
      // from scratch after most actions — the object that was under the cursor no longer exists.
      //
      // Observed: HP "+" took 8 -> 9, a second identical click at the same pixel did nothing, and a
      // 1px mouse nudge made the next one land. Re-intersecting every frame is the documented cure.
      intersectEveryFrame: true,
    });

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

  // Per-frame callbacks from features that need one (e.g. the in-scene panel's UI library).
  private frameHooks: ((deltaMs: number) => void)[] = [];
  private frameClock = { last: undefined as number | undefined };
  private loggedFrameErrors = new Set<string>();

  addFrameHook(fn: (deltaMs: number) => void) { this.frameHooks.push(fn); }
  removeFrameHook(fn: (deltaMs: number) => void) {
    const i = this.frameHooks.indexOf(fn);
    if (i >= 0) this.frameHooks.splice(i, 1);
  }

  private frameDelta(): number {
    const now = performance.now();
    const d = this.frameClock.last == null ? 0 : now - this.frameClock.last;
    this.frameClock.last = now;
    return d;
  }

  /**
   * Run one frame participant, isolated. A throw here must never skip renderer.render() below —
   * that blanks the whole canvas, board included. The participant is KEPT (a UI library needs
   * every frame; dropping it turns one bad frame into a permanently invisible panel) and the
   * error is reported once so the log does not fill up at 60fps.
   */
  private safeFrame(label: string, fn: () => void): boolean {
    try { fn(); return true; }
    catch (err) {
      if (!this.loggedFrameErrors.has(label)) {
        this.loggedFrameErrors.add(label);
        console.error(`[frame] "${label}" threw and was disabled for this session; the scene keeps rendering.`, err);
      }
      return false;
    }
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

    // Third-party UI libraries need a per-frame update, and this runs BEFORE renderer.render()
    // below — so an exception in one of them skips the render and blanks the entire canvas, board
    // included. Each is therefore isolated: a hook that throws is reported once and dropped, and
    // the frame still draws.
    this.safeFrame('three-mesh-ui', () => ThreeMeshUI.update());
    const delta = this.frameDelta();
    for (const hook of this.frameHooks) this.safeFrame('frameHook', () => hook(delta));

    if (this.controllers) {
      this.controllers.forEach((controller: any) => {
        this.handleController(controller);
      })
    }

    // VR entry: align the rig to the pre-VR desktop view (no-op once applied / when not in VR).
    if (this.renderer.xr.isPresenting && this.xrDesiredView) this.alignXrToDesktopView();

    this.orbitControls.update();
    // pointer-events batches per frame and flushes here (its docs: call update() every frame).
    // Isolated like the other frame participants — a throw must not skip the render below.
    this.safeFrame('pointer-events', () => this.pointerEvents?.update());
    this.interactionManager.update();
    // EffectComposer (OutlinePass) can't render to the WebXR framebuffer — use the plain
    // renderer in VR, the composer (with the selection/hover glow) otherwise.
    if (this.composer && !this.renderer.xr.isPresenting) this.composer.render();
    else this.renderer.render(this.scene, this.camera);
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
      position: 'absolute', top: this.magTop + 'px', left: '12px',
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
      position: 'absolute', top: (this.magTop + 4) + 'px', left: (15) + 'px',
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
      position: 'absolute', top: this.magTop + 'px', left: '12px',
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
      position: 'absolute', top: (this.magTop + size + 6) + 'px', left: '12px',
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

  // Cast a ray from the controller; return the first item (with click actions) it hits, and
  // shorten the pointer line to it. Skips cards/labels/terrain/sprites.
  private vrRaycastItem(controller: XRTargetRaySpace): { mesh: any, point: any } | null {
    const rot = new Matrix4().extractRotation(controller.matrixWorld);
    const ray = new Raycaster();
    ray.camera = this.camera;   // REQUIRED so raycasting sprites doesn't throw (blanking the scene)
    ray.ray.origin.setFromMatrixPosition(controller.matrixWorld);
    ray.ray.direction.set(0, 0, -1).applyMatrix4(rot);
    if (controller.children[0]) (controller.children[0] as any).scale.z = 10;

    // The UI first, and NON-recursively — each widget's own raycast still reports an
    // intersection, it just refuses to pass the ray to its children.
    const uiTargets = this.uiHitTargets?.() ?? [];
    const uiHits = uiTargets.length ? ray.intersectObjects(uiTargets, false) : [];
    const uiHit = uiHits.length ? uiHits[0] : null;

    const hits = ray.intersectObjects(this.scene.children, true);
    for (const h of hits) {
      // Anything at or behind the panel is occluded by it — same rule as the mouse.
      if (uiHit && uiHit.distance <= h.distance) break;
      let o: any = h.object;
      while (o && !(o.userData && o.userData['ItemData'] && o.userData['ItemData'].clickActions
                    && Object.keys(o.userData['ItemData'].clickActions).length)) o = o.parent;
      if (o) {
        if (controller.children[0]) (controller.children[0] as any).scale.z = h.distance;
        return { mesh: o, point: h.point };
      }
    }

    if (uiHit) {
      if (controller.children[0]) (controller.children[0] as any).scale.z = uiHit.distance;
      return { mesh: uiHit.object, point: uiHit.point };
    }
    return null;
  }

  handleController(controller: XRTargetRaySpace) {
    const pressed = controller.userData["selectPressed"];
    const prev = controller.userData["selectPressedPrev"];
    const hit = this.vrRaycastItem(controller);
    this.vrHoverHandler?.(hit ? hit.mesh : null);            // continuous hover glow
    if (pressed && !prev && hit) {
      // A panel widget carries `uiArgs.fire` (mg.panel3d.makeInteractive). Run it directly.
      //
      // Without this, VR panel buttons were dead: vrRaycastItem finds them because they carry a
      // synthetic ItemData whose clickActions value is the sentinel '__panel3d', so the click
      // went to MgGame.MeshClickFunc, which dutifully sent "__panel3d" to the server as an
      // action name — and the server rejected it as not a registered [GameAction]. `uiArgs` was
      // written for exactly this and never read by anything.
      const fire = hit.mesh?.userData?.['uiArgs']?.fire;
      if (typeof fire === 'function') fire();
      else this.vrClickHandler?.(hit.mesh, hit.point);       // trigger = click on a board item
    }
    controller.userData["selectPressedPrev"] = pressed;
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



  onSelectStart = (x: any) => { if (x?.target) x.target.userData['selectPressed'] = true; };
  onSelectEnd = (x: any) => { if (x?.target) x.target.userData['selectPressed'] = false; };

  /**
   * The LEFT controller, by handedness rather than index. Null outside a session, or before the
   * input sources have reported themselves — callers then fall back to the camera.
   */
  leftController(): any | null {
    const list: any[] = this.controllers || [];
    return list.find(c => c?.userData?.['handedness'] === 'left')
        || (this.renderer?.xr?.isPresenting ? (list[0] || null) : null);
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
      // WHICH HAND. getController(i) is connection order, not handedness — so "put this on the left
      // controller" cannot be answered by an index. The XR input source reports it on connect.
      controller.addEventListener('connected', (e: any) => {
        controller.userData['handedness'] = e?.data?.handedness || '';
      });
      controller.addEventListener('disconnected', () => {
        controller.userData['handedness'] = '';
      });
      this.scene.add(controller);
      controllers.push(controller);

      const grip = this.renderer.xr.getControllerGrip(i);
      grip.add(controllerModelFactory.createControllerModel(grip));
      this.scene.add(grip);
    }

    return controllers;
  }

  // ---- VR view preservation ----------------------------------------------------------------
  // Where the desktop camera was when the session started (restored on exit), and the view the
  // XR rig must be aligned to (consumed by alignXrToDesktopView on the first tracked frame).
  private cameraBeforeVr: { pos: THREE.Vector3, target: THREE.Vector3 } | null = null;
  private xrDesiredView: { pos: THREE.Vector3, target: THREE.Vector3 } | null = null;

  // Move the XR reference space so the headset appears exactly at the desktop camera's position,
  // yawed to face the orbit target (yaw only — pitch stays physical, the user just looks down at
  // the board like they would in real life). Runs once per session, on the first frame that has
  // a real headset pose: only then do we know the player's physical head height/offset, so the
  // delta between "where the head is" and "where it should be" is exact.
  private alignXrToDesktopView() {
    const base: any = this.renderer.xr.getReferenceSpace();
    if (!base || !this.xrDesiredView) return;

    const xrCam = this.renderer.xr.getCamera();
    const head = new THREE.Vector3().setFromMatrixPosition(xrCam.matrixWorld);
    if (head.lengthSq() < 1e-9) return;   // identity matrix = no tracked pose yet, try next frame

    const d = this.xrDesiredView;
    this.xrDesiredView = null;            // apply once

    // yaw delta between where the head currently looks and the desktop view direction
    const headDir = new THREE.Vector3(0, 0, -1)
      .applyQuaternion(new THREE.Quaternion().setFromRotationMatrix(xrCam.matrixWorld));
    const desDir = new THREE.Vector3().subVectors(d.target, d.pos);
    const yawDelta = Math.atan2(desDir.x, desDir.z) - Math.atan2(headDir.x, headDir.z);

    // M maps the current head pose onto the desired one: translate the head to the origin,
    // yaw it, then translate to the desktop camera position.
    const M = new THREE.Matrix4().makeTranslation(d.pos.x, d.pos.y, d.pos.z)
      .multiply(new THREE.Matrix4().makeRotationY(yawDelta))
      .multiply(new THREE.Matrix4().makeTranslation(-head.x, -head.y, -head.z));

    // getOffsetReferenceSpace expects the transform of the NEW space's origin expressed in the
    // old space (pose_new = O⁻¹ · pose_old), so pass O = M⁻¹.
    const p = new THREE.Vector3(), q = new THREE.Quaternion(), s = new THREE.Vector3();
    M.invert().decompose(p, q, s);
    const XRT: any = (window as any).XRRigidTransform;
    this.renderer.xr.setReferenceSpace(
      base.getOffsetReferenceSpace(new XRT({ x: p.x, y: p.y, z: p.z, w: 1 },
                                           { x: q.x, y: q.y, z: q.z, w: q.w })));
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
