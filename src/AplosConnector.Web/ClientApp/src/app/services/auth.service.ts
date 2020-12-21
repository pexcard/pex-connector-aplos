import { Injectable, Inject } from "@angular/core";
import { BehaviorSubject, Observable } from "rxjs";
import { HttpClient } from "@angular/common/http";
import { retryWithBackoff } from '../operators/retryWithBackoff.operator';
import { CacheRepositoryService } from './cache-repository.service';
import { Router } from '@angular/router';

@Injectable({
  providedIn: "root"
})
export class AuthService {
  public sessionId = new BehaviorSubject<string>(null);
  public businessName = new BehaviorSubject<string>(null);
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

  login(sessionId: string) {
    localStorage.setItem(this.SESSION_ID_KEY, sessionId);
    this.sessionId.next(sessionId);
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
          } else {
            console.log("Session is invalid, cleaning up storage & cache");
            this.cache.clearAllAppCache();
            localStorage.removeItem(this.SESSION_ID_KEY);
            this.sessionId.next(null);
          }
        });
    }
    else{
      console.log("No prior session in storage");
    }
  }

  logout() {
    let sessionId = localStorage.getItem(this.SESSION_ID_KEY);
    if (sessionId) {
      this.httpClient
        .delete(this.baseUrl + `api/Session/?sessionId=${sessionId}`)
        .pipe( retryWithBackoff() )
        .subscribe();
      localStorage.removeItem(this.SESSION_ID_KEY);
      this.sessionId.next(null);
    }
    this.cache.clearAllAppCache();
    this.router.navigate(['/', 'connect']);
  }

  headlessMode = new BehaviorSubject<boolean>(null);
  setHeadlessMode(mode: boolean){
    this.headlessMode.next(mode);
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
