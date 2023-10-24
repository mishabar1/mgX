export class AssetData {
  id!: string;
  name?: string;

  frontURL!: string;
  backURL?: string;

  type!:string;
  // "TOKEN"; // some "box" with very small height and 2 sides - front and back
  // "SOUND"; // mp3 sound - can be played on demand
  // "OBJECT"; // stl, gbl or obj file to load a 3d model
  // "TEXT"; // 3d text
}
