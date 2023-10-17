import { ItemData } from "./item.data";
import { PlayerData } from "./player.data";

export class GameData {

  id!: string;
  name?: string;
  items!: ItemData[];
  attributes: any;
  creatorId?: string;
  currentTurnId?: string;
  gameStatus!: number;
  gameType!: number;
  players!: PlayerData[];
  winners?: PlayerData[];
}

