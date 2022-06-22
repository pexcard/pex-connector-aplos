import { async, ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';

import { ManageConnectionsComponent } from './manage-connections.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('ManageConnectionsComponent', () => {
  let component: ManageConnectionsComponent;
  let fixture: ComponentFixture<ManageConnectionsComponent>;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ ManageConnectionsComponent ],
      schemas:[CUSTOM_ELEMENTS_SCHEMA],
      imports:[RouterTestingModule, HttpClientTestingModule],
      providers: [{ provide: 'BASE_URL', useValue: 'http://mock.url' }]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(ManageConnectionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
