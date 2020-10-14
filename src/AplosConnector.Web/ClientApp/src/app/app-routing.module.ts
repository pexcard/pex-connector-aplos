import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';
import { ConnectComponent } from './connect/connect.component';
import { ManageConnectionsComponent } from './manage-connections/manage-connections.component';
import { FinishPexLoginComponent } from './finish-pex-login/finish-pex-login.component';
import { HealthComponent } from './health/health.component';
import { FinishAplosLoginComponent } from './finish-aplos-login/finish-aplos-login.component';
import { SyncHistoryComponent } from './sync-history/sync-history.component';
import { HeadlessComponent } from './headless/headless.component';
import { HandlePexJwtComponent } from './handle-pex-jwt/handle-pex-jwt.component';

const childRoutes: Routes = [  {
  path: '',
  pathMatch: 'full',
  redirectTo: 'connect'
},
{
  path: 'connect',
  component: ConnectComponent
},
{
  path: 'manage-connections',
  component: ManageConnectionsComponent
},
{
  path: 'sync-history',
  component:SyncHistoryComponent
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
