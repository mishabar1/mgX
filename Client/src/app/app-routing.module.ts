import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import {GamePlayComponent} from './view/game-play/game-play.component';
import {AuthGuard} from './bl/auth.guard';
import {HomeViewComponent} from './view/home-view/home-view.component';

const routes: Routes = [
  { path: '', redirectTo: `/home`, pathMatch: 'full' },
  // {
  //   path: AppRouteName.UploadImage,
  //   canActivate: [AuthGuard, SMSVerificationGuard, AuthGuardMaintenance, AcceptTermsGuard, UploadImageGuard],
  //   canDeactivate: [UploadImageGuard],
  //   loadChildren: () => import('./_view/upload-image/upload-image.module').then((m) => m.UploadImageModule)
  // },
  {
    path: `home`,
    component: HomeViewComponent,
    canActivate: [AuthGuard]
  },
  {
    path: `game-play/:id`,
    component: GamePlayComponent,
    canActivate: [AuthGuard]
  },
  // {
  //   path: AppRouteName.Spaces_Old,
  //   redirectTo: AppRouteName.Properties,
  //   pathMatch: 'full'
  // },

  // {
  //   path: AppRouteName.Account,
  //   canActivate: [AuthGuard, SMSVerificationGuard, AuthGuardMaintenance, AcceptTermsGuard],
  //   loadChildren: () => import('./_view/account/account.module').then((m) => m.AccountModule)
  // },


  { path: '**', redirectTo: `/home` }


];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
