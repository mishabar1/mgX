import {AfterViewInit, Component, Input, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {SignalrService} from '../../services/SignalrService';
import {ActivatedRoute, Router} from '@angular/router';
import {GeneralService} from '../../bl/general.service';
import {UnsubscriberService} from '../../services/unsubscriber.service';
import {DALService} from '../../dal/dal.service';
import * as THREE from 'three';
import {GameData} from '../../entities/game.data';
import {AssetData} from '../../entities/asset.data';

@Component({
  selector: 'app-debug-view',
  templateUrl: './debug-view.component.html',
  styleUrls: ['./debug-view.component.scss']
})
export class DebugViewComponent  implements OnInit, OnDestroy, AfterViewInit, OnChanges{

  @Input() camera!: THREE.PerspectiveCamera;
  @Input() scene!: THREE.Scene;
  @Input() gameData!: GameData;

  showDebugWindow = false;
  debugSearchModel="";
  showAssets=false;
  constructor() {
  }
  ngAfterViewInit(): void {
  }

  ngOnChanges(changes: SimpleChanges): void {
  }

  ngOnDestroy(): void {
  }

  ngOnInit(): void {
  }




  getAnyClass(obj: any) {
    if (typeof obj === "undefined") return "undefined";
    if (obj === null) return "null";
    return obj.type
    //.constructor.name
  }

  onRemoveClick(obj: any) {
    obj.removeFromParent();
  }

  showDebug() {
    this.showDebugWindow = true;
  }

  addAsset(assetData: AssetData) {
    this.showAssets=false;



  }
}
