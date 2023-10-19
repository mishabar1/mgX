import {AfterViewInit, Component, OnChanges, OnDestroy, OnInit, SimpleChanges} from '@angular/core';
import {ActivatedRoute, Router} from '@angular/router';
import {RouteNames} from '../../app-routing.module';
import {SignalrService} from '../../services/SignalrService';
import {DALService} from '../../dal/dal.service';
import {GeneralService} from '../../bl/general.service';

@Component({
  selector: 'app-home-view',
  templateUrl: './home-view.component.html',
  styleUrls: ['./home-view.component.scss']
})
export class HomeViewComponent implements  OnInit, OnDestroy, AfterViewInit, OnChanges{

  usernameModel = "";
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
    this.dalService.login(this.usernameModel).subscribe(user=>{

      //set
      this.generalService.User = user;
      this.signalRService.startConnection(this.generalService.User.id);

      //save for next time
      localStorage.setItem("user", JSON.stringify(user));

      // navigate
      this.router.navigate([RouteNames.GamesList]);
    })


  }
}
