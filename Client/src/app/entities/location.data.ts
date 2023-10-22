import { V3 } from "./V3";
import { Group } from 'three/src/objects/Group';

export class LocationData {

  position!: V3;
  rotation!: V3;
  scale!: V3;

  mesh?: THREE.Group;
  markForDelete!: boolean;

}
