import {Injectable} from '@angular/core';
import {Router, RouterStateSnapshot, ActivatedRouteSnapshot, CanActivate} from '@angular/router';
import {Observable} from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {

  constructor(private router: Router) {
  }

  canActivate(): | Observable<boolean> | Promise<boolean> | boolean {
    // let isOnMaintenance = false;
    // if (isOnMaintenance) {
    //   this.router.navigate(['/maintenance']);
    //   return false;
    // } else {
    return true;
    // }
  }

}
