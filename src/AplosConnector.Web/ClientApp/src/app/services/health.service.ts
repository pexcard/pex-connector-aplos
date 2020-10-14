import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class HealthService {

  constructor(private httpClient: HttpClient,
    @Inject("BASE_URL") private baseUrl: string) { }

  private buildUrl(endpoint: string): string{
    return this.baseUrl + `api/Health/${endpoint}`
  }

  getPingResult(){
    return this.httpClient.get<PingResult>(this.buildUrl('Ping'));
  }

  getHealthResult(){
    return this.httpClient.get<HealthResult>(this.buildUrl('Check'));
  }
}

export interface PingResult{
  currentDateTime : Date
}

export interface HealthResult{
  currentDateTime: Date,
  aplosApiAvailable: boolean,
  aplosBaseUri: string,
  pexApiAvailable: boolean,
  pexBaseUri: string,
  storageAvailable: boolean
}
