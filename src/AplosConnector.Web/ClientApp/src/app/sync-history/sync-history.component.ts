import { Component, OnInit } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { MappingService, SettingsModel, SyncResultModel } from '../services/mapping.service';

@Component({
  selector: 'app-sync-history',
  templateUrl: './sync-history.component.html',
  styleUrls: ['./sync-history.component.css']
})
export class SyncHistoryComponent implements OnInit {
  sessionId = '';
  settings: SettingsModel;
  syncResults: SyncResultModel[];
  loadingHistory = false;

  constructor(private auth: AuthService, private mapping: MappingService) { }

  ngOnInit() {
    this.auth.sessionId.subscribe(sessionId => {
      this.sessionId = sessionId;
      if (sessionId) {
        this.getSettings();
        this.getSyncResults();
      }
    });
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
    this.mapping.sync(this.sessionId).subscribe(
      () => {
        this.loadingHistory = true;
        setTimeout(() => {
          this.getSyncResults();
        }, 60000);
      }
    );
  }


}
