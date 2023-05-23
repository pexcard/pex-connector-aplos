import { CurrencyPipe } from "@angular/common";
import { ChangeDetectorRef, Component, OnDestroy, OnInit } from "@angular/core";
import { Router } from "@angular/router";
import { Subject, Subscription } from "rxjs";
import truncate from "truncate";
import { AplosService, VendorForCard } from "../services/aplos.service";
import { AuthService } from "../services/auth.service";
import { MappingService } from "../services/mapping.service";
import { CreateVendorCard, PexConnectionDetailModel, PexService } from "../services/pex.service";

@Component({
  selector: "app-vendors-select",
  templateUrl: "./vendors-select.component.html",
  styleUrls: ["./vendors-select.component.css"]
})
export class VendorsSelectComponent implements OnInit, OnDestroy {
  sessionId: string;
  isReady = false;
  connectionDetails: PexConnectionDetailModel;
  vendors: ConfigureCreateVendorCard[] = [];
  selectedVendors: ConfigureCreateVendorCard[] = [];
  selectedSaveAmount: number = 0;

  maxVendorCards = 25;
  maxVendorCardsSelect = this.maxVendorCards;

  availableBalance = 0;
  fundingBalance = 0;

  countCardsInitialFunding = 0;
  countCardsAutoFunding = 0;

  mapVendorCardsToVendors = false;

  verifyingPexAuth = false;
  verifyingAplosAuth = false;

  isLoadingVendors = false;
  isLoadingSettings = false;
  isConfiguringSettings = false;
  isCreatingVendorCards = false;
  createVendorCardsFailed = false;

  private _sortVendorsAtoZ = (vA: ConfigureCreateVendorCard, vB: ConfigureCreateVendorCard) => vA.name.localeCompare(vB.name);
  private _sortVendorsZtoA = (vA: ConfigureCreateVendorCard, vB: ConfigureCreateVendorCard) => vB.name.localeCompare(vA.name);
  private _sortVendorsTotalSpend = (vA: ConfigureCreateVendorCard, vB: ConfigureCreateVendorCard) => vB.total - vA.total;

  vendorDisplay: (item: ConfigureCreateVendorCard) => string;
  vendorFilter: (item: ConfigureCreateVendorCard) => string;
  vendorSort: (itemA: ConfigureCreateVendorCard, itemB: ConfigureCreateVendorCard) => number;

  private _fundingSubscriptions: Subscription[] = [];

  constructor(
    private cd: ChangeDetectorRef,
    private router: Router,
    private auth: AuthService,
    private pex: PexService,
    private aplosService: AplosService,
    private mapping: MappingService,
    private ccyPipe: CurrencyPipe) {
  }

  ngOnInit() {
    this.isReady = false;
    this.validateConnections();
  }
  
  validateConnections() {
    this.auth.sessionId.subscribe(token => {
      if (token) {
        this.sessionId = token;
        this.loadData();
        this.vendorDisplay = (v) => `${truncate(v.name, 20)} (${this.ccyPipe.transform(v.total)})`;
        this.vendorFilter = (v) => v.name;
        this.vendorSort = this._sortVendorsTotalSpend;
      }
    });
  }

  ngOnDestroy(): void {
    this._fundingSubscriptions.forEach(x => x.unsubscribe());
  }

  onCancel() {
    this.router.navigate(["connect"]);
  }

  onFinish() {
    this.isCreatingVendorCards = true;
    const createVendorCardsData: CreateVendorCard[] = this.selectedVendors.map(x => {
      return {
        id: x.id,
        name: x.name,
        autoFunding: x.autoFunding,
        initialFunding: x.initialFunding,
      }
    });
    this.pex.createVendorCards(this.sessionId, createVendorCardsData)
      .subscribe({
        next: () => {
          this.router.navigate(["vendors-manage"]);
          this.isCreatingVendorCards = false;
        },
        error: (error) => {
          this.isCreatingVendorCards = false;
          this.createVendorCardsFailed = true;
        }
      });
  }

  loadData() {
    this.isReady = false;
    this.verifyingPexAuth = true;
    this.verifyingAplosAuth = true;
    this.pex.getConnectionAccountDetail(this.auth.sessionId.value)
      .subscribe({
        next: (connectionDetails) => {
          this.connectionDetails = connectionDetails;
          this.maxVendorCardsSelect = Math.min(this.maxVendorCards, connectionDetails.vendorCardsAvailable ?? this.maxVendorCards);
          this.availableBalance = connectionDetails.accountBalance;
          this.isReady = true;
          this.verifyingPexAuth = false;
          this.verifyingAplosAuth = false;
          this.cd.detectChanges();
        },
        error: () => {
          this.connectionDetails = undefined;
          this.isReady = true;
          this.verifyingPexAuth = false;
          this.verifyingAplosAuth = false;
          this.cd.detectChanges();
        },
      });
  }

  onAuthenticateWithPex() {
    this.verifyingPexAuth = true;
    this.auth.getOauthURL();
  }

  onApply() {
    window.location.href = "https://apply.pexcard.com";
  }

  onUseCurrentPexAccount() {
    this.pex.updatePexAccountLinked(this.sessionId)
      .subscribe(
        () => {
          this.verifyingPexAuth = true;
          this.pex.getConnectionAccountDetail(this.sessionId)
            .subscribe({
              next: (results) => {
                this.connectionDetails = results;
              },
              complete: () => this.verifyingPexAuth = false,
            });
        });
  }

  onConnectToAplos() {
    this.verifyingAplosAuth = true;
    this.auth.getAplosAuthURL();
  }

  onSelectVendorsWizardPageLoad() {
    this.isLoadingVendors = true;
    this.vendors = [];
    this.selectedVendors = [];
    this._fundingSubscriptions.forEach(x => x.unsubscribe());
    this._fundingSubscriptions = [];
    this.cd.detectChanges();
    this.aplosService.getVendorsForCards(this.sessionId)
      .subscribe({
        next: (vendors) => {
          this.vendors = vendors.map(x => new ConfigureCreateVendorCard(x));
          this.isLoadingVendors = false;
          this.cd.detectChanges();  
        },
        error: () => {  
          this.isLoadingVendors = false;
          this.cd.detectChanges();
        },
      });
  }

  onConfigureVendorCardsWizardPageLoad() {
    this.isLoadingSettings = true;
    this.pex.getConnectionAccountDetail(this.sessionId)
    .subscribe({
      next: (connectionDetails) => {
        this.connectionDetails = connectionDetails;
        this.maxVendorCardsSelect = Math.min(this.maxVendorCards, connectionDetails.vendorCardsAvailable ?? this.maxVendorCards);
        this.availableBalance = connectionDetails.accountBalance;
        this.cd.detectChanges();
      },
      error: () => {
        this.connectionDetails = undefined;
        this.verifyingPexAuth = false;
        this.verifyingAplosAuth = false;
        this.cd.detectChanges();
      },
    });
    this.mapping.getVendorCardsMapped(this.sessionId).subscribe({
      next: (enabled) => {
        this.mapVendorCardsToVendors = enabled;
        this.isLoadingSettings = false;
      },
      error: () => this.isLoadingSettings = false
    });
    this.availableBalance = this.connectionDetails.accountBalance;
    this.fundingBalance = 0;
    this.selectedSaveAmount = 0;
    this._fundingSubscriptions = [];
    this.selectedVendors.forEach((v) => {
      // auto funding on by default
      v.autoFunding = this.connectionDetails.isPrepaid && this.connectionDetails.useBusinessBalanceEnabled;
      this._fundingSubscriptions.push(
        v.fundingChanged$.subscribe({
          next: () => {
            this.fundingBalance = this.selectedVendors.reduce((sum, x) => sum + (x.initialFunding ?? 0), 0);
            this.availableBalance = this.connectionDetails.accountBalance > 0 ? this.connectionDetails.accountBalance - this.fundingBalance : 0;
            this.countCardsInitialFunding = this.selectedVendors.filter(x => x.initialFunding > 0).length;
            this.countCardsAutoFunding = this.selectedVendors.filter(x => x.autoFunding === true).length;
          }
        })
      );
      this.selectedSaveAmount += v.total * 0.01;
    });
  }

  onMapCardsToVendors() {
    this.isConfiguringSettings = true;
    this.mapping.setVendorCardsMapped(this.sessionId, this.mapVendorCardsToVendors)
      .subscribe({
        next: () => this.isConfiguringSettings = false,
        error: () => this.isConfiguringSettings = false
      })
  }

  onSortAtoZ() {
    this.vendorSort = this._sortVendorsAtoZ;
  }

  onSortZtoA() {
    this.vendorSort = this._sortVendorsZtoA;
  }

  onSortTotalSpend() {
    this.vendorSort = this._sortVendorsTotalSpend;
  }

}

export class ConfigureCreateVendorCard {
  id: number;
  name: string;
  total: number;
  autoFunding: boolean;
  initialFunding?: number;

  fundingChanged$ = new Subject<ConfigureCreateVendorCard>();

  constructor(data: VendorForCard) {
    this.id = data.id;
    this.name = data.name;
    this.total = data.total;
  }

  initialFundingChanged() {
    if (this.initialFunding) {
      this.initialFunding = Number.parseFloat(this.initialFunding?.toString());
    } else {
      this.initialFunding = undefined;
    }
    this.fundingChanged$.next(this);
  }

  autoFundingChanged() {
    this.initialFunding = undefined;
    this.initialFundingChanged();
  }

}
