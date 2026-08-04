import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ClarityModule } from '@clr/angular';
import { TruncateModule } from '@yellowspot/ng-truncate';

import { VendorsManageComponent } from './vendors-manage.component';

describe('VendorsManageComponent', () => {
  let component: VendorsManageComponent;
  let fixture: ComponentFixture<VendorsManageComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ VendorsManageComponent ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
      imports: [ClarityModule, FormsModule, RouterTestingModule, HttpClientTestingModule, BrowserAnimationsModule, TruncateModule],
      providers: [{ provide: 'BASE_URL', useValue: 'http://mock.url' }]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(VendorsManageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
