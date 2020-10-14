import { Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { publishReplay } from "rxjs/internal/operators/publishReplay";
import { refCount } from "rxjs/operators";

@Injectable({
  providedIn: "root"
})
export class CacheRepositoryService {
  constructor() {}

  private cacheMap = new Map();
  private cacheTimerMap = new Map();

  runAndCacheOrGetFromCache(caller, resultObs: Observable<any>, durationSeconds: number) {
    let cacheExpirationDate = this.cacheTimerMap.get(caller);

    if (cacheExpirationDate && new Date() > cacheExpirationDate) {
      this.clearCache(caller);
    }

    let cachedObs = this.cacheMap.get(caller);
    if (cachedObs == null) {
      cachedObs = resultObs.pipe(
        publishReplay(1),
        refCount()
      );

      this.cacheMap.set(caller, cachedObs);

      let expiration = new Date();
      expiration.setSeconds(
        expiration.getSeconds() + durationSeconds
      );

      this.cacheTimerMap.set(caller, expiration);
    }
    return cachedObs;
  }

  clearCache(caller){
    this.cacheMap.set(caller, null);
    this.cacheTimerMap.set(caller, null);
  }

  clearAllAppCache(){
    this.cacheMap = new Map();
    this.cacheTimerMap = new Map();
  }
}
