import { HttpClientTestingModule } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ClarityModule } from '@clr/angular';

import { SyncHistoryComponent } from './sync-history.component';

describe('SyncHistoryComponent', () => {
  let component: SyncHistoryComponent;
  let fixture: ComponentFixture<SyncHistoryComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [SyncHistoryComponent],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
      imports: [RouterTestingModule, HttpClientTestingModule, ClarityModule, BrowserAnimationsModule],
      providers: [
        { provide: 'BASE_URL', useValue: 'http://localhost:5001' }
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SyncHistoryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('marks only the new sync types with the New badge', () => {
    expect(component.isNewSyncType('Outstanding Bills')).toBeTrue();
    expect(component.isNewSyncType('Bill Payments')).toBeTrue();
    expect(component.isNewSyncType('Reimbursements')).toBeTrue();
    expect(component.isNewSyncType('Transactions')).toBeFalse();
    expect(component.isNewSyncType('Rebates')).toBeFalse();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
