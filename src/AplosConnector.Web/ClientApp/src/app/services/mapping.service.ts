import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { retryWithBackoff } from '../operators/retryWithBackoff.operator';
import { CacheRepositoryService } from './cache-repository.service';
import { AplosApiTaxTagCategoryDetail } from './aplos.service';

@Injectable({
  providedIn: 'root'
})
export class MappingService {
  constructor(
    private httpClient: HttpClient,
    @Inject('BASE_URL') private baseUrl: string,
    private cache: CacheRepositoryService
  ) {}

  private readonly CACHE_KEY_GET_AUTHENTICATION_STATUS = 'mapping.getAplosAuthenticationStatus';
  private readonly CACHE_KEY_GET_SETTINGS = 'mapping.getSettings';
  private readonly CACHE_KEY_GET_SYNC_RESULTS = 'mapping.getSyncResults';

  private buildUrl(sessionId: string, endpoint: string): string {
    return this.baseUrl + `api/Mapping/${endpoint}?sessionId=${sessionId}`;
  }

  private clearCache() {
    this.cache.clearCache(this.CACHE_KEY_GET_SETTINGS);
    this.cache.clearCache(this.CACHE_KEY_GET_SYNC_RESULTS);
  }

  getAplosAuthenticationStatus(sessionId: string): Observable<AplosAuthenticationStatusModel> {
    return this.cache.runAndCacheOrGetFromCache(
      this.CACHE_KEY_GET_AUTHENTICATION_STATUS,
      this.httpClient
        .get<AplosAuthenticationStatusModel>(
          this.buildUrl(sessionId, 'AplosAuthenticationStatus')
        )
        .pipe(retryWithBackoff(50, 1, 500)),
      5
    );
  }

  getSettings(sessionId: string): Observable<SettingsModel> {
    return this.cache.runAndCacheOrGetFromCache(this.CACHE_KEY_GET_SETTINGS, this.httpClient
      .get<SettingsModel>(this.buildUrl(sessionId, 'Settings'))
      .pipe(retryWithBackoff()), 5);
  }

  saveSettings(sessionId: string, settings: SettingsModel) {
    this.clearCache();
    return this.httpClient.put(this.buildUrl(sessionId, 'Settings'), settings);
  }

  getSyncResults(sessionId: string) {
    return this.cache.runAndCacheOrGetFromCache(this.CACHE_KEY_GET_SYNC_RESULTS, this.httpClient
      .get<SyncResultModel[]>(this.buildUrl(sessionId, 'SyncResults'))
      .pipe(retryWithBackoff()), 5);
  }

  sync(sessionId: string) {
    this.clearCache();
    return this.httpClient.post(this.buildUrl(sessionId, 'Sync'), null);
  }

  getVendorCardsMapped(sessionId: string): Observable<boolean> {
    return this.httpClient
      .get<boolean>(this.buildUrl(sessionId, 'Settings/VendorCardMapping'))
      .pipe(retryWithBackoff());
  }

  setVendorCardsMapped(sessionId: string, enable: boolean): Observable<boolean> {
    return this.httpClient
      .put<void>(this.buildUrl(sessionId,'Settings/VendorCardMapping'), enable)
      .pipe(retryWithBackoff());
  }

  disconnect(sessionId: string) {
    this.clearCache();
    return this.httpClient.delete(this.buildUrl(sessionId, ''));
  }
}

export interface AplosAuthenticationStatusModel {
  aplosAuthenticationMode: AplosAuthenticationMode,
  hasAplosAccountId: boolean,
  isAuthenticated: boolean,
  partnerVerificationUrl: string,
}

export interface SyncResultModel {
  createdUtc: Date;
  syncType: string;
  syncStatus: string;
  syncedRecords: number;
  syncNotes: string;
  PEXBusinessAcctId: number;
}

export interface SettingsModel {
  automaticSync: boolean;

  syncTransactions: boolean;
  syncTags: boolean;
  syncTaxTagToPex: boolean;

  syncApprovedOnly: boolean;
  earliestTransactionDateToSync: string;
  syncTransfers: boolean;
  syncInvoices: boolean,
  syncPexFees: boolean;

  transfersAplosContactId: number;
  transfersAplosFundId: number;
  transfersAplosTransactionAccountNumber: number;

  pexFeesAplosRegisterAccountNumber: number;
  pexFeesAplosContactId: number;
  pexFeesAplosFundId: number;
  pexFeesAplosTransactionAccountNumber: number;
  pexFeesAplosTaxTag: number;

  aplosRegisterAccountNumber: number;

  syncTransactionsCreateContact: boolean;
  defaultAplosContactId: number;

  syncFundsToPex: boolean;
  pexFundsTagId: string;
  defaultAplosFundId: number;

  defaultAplosTransactionAccountNumber: number;

  connectedOn: Date;
  lastSync: Date;

  aplosAccountId: string;
  aplosPartnerVerified: boolean;
  aplosClientId: string;
  aplosPrivateKey: string;
  aplosAuthenticationMode: AplosAuthenticationMode;

  expenseAccountMappings: ExpenseAccountMappingModel[];
  tagMappings: TagMappingModel[];
  taxTagCategoryDetails: AplosApiTaxTagCategoryDetail[];
  pexFundingSource: FundingSource;

  mapVendorCards: boolean;
  useNormalizedMerchantNames: boolean;
}

export interface ExpenseAccountMappingModel {
  //quickBooksExpenseCategoryIdFilter: number;
  syncExpenseAccounts: boolean;
  expenseAccountsPexTagId: string;
}

export interface TagMappingModel {
  aplosTagId: string;
  pexTagId: string;
  syncToPex: boolean;
}

export enum AplosAuthenticationMode {
  clientAuthentication = 0,
  partnerAuthentication = 1
}

export enum FundingSource {
  Unknown = 0,
  Prepaid = 1,
  Credit = 2
}
