import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { ExpenseAccountMappingModel, FundingSource, MappingService, SettingsModel, TagMappingModel } from '../services/mapping.service';
import { AuthService } from '../services/auth.service';
import { AplosService, AplosPreferences, AplosAccount, AplosObject, AplosApiTaxTagCategoryDetail } from '../services/aplos.service';
import { PexConnectionDetailModel, PexService } from '../services/pex.service';

@Component({
    selector: 'app-sync-manage',
    templateUrl: './sync-manage.component.html',
    styleUrls: ['./sync-manage.component.css'],
    standalone: false
})
export class SyncManageComponent implements OnInit {

  constructor(private mapping: MappingService, private auth: AuthService, private aplos: AplosService, private pex: PexService,
    private router: Router) { }

  sessionId: string = '';
  settings: SettingsModel;
  vendorName: string;
  connection: PexConnectionDetailModel;
  refreshingPexAccount: boolean = false;
  paymentMethod: string;
  paymentAccount: string;
  bankAccount: string;
  disconnectModal: boolean = false;
  hasTagsAvailable: boolean = true;
  showRebatesInfoBox: boolean = false;
  tagNameFund: string = '';
  tagNameAccount: ExpenseAccountMappingModel[] = [];
  tagMappings: TagMappingModel[] = [];
  taxTagCategories: AplosApiTaxTagCategoryDetail[] = [];
  transfersAplosTaxTagName: string = ''
  pexFeesAplosTaxTagName: string = ''
  pexRebatesAplosTaxTagName: string = ''

  defaultContact: AplosObject;
  defaultFund: AplosObject;
  defaultTransactionAccount: AplosAccount;

  transferContact: AplosObject;
  transferFund: AplosObject;
  transferAccount: AplosAccount;

  feesContact: AplosObject;
  feesFund: AplosObject;
  feesAccount: AplosObject;
  aplosTagCategories: AplosObject[];

  rebatesContact: AplosObject;
  rebatesFund: AplosObject;
  rebatesAccount: AplosObject;

  AplosPreferences: AplosPreferences = { isClassEnabled: false, isLocationEnabled: false, locationFieldName: '' };
  isPrepaid: boolean = false;
  isCredit: boolean = false;

  ngOnInit() {
    this.auth.sessionId.subscribe(sessionId => {
      this.sessionId = sessionId;
      if (sessionId) {
        this.getSettings();
        this.getPexConnectionDetail();
        this.getVendorCards();
      }
    });
  }

  private validatePexSetup() {
    this.pex.validatePexSetup(this.sessionId).subscribe(
      (result) => {
        console.log("pex setup is valid", result);
        this.hasTagsAvailable = result.useTagsEnabled;
        this.getTagNames();
        if (this.settings.syncTaxTagToPex) {
          this.getTaxTagCategories();
        }
      },
      () => {
        this.hasTagsAvailable = false;
        console.log("not valid");
      }
    );
  }

  private getSettings() {
    this.mapping.getSettings(this.sessionId).subscribe(settings => {
      if (settings) {
        this.settings = { ...settings };
        console.log('got settings', this.settings);
        this.isPrepaid = this.settings.pexFundingSource == FundingSource.Prepaid;
        this.isCredit = this.settings.pexFundingSource == FundingSource.Credit;

        this.validatePexSetup();
        this.getTransferInfo();
        this.getFeesInfo();
        this.getRebatesInfo();
      }
    }
    );
  }

  private getPexConnectionDetail(){
    this.pex.getConnectionAccountDetail(this.sessionId)
    .subscribe(result => {
        this.connection = result;
    });
  }

  onAutomaticSyncToggled(): void {
    this.mapping.saveSettings(this.sessionId, this.settings).subscribe();
  }

  refreshPexAccount(){
    this.refreshingPexAccount = true;
    this.pex.updatePexAccountLinked(this.sessionId)
    .subscribe(() => {
      this.getPexConnectionDetail();
      this.refreshingPexAccount = false;
    },
    () => {
      this.refreshingPexAccount = false;
    });
  }

  getTagNames() {
    this.pex.getTags(this.sessionId).subscribe(
      pexTags => {
        console.log('got tags', pexTags);

        if (pexTags.length == 0) {
          this.hasTagsAvailable = false;
          console.log('business has no tags');
          this.getDefaultInformation();
        }
        if (this.settings.pexFundsTagId) this.tagNameFund = pexTags.find(tag => { return tag.id == this.settings.pexFundsTagId }).name;
        if (this.settings.expenseAccountMappings && this.settings.expenseAccountMappings.length > 0) {
          console.log('multi-mapped');
          this.settings.expenseAccountMappings.forEach(mapping => {
            if (mapping.expenseAccountsPexTagId) {
              const pexTagName = pexTags.find(tag => { return tag.id === mapping.expenseAccountsPexTagId }).name;

              this.tagNameAccount.push({ 
                expenseAccountsPexTagId: pexTagName,
                syncExpenseAccounts: mapping.syncExpenseAccounts,
                defaultAplosTransactionAccountNumber: mapping.defaultAplosTransactionAccountNumber })
            }
          });
        }
        if (this.settings.tagMappings && this.settings.tagMappings.length > 0) {
          console.log('using tagMappings');
          this.aplos.getTagCategories(this.sessionId).subscribe(
            aplosTagCategories => {
              this.aplosTagCategories = [... aplosTagCategories];

              if (this.settings.syncTaxTagToPex) {
                this.aplosTagCategories.push( {id: 990, "name": "990"});
              }
              this.settings.tagMappings.forEach(tagMapping => {
                if (tagMapping.aplosTagId && tagMapping.pexTagId) {
                  const aplosTagName = this.aplosTagCategories.find(tag => { return tag.id.toString() === tagMapping.aplosTagId }).name;
                  const pexTagName = pexTags.find(tag => { return tag.id === tagMapping.pexTagId }).name;
                  this.tagMappings.push({ 
                    aplosTagId: aplosTagName,
                    pexTagId: pexTagName,
                    syncToPex: tagMapping.syncToPex,
                    defaultAplosTagId: tagMapping.defaultAplosTagId
                  });
                }
              });
            });
        }
      }
    )
  }

  getTaxTagCategories() {
    console.log('using getTaxTagCategories');
    this.aplos.getTaxTagCategories(this.sessionId).subscribe(
      taxTagCategoryDetails => {
        this.settings.taxTagCategoryDetails = taxTagCategoryDetails;
        this.fillTaxTagNames();
      }
    );
  }

  fillTaxTagNames() {
    if (this.settings.pexFeesAplosTaxTag) {
      this.pexFeesAplosTaxTagName = this.getTaxTagName(this.settings.pexFeesAplosTaxTag.toString());
    }
    if (this.settings.pexFeesAplosTaxTag) {
      this.pexRebatesAplosTaxTagName = this.getTaxTagName(this.settings.pexRebatesAplosTaxTag.toString());
    }
  }

  getTaxTagName(taxTagId: string) {
    for (let category of this.settings.taxTagCategoryDetails) {
      for (let tag of category.tax_tags) {
        if (tag.id == taxTagId) {
          return `${tag.name} - ${tag.group_name}`
        }
      }
    }
  }

  getTransferInfo() {
    if (this.settings.syncTransfers || this.settings.syncInvoices) {
      this.aplos.getContact(this.sessionId, this.settings.transfersAplosContactId).subscribe(
        contact => {
          console.log('got transfer contact', contact);
          this.transferContact = { ...contact };
        }
      );

      this.aplos.getFund(this.sessionId, this.settings.transfersAplosFundId).subscribe(
        fund => {
          console.log('got transfer fund', fund);
          this.transferFund = { ...fund };
        }
      );

      this.aplos.getAccount(this.sessionId, this.settings.transfersAplosTransactionAccountNumber).subscribe(
        account => {
          console.log('got transfer account', account);
          this.transferAccount = { ...account };
        }
      );
    }
  }

  getFeesInfo() {
    if (this.settings.syncPexFees || this.settings.syncInvoices) {
      this.aplos.getContact(this.sessionId, this.settings.pexFeesAplosContactId).subscribe(
        contact => {
          console.log('got fees contact', contact);
          this.feesContact = { ...contact };
        }
      );

      this.aplos.getFund(this.sessionId, this.settings.pexFeesAplosFundId).subscribe(
        fund => {
          console.log('got fees fund', fund);
          this.feesFund = { ...fund };
        }
      );

      this.aplos.getAccount(this.sessionId, this.settings.pexFeesAplosTransactionAccountNumber).subscribe(
        account => {
          console.log('got fees account', account);
          this.feesAccount = { ...account };
        }
      );
    }
  }

  getRebatesInfo() {
    if (this.settings.syncRebates || this.settings.syncInvoices) {
      this.aplos.getContact(this.sessionId, this.settings.pexRebatesAplosContactId).subscribe(
        contact => {
          console.log('got rebates contact', contact);
          this.rebatesContact = { ...contact };
        }
      );

      this.aplos.getFund(this.sessionId, this.settings.pexRebatesAplosFundId).subscribe(
        fund => {
          console.log('got rebates fund', fund);
          this.rebatesFund = { ...fund };
        }
      );

      this.aplos.getAccount(this.sessionId, this.settings.pexRebatesAplosTransactionAccountNumber).subscribe(
        account => {
          console.log('got rebates account', account);
          this.rebatesAccount = { ...account };
        }
      );
    }
  }

  getDefaultInformation() {
    if (this.settings.defaultAplosContactId > 0) {
      this.aplos.getContact(this.sessionId, this.settings.defaultAplosContactId).subscribe(
        contact => {
          console.log('got default aplos contact', contact);
          this.defaultContact = { ...contact };
        }
      );
    }

    if (this.settings.defaultAplosFundId > 0) {
      this.aplos.getFund(this.sessionId, this.settings.defaultAplosFundId).subscribe(
        fund => {
          console.log('got default fund', fund);
          this.defaultFund = { ...fund };
        }
      );
    }

    if (this.settings.defaultAplosTransactionAccountNumber > 0) {
      this.aplos.getAccount(this.sessionId, this.settings.defaultAplosTransactionAccountNumber).subscribe(
        account => {
          console.log('got default transaction account', account);
          this.defaultTransactionAccount = { ...account };
        }
      );
    }
  }

  getVendorCards() {
    this.pex.getVendorCards(this.sessionId).subscribe({
      next: (result) => {
        this.showRebatesInfoBox = !(result.length > 0 && result.some(i => i.cardOrders.some(o => o.status == "Success")));
      }
    })
  };

  onDisconnect() {
    this.mapping.disconnect(this.sessionId).subscribe(
      () => {
        this.router.navigate(['/', 'connect']);
      }
    );
  }
}

