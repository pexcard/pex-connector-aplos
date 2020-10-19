import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { LoadingPlaceholderComponent } from './loading-placeholder.component';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';

describe('LoadingPlaceholderComponent', () => {
  let component: LoadingPlaceholderComponent;
  let fixture: ComponentFixture<LoadingPlaceholderComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ LoadingPlaceholderComponent ],
      schemas: [NO_ERRORS_SCHEMA],
      imports: [HttpClientTestingModule]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(LoadingPlaceholderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
