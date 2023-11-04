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
import {GameFlowService} from '../../bl/game-flow.service';
import {MgThree} from '../../bl/mg.three';
import {MgGame} from '../../bl/mg.game';

@Component({
  selector: 'app-game-play',
  templateUrl: './game-play.component.html',
  styleUrls: ['./game-play.component.scss'],
  providers: [UnsubscriberService]
})
export class GamePlayComponent implements OnInit, OnDestroy, AfterViewInit, OnChanges {

  gameData!: GameData;
  playerData!: PlayerData;

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;



  // animationsObjects:any=[];

  gameId: string | null = "";

  mgThree!:MgThree;
  mgGame!:MgGame;

  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private activatedRoute: ActivatedRoute,
              private unsubscriberService: UnsubscriberService,
              private gameFlowService:GameFlowService,
              private dalService: DALService) {
  }

  ngOnInit() {

    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');
    console.log(this.gameId);

    // this.signalRService.addTransferChartDataListener();
    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      this.mgGame.updateGame(data);
    });

  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection.off('GameUpdated');
  }

  ngAfterViewInit(): void {

    this.dalService.getGameById(this.gameId!).subscribe(game => {
      if (!game) {
        this.router.navigate([RouteNames.GamesList]);
        return;
      }

      this.mgGame = new MgGame();
      this.mgGame.gameData = game;

      this.gameData = game;
      // this.playerData = this.getPlayerByUserId(this.generalService.User!.id)!;

      this.mgThree=new MgThree();
      this.mgThree.initThree(this.rendererContainer.nativeElement,()=>{
        this.mgGame.loadGame(this.mgThree,this.generalService.User!);
      });
    });
  }



  loadGame() {


  }

  ngOnChanges(changes: SimpleChanges): void {
  }



  onVrClick() {

    this.mgThree.startVr();



  }


}
