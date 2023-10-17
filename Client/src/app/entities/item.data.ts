import { V3 } from "./V3";

export class ItemData {
  Id!: string;
  Name?: string;

  Items!: ItemData[];
  Position!: V3;
  Rotation!: V3;
  Scale!: V3;

  Visible! : Map<string, string>;
  ClickActions! : any;
  HoverActions! : Map<string, string>;
}
