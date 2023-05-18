import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, CanActivateChild, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable } from 'rxjs';
import { catchError, filter, switchMap } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

/**
 * Guards routes against valid PEX authentication.
 */
@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate, CanActivateChild {

  public constructor(
    private _authService: AuthService,
    private _router: Router
  ) {
  }

  public canActivateChild(childRoute: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | UrlTree | Observable<boolean | UrlTree> | Promise<boolean | UrlTree> {
    return this._checkAuthenticated(childRoute, state);
  }

  public canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | UrlTree | Observable<boolean | UrlTree> | Promise<boolean | UrlTree> {
    return this._checkAuthenticated(route, state);
  }

  private _checkAuthenticated(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<boolean> {
    return this._authService.isAuthenticated
      .pipe(
        filter(isAuthenticated => isAuthenticated !== undefined && isAuthenticated !== null),
        switchMap(
          async (isAuthentiated) => {
            if (!isAuthentiated) {
              await this._redirectToLogin(route, state);
            }
            return isAuthentiated;
          }
        ),
        catchError(
          async () => {
            await this._redirectToLogin(route, state);
            return false;
          }
        )
      );    
  }

  private _redirectToLogin(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Promise<boolean> {
    return this._router.navigate(['login'], { relativeTo: undefined, queryParams: { returnUrl: state.url } });
  }

}
