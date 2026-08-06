import {
  AfterViewInit,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  ChangeDetectionStrategy,
  NgZone
} from '@angular/core';
import * as THREE from 'three';
import {OrbitControls} from 'three/examples/jsm/controls/OrbitControls.js';
import {GLTFLoader} from 'three/examples/jsm/loaders/GLTFLoader.js';
// import {InteractionManager} from 'three.interactive';
import {SignalrService} from '../../services/SignalrService';
import { HttpClient } from '@angular/common/http';
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
import {XRControllerModelFactory} from 'three/examples/jsm/webxr/XRControllerModelFactory.js';
import {XRTargetRaySpace} from 'three/src/renderers/webxr/WebXRController.js';
import {ActivatedRoute, Router} from '@angular/router';
// removed: unused TSL import 'color' (path not present in three r157+)
import {RouteNames} from '../../app-routing.module';
import {environment} from '../../../environments/environment';
import {Group} from 'three/src/objects/Group.js';
import {GeneralService} from '../../bl/general.service';
import {UnsubscriberService} from '../../services/unsubscriber.service';
import {MgThree} from '../../bl/mg.three';
import {MgGame} from '../../bl/mg.game';

@Component({
    selector: 'app-game-play',
    templateUrl: './game-play.component.html',
    styleUrls: ['./game-play.component.scss'],
    providers: [UnsubscriberService],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GamePlayComponent implements OnInit, OnDestroy, AfterViewInit {

  @ViewChild('rendererContainer', {static: true}) rendererContainer!: ElementRef;

  gameId: string | null = "";

  mgThree!:MgThree;

  mgGame!:MgGame;

  endMessage = ''; // friendly "game over" message shown when the game ends
  private lastStatus = ''; // to only pop the overlay on the transition into ENDED

  constructor(public signalRService: SignalrService,
              private router: Router,
              private generalService: GeneralService,
              private zone: NgZone,
              private activatedRoute: ActivatedRoute,
              private unsubscriberService: UnsubscriberService,
              private dalService: DALService) {
  }

  ngOnInit() {

    this.gameId = this.activatedRoute.snapshot.paramMap.get('id');
    console.log("ngOnInit", this.gameId);

    this.signalRService.hubConnection.off('GameUpdated');
    this.signalRService.hubConnection.on('GameUpdated', data => {
      console.log('GameUpdated', data);
      if (this.mgGame) this.mgGame.updateGame(data);
      // SignalR fires outside Angular's zone — run inside so the overlay renders.
      this.zone.run(() => {
        const status = String(data.gameStatus);
        // Only pop the overlay when the game FIRST ends — not on every later update
        // (otherwise it re-appears on each click after the user dismissed it).
        if (status === 'ENDED' && this.lastStatus !== 'ENDED') {
          this.endMessage = data.attributes?.result || 'Game over';
        }
        this.lastStatus = status;
      });
    });

  }

  backToList() {
    this.router.navigate([RouteNames.GamesList]);
  }

  ngOnDestroy(): void {
    this.signalRService.hubConnection.off('GameUpdated');
    this.mgThree?.dispose();
  }

  ngAfterViewInit(): void {

    this.dalService.getGameById(this.gameId!).subscribe(game => {
      if (!game) {
        this.router.navigate([RouteNames.GamesList]);
        return;
      }

      this.mgGame = new MgGame();
      this.mgGame.gameData = game;

      // If opening an already-finished game (e.g. to analyse it), show the result once.
      this.lastStatus = String(game.gameStatus);
      this.endMessage = this.lastStatus === 'ENDED'
        ? ((game.attributes?.result) || 'Game over')
        : '';

      this.mgThree=new MgThree();
      this.mgThree.initThree(this.rendererContainer.nativeElement,()=>{
        this.mgGame.loadGame(this.mgThree,this.generalService.User!);
      });
    });
  }

  onVrClick() {
    this.mgThree.startVr();
  }


}
