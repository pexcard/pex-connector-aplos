import { Injectable, Inject } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";

import { retryWithBackoff } from "../operators/retryWithBackoff.operator";
import { CacheRepositoryService } from './cache-repository.service';

@Injectable({
  providedIn: "root"
})
export class AplosService {
  availableProjects: number[] = [];
  projectMap: Map<number, string> = new Map();

  constructor(
    private httpClient: HttpClient,
    @Inject("BASE_URL") private baseUrl: string,
    private cache: CacheRepositoryService
  ) {}

  private buildUrl(sessionId: string, endpoint: string): string {
    return this.baseUrl + `api/Aplos/${endpoint}?sessionId=${sessionId}`;
  }

  getContacts(sessionId: string): Observable<AplosObject[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getContacts", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "Contacts"))
      .pipe(retryWithBackoff()), 60);
  }

  getContact(sessionId: string, contactId: number): Observable<AplosObject> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getContact"+contactId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Contact")}&aplosContactId=${contactId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFund(sessionId: string, fundId: number): Observable<AplosObject> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getFund"+fundId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Fund")}&aplosFundId=${fundId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFunds(sessionId: string): Observable<AplosObject[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getFunds", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "Funds"))
      .pipe(retryWithBackoff()), 60);
  }

  getBankAccounts(sessionId: string): Observable<AplosAccount[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getBankAccounts", this.httpClient
        .get<AplosAccount[]>(this.buildUrl(sessionId, "Accounts"))
      .pipe(retryWithBackoff()), 60);
  }

  getBankAccount(sessionId: string, bankAccountNumber: number): Observable<AplosAccount> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getBankAccount"+bankAccountNumber, this.httpClient
        .get<AplosAccount>(`${this.buildUrl(sessionId, "Account")}&accountNumber=${bankAccountNumber}`)
      .pipe(retryWithBackoff()), 60);
  }

  getTagCategories(sessionId: string): Observable<AplosObject[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTagCategories", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "tagCategories"))
      .pipe(retryWithBackoff()), 60);
  }
}

export interface AplosObject {
  id: number;
  name: string;
}

export interface AplosAccount extends AplosObject {
}

export interface AplosPreferences{
  isClassEnabled: boolean;
  isLocationEnabled: boolean;
  locationFieldName: string
}
