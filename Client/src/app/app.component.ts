import {Component, HostBinding} from '@angular/core';
import {GeneralService} from './bl/general.service';
import {environment} from '../environments/environment';
import {Title} from '@angular/platform-browser';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent {
  @HostBinding('attr.app-version') appVersionAttr = environment.appVersion;
  constructor(private generalService: GeneralService,private titleService:Title) {

    this.titleService.setTitle("MGx - v"+environment.appVersion);

    let savedUser: any = localStorage.getItem('user');
    try {
      savedUser = JSON.parse(savedUser);
      this.generalService.User = savedUser;
    } catch (e) {
      this.generalService.User = undefined;
    }
  }
}
