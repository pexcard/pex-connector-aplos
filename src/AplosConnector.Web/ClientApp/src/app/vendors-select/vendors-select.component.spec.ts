import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ClarityModule } from '@clr/angular';
import { TruncateModule } from '@yellowspot/ng-truncate';

import { VendorsSelectComponent } from './vendors-select.component';

describe('VendorsSelectComponent', () => {
  let component: VendorsSelectComponent;
  let fixture: ComponentFixture<VendorsSelectComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ VendorsSelectComponent ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
      imports: [ClarityModule, FormsModule, RouterTestingModule, HttpClientTestingModule, BrowserAnimationsModule, TruncateModule],
      providers: [
        CurrencyPipe,
        { provide: 'BASE_URL', useValue: 'http://mock.url' }
      ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(VendorsSelectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
