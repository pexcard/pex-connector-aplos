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
});
