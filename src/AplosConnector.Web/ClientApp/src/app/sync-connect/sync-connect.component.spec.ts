import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ClarityModule } from '@clr/angular';

import { of, throwError } from 'rxjs';

import { SyncConnectComponent } from './sync-connect.component';
import { AplosAccountPipe } from '../pipes/aplosAccount';
import { AplosService } from '../services/aplos.service';

describe('SyncConnectComponent', () => {
  let component: SyncConnectComponent;
  let fixture: ComponentFixture<SyncConnectComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ SyncConnectComponent, AplosAccountPipe ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
      imports: [ClarityModule, FormsModule, ReactiveFormsModule, RouterTestingModule, HttpClientTestingModule, BrowserAnimationsModule],
      providers: [{ provide: 'BASE_URL', useValue: 'http://mock.url' }]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SyncConnectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  // A saved account Aplos can't pay from (e.g. Buildings) would otherwise show blank and be saved again.
  it('clears a saved bill payment account the cash account list does not hold', () => {
    spyOn(TestBed.inject(AplosService), 'getAccounts').and.returnValue(of([{ id: 1010, name: 'Checking' }]));
    component.defaultCategoryForm.patchValue({ billPaymentsAchClearingAccountNumber: 1500, billPaymentsCardClearingAccountNumber: 1010 });

    component.getCashAccounts();

    expect(component.defaultCategoryForm.get('billPaymentsAchClearingAccountNumber').value).toBe(0);
    expect(component.defaultCategoryForm.get('billPaymentsCardClearingAccountNumber').value).toBe(1010);
  });

  it('clears a saved vendor card clearing account the cash account list does not hold', () => {
    spyOn(TestBed.inject(AplosService), 'getAccounts').and.returnValue(of([{ id: 1010, name: 'Checking' }]));
    component.defaultCategoryForm.patchValue({ billPaymentsCardClearingAccountNumber: 1501 });

    component.getCashAccounts();

    expect(component.defaultCategoryForm.get('billPaymentsCardClearingAccountNumber').value).toBe(0);
  });

  it('keeps saved bill payment accounts and reports the failure when the cash account list fails to load', () => {
    spyOn(TestBed.inject(AplosService), 'getAccounts').and.returnValue(throwError(() => new Error('503')));
    component.defaultCategoryForm.patchValue({ billPaymentsAchClearingAccountNumber: 1500 });

    component.getCashAccounts();

    expect(component.errorLoadingCashAccounts).toBeTrue();
    expect(component.loadingCashAccounts).toBeFalse();
    expect(component.defaultCategoryForm.get('billPaymentsAchClearingAccountNumber').value).toBe(1500);
  });

  // PEX picks the rail only when a bill is paid, so syncing bill payments needs the ACH bank account up front.
  it('requires the bill payment ACH bank account only while bill payments sync', () => {
    const achControl = component.defaultCategoryForm.get('billPaymentsAchClearingAccountNumber');
    achControl.setValue(0);

    component.settingsModel.syncBillPayments = false;
    component.updateOtherOptionsValidators();
    expect(achControl.valid).toBeTrue();

    component.settingsModel.syncBillPayments = true;
    component.updateOtherOptionsValidators();
    expect(achControl.valid).toBeFalse();

    achControl.setValue(1010);
    expect(achControl.valid).toBeTrue();
  });

  // Bill payments book through the purchases mapping, so a business without tags syncing only bill payments needs its defaults.
  it('requires the shared default fund and expense account for a business without tags syncing only bill payments', () => {
    const fundControl = component.defaultCategoryForm.get('defaultAplosFundId');
    const accountControl = component.defaultCategoryForm.get('defaultAplosTransactionAccountNumber');
    fundControl.setValue(0);
    accountControl.setValue(0);
    component.hasTagsAvailable = false;
    component.settingsModel.syncTransactions = false;
    component.settingsModel.syncReimbursements = false;
    component.settingsModel.syncBillPayments = true;

    component.updateOtherOptionsValidators();

    expect(fundControl.valid).toBeFalse();
    expect(accountControl.valid).toBeFalse();
  });

  it('asks for no bill payment account while Sync bill payments hides a ticked import', () => {
    const achControl = component.defaultCategoryForm.get('billPaymentsAchClearingAccountNumber');
    const clearingControl = component.defaultCategoryForm.get('billPaymentsCardClearingAccountNumber');
    achControl.setValue(0);
    clearingControl.setValue(0);
    component.isCredit = true;
    component.settingsModel.syncBillPayments = false;
    component.settingsModel.syncOutstandingBills = true;

    component.updateOtherOptionsValidators();

    expect(achControl.valid).toBeTrue();
    expect(clearingControl.valid).toBeTrue();
  });

  it('requires the vendor card clearing account only for a credit business importing bills', () => {
    const clearingControl = component.defaultCategoryForm.get('billPaymentsCardClearingAccountNumber');
    clearingControl.setValue(0);
    component.settingsModel.syncBillPayments = true;
    component.settingsModel.syncOutstandingBills = true;

    component.isCredit = false;
    component.updateOtherOptionsValidators();
    expect(clearingControl.valid).toBeTrue();

    component.isCredit = true;
    component.updateOtherOptionsValidators();
    expect(clearingControl.valid).toBeFalse();

    clearingControl.setValue(1611);
    expect(clearingControl.valid).toBeTrue();
  });
});
