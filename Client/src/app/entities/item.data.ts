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

  visible! : any;
  clickActions! : any;
  hoverActions! : any;
  attributes? : any;

  mesh?: THREE.Object3D;
  markForDelete!:boolean;


}
