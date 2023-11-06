import {AfterViewInit, Component, Input, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {GeneralService} from '../../bl/general.service';
import {AssetData} from '../../entities/asset.data';
import {MgGame} from "../../bl/mg.game";
import {MgThree} from "../../bl/mg.three";
import {ItemData} from "../../entities/item.data";
import {V3} from "../../entities/V3";

@Component({
  selector: 'app-debug-view',
  templateUrl: './debug-view.component.html',
  styleUrls: ['./debug-view.component.scss']
})
export class DebugViewComponent implements OnInit, OnDestroy, AfterViewInit, OnChanges {

  @Input() mgThree!: MgThree;
  @Input() mgGame!: MgGame;

  showDebugWindow = false;
  debugSearchModel = "";
  showAssets = false;

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
    this.showAssets = false;
    debugger

    let itemData = new ItemData();
    itemData.id = GeneralService.GenerateGuid();
    itemData.asset = assetData.name!;
    itemData.items=[];
    itemData.position = new V3(0,0,0);
    itemData.rotation = new V3(0,0,0);
    itemData.scale = new V3(1,1,1);
    itemData.visible = {};
    itemData.clickActions = {};
    itemData.hoverActions = {};

    this.mgGame.createItem(itemData, this.mgGame.gameData.table.mesh!);

  }
}
