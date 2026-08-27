import { V3 } from "./V3";
import * as THREE from 'three';
import {Group} from 'three/src/objects/Group.js';
import {Object3D} from 'three/src/core/Object3D.js';

export class ItemData {
  id!: string;
  name?: string;
  asset!:string;
  items!: ItemData[];
  position!: V3;
  rotation!: V3;
  scale!: V3;

  text?:string;
  playType?:string;
  animationIdx?:number;

  // The HOLDER mechanic: where this item's group attaches, and whose it is.
  // See ItemData.cs — "world" (default) | "avatar" | "camera" | "hand".
  anchor?: string;
  owner?: string;
  // asset type PANEL: a uikit panel carried BY this item (see ItemData.cs)
  ui?: any[];
  uiWidth?: number;

  visible! : any;
  clickActions! : any;
  hoverActions! : any;
  attributes? : any;

  mesh?: THREE.Object3D;
  markForDelete!:boolean;


}
