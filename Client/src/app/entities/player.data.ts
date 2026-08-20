import {UserData} from './user.data';
import {V3} from './V3';
import {ItemData} from './item.data';
import { LocationData } from './location.data';
import type { UiNode } from '../bl/mg.panel3d';

export class PlayerData {
  id!: string;
  name?: string;
  type!: string;
  user?: UserData;
  attributes?: any;

  avatar!: LocationData;
  camera!: LocationData;

  table!: ItemData;
  hand!: ItemData;

  /**
   * The complete 2D control panel the SERVER built for this seat (Entities/UiNode.cs), rendered
   * verbatim by MgPanel3d. Absent/null = this seat shows no panel.
   *
   * This was missing from the model entirely, so every read of it went through `as any` — which
   * is how the panel path stayed untyped end to end.
   */
  screen?: UiNode[];
}
