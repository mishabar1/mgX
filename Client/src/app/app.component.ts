import {Component, HostBinding} from '@angular/core';
import {GeneralService} from './bl/general.service';
import {environment} from '../environments/environment';
import {Title} from '@angular/platform-browser';
import {SignalrService} from './services/SignalrService';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent {
  @HostBinding('attr.app-version') appVersionAttr = environment.appVersion;
  constructor(private generalService: GeneralService,
              private titleService:Title,
              private signalRService: SignalrService) {

    this.titleService.setTitle("MGx - v"+environment.appVersion);

    let savedUser: any = localStorage.getItem('user');
    try {
      savedUser = JSON.parse(savedUser);
      this.generalService.User = savedUser;
      this.signalRService.startConnection(this.generalService.User!.id);
    } catch (e) {
      this.generalService.User = undefined;
    }
  }
}
