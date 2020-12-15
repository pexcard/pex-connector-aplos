import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';

import { ExpenseAccountMappingModel, MappingService, SettingsModel, TagMappingModel } from '../services/mapping.service';
import { AuthService } from '../services/auth.service';
import { AplosService, AplosPreferences, AplosAccount, AplosObject } from '../services/aplos.service';
import { PexService } from '../services/pex.service';

@Component({
  selector: 'app-manage-connections',
  templateUrl: './manage-connections.component.html',
  styleUrls: ['./manage-connections.component.css']
})
export class ManageConnectionsComponent implements OnInit {

  constructor(private mapping: MappingService, private auth: AuthService, private aplos: AplosService, private pex: PexService,
    private router: Router) { }

  sessionId = '';
  settings: SettingsModel;
  vendorName: string;
  paymentMethod: string;
  paymentAccount: string;
  bankAccount: string;
  disconnectModal = false;
  hasTagsAvailable = true;
  tagNameFund = '';
  tagNameAccount: ExpenseAccountMappingModel[] = [];
  tagMappings: TagMappingModel[] = [];

  defaultContact: AplosObject;
  defaultFund: AplosObject;
  defaultTransactionAccount: AplosAccount;

  transferContact: AplosObject;
  transferFund: AplosObject;
  transferAccount: AplosAccount;

  feesContact: AplosObject;
  feesFund: AplosObject;
  feesAccount: AplosObject;

  AplosPreferences: AplosPreferences = { isClassEnabled: false, isLocationEnabled: false, locationFieldName: '' };

  ngOnInit() {
    this.auth.sessionId.subscribe(sessionId => {
      this.sessionId = sessionId;
      if (sessionId) {
        this.getSettings();
      }
    });
  }

  private validatePexSetup() {
    this.pex.validatePexSetup(this.sessionId).subscribe(
      (result) => {
        console.log("pex setup is valid", result);
        this.hasTagsAvailable = result.useTagsEnabled;
        this.getTagNames();

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

        this.validatePexSetup();
        this.getTransferInfo();
        this.getFeesInfo();
      }
    }
    );
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

              this.tagNameAccount.push({ expenseAccountsPexTagId: pexTagName, syncExpenseAccounts: mapping.syncExpenseAccounts, })
            }
          });
        }
        if (this.settings.tagMappings && this.settings.tagMappings.length > 0) {
          console.log('using tagMappings');
          this.aplos.getTagCategories(this.sessionId).subscribe(
            aplosTags => {
              this.settings.tagMappings.forEach(tagMapping => {
                if (tagMapping.aplosTagId && tagMapping.pexTagId) {
                  const aplosTagName = aplosTags.find(tag => { return tag.id.toString() === tagMapping.aplosTagId }).name;
                  const pexTagName = pexTags.find(tag => { return tag.id === tagMapping.pexTagId }).name;

                  this.tagMappings.push({ aplosTagId: aplosTagName, pexTagId: pexTagName, syncToPex: tagMapping.syncToPex, });
                }
              });
            });
        }
      }
    );
  }

  getTransferInfo() {
    if (this.settings.syncTransfers) {
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
    if (this.settings.syncPexFees) {
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

  onDisconnect() {
    this.mapping.disconnect(this.sessionId).subscribe(
      () => {
        this.router.navigate(['/', 'connect']);
      }
    );
  }
}
