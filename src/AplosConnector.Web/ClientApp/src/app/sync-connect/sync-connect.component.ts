import { Component, OnInit, ViewChild } from "@angular/core";
import { Router, ActivatedRoute } from "@angular/router";
import { ClrWizard } from "@clr/angular";
import { UntypedFormGroup, UntypedFormArray, UntypedFormControl, Validators, FormArray, FormGroup, FormControl, AbstractControl, ValidationErrors } from "@angular/forms";

import { AuthService } from "../services/auth.service";
import {
  MappingService,
  SettingsModel,
  ExpenseAccountMappingModel,
  TagMappingModel,
  AplosAuthenticationStatusModel,
  AplosAuthenticationMode,
  FundingSource,
  PostDateType,
  SyncInvoicesMethod
} from "../services/mapping.service";
import { AplosService, AplosPreferences, AplosAccount, AplosObject, AplosApiTaxTagCategoryDetail } from "../services/aplos.service";
import { PexService, PexTagInfoModel, CustomFieldType } from '../services/pex.service';
import { catchError, switchMap, tap } from "rxjs/operators";
import { of, throwError } from "rxjs";

// A tag mapping row is valid when it is either completely empty (treated as
// non-existent and dropped on save) or fully filled. A partially filled row
// (only one of Aplos tag / PEX tag selected) is invalid.
function tagMappingRowValidator(group: AbstractControl): ValidationErrors | null {
  const isSet = (value: unknown) => value !== null && value !== undefined && value !== '';
  const aplosSet = isSet(group.get('aplosTag')?.value);
  const pexSet = isSet(group.get('pexTag')?.value);
  if (aplosSet === pexSet) {
    return null;
  }
  return { incompleteTagMapping: true };
}

@Component({
  selector: 'app-sync-connect',
  templateUrl: './sync-connect.component.html',
  styleUrls: ['./sync-connect.component.css'],
  standalone: false
})
export class SyncConnectComponent implements OnInit {
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
  defaultCategoryForm: UntypedFormGroup = new UntypedFormGroup({
    transferTagMappings: new UntypedFormArray([
      new UntypedFormGroup({
        aplosTagId: new UntypedFormControl(),
        defaultAplosTagValue: new UntypedFormControl()
      })
    ]),
    feeTagMappings: new UntypedFormArray([
      new UntypedFormGroup({
        aplosTagId: new UntypedFormControl(),
        defaultAplosTagValue: new UntypedFormControl()
      })
    ]),
    rebateTagMappings: new UntypedFormArray([
      new UntypedFormGroup({
        aplosTagId: new UntypedFormControl(),
        defaultAplosTagValue: new UntypedFormControl()
      })
    ]),
    defaultAplosFundId: new UntypedFormControl(),
    defaultAplosTransactionAccountNumber: new UntypedFormControl(),
    transfersAplosContactId: new UntypedFormControl(),
    transfersAplosFundId: new UntypedFormControl(),
    transfersAplosTransactionAccountNumber: new UntypedFormControl(),
    pexFeesAplosContactId: new UntypedFormControl(),
    pexFeesAplosFundId: new UntypedFormControl(),
    pexFeesAplosTransactionAccountNumber: new UntypedFormControl(),
    pexFeesAplosTaxTag: new UntypedFormControl(),
    pexRebatesAplosContactId: new UntypedFormControl(),
    pexRebatesAplosFundId: new UntypedFormControl(),
    pexRebatesAplosTransactionAccountNumber: new UntypedFormControl(),
    pexRebatesAplosTaxTag: new UntypedFormControl(),
    reimbursementsAplosRegisterAccountNumber: new UntypedFormControl()
  });


  expenseAccountForm: UntypedFormGroup = new UntypedFormGroup({
    expenseAccounts: new UntypedFormArray([
      new UntypedFormGroup({
        expenseAccount: new UntypedFormControl(),
        syncExpenseAccountToPex: new UntypedFormControl(false),
        defaultAplosTransactionAccountNumber: new UntypedFormControl()
      })
    ])
  });

  tagMappingForm: UntypedFormGroup = new UntypedFormGroup({
    tagMappings: new UntypedFormArray([
      new UntypedFormGroup({
        aplosTag: new UntypedFormControl(),
        pexTag: new UntypedFormControl(),
        syncToPex: new UntypedFormControl(false),
        defaultAplosTag: new UntypedFormControl()
      })
    ])
  });

  isAuthenticated = false;
  sessionId: string;
  isAplosLinked = false;
  isPexAccountLinked = false;
  isFirstInstalation = false;
  pexAdminEmailAccount: string = 'unknown';
  businessName:string = null;
  savingSettings = false;
  projectForm: UntypedFormGroup;
  savingProjects = false;
  aplosContacts: AplosObject[] = [];
  aplosAssetAccounts: AplosAccount[] = [];
  aplosIncomeAccounts: AplosAccount[] = [];
  aplosExpenseAccounts: AplosAccount[] = [];
  aplosLiabilityAccounts: AplosAccount[] = [];
  aplosFunds: AplosObject[] = [];
  aplosTagCategories: AplosObject[] = [];
  aplosTagsByCategoryId: { [categoryId: string]: AplosObject[] } = {};
  aplosTags: { [id: string] : AplosObject[]; } = {};
  aplosTaxTagCategories: AplosApiTaxTagCategoryDetail[] = [];
  hasTagsAvailable = true;
  savingServiceAccountSucceeded = true;
  availablePexTags: PexTagInfoModel[] = [];
  aplosPreferences: AplosPreferences = { isClassEnabled: false, isLocationEnabled: false, locationFieldName: '' };
  isPrepaid: boolean = false;
  isCredit: boolean = false;
  invoiceMethodRequiresRebateFund: boolean = false;

  public readonly postDateTypeTransaction = PostDateType.Transaction;
  public readonly postDateTypeSettlement = PostDateType.Settlement;

  settingsModel: SettingsModel = {
    automaticSync: false,
    syncTransactions: true,
    syncTransfers: false,
    syncInvoices: false,
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
    syncTaxTagToPex: false,
    pexFeesAplosContactId: 0,
    pexFeesAplosFundId: 0,
    pexFeesAplosRegisterAccountNumber: 0,
    pexFeesAplosTransactionAccountNumber: 0,
    pexFeesAplosTaxTag: '',
    syncPexFees: false,
    syncRebates: false,
    syncReimbursements: false,
    transfersAplosContactId: 0,
    transfersAplosFundId: 0,
    transfersAplosTransactionAccountNumber: 0,
    pexRebatesAplosContactId: 0,
    pexRebatesAplosFundId: 0,
    pexRebatesAplosTransactionAccountNumber: 0,
    pexRebatesAplosTaxTag: '',
    syncReimbursementsCreateContact: true, // default new mappings to "Create a contact for each employee"
    reimbursementsAplosContactId: 0,
    reimbursementsAplosRegisterAccountNumber: 0,
    expenseAccountMappings: [],
    tagMappings: [],
    taxTagCategoryDetails: [],
    pexFundingSource: FundingSource.Unknown,
    mapVendorCards: true,
    useNormalizedMerchantNames: true,
    postDateType: PostDateType.Transaction,
    transferTagMappings: [],
    feeTagMappings: [],
    rebateTagMappings: [],
    syncInvoicesMethod: SyncInvoicesMethod.RebateDistribute
  };

  getExpenseAccountFormElements() {
    return this.expenseAccountForm.get('expenseAccounts') as UntypedFormArray;
  }

  deleteExpenseAccountElement(idx: number) {
    this.getExpenseAccountFormElements().removeAt(idx);
  }

  addExpenseAccountElement(expenseAccountMapping: ExpenseAccountMappingModel = { expenseAccountsPexTagId: null, syncExpenseAccounts: false, defaultAplosTransactionAccountNumber: 0 }) {
    console.log('adding expense account element');
    this.getExpenseAccountFormElements().push(
      new UntypedFormGroup(
        {
          expenseAccount: new UntypedFormControl(expenseAccountMapping.expenseAccountsPexTagId, Validators.required),
          syncExpenseAccountToPex: new UntypedFormControl(expenseAccountMapping.syncExpenseAccounts),
          defaultAplosTransactionAccountNumber: new UntypedFormControl(expenseAccountMapping.defaultAplosTransactionAccountNumber),
        }
      )
    );
  }

  getTagMappingFormElements() {
    return this.tagMappingForm.get('tagMappings') as UntypedFormArray;
  }

  deleteTagMappingElement(idx: number) {
    this.getTagMappingFormElements().removeAt(idx);
  }

  addTagMappingElement(tagMapping: TagMappingModel = { aplosTagId: null, pexTagId: null, syncToPex: false, defaultAplosTagId: null }) {
    console.log('adding tag mapping element');
    this.getTagMappingFormElements().push(
      new UntypedFormGroup(
        {
          aplosTag: new UntypedFormControl(tagMapping.aplosTagId),
          pexTag: new UntypedFormControl(tagMapping.pexTagId),
          syncToPex: new UntypedFormControl(tagMapping.syncToPex),
          defaultAplosTag: new UntypedFormControl(tagMapping.defaultAplosTagId)
        },
        tagMappingRowValidator
      )
    );
  }

  // Single shared mapping step: the title reflects which syncs are enabled.
  get tagMappingPageTitle(): string {
    const purchases = this.settingsModel.syncTransactions;
    const reimbursements = this.settingsModel.syncReimbursements;
    if (purchases && reimbursements) {
      return 'Purchases and Reimbursements';
    }
    return reimbursements ? 'Reimbursements' : 'Purchases';
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
  verifyingPexAuthentication = false; 
  
  aplosAuthenticationStatus: AplosAuthenticationStatusModel;
  ngOnInit() {
    this.validateConnections();

    this.auth.businessName.subscribe(
      name => { this.businessName = name }
    );
  }

  validateConnections() {
    this.auth.sessionId.subscribe(token => {
      if (token) {
        this.isAuthenticated = true;
        this.sessionId = token;
        this.verifyingAplosAuthentication = true;
        this.verifyingPexAuthentication = true;
        
        this.pex.getAuthenticationStatus(this.sessionId)
          .pipe(
            tap(() => {
              this.isPexAccountLinked = true;
              this.verifyingPexAuthentication = false;
            }),
            catchError(err => {
              this.isPexAccountLinked = false;
              this.verifyingPexAuthentication = false;
              if(err.status === 404){
                this.isFirstInstalation = true;
                console.log(err);
                return of(err); 
              }
              
              this.isPexAccountLinked = false;
              this.getConnectionDetail();
              return throwError(err);  
            }),
            switchMap(() => this.mapping.getAplosAuthenticationStatus(this.sessionId)))
          .subscribe(
            (result: AplosAuthenticationStatusModel) => {
              console.log('aplosAuthenticationStatus', result);
              this.aplosAuthenticationStatus = { ...result };

              this.verifyingAplosAuthentication = false;

              if (result.isAuthenticated) {
                this.getSettings();
                this.validatePexSetup();
              }
            },
            () => {
              this.isAplosLinked = false;
              this.verifyingAplosAuthentication = false;
            }
          );
      }
    });
  }

  getConnectionDetail(){
    this.pex.getConnectionAccountDetail(this.sessionId)
    .subscribe(result => {
       this.pexAdminEmailAccount = 
                    result.email === '' || result.email === undefined || result.email === null ? 'unknown' : result.email; 
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

  loadingIncomeAccounts = false;
  errorLoadingIncomeAccounts = false;
  getIncomeAccounts() {
    return this.aplos.getAccounts(this.sessionId, "income").subscribe(
      incomeAccounts => {
        console.log('getting income accounts', incomeAccounts);
        this.aplosIncomeAccounts = [...incomeAccounts];
        this.loadingIncomeAccounts = false;
        console.log('got income accounts', this.aplosIncomeAccounts);
      },
      () => {
        this.loadingIncomeAccounts = false;
        this.errorLoadingIncomeAccounts = true;
      }
    )
  }

  loadingLiabilityAccounts = false;
  errorLoadingLiabilityAccounts = false;
  getLiabilityAccounts() {
    this.loadingLiabilityAccounts = true;
    this.errorLoadingLiabilityAccounts = false;

    return this.aplos.getAccounts(this.sessionId, "liability").subscribe(
      liabilityAccounts => {
        console.log('getting liability accounts', liabilityAccounts);
        this.aplosLiabilityAccounts = [...liabilityAccounts];
        this.loadingLiabilityAccounts = false;
        console.log('got liability accounts', this.aplosLiabilityAccounts);
      },
      () => {
        this.loadingLiabilityAccounts = false;
        this.errorLoadingLiabilityAccounts = true;
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
        // Load tags for each category
        this.loadTagsForAllCategories();
      },
      () => {
        this.loadingAplosTagCategories = false;
        this.errorLoadingAplosTagCategories = true;
      }
    );  }

  loadTagsForAllCategories() {
    this.aplosTagCategories.forEach(category => {
      this.loadTagsForCategory(category.id.toString());
    });
  }

  loadTagsForCategory(categoryId: string) {
    this.aplos.getTags(this.sessionId, categoryId).subscribe(
      tags => {
        this.aplosTagsByCategoryId[categoryId] = tags;
        console.log(`Loaded tags for category ${categoryId}:`, tags);
        // Initialize form after all categories have been processed
        this.checkAndInitializeForm();
      },
      error => {
        console.error(`Error loading tags for category ${categoryId}:`, error);
      }
    );
  }

  checkAndInitializeForm() {
    // Check if we have tags for all categories, then initialize the form
    const allCategoriesLoaded = this.aplosTagCategories.every(category => 
      this.aplosTagsByCategoryId[category.id.toString()]
    );
    
    if (allCategoriesLoaded) {
      this.initializeTransferTagMappings();
      this.initializeFeeTagMappings();
      this.initializeRebateTagMappings();
    }
  }

  loadingAplosTags = false;
  errorLoadingAplosTags = false;
  getTags(categoryId: string) {
    this.loadingAplosTags = true;
    this.errorLoadingAplosTags = false;

    return this.aplos.getTags(this.sessionId, categoryId).subscribe(
      aplosTags => {
        console.log('getting Tags', aplosTags);
        this.aplosTags[categoryId] = [ ...aplosTags];
        this.loadingAplosTags = false;
        console.log('got Tags', this.aplosTags);
      },
      () => {
        this.loadingAplosTags = false;
        this.errorLoadingAplosTags = true;
      }
    );
  }

  loadingAplosTaxTags = false;
  errorLoadingAplosTaxTags = false;
  getTaxTagCategories() {
    this.loadingAplosTaxTags = true;
    this.errorLoadingAplosTaxTags = false;

    return this.aplos.getTaxTagCategories(this.sessionId).subscribe(
      aplosTaxTagCategories => {
        console.log('getting TaxTags', aplosTaxTagCategories);
        this.aplosTaxTagCategories = [ ...aplosTaxTagCategories];
        this.loadingAplosTaxTags = false;
        console.log('got TaxTags', this.aplosTaxTagCategories);
        // Initialize the form array with the loaded categories
        this.initializeTransferTagMappings();
        this.initializeFeeTagMappings();
        this.initializeRebateTagMappings();
      },
      () => {
        this.loadingAplosTaxTags = false;
        this.errorLoadingAplosTaxTags = true;
      }
    );
  }

  onAplosCategoryChange(formGroupName: number) {
    let aplosTagCategoryId = this.tagMappingForm.value.tagMappings[formGroupName].aplosTag;

    if (aplosTagCategoryId && aplosTagCategoryId != "990") {
      this.getTags(aplosTagCategoryId);
    }
  }

  onAuthenticateWithPex() {
    this.auth.getOauthURL();
  }

  onUseCurrentPexAccount() {
    this.pex.updatePexAccountLinked(this.sessionId)
      .subscribe(
        () => {
          this.validateConnections();
        });
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

      this.isPrepaid = this.settingsModel.pexFundingSource == FundingSource.Prepaid;
      this.isCredit = this.settingsModel.pexFundingSource == FundingSource.Credit;
      this.invoiceMethodRequiresRebateFund = this.settingsModel.syncInvoicesMethod === SyncInvoicesMethod.Simple
        || this.settingsModel.syncInvoicesMethod === SyncInvoicesMethod.RebateDeposit;

      let d = new Date(data.earliestTransactionDateToSync);
      let newDate = 1 + d.getMonth() + '/' + d.getDate() + '/' + d.getFullYear();
      this.settingsModel.earliestTransactionDateToSync = newDate;

      this.getContacts();
      this.getAssetAccounts();
      this.getExpenseAccounts();
      this.getIncomeAccounts();
      
      if (this.isCredit) {
        this.getLiabilityAccounts();
      }

      this.getFunds();
      this.getTagCategories();
      this.getTaxTagCategories();
      this.initExpenseAccountMappingFormFromSettings(this.settingsModel);
      this.initTagMappingFormFromSettings(this.settingsModel);
      this.initDefaultCategoryFormFromSettings(this.settingsModel);

      // Initialize tag mappings from settings
      this.initializeTransferTagMappings();
      this.initializeFeeTagMappings();
      this.initializeRebateTagMappings();

      if (this.settingsModel.tagMappings.length > 0) {
        this.settingsModel.tagMappings.forEach(
          mapping => {
            if (mapping.aplosTagId) {
              this.getTags(mapping.aplosTagId)
            }
          }
        )
      }
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

  initDefaultCategoryFormFromSettings(settings: SettingsModel) {
    // Initialize form controls with values from settingsModel
    this.defaultCategoryForm.patchValue({
      defaultAplosFundId: settings.defaultAplosFundId,
      defaultAplosTransactionAccountNumber: settings.defaultAplosTransactionAccountNumber,
      transfersAplosContactId: settings.transfersAplosContactId,
      transfersAplosFundId: settings.transfersAplosFundId,
      transfersAplosTransactionAccountNumber: settings.transfersAplosTransactionAccountNumber,
      pexFeesAplosContactId: settings.pexFeesAplosContactId,
      pexFeesAplosFundId: settings.pexFeesAplosFundId,
      pexFeesAplosTransactionAccountNumber: settings.pexFeesAplosTransactionAccountNumber,
      pexFeesAplosTaxTag: settings.pexFeesAplosTaxTag,
      pexRebatesAplosContactId: settings.pexRebatesAplosContactId,
      pexRebatesAplosFundId: settings.pexRebatesAplosFundId,
      pexRebatesAplosTransactionAccountNumber: settings.pexRebatesAplosTransactionAccountNumber,
      pexRebatesAplosTaxTag: settings.pexRebatesAplosTaxTag,
      reimbursementsAplosRegisterAccountNumber: settings.reimbursementsAplosRegisterAccountNumber
    });

    this.updateOtherOptionsValidators();
  }

  // Numeric id selects are patched with the server's 0, which Validators.required
  // treats as a present value — min(1) is what actually forces a selection. String
  // controls (tax tags) keep plain required.
  private applyRequired(controlName: string, isRequired: boolean, isNumericId = true) {
    const control = this.defaultCategoryForm.get(controlName);
    if (!control) { return; }
    if (isRequired) {
      control.setValidators(isNumericId ? [Validators.required, Validators.min(1)] : Validators.required);
    } else {
      control.clearValidators();
    }
    control.updateValueAndValidity({ emitEvent: false });
  }

  // Required-ness of the "Other Options" fields depends on which sync types are
  // enabled. Driving it from the template `required` attribute leaves stale
  // validators on hidden controls (a known reactive-forms + *ngIf pitfall), which
  // blocks Finish for fields the user can't even see. Instead we recompute the
  // validators here from the same conditions the section *ngIf/row *ngIf use.
  //
  // Invoked on init and via (clrWizardPageOnLoad) when the user enters this page.
  // That is sufficient ONLY because every sync-type toggle it reads (syncTransactions,
  // syncTransfers, syncInvoices, syncPexFees, syncRebates, syncReimbursements,
  // syncTaxTagToPex) lives on an EARLIER wizard page and cannot be changed while
  // "Other Options" is shown. If any of those toggles is ever surfaced on this page,
  // also call this from that toggle's (change) handler or the validators will go stale.
  updateOtherOptionsValidators() {
    const s = this.settingsModel;
    const transfersOrInvoices = s.syncTransfers || s.syncInvoices;
    const rebatesVisible = (s.syncTransactions && s.syncRebates) || s.syncInvoices;

    // Shared purchases/reimbursements defaults (only shown when tags unavailable)
    const purchasesVisible = !this.hasTagsAvailable && (s.syncTransactions || s.syncReimbursements);
    this.applyRequired('defaultAplosFundId', purchasesVisible);
    this.applyRequired('defaultAplosTransactionAccountNumber', purchasesVisible);

    // Transfers / Bill payments
    this.applyRequired('transfersAplosContactId', transfersOrInvoices);
    this.applyRequired('transfersAplosFundId', transfersOrInvoices && this.isPrepaid);
    this.applyRequired('transfersAplosTransactionAccountNumber', transfersOrInvoices);

    // Fees
    this.applyRequired('pexFeesAplosContactId', s.syncPexFees);
    this.applyRequired('pexFeesAplosFundId', s.syncPexFees);
    this.applyRequired('pexFeesAplosTransactionAccountNumber', s.syncPexFees);
    this.applyRequired('pexFeesAplosTaxTag', s.syncPexFees && s.syncTaxTagToPex, false);

    // Rebates
    this.applyRequired('pexRebatesAplosContactId', rebatesVisible && this.isPrepaid);
    this.applyRequired('pexRebatesAplosFundId', rebatesVisible && (this.isPrepaid || (this.isCredit && this.invoiceMethodRequiresRebateFund)));
    this.applyRequired('pexRebatesAplosTransactionAccountNumber', rebatesVisible);
    this.applyRequired('pexRebatesAplosTaxTag', rebatesVisible && s.syncTaxTagToPex, false);

    // Reimbursements: only the bank account (register line) is configured here —
    // fund/expense/tags come from the shared mapping page or the shared defaults above.
    this.applyRequired('reimbursementsAplosRegisterAccountNumber', s.syncReimbursements);
  }

  updateSettingsFromDefaultCategoryForm() {
    // Update settingsModel with values from form controls
    const formValue = this.defaultCategoryForm.value;
    
    // Only update defaultAplosFundId if it's being used in the transaction options form
    // (when tags are not available and purchases or reimbursements sync is enabled)
    if (!this.hasTagsAvailable && (this.settingsModel.syncTransactions || this.settingsModel.syncReimbursements)) {
      this.settingsModel.defaultAplosFundId = formValue.defaultAplosFundId;
      this.settingsModel.defaultAplosTransactionAccountNumber = formValue.defaultAplosTransactionAccountNumber;
    }
    
    this.settingsModel.transfersAplosContactId = formValue.transfersAplosContactId;
    this.settingsModel.transfersAplosFundId = formValue.transfersAplosFundId;
    this.settingsModel.transfersAplosTransactionAccountNumber = formValue.transfersAplosTransactionAccountNumber;
    this.settingsModel.pexFeesAplosContactId = formValue.pexFeesAplosContactId;
    this.settingsModel.pexFeesAplosFundId = formValue.pexFeesAplosFundId;
    this.settingsModel.pexFeesAplosTransactionAccountNumber = formValue.pexFeesAplosTransactionAccountNumber;
    this.settingsModel.pexFeesAplosTaxTag = formValue.pexFeesAplosTaxTag;
    this.settingsModel.pexRebatesAplosContactId = formValue.pexRebatesAplosContactId;
    this.settingsModel.pexRebatesAplosFundId = formValue.pexRebatesAplosFundId;
    this.settingsModel.pexRebatesAplosTransactionAccountNumber = formValue.pexRebatesAplosTransactionAccountNumber;
    this.settingsModel.pexRebatesAplosTaxTag = formValue.pexRebatesAplosTaxTag;
    this.settingsModel.reimbursementsAplosRegisterAccountNumber = formValue.reimbursementsAplosRegisterAccountNumber;
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
  onVendorCommit() {
    this.saveContactOptionsSettings().subscribe(() => {
      this.savingSettings = false;
    });
  }

  onSettingsCommit() {
    this.saveSyncSelectionSettings().subscribe(() => {
      this.savingSettings = false;
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

        this.saveAplosCredentialsSettings().subscribe(() => {
          this.savingSettings = false;

          this.verifyingAplosAuthentication = true;
          this.mapping.getAplosAuthenticationStatus(this.sessionId)
            .subscribe(
              (result) => {
                console.log('aplosAuthenticationStatus', result);
                this.aplosAuthenticationStatus = { ...result };

                this.verifyingAplosAuthentication = false;

                if (result.isAuthenticated) {
                  this.getSettings();
                }
              }
            );

        });
      },
        () => {
          console.warn("Error saving Aplos credentials.");

          this.savingAplosCredentials = false;
          this.errorSavingAplosCredentials = true;
        }
      );
  }

  onTagMappingCommit() {
    // Update both expense account mappings and tag mappings using dedicated methods
    this.saveMappingOptionsSettings().subscribe(() => {
      this.savingSettings = false;
    });
  }

  onDefaultCategoryCommit() {
    this.saveTransactionOptionsSettings().subscribe(() => {
      this.savingSettings = false;
    });
  }

  onSyncTransactionChange() {
    if (!this.settingsModel.syncTransactions) {
      this.settingsModel.syncRebates = false;
      if (this.isCredit) {
        this.settingsModel.syncInvoices = false;
      }
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
  getTransferTagMappingFormElements() {
    return this.defaultCategoryForm.get('transferTagMappings') as FormArray;
  }  
  
  initializeTransferTagMappings() {
    const transferTagMappingsArray = this.defaultCategoryForm.get('transferTagMappings') as FormArray;
    
    // Clear existing form controls
    while (transferTagMappingsArray.length !== 0) {
      transferTagMappingsArray.removeAt(0);
    }
    
    // Add a form group for each aplos tag category
    this.aplosTagCategories.forEach(category => {
      // Find existing setting for this category
      const existingMapping = this.settingsModel.transferTagMappings?.find(
        mapping => mapping.aplosTagId === category.id.toString()
      );
      
      const formGroup = new UntypedFormGroup({
        aplosTagCategoryId: new UntypedFormControl(category.id.toString()),
        aplosTagCategoryName: new UntypedFormControl(category.name),
        defaultAplosTagValue: new UntypedFormControl(existingMapping?.defaultAplosTagValue || '')
      });
      transferTagMappingsArray.push(formGroup);
    });
  }
  
  getTagValuesForCategory(categoryId: string): AplosObject[] {
    return this.aplosTagsByCategoryId[categoryId] || [];
  }

  updateSettingsFromTransferTagMappingsForm() {
    const formArray = this.getTransferTagMappingFormElements();
    this.settingsModel.transferTagMappings = [];
    
    formArray.controls.forEach(control => {
      const categoryId = control.get('aplosTagCategoryId')?.value;
      const selectedValue = control.get('defaultAplosTagValue')?.value;
      
      if (categoryId && selectedValue) {
        this.settingsModel.transferTagMappings.push({
          aplosTagId: categoryId,
          defaultAplosTagValue: selectedValue
        });
      }
    });
    
    console.log('Updated transferTagMappings:', this.settingsModel.transferTagMappings);
  }

  getFeeTagMappingFormElements() {
    return this.defaultCategoryForm.get('feeTagMappings') as FormArray;
  }

  getRebateTagMappingFormElements() {
    return this.defaultCategoryForm.get('rebateTagMappings') as FormArray;
  }

  initializeFeeTagMappings() {
    const feeTagMappingsArray = this.defaultCategoryForm.get('feeTagMappings') as FormArray;
    
    // Clear existing form controls
    while (feeTagMappingsArray.length !== 0) {
      feeTagMappingsArray.removeAt(0);
    }
    
    // Add a form group for each aplos tag category
    this.aplosTagCategories.forEach(category => {
      // Find existing setting for this category
      const existingMapping = this.settingsModel.feeTagMappings?.find(
        mapping => mapping.aplosTagId === category.id.toString()
      );
      
      const formGroup = new UntypedFormGroup({
        aplosTagCategoryId: new UntypedFormControl(category.id.toString()),
        aplosTagCategoryName: new UntypedFormControl(category.name),
        defaultAplosTagValue: new UntypedFormControl(existingMapping?.defaultAplosTagValue || '')
      });
      feeTagMappingsArray.push(formGroup);
    });
  }

  initializeRebateTagMappings() {
    const rebateTagMappingsArray = this.defaultCategoryForm.get('rebateTagMappings') as FormArray;
    
    // Clear existing form controls
    while (rebateTagMappingsArray.length !== 0) {
      rebateTagMappingsArray.removeAt(0);
    }
    
    // Add a form group for each aplos tag category
    this.aplosTagCategories.forEach(category => {
      // Find existing setting for this category
      const existingMapping = this.settingsModel.rebateTagMappings?.find(
        mapping => mapping.aplosTagId === category.id.toString()
      );
      
      const formGroup = new UntypedFormGroup({
        aplosTagCategoryId: new UntypedFormControl(category.id.toString()),
        aplosTagCategoryName: new UntypedFormControl(category.name),
        defaultAplosTagValue: new UntypedFormControl(existingMapping?.defaultAplosTagValue || '')
      });
      rebateTagMappingsArray.push(formGroup);
    });
  }

  updateSettingsFromFeeTagMappingsForm() {
    const formArray = this.getFeeTagMappingFormElements();
    this.settingsModel.feeTagMappings = [];
    
    formArray.controls.forEach(control => {
      const categoryId = control.get('aplosTagCategoryId')?.value;
      const selectedValue = control.get('defaultAplosTagValue')?.value;
      
      if (categoryId && selectedValue) {
        this.settingsModel.feeTagMappings.push({
          aplosTagId: categoryId,
          defaultAplosTagValue: selectedValue
        });
      }
    });
    
    console.log('Updated feeTagMappings:', this.settingsModel.feeTagMappings);
  }

  updateSettingsFromRebateTagMappingsForm() {
    const formArray = this.getRebateTagMappingFormElements();
    this.settingsModel.rebateTagMappings = [];
    
    formArray.controls.forEach(control => {
      const categoryId = control.get('aplosTagCategoryId')?.value;
      const selectedValue = control.get('defaultAplosTagValue')?.value;
      
      if (categoryId && selectedValue) {
        this.settingsModel.rebateTagMappings.push({
          aplosTagId: categoryId,
          defaultAplosTagValue: selectedValue
        });
      }
    });
    console.log('Updated rebateTagMappings:', this.settingsModel.rebateTagMappings);
  }

  updateSettingsFromTagMappingForm() {
    // Update regular tag mappings from tagMappingForm
    let tagMappings: TagMappingModel[] = [];
    if (this.tagMappingForm.value.tagMappings) {
      this.tagMappingForm.value.tagMappings.forEach(element => {
        if (element.aplosTag && element.pexTag) {
          tagMappings.push({
            aplosTagId: element.aplosTag,
            pexTagId: element.pexTag,
            syncToPex: element.syncToPex,
            defaultAplosTagId: element.defaultAplosTag
          });
        }
      });
    }
    this.settingsModel.tagMappings = tagMappings;
    console.log('Updated tagMappings:', this.settingsModel.tagMappings);
  }

  updateSettingsFromExpenseAccountForm() {
    // Update expense account mappings from expenseAccountForm
    if (this.expenseAccountForm.valid && this.expenseAccountForm.value.expenseAccounts) {
      let accounts: ExpenseAccountMappingModel[] = [];
      // Sync is only supported for a single mapping (the toggle is hidden for
      // multi-row setups); force false so a stale value from a previously
      // single-row form can't linger after rows are added.
      let hasMultipleMappings = this.expenseAccountForm.value.expenseAccounts.length > 1;
      this.expenseAccountForm.value.expenseAccounts.forEach(element => {
        accounts.push({
          expenseAccountsPexTagId: element.expenseAccount,
          syncExpenseAccounts: hasMultipleMappings ? false : element.syncExpenseAccountToPex,
          defaultAplosTransactionAccountNumber: element.defaultAplosTransactionAccountNumber
        });
      });
      this.settingsModel.expenseAccountMappings = accounts;
    }
    console.log('Updated expenseAccountMappings:', this.settingsModel.expenseAccountMappings);
  }

  saveAplosCredentialsSettings() {
    this.savingSettings = true;
    // Only update Aplos credentials related settings
    console.log('saving Aplos credentials settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }

  saveSyncSelectionSettings() {
    this.savingSettings = true;
    // Only update sync selection related settings
    console.log('saving sync selection settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }

  saveContactOptionsSettings() {
    this.savingSettings = true;
    // Only update contact options related settings
    if (this.settingsModel.syncTransactionsCreateContact) {
      this.settingsModel.defaultAplosContactId = 0;
    } else {
      this.settingsModel.useNormalizedMerchantNames = false;
    }
    if (this.settingsModel.syncReimbursementsCreateContact) {
      this.settingsModel.reimbursementsAplosContactId = 0;
    }
    console.log('saving contact options settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }

  saveMappingOptionsSettings() {
    this.savingSettings = true;
    // Update expense account mappings and tag mappings from forms
    this.updateSettingsFromExpenseAccountForm();
    this.updateSettingsFromTagMappingForm();
    console.log('saving mapping options settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }

  saveTransactionOptionsSettings() {
    this.savingSettings = true;
    // Update default category form and tag mappings from forms
    this.updateSettingsFromDefaultCategoryForm();
    this.updateSettingsFromTransferTagMappingsForm();
    this.updateSettingsFromFeeTagMappingsForm();
    this.updateSettingsFromRebateTagMappingsForm();
    console.log('saving transaction options settings', this.settingsModel);
    return this.mapping.saveSettings(this.sessionId, this.settingsModel);
  }
}

export interface OauthResponse {
  OAuthUrl: string;
}
