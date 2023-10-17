import { V3 } from "./V3";

export class ItemData {
  id!: string;
  name?: string;

  items!: ItemData[];
  position!: V3;
  rotation!: V3;
  scale!: V3;

  visible! : any;
  clickActions! : any;
  hoverActions! : any;
  mesh?: THREE.Mesh;
}
