import { Component, OnInit, ViewChild } from "@angular/core";
import { Router, ActivatedRoute } from "@angular/router";
import { ClrWizard } from "@clr/angular";
import { FormGroup, FormArray, FormControl, Validators } from "@angular/forms";

import { AuthService } from "../services/auth.service";
import {
  MappingService,
  SettingsModel,
  ExpenseAccountMappingModel,

  TagMappingModel,
  AplosAuthenticationStatusModel,
  AplosAuthenticationMode
} from "../services/mapping.service";
import { AplosService, AplosPreferences, AplosAccount, AplosObject } from "../services/aplos.service";
import { PexService, PexTagInfoModel, CustomFieldType } from '../services/pex.service';

@Component({
  selector: "app-connect",
  templateUrl: "./connect.component.html",
  styleUrls: ["./connect.component.css"]
})
export class ConnectComponent implements OnInit {
  constructor(
    private router: Router,
    private activatedRoute: ActivatedRoute,
    private auth: AuthService,
    private mapping: MappingService,
    private aplos: AplosService,
    private pex: PexService
  ) { }

  private _open = true;
  @ViewChild('wizard', { static: true }) wizard: ClrWizard;
  expenseAccountForm: FormGroup = new FormGroup({
    expenseAccounts: new FormArray([
      new FormGroup({
        expenseAccount: new FormControl(),
        syncExpenseAccountToPex: new FormControl(false)
      })
    ])
  });

  tagMappingForm: FormGroup = new FormGroup({
    tagMappings: new FormArray([
      new FormGroup({
        aplosTag: new FormControl(),
        pexTag: new FormControl(),
        syncToPex: new FormControl(false)
      })
    ])
  });

  isAuthenticated = false;
  sessionId: string;
  savingSettings = false;
  projectForm: FormGroup;
  savingProjects = false;
  aplosContacts: AplosObject[] = [];
  aplosAssetAccounts: AplosAccount[] = [];
  aplosExpenseAccounts: AplosAccount[] = [];
  aplosFunds: AplosObject[] = [];
  aplosTagCategories: AplosObject[] = [];
  hasTagsAvailable = true;
  savingServiceAccountSucceeded = true;
  availablePexTags: PexTagInfoModel[] = [];
  aplosPreferences: AplosPreferences = { isClassEnabled: false, isLocationEnabled: false, locationFieldName: '' };

  settingsModel: SettingsModel = {
    syncTransactions: true,
    syncTransfers: true,
    syncApprovedOnly: true,
    connectedOn: new Date(),
    lastSync: new Date(),
    earliestTransactionDateToSync: '11/1/2019',
    aplosAccountId: '',
    aplosPartnerVerified: false,
    aplosClientId: '',
    aplosPrivateKey: '',
    aplosAuthenticationMode: AplosAuthenticationMode.clientAuthentication,
    syncTransactionsCreateContact: true,
    defaultAplosContactId: 0,
    defaultAplosFundId: 0,
    defaultAplosTransactionAccountNumber: 0,
    pexFundsTagId: '',
    aplosRegisterAccountNumber: 0,
    syncFundsToPex: false,
    syncTags: true,
    pexFeesAplosContactId: 0,
    pexFeesAplosFundId: 0,
    pexFeesAplosRegisterAccountNumber: 0,
    pexFeesAplosTransactionAccountNumber: 0,
    syncPexFees: false,
    transfersAplosContactId: 0,
    transfersAplosFundId: 0,
    transfersAplosTransactionAccountNumber: 0,
    expenseAccountMappings: [],
    tagMappings: []
  };

  getExpenseAccountFormElements() {
    return this.expenseAccountForm.get('expenseAccounts') as FormArray;
  }

  deleteExpenseAccountElement(idx: number) {
    this.getExpenseAccountFormElements().removeAt(idx);
  }

  addExpenseAccountElement(expenseAccountMapping: ExpenseAccountMappingModel = { expenseAccountsPexTagId: null, /*quickBooksExpenseCategoryIdFilter: 0,*/ syncExpenseAccounts: false }) {
    console.log('adding expense account element');
    this.getExpenseAccountFormElements().push(
      new FormGroup(
        {
          expenseAccount: new FormControl(expenseAccountMapping.expenseAccountsPexTagId, Validators.required),
          syncExpenseAccountToPex: new FormControl(expenseAccountMapping.syncExpenseAccounts)
        }
      )
    );
  }

  getTagMappingFormElements() {
    return this.tagMappingForm.get('tagMappings') as FormArray;
  }

  deleteTagMappingElement(idx: number) {
    this.getTagMappingFormElements().removeAt(idx);
  }

  addTagMappingElement(tagMapping: TagMappingModel = { aplosTagId: null, pexTagId: null, syncToPex: false }) {
    console.log('adding tag mapping element');
    this.getTagMappingFormElements().push(
      new FormGroup(
        {
          aplosTag: new FormControl(tagMapping.aplosTagId, Validators.required),
          pexTag: new FormControl(tagMapping.pexTagId, Validators.required),
          syncToPex: new FormControl(tagMapping.syncToPex)
        }
      )
    );
  }

  get earliestTransactionDateToSync() {
    return this.settingsModel.earliestTransactionDateToSync;
  }

  set earliestTransactionDateToSync(value: string) {
    this.settingsModel.earliestTransactionDateToSync = value;
  }

  public get open() {
    return this._open;
  }

  public set open(value: boolean) {
    this._open = value;
    if (!value) {
      this.router.navigate(["../", "manage-connections"], {
        relativeTo: this.activatedRoute
      });
    }
  }

  verifyingAplosAuthentication = false;
  aplosAuthenticationStatus: AplosAuthenticationStatusModel;
  ngOnInit() {
    this.auth.sessionId.subscribe(token => {
      if (token) {
        this.isAuthenticated = true;
        this.sessionId = token;
        this.verifyingAplosAuthentication = true;
        this.mapping.getAplosAuthenticationStatus(this.sessionId)
          .subscribe(
            (result) => {
              console.log('aplosAuthenticationStatus', result);
              this.aplosAuthenticationStatus = { ...result };

              this.verifyingAplosAuthentication = false;

              if (result.isAuthenticated) {
                this.getSettings();
                this.validatePexSetup();
              }
            }
          );
      }
    });
  }


  loadingAplosAccounts = false;
  errorLoadingAplosAccounts = false;
  getAssetAccounts() {
    this.loadingAplosAccounts = true;
    this.errorLoadingAplosAccounts = false;

    return this.aplos.getAccounts(this.sessionId, "asset").subscribe(
      aplosAccounts => {
        console.log('getting asset accounts', aplosAccounts);
        this.aplosAssetAccounts = [ ...aplosAccounts ];
        this.loadingAplosAccounts = false;
        console.log('got asset accounts', this.aplosAssetAccounts);
      },
      () => {
        this.loadingAplosAccounts = false;
        this.errorLoadingAplosAccounts = true;
      }
    )
  }

  loadingExpenseAccounts = false;
  errorLoadingExpenseAccounts = false;
  getExpenseAccounts() {
    this.loadingExpenseAccounts = true;
    this.errorLoadingExpenseAccounts = false;

    return this.aplos.getAccounts(this.sessionId, "expense").subscribe(
      expenseAccounts => {
        console.log('getting expense accounts', expenseAccounts);
        this.aplosExpenseAccounts = [...expenseAccounts];
        this.loadingExpenseAccounts = false;
        console.log('got expense accounts', this.aplosExpenseAccounts);
      },
      () => {
        this.loadingExpenseAccounts = false;
        this.errorLoadingExpenseAccounts = true;
      }
    )
  }

  loadingAplosContacts = false;
  errorLoadingAplosContacts = false;
  getContacts() {
    this.loadingAplosContacts = true;
    this.errorLoadingAplosContacts = false;

    return this.aplos.getContacts(this.sessionId).subscribe(
      aplosContacts => {
        console.log('getting contacts', aplosContacts);
        this.aplosContacts = [ ...aplosContacts ];
        this.loadingAplosContacts = false;
        console.log('got contacts', this.aplosContacts);
      },
      () => {
        this.loadingAplosContacts = false;
        this.errorLoadingAplosContacts = true;
      }
    );
  }

  loadingAplosFunds = false;
  errorLoadingAplosFunds = false;
  getFunds() {
    this.loadingAplosFunds = true;
    this.errorLoadingAplosFunds = false;

    return this.aplos.getFunds(this.sessionId).subscribe(
      aplosFunds => {
        console.log('getting funds', aplosFunds);
        this.aplosFunds = [ ...aplosFunds];
        this.loadingAplosFunds = false;
        console.log('got funds', this.aplosFunds);
      },
      () => {
        this.loadingAplosFunds = false;
        this.errorLoadingAplosFunds = true;
      }
    );
  }

  loadingAplosTagCategories = false;
  errorLoadingAplosTagCategories = false;
  getTagCategories() {
    this.loadingAplosTagCategories = true;
    this.errorLoadingAplosTagCategories = false;

    return this.aplos.getTagCategories(this.sessionId).subscribe(
      aplosTagCategories => {
        console.log('getting TagCategories', aplosTagCategories);
        this.aplosTagCategories = [ ...aplosTagCategories];
        this.loadingAplosTagCategories = false;
        console.log('got TagCategories', this.aplosTagCategories);
      },
      () => {
        this.loadingAplosTagCategories = false;
        this.errorLoadingAplosTagCategories = true;
      }
    );
  }

  onAuthenticateWithPex() {
    this.auth.getOauthURL();
  }

  onApply() {
    window.location.href = "https://apply.pexcard.com";
  }

  redirectingToAplosAuth = false;
  onAplosPartnerVerification() {
    this.redirectingToAplosAuth = true;
    window.location.href = this.aplosAuthenticationStatus.partnerVerificationUrl;
}

  getSettings() {
    this.mapping.getSettings(this.sessionId).subscribe(data => {
      this.settingsModel = { ...data };
      console.log('got settings.', this.settingsModel);
      let d = new Date(data.earliestTransactionDateToSync);

      let newDate = 1 + d.getMonth() + '/' + d.getDate() + '/' + d.getFullYear();
      this.settingsModel.earliestTransactionDateToSync = newDate;

      this.getContacts();
      this.getAssetAccounts();
      this.getExpenseAccounts();
      this.getFunds();
      this.getTagCategories();
      this.initExpenseAccountMappingFormFromSettings(this.settingsModel);
      this.initTagMappingFormFromSettings(this.settingsModel);
    });
  }

  initExpenseAccountMappingFormFromSettings(settings: SettingsModel) {
    this.getExpenseAccountFormElements().clear();
    if (settings.expenseAccountMappings && settings.expenseAccountMappings.length) {
      settings.expenseAccountMappings.forEach(
        mapping => {
          console.log('Adding expense account mapping', mapping);
          this.addExpenseAccountElement(mapping);
        }
      );
    }
    else {
      this.addExpenseAccountElement();
    }
  }

  initTagMappingFormFromSettings(settings: SettingsModel) {
    this.getTagMappingFormElements().clear();
    while (this.getTagMappingFormElements().length !== 0) {
      this.getTagMappingFormElements().removeAt(0);
    }
    if (settings.tagMappings && settings.tagMappings.length) {
      settings.tagMappings.forEach(
        mapping => {
          console.log('Adding tag mapping', mapping);
          this.addTagMappingElement(mapping);
        }
      );
    }
    else {
      this.addTagMappingElement();
    }
  }

  private validatePexSetup() {
    this.pex.validatePexSetup(this.sessionId).subscribe(
      (result) => {
        console.log("pex setup is valid", result);
        this.hasTagsAvailable = result.useTagsEnabled;
        this.getAvailableTags();
      },
      () => {
        this.hasTagsAvailable = false;
        console.log("not valid");
      }
    );
  }

  onSettingsCommit() {
    this.saveSettings(false).subscribe(() => {
      this.savingSettings = false;
      this.handleStepCompleted();
    });
  }

  savingAplosCredentials = false;
  errorSavingAplosCredentials = false;
  onAplosCredentialsCommit() {
    this.savingAplosCredentials = true;
    this.errorSavingAplosCredentials = false;

    console.info("Saving Aplos credentials...");

    this.auth.createAplosToken(this.sessionId, this.settingsModel.aplosClientId, this.settingsModel.aplosPrivateKey)
      .subscribe(result => {
        this.savingAplosCredentials = false;

        console.info(result);

        this.saveSettings().subscribe(() => {
          this.savingSettings = false;

          this.getSettings();
          this.handleStepCompleted();
        });
      },
        () => {
          console.warn("Error saving Aplos credentials.");

          this.savingAplosCredentials = false;
          this.errorSavingAplosCredentials = true;
        }
      );
  }

  saveSettings(closeWizard: boolean = false) {
    this.savingSettings = true;
    console.log('saving settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }

  onTagMappingCommit() {
    if (this.expenseAccountForm.valid) {
      let accounts: ExpenseAccountMappingModel[] = [];
      let hasMultipleMappings = this.expenseAccountForm.value.expenseAccounts.length > 1;
      this.expenseAccountForm.value.expenseAccounts.forEach(element => {
        accounts.push({
          expenseAccountsPexTagId: element.expenseAccount,
          //quickBooksExpenseCategoryIdFilter: 0,
          syncExpenseAccounts: hasMultipleMappings ? false : element.syncExpenseAccountToPex
        });
      });

      this.settingsModel.expenseAccountMappings = accounts;

      let tagMappings: TagMappingModel[] = [];
      //let hasMultipleMappings = this.expenseAccountForm.value.expenseAccounts.length > 1;
      this.tagMappingForm.value.tagMappings.forEach(element => {
        if (element.aplosTag && element.pexTag) {
          tagMappings.push({
            aplosTagId: element.aplosTag,
            pexTagId: element.pexTag,
            syncToPex: element.syncToPex
          });
        }
      });

      this.settingsModel.tagMappings = tagMappings;

      this.saveSettings(false).subscribe(() => {
        this.savingSettings = false;
        this.handleStepCompleted();
      });
    }
  }

  onDefaultCategoryCommit() {
    this.saveSettings(true).subscribe(() => {
      this.savingSettings = false;
      this.handleStepCompleted();
    });
  }

  handleStepCompleted(): void {
    if (this.wizard.isLast) {
      this.wizard.close();
    }
    else {
      this.wizard.forceNext();
    }
  }

  getAvailableTags() {
    this.availablePexTags = [];
    this.pex.getTags(this.sessionId).subscribe(
      tags => {
        tags.forEach(
          t => {
            let tag: PexTagInfoModel = { ...t };
            tag.name += tag.isRequired ? '*' : '';
            this.availablePexTags.push(tag);
          }
        )
        console.log('got tags', this.availablePexTags);

        if (this.availablePexTags.length == 0) {
          this.hasTagsAvailable = false;
          console.log('business has no tags');
        }
      }
    );
  }

  getDropDownTags(): PexTagInfoModel[] {
    const tags = this.availablePexTags.filter(t => t.type == CustomFieldType.Dropdown);
    return [ ...tags ];
  }

  getYesNoTags(): PexTagInfoModel[] {
    const tags = this.availablePexTags.filter(t => t.type == CustomFieldType.YesNo);
    return [ ...tags ];
  }

  onCloseWizard() {
    this.open = false;
  }

  onTagMappingCancel() {
    this.open = false;
  }
}

export interface OauthResponse {
  OAuthUrl: string;
}
