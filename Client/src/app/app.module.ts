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
import { GamesListComponent } from './view/games-list/games-list.component';
import { GameSetupComponent } from './view/game-setup/game-setup.component';
import {NgxJsonViewerModule} from 'ngx-json-viewer';
import { EditorComponent } from './view/editor/editor.component';

@NgModule({
  declarations: [
    AppComponent,
    HomeViewComponent,
    GamesListComponent,
    GameSetupComponent,
    GamePlayComponent,
    EditorComponent
  ],
  imports: [
    BrowserModule,
    BrowserAnimationsModule,
    FormsModule,
    AppRoutingModule,
    ButtonModule,
    CheckboxModule,
    HttpClientModule,
    NgxJsonViewerModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
