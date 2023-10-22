import {UserData} from './user.data';
import {V3} from './V3';
import {ItemData} from './item.data';
import { LocationData } from './location.data';

export class PlayerData {
  id!: string;
  name?: string;
  type!: string;
  user?: UserData;

  avatar!: LocationData;
  camera!: LocationData;

  table!: ItemData;
  hand!: ItemData;
}
