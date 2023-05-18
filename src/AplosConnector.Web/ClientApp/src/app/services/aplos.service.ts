import { Injectable, Inject } from "@angular/core";
import { HttpClient, HttpParams } from "@angular/common/http";
import { Observable, EMPTY } from "rxjs";

import { retryWithBackoff } from "../operators/retryWithBackoff.operator";
import { CacheRepositoryService } from './cache-repository.service';
import { timeout } from "rxjs/operators";

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
    if (!contactId) return EMPTY;

    return this.cache.runAndCacheOrGetFromCache("Aplos.getContact"+contactId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Contact")}&aplosContactId=${contactId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFund(sessionId: string, fundId: number): Observable<AplosObject> {
    if (!fundId) return EMPTY;

    return this.cache.runAndCacheOrGetFromCache("Aplos.getFund"+fundId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Fund")}&aplosFundId=${fundId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFunds(sessionId: string): Observable<AplosObject[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getFunds", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "Funds"))
      .pipe(retryWithBackoff()), 60);
  }

  getAccounts(sessionId: string, category: AplosAccountCategory): Observable<AplosAccount[]> {
    const url = this.buildUrl(sessionId, "Accounts") + `&category=${category}`
    return this.cache.runAndCacheOrGetFromCache(`Aplos.getBankAccounts.${category}`, this.httpClient
      .get<AplosAccount[]>(url)
      .pipe(retryWithBackoff()), 60);
  }

  getAccount(sessionId: string, bankAccountNumber: number): Observable<AplosAccount> {
    if (!bankAccountNumber) return EMPTY;

    return this.cache.runAndCacheOrGetFromCache("Aplos.getBankAccount"+bankAccountNumber, this.httpClient
        .get<AplosAccount>(`${this.buildUrl(sessionId, "Account")}&accountNumber=${bankAccountNumber}`)
      .pipe(retryWithBackoff()), 60);
  }

  getTagCategories(sessionId: string): Observable<AplosObject[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTagCategories", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "tagCategories"))
      .pipe(retryWithBackoff()), 60);
  }

  getTaxTagCategories(sessionId: string): Observable<AplosApiTaxTagCategoryDetail[]> {
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTaxTagCategories", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "TaxTagCategories"))
      .pipe(retryWithBackoff()), 60);
  }

  getVendorsForCards(sessionId: string, activeOnly: boolean = undefined, takeOnly: boolean = undefined): Observable<VendorForCard[]> {
    let requestParams = new HttpParams();
    if (activeOnly !== undefined) {
      requestParams = requestParams.set('activeOnly', activeOnly);
    }
    if (takeOnly !== undefined) {
      requestParams = requestParams.set('takeOnly', takeOnly);
    }
    return this.httpClient
      .get<Vendor[]>(this.buildUrl(sessionId, "Vendors/ForCards"), { params: requestParams })
      .pipe(
        timeout(10000),
        retryWithBackoff()
      );
  }
  
}

export type AplosAccountCategory = "asset" | "expense" | "liability";

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

export interface AplosApiTaxTagCategoryDetail {
  id: string;
  name: string;
  tax_tags: AplosApiTaxTagDetail[];
}

export interface AplosApiTaxTagDetail {
  id: string;
  name: string;
  group_name: string;
}

export interface Vendor {
  id: number;
  name: string;
  active: boolean;
}

export interface VendorForCard extends Vendor {
  total: number;
}