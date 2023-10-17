import { Component } from '@angular/core';
import {Router} from '@angular/router';

@Component({
  selector: 'app-home-view',
  templateUrl: './home-view.component.html',
  styleUrls: ['./home-view.component.scss']
})
export class HomeViewComponent {

  usernameModel = "";
constructor(private router: Router) {
}
  login() {
    this.router.navigateByUrl('/game-play/123');
  }
}
