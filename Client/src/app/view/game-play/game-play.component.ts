import {AfterViewInit, Component, ElementRef, ViewChild} from '@angular/core';
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
import {AnimationClip, AnimationMixer, Clock, LoopOnce, VectorKeyframeTrack} from 'three';
import {VRButton} from 'three/examples/jsm/webxr/VRButton';
import * as TWEEN from "@tweenjs/tween.js";

@Component({
  selector: 'app-game-play',
  templateUrl: './game-play.component.html',
  styleUrls: ['./game-play.component.scss']
})
export class GamePlayComponent implements AfterViewInit {

  gameData!: GameData;
  playerData!: PlayerData;

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;
  public scene!: THREE.Scene;
  public camera!: THREE.PerspectiveCamera;
  public renderer!: any;
  public controls!: OrbitControls;
  public loader!: GLTFLoader;
  interactionManager!: InteractionManager;

  allItems: { [key: string]: ItemData } = {};
  animationsObjects:any=[];

  constructor(public signalRService: SignalrService,
              private dalService: DALService) {
  }

  ngOnInit() {
    this.signalRService.startConnection();
    // this.signalRService.addTransferChartDataListener();

    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      this.updateGame(data)

      //this.click1();
    });

  }

  ngAfterViewInit(): void {
    this.initThree();
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
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.addEventListener('change', () => {
      this.renderer.render(this.scene, this.camera);
    });
    this.controls.enableZoom = true
    this.controls.update();


    this.interactionManager = new InteractionManager(this.renderer, this.camera, this.renderer.domElement);

    // Add ambient light
    const ambientLight = new THREE.AmbientLight(0xffffff);
    this.scene.add(ambientLight);

    // Start the animation loop
    this.renderer.setAnimationLoop(  ()=> {

      this.controls.update();
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
    });
    cube.addEventListener('mouseout', (event: any) => {
      let c: any = event.target.userData['c'];
      event.target.material.color.set(c.r, c.g, c.b);
      document.body.style.cursor = 'default';
    });

    this.interactionManager.add(cube);
  }

  // onMouseDown(event: MouseEvent) {
  //
  // }
  //
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

  createGame() {
    this.dalService.createGame().subscribe(gameData => {
      this.loadGame(gameData);
    })
  }

  loadGame(gameData: GameData) {
    console.log(gameData, dayjs().startOf('month').add(1, 'day').set('year', 2018).format('YYYY-MM-DD HH:mm:ss'));

    this.gameData = gameData;
    // TODO !!!! TEMP only !!!!
    this.playerData = this.gameData.players[0];

    forEach(gameData.items, (itemData: ItemData) => {
      this.createItem(itemData, null);
    })
  }


}
