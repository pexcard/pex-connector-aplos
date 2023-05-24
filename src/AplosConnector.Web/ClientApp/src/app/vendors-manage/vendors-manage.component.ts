import { ChangeDetectorRef, Component, OnInit } from "@angular/core";
import { AuthService } from "../services/auth.service";
import { PexConnectionDetailModel, PexService, VendorCardOrdered } from "../services/pex.service";

@Component({
  selector: "app-vendors-manage",
  templateUrl: "./vendors-manage.component.html",
  styleUrls: ["./vendors-manage.component.css"]
})
export class VendorsManageComponent implements OnInit {
  
  sessionId: string;
  isReady:boolean = false;
  hasVendorCards:boolean = false;
  vendorCardOrders: VendorCardOrdered[] = [];
  connectionDetails: PexConnectionDetailModel;

  constructor(
    private cd: ChangeDetectorRef,
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
          this.vendorCardOrders = result
            .flatMap(o => o.cardOrders)
            .sort((a,b) => {
              return new Date(b.orderDate).getTime() - new Date(a.orderDate).getTime();
            });
          this.hasVendorCards = result.length > 0 && result.some(i => i.cardOrders.some(o => o.status == "Success"));
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
