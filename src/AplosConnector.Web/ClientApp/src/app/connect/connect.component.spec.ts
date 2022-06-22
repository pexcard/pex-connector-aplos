import { async, ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';

import { ConnectComponent } from './connect.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ClarityModule } from '@clr/angular';
import { FormsModule } from '@angular/forms';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

describe('ConnectComponent', () => {
  let component: ConnectComponent;
  let fixture: ComponentFixture<ConnectComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ ConnectComponent ],
      schemas:[CUSTOM_ELEMENTS_SCHEMA],
      imports:[ClarityModule, FormsModule, RouterTestingModule, HttpClientTestingModule, BrowserAnimationsModule],
      providers: [{ provide: 'BASE_URL', useValue: 'http://mock.url' }]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(ConnectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
