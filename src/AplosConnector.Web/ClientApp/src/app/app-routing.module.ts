import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';
import { ConnectComponent } from './connect/connect.component';
import { FinishPexLoginComponent } from './finish-pex-login/finish-pex-login.component';
import { HealthComponent } from './health/health.component';
import { FinishAplosLoginComponent } from './finish-aplos-login/finish-aplos-login.component';
import { SyncHistoryComponent } from './sync-history/sync-history.component';
import { HeadlessComponent } from './headless/headless.component';
import { HandlePexJwtComponent } from './handle-pex-jwt/handle-pex-jwt.component';
import { LoginComponent } from './login/login.component';
import { AuthGuard } from './guard/auth.guard';
import { SyncConnectComponent } from './sync-connect/sync-connect.component';
import { SyncManageComponent } from './sync-manage/sync-manage.component';
import { VendorsSelectComponent } from './vendors-select/vendors-select.component';
import { VendorsManageComponent } from './vendors-manage/vendors-manage.component';

const childRoutes: Routes = [  {
  path: '',
  pathMatch: 'full',
  redirectTo: 'connect'
},
{
  path: 'login',
  component: LoginComponent
},
{
  path: 'connect',
  component: ConnectComponent,
  canActivate: [AuthGuard]
},
{
  path: 'sync-connect',
  component: SyncConnectComponent,
  canActivate: [AuthGuard]
},
{
  path: 'sync-manage',
  component: SyncManageComponent,
  canActivate: [AuthGuard]
},
{
  path: 'vendors-select',
  component: VendorsSelectComponent,
  canActivate: [AuthGuard]
},
{
  path: 'vendors-manage',
  component: VendorsManageComponent,
  canActivate: [AuthGuard]
},

{
  path: 'sync-history',
  component:SyncHistoryComponent,
  canActivate: [AuthGuard]
},
{
  path: 'finish-pex-login/:sessionId',
  component: FinishPexLoginComponent
},
{
  path: 'handle-pex-jwt',
  component: HandlePexJwtComponent
},
{
  path: 'finish-aplos-login',
  component: FinishAplosLoginComponent
},
{
  path: 'health',
  component: HealthComponent
},
{
  path:'**',
  redirectTo: 'connect'
}]

const routes: Routes = [
  {
    path: 'headless',
    component: HeadlessComponent,
    children: [...childRoutes]
  },
  ...childRoutes
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
