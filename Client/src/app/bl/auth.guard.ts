import {Injectable} from '@angular/core';
import {Router, RouterStateSnapshot, ActivatedRouteSnapshot, CanActivate} from '@angular/router';
import {Observable} from 'rxjs';
import {GeneralService} from './general.service';
import {RouteNames} from '../app-routing.module';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {

  constructor(private router: Router,private generalService:GeneralService) {
  }

  canActivate(): boolean {
    if(!this.generalService.User){
      this.router.navigate([RouteNames.Home]);
      return false;
    }

    return true;
  }

}
