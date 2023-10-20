import { V3 } from "./V3";
import {Group} from 'three/src/objects/Group';

export class ItemData {
  id!: string;
  name?: string;
  asset!:string;
  items!: ItemData[];
  position!: V3;
  rotation!: V3;
  scale!: V3;

  visible! : any;
  clickActions! : any;
  hoverActions! : any;


  mesh?: THREE.Group;
  markForDelete!:boolean;


}
