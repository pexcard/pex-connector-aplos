import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ClarityModule } from '@clr/angular';

import { SyncConnectComponent } from './sync-connect.component';
import { AplosAccountPipe } from '../pipes/aplosAccount';

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

  // PEX picks the rail only when a bill is paid, so importing bills needs both clearing accounts up front.
  it('requires both bill payment clearing accounts only while Aplos bills are imported', () => {
    const achControl = component.defaultCategoryForm.get('billPaymentsAchClearingAccountNumber');
    const cardControl = component.defaultCategoryForm.get('billPaymentsCardClearingAccountNumber');
    achControl.setValue(0);
    cardControl.setValue(0);

    component.settingsModel.syncOutstandingBills = false;
    component.updateOtherOptionsValidators();
    expect(achControl.valid).toBeTrue();
    expect(cardControl.valid).toBeTrue();

    component.settingsModel.syncOutstandingBills = true;
    component.updateOtherOptionsValidators();
    expect(achControl.valid).toBeFalse();
    expect(cardControl.valid).toBeFalse();

    achControl.setValue(1010);
    cardControl.setValue(1020);
    expect(achControl.valid).toBeTrue();
    expect(cardControl.valid).toBeTrue();
  });
});
