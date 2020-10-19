import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import {HttpClientModule} from '@angular/common/http'

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { ClarityModule } from '@clr/angular';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { ConnectComponent } from './connect/connect.component';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';
import { ManageConnectionsComponent } from './manage-connections/manage-connections.component';
import { FinishPexLoginComponent } from './finish-pex-login/finish-pex-login.component';
import { HealthComponent } from './health/health.component';
import { FinishAplosLoginComponent } from './finish-aplos-login/finish-aplos-login.component';
import { LoadingPlaceholderComponent } from './loading-placeholder/loading-placeholder.component';
import { SyncHistoryComponent } from './sync-history/sync-history.component';
import { HeadlessComponent } from './headless/headless.component';
import { HandlePexJwtComponent } from './handle-pex-jwt/handle-pex-jwt.component';

@NgModule({
  declarations: [
    AppComponent,
    ConnectComponent,
    ManageConnectionsComponent,
    FinishPexLoginComponent,
    HealthComponent,
    FinishAplosLoginComponent,
    LoadingPlaceholderComponent,
    SyncHistoryComponent,
    HeadlessComponent,
    HandlePexJwtComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    HttpClientModule,
    FormsModule,
    ClarityModule,
    BrowserAnimationsModule,
    ReactiveFormsModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
