import { ItemData } from "./item.data";
import { PlayerData } from "./player.data";
import {AssetData} from './asset.data';
import {find} from 'lodash';
import {LocationData} from './location.data';

export class GameData {

  id!: string;
  name?: string;
  assets!: { [key: string]: AssetData };
  table!: ItemData;
  attributes: any;
  creatorId?: string;
  currentTurnId?: string;
  gameStatus!: number;
  gameType!: number;
  players!: PlayerData[];
  winners?: PlayerData[];
  minPlayers?: number;   // required occupied seats before the game can start

  observer!:LocationData;

}

