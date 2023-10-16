import { ItemData } from "./item.data";
import { PlayerData } from "./player.data";

export class GameData {

  Id!: string;
  Name?: string;
  Items!: ItemData[];
  Attributes: any;
  CreatorId?: string;
  CurrentTurnId?: string;
  GameStatus!: number;
  GameType!: number;
  Players!: PlayerData[];  
  Winners?: PlayerData[];
}

