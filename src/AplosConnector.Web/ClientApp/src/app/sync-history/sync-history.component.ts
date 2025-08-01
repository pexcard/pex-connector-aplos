import { Component, OnDestroy, OnInit } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { MappingService, SettingsModel, SyncResultModel } from '../services/mapping.service';
import { PexConnectionDetailModel, PexService } from '../services/pex.service';
import { interval, of, Subscription } from 'rxjs';
import { concatMap, timeout, catchError } from 'rxjs/operators';

@Component({
    selector: 'app-sync-history',
    templateUrl: './sync-history.component.html',
    styleUrls: ['./sync-history.component.css'],
    standalone: false
})
export class SyncHistoryComponent implements OnInit, OnDestroy {
  sessionId = '';
  connection?: PexConnectionDetailModel;
  settings: SettingsModel;
  syncResults: SyncResultModel[];
  syncing = false;
  loadingHistory = false;

  private readonly CONNECTION_POLLING_INTERVAL_MS = 60 * 1000; // 60 sec
  private _connectionPollingSubscription: Subscription = Subscription.EMPTY;

  constructor(private auth: AuthService, private mapping: MappingService, private pex: PexService) { }


  ngOnInit() {
    this.auth.sessionId.subscribe(sessionId => {
      this.sessionId = sessionId;
      if (sessionId) {
        this.pex.getConnectionAccountDetail(this.sessionId)
          .subscribe({
            next: () => {
              this._pollForConnection();
              this.getSettings();
              this.getSyncResults();
            }
          });
      }
    });
  }

  ngOnDestroy(): void {
    this._connectionPollingSubscription?.unsubscribe();
  }

  private getSettings() {
    this.mapping.getSettings(this.sessionId).subscribe(settings => {
      if (settings) {
        this.settings = { ...settings };
        console.log('got settings', this.settings);
      }
    }
    );
  }

  getSyncResults() {
    this.loadingHistory = true;
    this.mapping.getSyncResults(this.sessionId).subscribe(
      results => {
        if (results && results.length > 0) {
          console.log('got results', results);
          this.syncResults = [...results];
        }
        this.loadingHistory = false;
      }
    );
  }

  sync() {
    this.syncing = true;
    this.mapping.sync(this.sessionId).subscribe({
      next: () => this._pollForConnection()
    });
  }

  private _pollForConnection(pollingIntervalMs?: number): void {
    this._connectionPollingSubscription?.unsubscribe();
    this._connectionPollingSubscription = interval(pollingIntervalMs ?? this.CONNECTION_POLLING_INTERVAL_MS)
      .pipe(
        concatMap(() =>
          this.pex.getConnectionAccountDetail(this.sessionId)
            .pipe(
              timeout(30000),
              catchError(() => of(undefined)),
              concatMap((connection) => {
                if (connection) {
                  if (this.connection?.isSyncing && !connection.isSyncing) {
                    this.getSyncResults();
                  }
                  this.syncing = connection.isSyncing;
                }
                this.connection = connection;
                return of(void 0);
              })
            )),
      ).subscribe();
  }


}
