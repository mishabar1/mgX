import { V3 } from "./V3";
import {Group} from 'three/src/objects/Group';
import {Object3D} from 'three/src/core/Object3D';

export class ItemData {
  id!: string;
  name?: string;
  asset!:string;
  items!: ItemData[];
  position!: V3;
  rotation!: V3;
  scale!: V3;

  text?:string;

  visible! : any;
  clickActions! : any;
  hoverActions! : any;

  mesh?: THREE.Object3D;
  markForDelete!:boolean;


}
