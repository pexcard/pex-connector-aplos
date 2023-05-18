import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import {HttpClientModule} from '@angular/common/http'

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { ClarityModule } from '@clr/angular';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { ConnectComponent } from './connect/connect.component';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';
import { FinishPexLoginComponent } from './finish-pex-login/finish-pex-login.component';
import { HealthComponent } from './health/health.component';
import { FinishAplosLoginComponent } from './finish-aplos-login/finish-aplos-login.component';
import { LoadingPlaceholderComponent } from './loading-placeholder/loading-placeholder.component';
import { SyncHistoryComponent } from './sync-history/sync-history.component';
import { HeadlessComponent } from './headless/headless.component';
import { HandlePexJwtComponent } from './handle-pex-jwt/handle-pex-jwt.component';
import { AplosAccountPipe } from "./pipes/aplosAccount";
import { LoginComponent } from './login/login.component';
import { SyncConnectComponent } from './sync-connect/sync-connect.component';
import { SyncManageComponent } from './sync-manage/sync-manage.component';
import { VendorsManageComponent } from './vendors-manage/vendors-manage.component';
import { VendorsSelectComponent } from './vendors-select/vendors-select.component';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { SelectListComponent } from './select-list/select-list.component';
import { TruncateModule } from '@yellowspot/ng-truncate';

@NgModule({
  declarations: [
    AppComponent,
    ConnectComponent,
    FinishPexLoginComponent,
    HealthComponent,
    FinishAplosLoginComponent,
    LoadingPlaceholderComponent,
    SyncHistoryComponent,
    HeadlessComponent,
    HandlePexJwtComponent,
    AplosAccountPipe,
    LoginComponent,
    SyncConnectComponent,
    SyncManageComponent,
    VendorsManageComponent,
    VendorsSelectComponent,
    SelectListComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    HttpClientModule,
    FormsModule,
    ClarityModule,
    BrowserAnimationsModule,
    ReactiveFormsModule,
    CommonModule,
    TruncateModule
  ],
  providers: [CurrencyPipe],
  bootstrap: [AppComponent]
})
export class AppModule { }
