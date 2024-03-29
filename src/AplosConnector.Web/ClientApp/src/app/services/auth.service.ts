import { Injectable, Inject } from "@angular/core";
import { BehaviorSubject, Observable, of } from "rxjs";
import { HttpClient } from "@angular/common/http";
import { retryWithBackoff } from '../operators/retryWithBackoff.operator';
import { CacheRepositoryService } from './cache-repository.service';
import { Router } from '@angular/router';
import { catchError, concatMap } from "rxjs/operators";
import { FundingSource } from "./mapping.service";

@Injectable({
  providedIn: "root"
})
export class AuthService {

  public isAuthenticated = new BehaviorSubject<boolean | null>(null);
  public sessionId = new BehaviorSubject<string | null>(null);
  public businessName = new BehaviorSubject<string | null>(null);
  public fundingSource = new BehaviorSubject<FundingSource | 0>(0);
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

  getAplosAuthURL() {
    this.httpClient
      .get<OAuthURLResponse>(`${this.baseUrl}api/Session/aplosAuthURL`)
      .subscribe(
        result => {
          console.log('AplosAuthUrl', result);
          window.location.href = result.oAuthUrl;
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
          this.isAuthenticated.next(true);
          this.sessionId.next(sessionId);
          this.getBusinessInfo(sessionId);
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
            this.isAuthenticated.next(true);
            this.sessionId.next(sessionId);
            this.getBusinessInfo(sessionId);
          } else {
            console.log("Session is invalid, cleaning up storage & cache");
            this.cache.clearAllAppCache();
            localStorage.removeItem(this.SESSION_ID_KEY);
            this.isAuthenticated.next(false);
            this.sessionId.next(null);
            this.businessName.next(null);
            this.fundingSource.next(0);
          }
        });
    }
    else{
      this.isAuthenticated.next(false);
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
            this.isAuthenticated.next(false);
            this.sessionId.next(null);
            this.businessName.next(null);
            this.fundingSource.next(0);
            return of(void 0);
          })
        );
    } else {
      this.isAuthenticated.next(false);
      return of(void 0);
    }
  }

  private getBusinessInfo(sessionId: string) {
    return this.cache.runAndCacheOrGetFromCache(
      this.CACHE_KEY_BUSINESS_NAME,
      this.httpClient
        .get<BusinessInfoModel>(`${this.baseUrl}api/Session/BusinessInfo?sessionId=${sessionId}`)
        .pipe(retryWithBackoff())
      , 60).subscribe(result => {
        this.businessName.next(result.businessName);
        this.fundingSource.next(result.fundingSource);
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

interface BusinessInfoModel {
  businessName: string;
  fundingSource: FundingSource;
}