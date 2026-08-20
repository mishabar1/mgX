import {Component, HostBinding, ChangeDetectionStrategy} from '@angular/core';
import {GeneralService} from './bl/general.service';
import {environment} from '../environments/environment';
import {Title} from '@angular/platform-browser';
import {SignalrService} from './services/SignalrService';

@Component({
    selector: 'app-root',
    templateUrl: './app.component.html',
    styleUrls: ['./app.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class AppComponent {
  @HostBinding('attr.app-version') appVersionAttr = environment.appVersion;
  constructor(private generalService: GeneralService,
              private titleService:Title,
              private signalRService: SignalrService) {

    this.titleService.setTitle("MGx - v"+environment.appVersion);

    // Restore the signed-in user. This used to lean on the throw: JSON.parse(null) returns null
    // rather than throwing, so User was set to null and `User!.id` was what raised — leaving
    // hubConnection undefined for the rest of the session while looking like a parse failure.
    let savedUser: any = undefined;
    try {
      const raw = localStorage.getItem('user');
      savedUser = raw ? JSON.parse(raw) : undefined;
    } catch {
      savedUser = undefined;
    }
    this.generalService.User = savedUser || undefined;

    if (this.generalService.User?.id) {
      this.signalRService.startConnection(this.generalService.User.id);
    }
  }
}
