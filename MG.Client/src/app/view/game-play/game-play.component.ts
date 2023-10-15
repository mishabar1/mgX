import {AfterViewInit, Component, ElementRef, ViewChild} from '@angular/core';
import * as THREE from 'three';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader';
import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
import {HttpClient} from '@angular/common/http';
import {DALService} from '../../dal/dal.service';

@Component({
  selector: 'app-game-play',
  templateUrl: './game-play.component.html',
  styleUrls: ['./game-play.component.scss']
})
export class GamePlayComponent implements AfterViewInit {
  checked: any;

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;
  public scene!: THREE.Scene;
  public camera!: THREE.PerspectiveCamera;
  public renderer!: any;
  public controls!: OrbitControls;
  public loader!: GLTFLoader;
  interactionManager!: InteractionManager;

  constructor(public signalRService: SignalrService,
              private dalService: DALService) {
  }

  ngOnInit() {
    this.signalRService.startConnection();
    // this.signalRService.addTransferChartDataListener();

    this.signalRService.hubConnection.on('test', data => {

      console.log(data);
      this.click1();
    })
  }

  ngAfterViewInit(): void {
    this.initThree();
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
    this.scene.background = new THREE.Color(0xffffff)

    // Initialize camera
    this.camera = new THREE.PerspectiveCamera(75, this.rendererContainer.nativeElement.clientWidth / this.rendererContainer.nativeElement.clientHeight, 0.1, 1000);
    this.camera.position.set(-1.8, 0.6, 2.7);

    // Initialize renderer
    this.renderer = new THREE.WebGLRenderer({antialias: true});
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
    const animate = () => {
      requestAnimationFrame(animate);
      this.controls.update();
      this.interactionManager.update();
      this.renderer.render(this.scene, this.camera);
      //console.log("running");
    };
    animate();

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
  }

  randomIntFromInterval(min: number, max: number) { // min and max included
    return Math.random() * (max - min + 1) + min
    //Math.floor(

    //)
  }

  click1() {
    const color = THREE.MathUtils.randInt(0, 0xffffff)
    const geometry = new THREE.BoxGeometry(this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1));
    const material = new THREE.MeshBasicMaterial({color});
    const cube = new THREE.Mesh(geometry, material);
    cube.position.set(this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1), this.randomIntFromInterval(-1, 1));
    this.scene.add(cube);


    cube.userData['who'] = () => {
      console.log('WHOOOOO', cube.id);
    }

    cube.addEventListener('click', (event: any) => {
      event.stopPropagation();
      this.signalRService.testSendXXX1();
      console.log(cube);
    });
    cube.addEventListener('mouseover', (event) => {
      event.target.userData['c'] = event.target.material.clone().color;
      event.target.material.color.set(0xff0000);
      document.body.style.cursor = 'pointer';
    });
    cube.addEventListener('mouseout', (event) => {
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
  loadGame() {
    this.dalService.getGameById('xx').subscribe(gameData => {
      console.log(gameData);
      debugger;
    })
  }
}
