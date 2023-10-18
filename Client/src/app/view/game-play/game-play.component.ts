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
import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
import {HttpClient} from '@angular/common/http';
import {DALService} from '../../dal/dal.service';
import {GameData} from '../../entities/game.data';
import {ItemData} from '../../entities/item.data';
import * as _ from 'lodash';
import {debounce, forEach, isEqual} from 'lodash';
import * as dayjs from 'dayjs';
import {PlayerData} from '../../entities/player.data';
import {UserData} from '../../entities/user.data';
import {V3} from '../../entities/V3';
import {
  AnimationClip,
  AnimationMixer,
  BufferGeometry,
  Clock,
  Line,
  LoopOnce, Matrix4, Raycaster,
  Vector3,
  VectorKeyframeTrack
} from 'three';
import {VRButton} from 'three/examples/jsm/webxr/VRButton';
import * as TWEEN from "@tweenjs/tween.js";
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory';
import {ARButton} from 'three/examples/jsm/webxr/ARButton';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController';
import {ActivatedRoute, Router} from '@angular/router';
import {color} from 'three/examples/jsm/nodes/shadernode/ShaderNodeBaseElements';

@Component({
  selector: 'app-game-play',
  templateUrl: './game-play.component.html',
  styleUrls: ['./game-play.component.scss']
})
export class GamePlayComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges {

  gameData!: GameData;
  playerData!: PlayerData;

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;
  public scene!: THREE.Scene;
  public camera!: THREE.PerspectiveCamera;
  public renderer!: any;
  public orbitControls!: OrbitControls;
  public loader!: GLTFLoader;
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
  constructor(public signalRService: SignalrService,
              private activatedRoute: ActivatedRoute,
              private dalService: DALService) {
  }

  ngOnInit() {

    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');
    console.log(this.gameId);


    this.signalRService.startConnection();
    // this.signalRService.addTransferChartDataListener();

    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      this.updateGame(data);
    });

  }

  ngAfterViewInit(): void {
    this.dalService.getGameById(this.gameId!).subscribe(game=>{
      this.gameData = game;
      this.initThree();
    });
  }

  updateGame(game: any) {

    // items - move / add / remove
    forEach(game.items, item => {
      console.log(item);
      this.updateItem(item,null);
    });
    //TODO - need to handle remove...

    //players - move / add / remove

  }

  updateItem(item: ItemData, parentMesh:any) {

    if (this.allItems[item.id]) {
      // item exist - update position/scale/rotation/actions....
      this.updateItemPosition(this.allItems[item.id],item.position)


    } else {
      //item not exist - just add
      this.createItem(item, parentMesh);
    }

    parentMesh = this.allItems[item.id].mesh;

    forEach(item.items, item => {
      this.updateItem(item,parentMesh);
    });
  }
  deepEqual(object1:any, object2:any) {
    const keys1 = Object.keys(object1);
    const keys2 = Object.keys(object2);

    if (keys1.length !== keys2.length) {
      return false;
    }

    for (const key of keys1) {
      const val1 = object1[key];
      const val2 = object2[key];
      const areObjects = this.isObject(val1) && this.isObject(val2);
      if (
        areObjects && !this.deepEqual(val1, val2) ||
        !areObjects && val1 !== val2
      ) {
        return false;
      }
    }

    return true;
  }

  isObject(object:any) {
    return object != null && typeof object === 'object';
  }
  updateItemPosition(item: ItemData,position:any){
    item.position = new V3(position.x, position.y, position.z);
    // item.mesh!.position.set(position.x, position.y, position.z);
    // this.createMoveAnimation(item.mesh,item.mesh!.position,position)
    if(!this.deepEqual(position,item.mesh!.position) ) {
      new TWEEN.Tween(item.mesh!.position).to(position,300).start();
    }

  }

  // createMoveAnimation(mesh:any, startPosition:any, endPosition:any )  {
  //   mesh.userData.mixer = new AnimationMixer(mesh);
  //   let track = new VectorKeyframeTrack(
  //     '.position',
  //     [0, 1],
  //     [
  //       startPosition.x,
  //       startPosition.y,
  //       startPosition.z,
  //       endPosition.x,
  //       endPosition.y,
  //       endPosition.z,
  //     ]
  //   );
  //   const animationClip = new AnimationClip(undefined, 5, [track]);
  //   const animationAction = mesh.userData.mixer.clipAction(animationClip);
  //   animationAction.setLoop(LoopOnce);
  //   animationAction.play();
  //   mesh.userData.clock = new Clock();
  //   this.animationsObjects.push(mesh);
  // };

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
    this.scene.background = new THREE.Color(0xffffff)

    // Initialize camera
    this.camera = new THREE.PerspectiveCamera(75, this.rendererContainer.nativeElement.clientWidth / this.rendererContainer.nativeElement.clientHeight, 0.1, 1000);
    this.camera.position.set(-1.8, 0.6, 2.7);

    // Initialize renderer
    this.renderer = new THREE.WebGLRenderer({antialias: true});
    this.renderer.xr.enabled = true;
    this.renderer.setSize(this.rendererContainer.nativeElement.clientWidth, this.rendererContainer.nativeElement.clientHeight);
    this.rendererContainer.nativeElement.appendChild(this.renderer.domElement);

    // Initialize OrbitControls
    this.orbitControls = new OrbitControls(this.camera, this.renderer.domElement);
    this.orbitControls.addEventListener('change', () => {
      this.renderer.render(this.scene, this.camera);
    });
    this.orbitControls.enableZoom = true
    this.orbitControls.update();


    this.interactionManager = new InteractionManager(this.renderer, this.camera, this.renderer.domElement);

    // Add ambient light
    const ambientLight = new THREE.AmbientLight(0xffffff);
    this.scene.add(ambientLight);

    // Start the animation loop
    this.renderer.setAnimationLoop(  ()=> {

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

    // const animate = () => {
    //   requestAnimationFrame(animate);
    //   this.controls.update();
    //   this.interactionManager.update();
    //   this.renderer.render(this.scene, this.camera);
    //
    //   this.animationsObjects.forEach((mesh:any) => {
    //     if (mesh.userData.clock && mesh.userData.mixer) {
    //       mesh.userData.mixer.update(mesh.userData.clock.getDelta());
    //     }
    //   });
    //   //console.log("running");
    // };
    // animate();

    // // Load 3D model
    // this.loader.load('../../../assets/gltf/new/ss.glb', (gltf) => {
    //   const model = gltf.scene;
    //   model.position.set(0, 1, 0);
    //   this.scene.add(model);
    //

    //
    //   // Start the animation loop
    //   const animate = () => {
    //     requestAnimationFrame(animate);
    //     this.controls.update();
    //     this.renderer.render(this.scene, this.camera);
    //     console.log("running");
    //   };
    //   animate();
    //
    // }, undefined, (error) => {
    //   console.error('Error loading GLTF:', error);
    // });

    document.body.appendChild( VRButton.createButton( this.renderer ) );
    this.controllers = this.buildControllers();
    this.controllers.forEach((controller:any) => {
      controller.addEventListener('selectstart', this.onSelectStart);
      controller.addEventListener('selectend', this.onSelectEnd);
    });


    // @ts-ignore
    // document.body.appendChild(ARButton.createButton(this.renderer, {sessionInit: {requiredFeatures: ['hit-test']}}));
    // this.controller = this.renderer.xr.getController(0);
    // this.controller.addEventListener('select', this.onSelect.bind(this));

    this.loadGame();
  }
  onSelect() {
    if (this.reticle.visible) {
      this.box.position.setFromMatrixPosition(this.reticle.matrix);
      this.box.position.y += this.box.geometry.parameters.height / 2;
      this.box.visible = true;
    }
  }
  async requestHitTestSource() {
    const session = this.renderer.xr.getSession();
    session.addEventListener('end', () => {
      this.hitTestSourceRequested = false;
      this.hitTestSource = null;
    });
    const referenceSpace = await session.requestReferenceSpace('viewer');
    this.hitTestSource = await session.requestHitTestSource({ space: referenceSpace, entityTypes: ['plane'] });
    this.hitTestSourceRequested = true;
  }
  getHitTestResults(frame:any) {
    const hitTestResults = frame.getHitTestResults(this.hitTestSource);
    if (hitTestResults.length) {
      const hit = hitTestResults[0];
      const pose = hit.getPose(this.renderer.xr.getReferenceSpace());
      this.reticle.visible = true;
      this.reticle.matrix.fromArray(pose.transform.matrix);
    } else {
      this.reticle.visible = false;
    }
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
  randomIntFromInterval(min: number, max: number) { // min and max included
    return Math.random() * (max - min + 1) + min
    //Math.floor(

    //)
  }


  createItem(itemData: ItemData, parentMesh: THREE.Mesh | null) {
    const color = THREE.MathUtils.randInt(0, 0xffffff)
    //const geometry = new THREE.BoxGeometry(this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1));
    const geometry = new THREE.BoxGeometry(0.1, 0.1, 0.1);
    const material = new THREE.MeshBasicMaterial({color});
    const cube = new THREE.Mesh(geometry, material);
    //cube.position.set(this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1));
    cube.position.set(itemData.position.x, itemData.position.y, itemData.position.z);

    if (parentMesh) {
      parentMesh.add(cube);
    } else {
      this.scene.add(cube);
    }
    const pId = this.gameData.players[0].id;
    cube.userData['ItemData'] = itemData;

    let action = itemData.clickActions[pId] || itemData.clickActions[''];
    if (action) {
      this.addClickAction(cube, itemData, action);
    }

    forEach(itemData.items, (itemData: ItemData) => {
      this.createItem(itemData, cube);
    });

    itemData.mesh = cube;
    this.allItems[itemData.id] = itemData;
  }

  private addClickAction(cube: THREE.Mesh, itemData: ItemData, action: string) {
    cube.addEventListener('click', (event: any) => {
      event.stopPropagation();
      //this.signalRService.testSendXXX1();
      console.log(cube);

      this.signalRService.executeAction(
        this.gameData.id,
        this.playerData.id,
        itemData.id,
        action,
        '', 0, 0);

    });
    cube.addEventListener('mouseover', (event: any) => {
      event.target.userData['c'] = event.target.material.clone().color;
      event.target.material.color.set(0xff0000);
      document.body.style.cursor = 'pointer';
      this.orbitControls.enabled = false;
    });
    cube.addEventListener('mouseout', (event: any) => {
      let c: any = event.target.userData['c'];
      event.target.material.color.set(c.r, c.g, c.b);
      document.body.style.cursor = 'default';
      this.orbitControls.enabled = true;
    });

    this.interactionManager.add(cube);
  }


  // onMouseUp(event: MouseEvent) {
  //
  //   let raycaster = new THREE.Raycaster();
  //   let mouse = new THREE.Vector2();
  //   event.preventDefault();
  //
  //   mouse.x = ( event.clientX / this.renderer.domElement.clientWidth ) * 2 - 1;
  //   mouse.y = - ( event.clientY / this.renderer.domElement.clientHeight ) * 2 + 1;
  //
  //   raycaster.setFromCamera( mouse, this.camera );
  //
  //   var intersects = raycaster.intersectObjects( this.scene.children );
  //
  //   if ( intersects.length > 0 ) {
  //     debugger;
  //     intersects[0].object.userData["who"] ();
  //
  //   }
  // }



  loadGame() {
    // console.log(gameData, dayjs().startOf('month').add(1, 'day').set('year', 2018).format('YYYY-MM-DD HH:mm:ss'));

    // this.gameData = gameData;
    // TODO !!!! TEMP only !!!!
    this.playerData = this.gameData.players[0];

    forEach(this.gameData.items, (itemData: ItemData) => {
      this.createItem(itemData, null);
    })
  }

  ngOnChanges(changes: SimpleChanges): void {
  }

  ngOnDestroy(): void {
  }


}
