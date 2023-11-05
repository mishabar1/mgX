import {Injectable} from '@angular/core';
import {UserData} from '../entities/user.data';

@Injectable({
  providedIn: 'root'
})
export class GeneralService{

  public User? : UserData;

  // public SetUser(name:string){
  //   this.User = new UserData();
  //   this.User.id =this.GenerateGuid();
  //   this.User.name = name;
  // }


  static GenerateGuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  randomIntFromInterval(min: number, max: number) { // min and max included
    return Math.random() * (max - min + 1) + min
  }

  static deepEqual(object1:any, object2:any) {
    const keys1 = Object.keys(object1);
    const keys2 = Object.keys(object2);

    if (keys1.length !== keys2.length) {
      return false;
    }

    for (const key of keys1) {
      const val1 = object1[key];
      const val2 = object2[key];
      const areObjects = this.isObject(val1) && this.isObject(val2);
      if (
          areObjects && !this.deepEqual(val1, val2) ||
          !areObjects && val1 !== val2
      ) {
        return false;
      }
    }

    return true;
  }
  static  isObject(object:any) {
    return object != null && typeof object === 'object';
  }
}
