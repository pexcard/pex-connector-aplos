import { Injectable, Inject } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { retryWithBackoff } from "../operators/retryWithBackoff.operator";
import { CacheRepositoryService } from './cache-repository.service';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: "root"
})
export class PexService {
  selectedProjects: number[] = [];

  private readonly CACHE_KEY_PEX_VALIDITY = "pex.getPexValidity";
  private readonly CACHE_KEY_PEX_TAGS = "pex.getPexTags";

  constructor(
    private httpClient: HttpClient,
    @Inject("BASE_URL") private baseUrl: string,
    private cache: CacheRepositoryService
  ) { }

  private buildUrl(sessionId: string, endpoint: string): string {
    return this.baseUrl + `api/PEX/${endpoint}?sessionId=${sessionId}`;
  }

  validatePexSetup(sessionId: string): Observable<PexValidityModel> {
    return this.cache.runAndCacheOrGetFromCache(
      this.CACHE_KEY_PEX_VALIDITY, this.httpClient
        .get(this.buildUrl(sessionId, 'Validity'))
        .pipe(retryWithBackoff(1000, 1, 100))
      , 60);
  }

  getTags(sessionId: string): Observable<PexTagInfoModel[]> {
    return this.cache.runAndCacheOrGetFromCache(
      this.CACHE_KEY_PEX_TAGS,
      this.httpClient
        .get(this.buildUrl(sessionId, 'Tags'))
        .pipe(retryWithBackoff())
      , 60);
  }

  getAuthenticationStatus(sessionId: string) {
    return this.cache.runAndCacheOrGetFromCache("Pex.AuthenticationStatus", this.httpClient
      .get(this.buildUrl(sessionId, 'AuthenticationStatus'))
      .pipe(retryWithBackoff(50, 1, 500)), 5);
  }

  getConnectionAccountDetail(sessionId: string): Observable<PexConnectionDetailModel> {
    return this.cache.runAndCacheOrGetFromCache("Pex.ConnectionAccountDetail", this.httpClient
      .get<PexConnectionDetailModel>(this.buildUrl(sessionId, 'ConnectionAccountDetail'))
      .pipe(retryWithBackoff(50, 1, 500)), 5);
  }

  updatePexAccountLinked(sessionId: string) {
    return this.httpClient.post(this.buildUrl(sessionId, "UpdatePexAccountLinked"), null)
      .pipe(retryWithBackoff(50, 1, 500));
  }

  createVendorCards(sessionId: string, selectedVendors: CreateVendorCard[]) {
    return this.httpClient.post<void>(this.buildUrl(sessionId, "VendorCards"), selectedVendors);
  }

  getVendorCards(sessionId: string) {
    return this.httpClient.get<VendorCardsOrdered[]>(this.buildUrl(sessionId, "VendorCards"));
  }

  disconnectPexAccountLinked(sessionId: string) {
    return this.httpClient.post(this.buildUrl(sessionId, "Disconnect"), null);
  }
}

export interface PexValidityModel {
  isValid: boolean;
  useTagsEnabled: boolean;
  requestAchTransferEnabled: boolean;
}

export interface PexTagInfoModel {
  id: string,
  type: CustomFieldType,
  name: string,
  description: string,
  order: number,
  isRequired: boolean
}

export interface PexConnectionDetailModel {
  name: string,
  email: string,
  pexConnection: boolean,
  aplosConnection: boolean,
  syncingSetup: boolean,
  vendorsSetup: boolean,
  isSyncing: boolean;
  lastSync: string,
  accountBalance?: number;
  useBusinessBalanceEnabled: boolean;
  vendorCardsAvailable?: number;
  isPrepaid: boolean;
  isCredit: boolean;
}

export enum CustomFieldType {
  Text = 0,
  YesNo = 1,
  Dropdown = 2,
  Decimal = 3,
  PercentageTax = 4,
  AbsoluteTax = 5,
  MerchantAddress = 6
}

export interface CreateVendorCard {
  id: number;
  name: string;
  autoFunding: boolean;
  initialFunding?: number;
  groupId?: number;
}

export interface VendorCardOrdered {
  orderId: number;
  id: number;
  name: string;
  autoFunding: boolean;
  initialFunding?: number;
  groupId?: number;
  orderDate: Date;
  accountId: number;
  accountUrl: string;
  status?: string;
  error?: string;
}

export interface VendorCardsOrdered {
  id: number;
  cardOrders: VendorCardOrdered[];
}
