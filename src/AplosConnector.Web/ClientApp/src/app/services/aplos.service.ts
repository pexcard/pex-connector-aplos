import { Injectable, Inject } from "@angular/core";
import { HttpClient, HttpParams } from "@angular/common/http";
import { Observable, EMPTY, of } from "rxjs";

import { retryWithBackoff } from "../operators/retryWithBackoff.operator";
import { CacheRepositoryService } from './cache-repository.service';
import { timeout } from "rxjs/operators";
import { ENABLE_MOCK_MODE } from './mapping.service';

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
    if (ENABLE_MOCK_MODE) return of([
      { id: 1, name: 'Mock Contact 1' },
      { id: 2, name: 'Mock Contact 2' },
      { id: 3, name: 'Mock Contact 3' },
    ]);
    return this.cache.runAndCacheOrGetFromCache("Aplos.getContacts", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "Contacts"))
      .pipe(retryWithBackoff()), 60);
  }

  getContact(sessionId: string, contactId: number): Observable<AplosObject> {
    if (!contactId) return EMPTY;
    if (ENABLE_MOCK_MODE) return of({ id: contactId, name: `Mock Contact ${contactId}` });

    return this.cache.runAndCacheOrGetFromCache("Aplos.getContact"+contactId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Contact")}&aplosContactId=${contactId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFund(sessionId: string, fundId: number): Observable<AplosObject> {
    if (!fundId) return EMPTY;
    if (ENABLE_MOCK_MODE) return of({ id: fundId, name: `Mock Fund ${fundId}` });

    return this.cache.runAndCacheOrGetFromCache("Aplos.getFund"+fundId, this.httpClient
      .get<AplosObject>(`${this.buildUrl(sessionId, "Fund")}&aplosFundId=${fundId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getFunds(sessionId: string): Observable<AplosObject[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: 100, name: 'General Fund' },
      { id: 101, name: 'Operating Fund' },
      { id: 102, name: 'Restricted Fund' },
    ]);
    return this.cache.runAndCacheOrGetFromCache("Aplos.getFunds", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "Funds"))
      .pipe(retryWithBackoff()), 60);
  }

  getAccounts(sessionId: string, category: AplosAccountCategory): Observable<AplosAccount[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: 1000, name: '1000 - PEX Bank Account' },
      { id: 5000, name: '5000 - Office Supplies' },
      { id: 5100, name: '5100 - Travel Expenses' },
      { id: 5200, name: '5200 - Meals & Entertainment' },
    ]);
    const url = this.buildUrl(sessionId, "Accounts") + `&category=${category}`
    return this.cache.runAndCacheOrGetFromCache(`Aplos.getBankAccounts.${category}`, this.httpClient
      .get<AplosAccount[]>(url)
      .pipe(retryWithBackoff()), 60);
  }

  getAccount(sessionId: string, bankAccountNumber: number): Observable<AplosAccount> {
    if (!bankAccountNumber) return EMPTY;
    if (ENABLE_MOCK_MODE) return of({ id: bankAccountNumber, name: `Mock Account ${bankAccountNumber}` });

    return this.cache.runAndCacheOrGetFromCache("Aplos.getBankAccount"+bankAccountNumber, this.httpClient
        .get<AplosAccount>(`${this.buildUrl(sessionId, "Account")}&accountNumber=${bankAccountNumber}`)
      .pipe(retryWithBackoff()), 60);
  }

  getTagCategories(sessionId: string): Observable<AplosObject[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: 10, name: 'Department' },
      { id: 20, name: 'Location' },
      { id: 30, name: 'Project' },
    ]);
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTagCategories", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "tagCategories"))
      .pipe(retryWithBackoff()), 60);
  }

  getTags(sessionId: string, categoryId: string): Observable<AplosObject[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: 1, name: 'Tag Value A' },
      { id: 2, name: 'Tag Value B' },
      { id: 3, name: 'Tag Value C' },
    ]);
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTags"+categoryId, this.httpClient
      .get<AplosObject[]>(`${this.buildUrl(sessionId, "Tags")}&categoryId=${categoryId}`)
      .pipe(retryWithBackoff()), 60);
  }

  getTaxTagCategories(sessionId: string): Observable<AplosApiTaxTagCategoryDetail[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: '990', name: '990', tax_tags: [
        { id: '1', name: 'Program Services', group_name: '990' },
        { id: '2', name: 'Management & General', group_name: '990' },
        { id: '3', name: 'Fundraising', group_name: '990' },
      ]}
    ]);
    return this.cache.runAndCacheOrGetFromCache("Aplos.getTaxTagCategories", this.httpClient
      .get<AplosObject[]>(this.buildUrl(sessionId, "TaxTagCategories"))
      .pipe(retryWithBackoff()), 60);
  }

  getVendorsForCards(sessionId: string, activeOnly: boolean = undefined, takeOnly: boolean = undefined): Observable<VendorForCard[]> {
    if (ENABLE_MOCK_MODE) return of([
      { id: 1, name: 'Mock Vendor A', active: true, total: 1500 },
      { id: 2, name: 'Mock Vendor B', active: true, total: 2300 },
      { id: 3, name: 'Mock Vendor C', active: false, total: 800 },
    ]);
    let requestParams = new HttpParams();
    if (activeOnly !== undefined) {
      requestParams = requestParams.set('activeOnly', activeOnly);
    }
    if (takeOnly !== undefined) {
      requestParams = requestParams.set('takeOnly', takeOnly);
    }

    return this.cache.runAndCacheOrGetFromCache("Aplos.getVendorsForCards", this.httpClient
    .get<Vendor[]>(this.buildUrl(sessionId, "Vendors/ForCards"), { params: requestParams })
    .pipe(
      timeout(180000),
      retryWithBackoff()), 60);
  }
  
}

export type AplosAccountCategory = "asset" | "expense" | "liability" | "income";

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