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

  validatePexSetup(sessionId: string) : Observable<PexValidityModel> {
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
}

export interface PexValidityModel{
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

export enum CustomFieldType
{
    Text=0,
    YesNo=1,
    Dropdown=2,
    Decimal=3,
    PercentageTax=4,
    AbsoluteTax=5,
    MerchantAddress=6
}
