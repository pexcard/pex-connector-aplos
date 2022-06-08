import { Injectable, Inject } from "@angular/core";
import { BehaviorSubject, Observable, of } from "rxjs";
import { HttpClient } from "@angular/common/http";
import { retryWithBackoff } from '../operators/retryWithBackoff.operator';
import { CacheRepositoryService } from './cache-repository.service';
import { Router } from '@angular/router';
import { catchError, concatMap } from "rxjs/operators";

@Injectable({
  providedIn: "root"
})
export class AuthService {
  public sessionId = new BehaviorSubject<string>(null);
  public businessName = new BehaviorSubject<string>(null);
  private readonly CACHE_KEY_BUSINESS_NAME = 'pex.GetBusinessName';
  private readonly SESSION_ID_KEY = "SESSION_ID";

  constructor(
    private httpClient: HttpClient,
    @Inject("BASE_URL") private baseUrl: string,
    private cache: CacheRepositoryService,
    private router: Router
  ) {}

  getOauthURL() {
    this.httpClient
      .get<OAuthURLResponse>(this.baseUrl + "api/Session/OAuthURL")
      .pipe(retryWithBackoff())
      .subscribe(
        result => {
          console.log(result);
          window.location.href = result.OAuthUrl;
        },
        error => console.error(error)
      );
  }

  createAplosToken(sessionId: string, aplosClientId: string, aplosPrivateKey: string): Observable<AplosCredentialVerificationResult> {
    return this.httpClient.post<AplosCredentialVerificationResult>(this.baseUrl + `api/Session/AplosToken/?sessionId=${sessionId}`,
      {
        aplosClientId: aplosClientId,
        aplosPrivateKey: aplosPrivateKey
      });
  }

  login(sessionId: string): Observable<void> {
    return this.logout()
      .pipe(
        concatMap(() => {
          localStorage.setItem(this.SESSION_ID_KEY, sessionId);
          this.sessionId.next(sessionId);
          this.getBusinessName(sessionId);
          return of(void 0);
        })
      );
  }

  exchangeJWTForSession(jwt: string): Observable<TokenModel>{
    return this.httpClient.post<TokenModel>(this.baseUrl + 'api/Session/JWT', {token:jwt});
  }

  autoLogIn() {
    let sessionId = localStorage.getItem(this.SESSION_ID_KEY);
    if (sessionId) {
      this.httpClient
        .get<SessionValidityModel>(
          this.baseUrl + `api/Session/Validity?sessionId=${sessionId}`
        )
        .pipe( retryWithBackoff() )
        .subscribe(result => {
          if (result.isValid) {
            console.log("Session Found -- Auto-Logging in")
            this.sessionId.next(sessionId);
            this.getBusinessName(sessionId);
          } else {
            console.log("Session is invalid, cleaning up storage & cache");
            this.cache.clearAllAppCache();
            localStorage.removeItem(this.SESSION_ID_KEY);
            this.sessionId.next(null);
            this.businessName.next(null);
          }
        });
    }
    else{
      console.log("No prior session in storage");
    }
  }

  logout(): Observable<void> {
    let sessionId = localStorage.getItem(this.SESSION_ID_KEY);
    if (sessionId) {
      return this.httpClient
        .delete(this.baseUrl + `api/Session/?sessionId=${sessionId}`)
        .pipe(
          catchError(() => of(void 0)),
          concatMap(() => {
            localStorage.removeItem(this.SESSION_ID_KEY);
            this.cache.clearAllAppCache();
            this.sessionId.next(null);
            this.businessName.next(null);            
            return of(void 0);
          })
        );
    } else {
      return of(void 0);
    }
  }

  headlessMode = new BehaviorSubject<boolean>(null);
  setHeadlessMode(mode: boolean){
    this.headlessMode.next(mode);
  }

  private getBusinessName(sessionId: string) {
    return this.cache.runAndCacheOrGetFromCache(
      this.CACHE_KEY_BUSINESS_NAME,
      this.httpClient
        .get<BusinessNameModel>(`${this.baseUrl}api/Session/PEXBusinessName?sessionId=${sessionId}`)
        .pipe(retryWithBackoff())
      , 60).subscribe(result => {
        this.businessName.next(result.businessName);
      });
  }
}

interface OAuthURLResponse {
  oAuthUrl: string;
}

interface SessionValidityModel {
  isValid: boolean;
}

interface TokenModel{
  token: string;
}

interface AplosCredentialVerificationResult {
  canObtainAccessToken: boolean;
  isPartnerVerified: boolean;
  partnerVerificationUrl: string;
}

interface BusinessNameModel {
  businessName: string;
}