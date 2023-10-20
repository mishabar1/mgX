import {UserData} from './user.data';

export class PlayerData {
  id!: string;
  name?: string;
  type!: string;
  user?: UserData;
}
