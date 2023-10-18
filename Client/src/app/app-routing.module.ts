import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import {GamePlayComponent} from './view/game-play/game-play.component';
import {AuthGuard} from './bl/auth.guard';
import {HomeViewComponent} from './view/home-view/home-view.component';
import {GamesListComponent} from './view/games-list/games-list.component';

export enum RouteNames {
  Home = 'home',
  GamePlay = 'game-play',
  GamesList = 'games-list'
}

const routes: Routes = [
  { path: '', redirectTo: `/${RouteNames.Home}`, pathMatch: 'full' },

  {
    path: RouteNames.Home,
    component: HomeViewComponent
  },
  {
    path: RouteNames.GamesList,
    component: GamesListComponent,
    canActivate: [AuthGuard]
  },
  {
    path: `${RouteNames.GamePlay}/:id`,
    component: GamePlayComponent,
    canActivate: [AuthGuard]
  },

  { path: '**', redirectTo: `/${RouteNames.Home}` }


];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }


