import { ChangeDetectorRef, Component, OnInit } from "@angular/core";
import { Router } from "@angular/router";
import { AuthService } from "../services/auth.service";
import { PexConnectionDetailModel, PexService, VendorCardsOrdered } from "../services/pex.service";

@Component({
  selector: "app-vendors-manage",
  templateUrl: "./vendors-manage.component.html",
  styleUrls: ["./vendors-manage.component.css"]
})
export class VendorsManageComponent implements OnInit {
  
  sessionId: string;
  isReady:boolean = false;
  hasVendorCards:boolean = false;
  vendorCardsOrders: VendorCardsOrdered[] = [];
  connectionDetails: PexConnectionDetailModel;

  constructor(
    private cd: ChangeDetectorRef,
    private router: Router,
    private pex: PexService,
    private auth: AuthService) {
  }

  
  ngOnInit() {
    this.isReady = false;
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
        this.connectionDetails = connectionDetails;
        this.getVendorCards();
      },
      error: (error) => {
      }
    });    
  }

  getVendorCards() {
    this.isReady = false;
    this.cd.detectChanges();
      this.pex.getVendorCards(this.sessionId).subscribe({
        next: (result) => {
          this.vendorCardsOrders = result;
          this.hasVendorCards = result.length > 0;
          this.isReady = true;
          this.cd.detectChanges();
        },
        error: () => {
          this.isReady = true;
          this.cd.detectChanges();
        }
      })
  };
}
