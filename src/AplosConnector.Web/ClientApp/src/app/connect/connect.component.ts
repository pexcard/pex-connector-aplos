import { Component, OnInit } from "@angular/core";
import { AuthService } from "../services/auth.service";
import { PexConnectionDetailModel, PexService } from "../services/pex.service";

@Component({
  selector: "app-connect",
  templateUrl: "./connect.component.html",
  styleUrls: ["./connect.component.css"]
})
export class ConnectComponent implements OnInit {
  sessionId: string;

  isReady = false;
  connectionDetails: PexConnectionDetailModel;
  syncingRoute: string = '../sync-connect';
  vendorCardsRoute: string = '../vendors-select';

  constructor(private pex: PexService, private auth: AuthService) {

  }

  ngOnInit() {
    this.validateConnections();
  }
  
  validateConnections() {
    this.auth.sessionId.subscribe(token => {
      if (token) {
        this.sessionId = token;
        this.getConnectionDetails();
      }
    });
  }

  getConnectionDetails() {
    this.pex.getConnectionAccountDetail(this.sessionId).subscribe({
      next: (connectionDetails) => {
        this.isReady = true;
        this.connectionDetails = connectionDetails;
        this.syncingRoute = connectionDetails.pexConnection && connectionDetails.aplosConnection && connectionDetails.syncingSetup ? '../sync-manage' : '../sync-connect';
      },
      error: (error) => {
        this.isReady = true;
      }
    });    
  }
}