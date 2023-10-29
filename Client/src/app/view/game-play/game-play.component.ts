import {
  AfterViewInit,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild
} from '@angular/core';
import * as THREE from 'three';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader';
// import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
import {HttpClient} from '@angular/common/http';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {ItemData} from '../../entities/item.data';
import * as _ from 'lodash';
import {debounce, filter, find, forEach, isEqual, keys} from 'lodash';
import * as dayjs from 'dayjs';
import {PlayerData} from '../../entities/player.data';
import {UserData} from '../../entities/user.data';
import {V3} from '../../entities/V3';
import {
  AnimationClip,
  AnimationMixer,
  BufferGeometry,
  Clock,
  Line, Loader,
  LoopOnce, MathUtils, Matrix4, Mesh, MeshBasicMaterial, PlaneGeometry, Raycaster, TextureLoader,
  Vector3,
  VectorKeyframeTrack
} from 'three';
import * as TWEEN from "@tweenjs/tween.js";
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController';
import {ActivatedRoute, Router} from '@angular/router';
import {color} from 'three/examples/jsm/nodes/shadernode/ShaderNodeBaseElements';
import {RouteNames} from '../../app-routing.module';
import {environment} from '../../../environments/environment';
import {Group} from 'three/src/objects/Group';
import {GeneralService} from '../../bl/general.service';
import {UnsubscriberService} from '../../services/unsubscriber.service';
import {RoomEnvironment} from 'three/examples/jsm/environments/RoomEnvironment';
import {STLLoader} from 'three/examples/jsm/loaders/STLLoader';
import {OBJLoader} from 'three/examples/jsm/loaders/OBJLoader';
import {FontLoader} from 'three/examples/jsm/loaders/FontLoader';
import {TextGeometry} from 'three/examples/jsm/geometries/TextGeometry';
import * as ThreeMeshUI from 'three-mesh-ui'
import {VRButton} from 'three/examples/jsm/webxr/VRButton';
import {InteractionManager} from '../../services/mg.interaction.manager';

@Component({
  selector: 'app-game-play',
  templateUrl: './game-play.component.html',
  styleUrls: ['./game-play.component.scss'],
  providers: [UnsubscriberService]
})
export class GamePlayComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  gameData!: GameData;
  playerData!: PlayerData;

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;
  scene!: THREE.Scene;
  cameraGroup!:Group;
  camera!: THREE.PerspectiveCamera;
  audioListener!: THREE.AudioListener;
  renderer!: THREE.WebGLRenderer;
  orbitControls!: OrbitControls;
  gltfLoader!: GLTFLoader;
  stlLoader!: STLLoader  ;
  objLoader!: OBJLoader  ;
  textureLoader!: TextureLoader;
  fontLoader!: FontLoader;
  interactionManager!: InteractionManager;

  controllers:any;
  selectedObject:any;
  interactionObjects:any=[];
  selectedObjectDistance:any;
  objectUnselectedColor="red";
  objectSelectedColor="blue";


  controller:any;
  reticle:any;
  box:any;
  hitTestSourceRequested:any;
  hitTestSource:any;

  allItems: { [key: string]: ItemData } = {};
  // animationsObjects:any=[];

  gameId:string|null = "";

  debugObjects:any[]=[];

  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private activatedRoute: ActivatedRoute,
              private unsubscriberService: UnsubscriberService,
              private dalService: DALService) {
  }

  ngOnInit() {

    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');
    console.log(this.gameId);


    // this.signalRService.addTransferChartDataListener();
    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      this.updateGame(data);
    });

  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection.off('GameUpdated');
  }

  ngAfterViewInit(): void {
    this.dalService.getGameById(this.gameId!).subscribe(game=>{
      if(!game){
        this.router.navigate([RouteNames.GamesList]);
        return;
      }
      this.gameData = game;
      this.playerData = this.getPlayerByUserId(this.generalService.User!.id)!;

      this.initThree();
    });
  }

  getPlayerByUserId(userId:string):PlayerData | null | undefined {
    return find(this.gameData.players,p=> p.user?.id==userId);
  }
  updateGame(new_game: GameData) {
    //console.log("updateGame",new_game);

    //mark all items to delete - and each item that updated - will be mrked "not"
    forEach(this.allItems, (item, key) => {
      item.markForDelete=true;
    });

    this.updateItem(new_game.table,null);

    forEach(this.allItems, (item, key) => {
      if(item.markForDelete){
        this.removeAction(item);

        if(item.playType){
          (item.mesh!.children[0] as THREE.PositionalAudio).stop()
        }
        item.mesh?.parent?.remove(item.mesh);
        delete this.allItems[item.id];
      }
    });

    //players - move / add / remove

  }

  updateItem(new_item: ItemData, parentMesh:any) {
    //console.log("updateItem",new_item, parentMesh);

    let old_item = this.allItems[new_item.id];
    if(!old_item){
      this.createItem(new_item, parentMesh);
      return;
    }

    old_item.markForDelete=false;

    //update props
    old_item.clickActions = new_item.clickActions;
    old_item.visible = new_item.visible;
    old_item.hoverActions = new_item.hoverActions;

    // update position/scale/rotation/actions....
    this.updateItemPosition(old_item, V3.FromJson(new_item.position));
    this.updateItemScale(old_item, V3.FromJson(new_item.scale))
    this.updateItemRotation(old_item, V3.FromJson(new_item.rotation))

    this.updateItemText(old_item, new_item.text)

    //update all child items
    forEach(new_item.items, new_item => {
      this.updateItem(new_item, old_item.mesh);
    });


    this.handleItemClickActions(old_item);

    this.handleItemVisibility(old_item);
  }



  updateItemPosition(item: ItemData, position:V3){
    //console.log("updateItemPosition", item, position);

    item.position = position;
    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    if(!this.generalService.deepEqual(position,item.mesh!.position) ) {
      new TWEEN.Tween(item.mesh!.position).to(position,300).start();
    }

  }

  updateItemScale(item: ItemData, scale:V3){
    //console.log("updateItemScale", item, scale);

    item.scale = scale;
    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    if(!this.generalService.deepEqual(scale,item.mesh!.scale) ) {
      new TWEEN.Tween(item.mesh!.scale).to(scale,300).start();
    }

  }

  updateItemRotation(item: ItemData, rot:V3){
    //console.log("updateItemRotation", item, rot);

    item.rotation = rot;

    rot = {
      x:MathUtils.degToRad( rot.x),
      y:MathUtils.degToRad(rot.y),
      z:MathUtils.degToRad(rot.z)
    }

    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    // let meshRot =new THREE.Vector3();
    // meshRot = meshRot.applyEuler(item.mesh!.rotation);
    if(!this.generalService.deepEqual(rot, {x:item.mesh!.rotation.x,y:item.mesh!.rotation.y,z:item.mesh!.rotation.z} ) ) {
      // const u = new THREE.Euler( rot.x, rot.y, rot.z, 'XYZ' )
      new TWEEN.Tween(item.mesh!.rotation).to(rot,300).start();
    }

  }

  updateItemText(item: ItemData, text?:string){
    if(item.asset=="TEXTBLOCK"){
      item.text = text;
      (item.mesh! as any).childrenTexts[0].set({content: text});
    }
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

  initThree() {
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
    this.camera = new THREE.PerspectiveCamera(75, this.rendererContainer.nativeElement.clientWidth / this.rendererContainer.nativeElement.clientHeight, 0.1, 5000);
    // this.camera.position.set(-1.8, 0.6, 2.7);

    // create an AudioListener and add it to the camera
    this.audioListener = new THREE.AudioListener();
    this.camera.add( this.audioListener );

    // Initialize renderer
    this.renderer = new THREE.WebGLRenderer({antialias: true});
    this.renderer.xr.enabled = true;
    this.renderer.setSize(this.rendererContainer.nativeElement.clientWidth, this.rendererContainer.nativeElement.clientHeight);
    this.rendererContainer.nativeElement.appendChild(this.renderer.domElement);

    this.renderer.xr.addEventListener("sessionstart", () => {


      this.renderer.xr.getCamera().position.copy( this.camera.position);
      this.renderer.xr.getCamera().lookAt( this.orbitControls.target );

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
    const pmremGenerator = new THREE.PMREMGenerator( this.renderer );
    this.scene.environment = pmremGenerator.fromScene( new RoomEnvironment(), 0 ).texture;

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

    // Start the animation loop
    this.renderer.setAnimationLoop(  ()=> {

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

    } );

    // Instantiate a loader
    this.gltfLoader = new GLTFLoader();
    this.stlLoader  = new STLLoader();
    this.objLoader = new OBJLoader();
    this.textureLoader = new TextureLoader();
    this.fontLoader = new FontLoader();

    // const x = VRButton.createButton( this.renderer )
    // document.body.appendChild( x );


    this.loadGame();
  }



  onSelectStart(x:any){
    console.log("onSelectStart",x);
    // this refers to the controller
    // this.children[0].scale.z = 10;
    // this.userData.selectPressed = true;
  }
  onSelectEnd(x:any) {
    console.log("onSelectEnd",x);
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
      const controller:XRTargetRaySpace = this.renderer.xr.getController(i);
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

  handleController(controller:XRTargetRaySpace) {
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


  createItem(itemData: ItemData, parentMesh: THREE.Object3D | null) {
    //console.log("createItem",itemData,parentMesh);

    if(itemData.asset) {
      const frontURL = '\\assets\\games\\' + this.gameData.assets[itemData.asset].frontURL;
      const backURL = '\\assets\\games\\' + (this.gameData.assets[itemData.asset].backURL || this.gameData.assets[itemData.asset].frontURL);
      const assetType = this.gameData.assets[itemData.asset].type;

      if(assetType=="OBJECT") {
        if (frontURL.toLowerCase().endsWith("glb") || frontURL.toLowerCase().endsWith("gltf")) {
          this.gltfLoader.load(frontURL, (gltf) => {
            const mesh: THREE.Group = gltf.scene;
            this.processItem(itemData, mesh, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("stl")) {
          this.stlLoader.load(frontURL, (geometry) => {
            const mesh = new THREE.Mesh(geometry);
            this.processItem(itemData, mesh, parentMesh);
          });
        }

        if (frontURL.toLowerCase().endsWith("obj")) {
          this.objLoader.load(frontURL, (group) => {
            //     // (object.children[0] as THREE.Mesh).material = material
            //     // object.traverse(function (child) {
            //     //     if ((child as THREE.Mesh).isMesh) {
            //     //         (child as THREE.Mesh).material = material
            //     //     }
            //     // })
            this.processItem(itemData, group, parentMesh);
          });
        }
      }

      if(assetType=="TOKEN") {

        this.textureLoader.load(frontURL,frontTexture=>{
          // console.log( frontTexture.image.width, frontTexture.image.height );
          let aspect = frontTexture.image.width / frontTexture.image.height;
          let x = 1;
          let z = 1 / aspect;
          if(aspect < 1){
            z = 1;
            x = aspect;
          }

          var cubeMaterial = [

            new THREE.MeshBasicMaterial({
              //left
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //right
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              // top
              map: frontTexture, transparent:true
            }),
            new THREE.MeshBasicMaterial({
              // bottom
              map: this.textureLoader.load(backURL),transparent:true
            }),
            new THREE.MeshBasicMaterial({
              // front
              color: 0xffffff, opacity: 0.5, transparent: true
            }),
            new THREE.MeshBasicMaterial({
              //back
              color: 0xffffff, opacity: 0.5, transparent: true
            })
          ];

          let  mesh = new THREE.Mesh(new THREE.BoxGeometry(x,x/100,z), cubeMaterial);
          this.processItem(itemData, mesh, parentMesh);

        });


      }

      if(assetType=="TEXT3D") {
        this.fontLoader.load( 'https://threejs.org/examples/fonts/helvetiker_regular.typeface.json',  ( font )=> {

          const geometry = new TextGeometry( 'Hello three.js!', {
            font: font,
            size: 0.5,
            height: 0.2,
            curveSegments: 12,
            bevelEnabled: true,
            bevelThickness: 0.03,
            bevelSize: 0.02,
            bevelOffset: 0,
            bevelSegments: 5,
          } );
          var textMaterial = new THREE.MeshPhongMaterial(
            { color: 0xff0000, specular: 0xffffff }
          );
          let  mesh = new THREE.Mesh(geometry,textMaterial);
          this.processItem(itemData, mesh, parentMesh);
        } );

      }
      if(assetType=="TEXTBLOCK") {

        // DOCS ! - RTFM !
        // https://github.com/felixmariotto/three-mesh-ui/wiki/API-documentation
        //
        const container:any = new ThreeMeshUI.Block( {

          bestFit:'auto',
          width: 100,
          height: 100,
          justifyContent: 'center',
          textAlign: 'center',
          fontFamily: 'assets/fonts/Roboto-msdf.json',
          fontTexture: 'assets/fonts/Roboto-msdf.png',
          fontColor:  new THREE.Color( 0x000000 ),
          // borderRadius: 0.05,
          backgroundOpacity:0
        } );

        // container.position.set( 0, 0, 0 );
        // container.rotation.x = -0.55;
        // this.scene.add( container );

        const t1:any = new ThreeMeshUI.Text( {content: itemData.text } );
        container.add(t1);

        this.processItem(itemData, container, parentMesh);

      }
      if(assetType=="SOUND") {

        // console.log(itemData,frontURL,backURL,assetType);

        // create the PositionalAudio object (passing in the listener)
        const sound = new THREE.PositionalAudio( this.audioListener );

        // load a sound and set it as the Audio object's buffer
        const audioLoader = new THREE.AudioLoader();
        audioLoader.load( frontURL, function( buffer ) {
          sound.setBuffer( buffer );
          sound.setLoop(itemData.playType=="LOOP");
          sound.setVolume(1);
          sound.play();
          // sound.stop()
        });

        let soundGroup = new THREE.Group()
        soundGroup.add(sound);
        this.processItem(itemData, soundGroup, parentMesh);


      }



    }else{
      const mesh: THREE.Group = new THREE.Group()
      this.processItem(itemData, mesh, parentMesh);
    }


  }
  processItem(itemData: ItemData, mesh:THREE.Object3D, parentMesh: THREE.Object3D | null){
    //console.log("processItem",itemData,mesh,parentMesh);

    // position
    mesh.position.set(itemData.position.x, itemData.position.y, itemData.position.z);

    // rotation
    mesh.rotation.set(
      MathUtils.degToRad( itemData.rotation.x),
      MathUtils.degToRad(itemData.rotation.y),
      MathUtils.degToRad(itemData.rotation.z));

    //scale
    mesh.scale.set(itemData.scale.x, itemData.scale.y, itemData.scale.z);


    if (parentMesh) {
      parentMesh.add(mesh);
    } else {
      this.scene.add(mesh);
    }

    mesh.userData['ItemData'] = itemData;
    itemData.mesh = mesh

    forEach(itemData.items, (itemData: ItemData) => {
      this.createItem(itemData, mesh);
    });

    // ClickActions
    this.handleItemClickActions(itemData);

    // visibility
    this.handleItemVisibility(itemData);

    this.allItems[itemData.id] = itemData;
  }

  handleItemClickActions(itemData:ItemData){
    //console.log("handleItemClickActions",itemData);
    let action = null;
    if(this.playerData){
      action = itemData.clickActions[this.playerData.id] || itemData.clickActions[''];
    }
    if (action) {
      this.addClickAction(itemData, action);
    }else{
      this.removeAction(itemData);
    }

  }

  handleItemVisibility(itemData:ItemData){
    //console.log("handleItemVisibility",itemData);
    let isVisible:boolean = keys(itemData.visible).length==0;
    if(this.playerData){
      isVisible = isVisible || itemData.visible[this.playerData.id]==true;
    }
    //console.log("handleItemVisibility","isVisible",isVisible);
    itemData.mesh!.visible = isVisible;

    if(!isVisible){
      this.removeAction(itemData);
    }
  }

  MeshClickFunc(event:any){
    // console.log(event.point);
    // const direction = new THREE.Vector3();
    // direction.subVectors( event.target.position, event.point ) ;
    // console.log(direction);

    if(this.playerData){

      let action = event.target.userData.ItemData.clickActions[this.playerData.id] || event.target.userData.ItemData.clickActions[''];
      this.signalRService.executeAction(
        this.gameData.id,
        this.playerData.id,
        event.target.userData.ItemData.id,
        action,
        '',
        event.point);
    }
  }
  MeshMouseOverFunc(event:any){
    // event.target.userData['c'] = event.target.material.clone().color;
    // event.target.material.color.set(0xff0000);
    document.body.style.cursor = 'pointer';
    this.orbitControls.enabled = false;
  }
  MeshMouseOutFunc(event:any){
    // let c: any = event.target.userData['c'];
    // event.target.material.color.set(c.r, c.g, c.b);
    document.body.style.cursor = 'default';
    this.orbitControls.enabled = true;
  }

  onMeshClickFunc = this.MeshClickFunc.bind(this);
  onMeshMouseOverFunc = this.MeshMouseOverFunc.bind(this);
  onMeshMouseOutFunc = this.MeshMouseOutFunc.bind(this);
  addClickAction(itemData: ItemData, action: string) {
    //console.log("addClickAction", itemData ,action);

    this.removeAction(itemData);

    itemData.mesh!.addEventListener('click', this.onMeshClickFunc );

    itemData.mesh!.addEventListener('mouseover', this.onMeshMouseOverFunc);

    itemData.mesh!.addEventListener('mouseout', this.onMeshMouseOutFunc);

    this.interactionManager.add(itemData.mesh!);
  }
  removeAction(itemData: ItemData) {
    //console.log("removeAction",itemData);
    itemData.mesh!.removeEventListener('click',this.onMeshClickFunc);
    itemData.mesh!.removeEventListener('click',this.onMeshMouseOverFunc);
    this.interactionManager.remove(itemData.mesh!);
  }

  loadGame() {
    console.log("loadGame");
    // console.log(gameData, dayjs().startOf('month').add(1, 'day').set('year', 2018).format('YYYY-MM-DD HH:mm:ss'));
    this.createItem(this.gameData.table, null);

    if(this.playerData){
      this.camera.position.set(this.playerData.camera.position.x,this.playerData.camera.position.y,this.playerData.camera.position.z);
    }else{
      this.camera.position.set(this.gameData.observer.position.x,this.gameData.observer.position.y,this.gameData.observer.position.z);
    }

  }

  ngOnChanges(changes: SimpleChanges): void {
  }


  currentSession:any = null;
  onVrClick() {

    this.controllers = this.buildControllers();
    this.controllers.forEach((controller:any) => {
      controller.addEventListener('selectstart', this.onSelectStart);
      controller.addEventListener('selectend', this.onSelectEnd);
    });

    const sessionInit = { optionalFeatures: [ 'local-floor', 'bounded-floor', 'hand-tracking', 'layers' ] };
    // @ts-ignore
    window.navigator.xr.requestSession( 'immersive-vr', sessionInit ).then( async session=>{
      //session.addEventListener( 'end', this.onSessionEnded );
      await this.renderer.xr.setSession( session );
      this.currentSession = session;
    } );


  }


  loadDebugObjects() {
    this.debugObjects=[];
    this.scene.traverse( ( object:any ) =>{

      // if ( object.isMesh ) {
        this.debugObjects.push(object);
        console.log( object )
      // };

    } );
  }

  showDebugWindow=false;
  getAnyClass(obj:any) {
    if (typeof obj === "undefined") return "undefined";
    if (obj === null) return "null";
    return obj.constructor.name  }

  onRemoveClick(obj: any) {
    obj.removeFromParent();
  }

  showDebug() {
    this.showDebugWindow=true;
  }
}
