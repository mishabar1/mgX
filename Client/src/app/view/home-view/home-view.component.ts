import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges, ChangeDetectionStrategy} from '@angular/core';
import {ActivatedRoute, Router} from '@angular/router';
import {RouteNames} from '../../app-routing.module';
import {SignalrService} from '../../services/SignalrService';
import {DALService} from '../../dal/dal.service';
import {GeneralService} from '../../bl/general.service';

@Component({
    selector: 'app-home-view',
    templateUrl: './home-view.component.html',
    styleUrls: ['./home-view.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class HomeViewComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges{

  usernameModel = "";
  error = "";
constructor(private router: Router,
            private generalService:GeneralService,
            private signalRService: SignalrService,
            private activatedRoute: ActivatedRoute,
            private dalService: DALService) {

  this.usernameModel =  generalService.User? generalService.User.name! : "";

}


  ngAfterViewInit(): void {
  }

  ngOnChanges(changes: SimpleChanges): void {
  }

  ngOnDestroy(): void {
  }

  ngOnInit(): void {
  }

  login() {
    if (!this.usernameModel.trim()) return;   // the server rejects a blank name with a 400
    this.error = "";

    this.dalService.login(this.usernameModel).subscribe({
      next: res => {
        //store token + user (also persists to localStorage for next time)
        this.generalService.setAuth(res.token, res.user);
        this.signalRService.startConnection(res.user.id);

        // navigate
        this.router.navigate([RouteNames.GamesList]);
      },
      // Without this the request just failed silently and the screen sat there doing nothing.
      error: err => {
        this.error = err?.error?.error || 'Could not sign in. Is the server running?';
      }
    })


  }
}
