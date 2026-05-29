import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';

import { retryWithBackoff } from '../operators/retryWithBackoff.operator';
import { CacheRepositoryService } from './cache-repository.service';
import { AplosApiTaxTagCategoryDetail } from './aplos.service';

// TO DELETE BEFORE PUSH — Mock mode for local UI testing without backend
export const ENABLE_MOCK_MODE = true;

@Injectable({
  providedIn: 'root'
})
export class MappingService {
  constructor(
    private httpClient: HttpClient,
    @Inject('BASE_URL') private baseUrl: string,
    private cache: CacheRepositoryService
  ) { }

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
    if (ENABLE_MOCK_MODE) {
      return of({
        isAuthenticated: true,
        hasAplosAccountId: true,
        aplosAuthenticationMode: AplosAuthenticationMode.partnerAuthentication,
        partnerVerificationUrl: null
      });
    }
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
    // TO DELETE BEFORE PUSH — Mock mode for local UI testing
    if (ENABLE_MOCK_MODE) {
      const mockSettings: SettingsModel = {
        automaticSync: true,
        syncTransactions: true,
        syncTaxTagToPex: false,
        syncApprovedOnly: false,
        earliestTransactionDateToSync: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
        syncTransfers: false,
        syncInvoices: false,
        syncPexFees: false,
        syncRebates: false,
        syncReimbursements: true, // Enable reimbursements for testing
        transfersAplosContactId: 1,
        transfersAplosFundId: 100,
        transfersAplosTransactionAccountNumber: 1000,
        pexFeesAplosRegisterAccountNumber: 1000,
        pexFeesAplosContactId: 1,
        pexFeesAplosFundId: 100,
        pexFeesAplosTransactionAccountNumber: 5000,
        pexFeesAplosTaxTag: '',
        pexRebatesAplosContactId: 1,
        pexRebatesAplosFundId: 100,
        pexRebatesAplosTransactionAccountNumber: 5000,
        pexRebatesAplosTaxTag: '',
        syncReimbursementsCreateContact: false,
        reimbursementsAplosContactId: 1,
        reimbursementsAplosFundId: 100,
        reimbursementsAplosTransactionAccountNumber: 5000,
        reimbursementsAplosTaxTag: '',
        aplosRegisterAccountNumber: 1000,
        syncTransactionsCreateContact: false,
        defaultAplosContactId: 0,
        syncFundsToPex: true,
        pexFundsTagId: 'pex-3',
        defaultAplosFundId: 0,
        defaultAplosTransactionAccountNumber: 0,
        connectedOn: new Date(),
        lastSync: new Date(),
        aplosAccountId: 'sandbox-account',
        aplosPartnerVerified: false,
        aplosClientId: '',
        aplosPrivateKey: '',
        aplosAuthenticationMode: AplosAuthenticationMode.partnerAuthentication,
        expenseAccountMappings: [
          { expenseAccountsPexTagId: 'pex-1', syncExpenseAccounts: true, defaultAplosTransactionAccountNumber: 5000 }
        ],
        tagMappings: [
          { aplosTagId: '10', pexTagId: 'pex-1', syncToPex: true, defaultAplosTagId: '' },
          { aplosTagId: '20', pexTagId: 'pex-2', syncToPex: false, defaultAplosTagId: '' },
          { aplosTagId: '30', pexTagId: 'pex-3', syncToPex: true, defaultAplosTagId: '' },
        ],
        taxTagCategoryDetails: [],
        pexFundingSource: FundingSource.Prepaid,
        mapVendorCards: false,
        useNormalizedMerchantNames: false,
        postDateType: PostDateType.Transaction,
        transferTagMappings: [],
        feeTagMappings: [],
        rebateTagMappings: [],
        reimbursementTagMappings: [
          { aplosTagId: '1', defaultAplosTagValue: 'Meals & Entertainment' }
        ],
        syncInvoicesMethod: SyncInvoicesMethod.Simple
      };
      return of(mockSettings);
    }

    return this.cache.runAndCacheOrGetFromCache(this.CACHE_KEY_GET_SETTINGS, this.httpClient
      .get<SettingsModel>(this.buildUrl(sessionId, 'Settings'))
      .pipe(retryWithBackoff()), 5);
  }

  saveSettings(sessionId: string, settings: SettingsModel) {
    if (ENABLE_MOCK_MODE) {
      return of(null);
    }
    this.clearCache();
    return this.httpClient.put(this.buildUrl(sessionId, 'Settings'), settings);
  }

  getSyncResults(sessionId: string) {
    if (ENABLE_MOCK_MODE) {
      const now = new Date();
      return of([
        { createdUtc: now, syncType: 'Transactions', syncStatus: 'Success', syncedRecords: 12, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'Transfers', syncStatus: 'Success', syncedRecords: 3, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'Reimbursements', syncStatus: 'Success', syncedRecords: 5, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'Rebates', syncStatus: 'Success', syncedRecords: 0, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'PEX Account Fees', syncStatus: 'Success', syncedRecords: 1, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'Tag Values (Funds)', syncStatus: 'Success', syncedRecords: 0, syncNotes: '', PEXBusinessAcctId: 0 },
        { createdUtc: now, syncType: 'Tag Values (Accounts)', syncStatus: 'Success', syncedRecords: 0, syncNotes: '', PEXBusinessAcctId: 0 },
      ]);
    }
    return this.cache.runAndCacheOrGetFromCache(this.CACHE_KEY_GET_SYNC_RESULTS, this.httpClient
      .get<SyncResultModel[]>(this.buildUrl(sessionId, 'SyncResults'))
      .pipe(retryWithBackoff()), 5);
  }

  sync(sessionId: string) {
    if (ENABLE_MOCK_MODE) return of(null);
    this.clearCache();
    return this.httpClient.post(this.buildUrl(sessionId, 'Sync'), null);
  }

  getVendorCardsMapped(sessionId: string): Observable<boolean> {
    if (ENABLE_MOCK_MODE) return of(true);
    return this.httpClient
      .get<boolean>(this.buildUrl(sessionId, 'Settings/VendorCardMapping'))
      .pipe(retryWithBackoff());
  }

  setVendorCardsMapped(sessionId: string, enable: boolean): Observable<boolean> {
    if (ENABLE_MOCK_MODE) return of(true);
    return this.httpClient
      .put<void>(this.buildUrl(sessionId, 'Settings/VendorCardMapping'), enable)
      .pipe(retryWithBackoff());
  }

  disconnect(sessionId: string) {
    if (ENABLE_MOCK_MODE) return of(null);
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
  syncTaxTagToPex: boolean;

  syncApprovedOnly: boolean;
  earliestTransactionDateToSync: string;
  syncTransfers: boolean;
  syncInvoices: boolean,
  syncPexFees: boolean;
  syncRebates: boolean;
  syncReimbursements: boolean;

  transfersAplosContactId: number;
  transfersAplosFundId: number;
  transfersAplosTransactionAccountNumber: number;

  pexFeesAplosRegisterAccountNumber: number;
  pexFeesAplosContactId: number;
  pexFeesAplosFundId: number;
  pexFeesAplosTransactionAccountNumber: number;
  pexFeesAplosTaxTag: string;

  pexRebatesAplosContactId: number;
  pexRebatesAplosFundId: number;
  pexRebatesAplosTransactionAccountNumber: number;
  pexRebatesAplosTaxTag: string;

  syncReimbursementsCreateContact: boolean;
  reimbursementsAplosContactId: number;
  reimbursementsAplosFundId: number;
  reimbursementsAplosTransactionAccountNumber: number;
  reimbursementsAplosTaxTag: string;

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
  postDateType: PostDateType;

  transferTagMappings: AplosTagMappingModel[];
  feeTagMappings: AplosTagMappingModel[];
  rebateTagMappings: AplosTagMappingModel[];
  reimbursementTagMappings: AplosTagMappingModel[];
  syncInvoicesMethod: SyncInvoicesMethod;
}

export interface ExpenseAccountMappingModel {
  syncExpenseAccounts: boolean;
  expenseAccountsPexTagId: string;
  defaultAplosTransactionAccountNumber: number;
}

export interface TagMappingModel {
  aplosTagId: string;
  pexTagId: string;
  syncToPex: boolean;
  defaultAplosTagId: string;
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

export enum PostDateType {
  Transaction = 0,
  Settlement = 1
}

export enum SyncInvoicesMethod {
  Simple = 'simple',
  RebateDeposit = 'rebate-deposit',
  RebateDistribute = 'rebate-distribute'
}

export interface AplosTagMappingModel {
  aplosTagId: string;
  defaultAplosTagValue: string;
}
