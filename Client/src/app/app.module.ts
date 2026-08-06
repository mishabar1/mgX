import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import {ButtonModule} from 'primeng/button';
import {CheckboxModule} from 'primeng/checkbox';
import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';
import {AuthInterceptor} from './bl/auth.interceptor';
import {GamePlayComponent} from './view/game-play/game-play.component';
import {HomeViewComponent} from './view/home-view/home-view.component';
import { GamesListComponent } from './view/games-list/games-list.component';
import { GameSetupComponent } from './view/game-setup/game-setup.component';
import {NgxJsonViewerModule} from 'ngx-json-viewer';
import { EditorComponent } from './view/editor/editor.component';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome'
import {TooltipModule} from "primeng/tooltip";
import {InputNumberModule} from "primeng/inputnumber";
import {ConfirmDialogModule} from 'primeng/confirmdialog';
import {ConfirmationService} from 'primeng/api';
import {DebugViewComponent} from "./comps/debug-view/debug-view.component";
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';

@NgModule({ declarations: [
        AppComponent,
        HomeViewComponent,
        GamesListComponent,
        GameSetupComponent,
        GamePlayComponent,
        EditorComponent,
        DebugViewComponent
    ],
    bootstrap: [AppComponent], imports: [BrowserModule,
        BrowserAnimationsModule,
        FormsModule,
        AppRoutingModule,
        ButtonModule,
        CheckboxModule,
        NgxJsonViewerModule,
        TooltipModule,
        FontAwesomeModule,
        ConfirmDialogModule,
        InputNumberModule], providers: [
        { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
        provideHttpClient(withXhr(), withInterceptorsFromDi()),
        providePrimeNG({ theme: { preset: Aura } }),
        ConfirmationService
    ] })
export class AppModule { }
