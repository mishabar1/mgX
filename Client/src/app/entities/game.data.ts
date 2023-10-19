import { ItemData } from "./item.data";
import { PlayerData } from "./player.data";
import {AssetData} from './asset.data';

export class GameData {

  id!: string;
  name?: string;
  assets!: { [key: string]: AssetData };
  items!: ItemData[];
  attributes: any;
  creatorId?: string;
  currentTurnId?: string;
  gameStatus!: number;
  gameType!: number;
  players!: PlayerData[];
  winners?: PlayerData[];



}

