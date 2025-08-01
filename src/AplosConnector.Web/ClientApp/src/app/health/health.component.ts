import { Component, OnInit } from '@angular/core';
import { HealthService, HealthResult } from '../services/health.service';

@Component({
    selector: 'app-health',
    templateUrl: './health.component.html',
    styleUrls: ['./health.component.css'],
    standalone: false
})
export class HealthComponent implements OnInit {
  loadingHealthData = false;
  pingSucceeded = true;
  healthResult: HealthResult = {currentDateTime:new Date(), pexApiAvailable:false, pexBaseUri:'', aplosApiAvailable:false, aplosBaseUri:'', storageAvailable:false };
  currentServerTime: Date = null;
  constructor(private health: HealthService) { }

  ngOnInit() {
    this.loadingHealthData = true;
    this.health.getPingResult().subscribe(
      (result)=>{
        this.pingSucceeded=true;
        this.currentServerTime= result.currentDateTime;
      }
    );

    this.health.getHealthResult().subscribe(
      (result)=>{
        this.healthResult = {...result};
        this.loadingHealthData = false;
      }
    );
  }
}
