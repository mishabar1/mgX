import * as THREE from "three";

export class V3 {
  x!: number;
  y!: number;
  z!: number;

  constructor(x: number, y: number, z: number) {
    this.x = x;
    this.y = y;
    this.z = z;
  }

  static FromJson(data: any) {
    return new V3(data.x, data.y, data.z);
  }

}
