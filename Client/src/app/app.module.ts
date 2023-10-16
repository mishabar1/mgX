import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import {ButtonModule} from 'primeng/button';
import {CheckboxModule} from 'primeng/checkbox';
import {HttpClientModule} from '@angular/common/http';
import {GamePlayComponent} from './view/game-play/game-play.component';
import {HomeViewComponent} from './view/home-view/home-view.component';
import { LoginComponent } from './view/login/login.component';
import { GamesListComponent } from './view/games-list/games-list.component';
import { GameSettingsComponent } from './view/game-settings/game-settings.component';

@NgModule({
  declarations: [
    AppComponent,
    GamePlayComponent,
    HomeViewComponent,
    LoginComponent,
    GamesListComponent,
    GameSettingsComponent
  ],
  imports: [
    BrowserModule,
    BrowserAnimationsModule,
    FormsModule,
    AppRoutingModule,
    ButtonModule,
    CheckboxModule,
    HttpClientModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
