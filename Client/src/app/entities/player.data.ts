import {UserData} from './user.data';
import {V3} from './V3';
import {ItemData} from './item.data';

export class PlayerData {
  id!: string;
  name?: string;
  type!: string;
  user?: UserData;

  position!: V3;
  rotation!: V3;

  table!: ItemData;
  hand!: ItemData;
}
