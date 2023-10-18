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


  GenerateGuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }
}
